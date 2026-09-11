using System.Diagnostics;
using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace Microsoft.Maui.DevFlow.Agent.Core.Sqlite;


/// <summary>
/// Answers questions about a SQLite file, and runs the SQL it is handed.
/// </summary>
/// <remarks>
/// <para>
/// <b>Writes are on, deliberately.</b> This is a database client, and a database client that cannot
/// fix a row is a table viewer with a text box. It can UPDATE, DELETE and run DDL. There is no undo.
/// The agent only ever listens on loopback and only ships in Debug builds, which is the setting that
/// makes that trade a reasonable one.
/// </para>
/// <para>
/// <b>What it cannot do is leave the file.</b> That guarantee is enforced by the authorizer in
/// <see cref="SqliteGateway"/> rather than by anything here.
/// </para>
/// <para>
/// Nothing in here throws for something the SQL did. A statement that will not parse, names a table
/// that is not there, or breaks a constraint comes back in the result's error - to the person who
/// just typed it, those are answers rather than faults.
/// </para>
/// </remarks>
internal static class SqliteBrowser
{
    /// <summary>
    /// How long any one statement gets before it is interrupted.
    /// </summary>
    /// <remarks>
    /// This may be a phone. A cartesian join typed by accident is not a hung request to be waited
    /// out - it is a warm device with a flat battery, holding a write lock on a file the file
    /// manager is also showing.
    /// </remarks>
    private static readonly TimeSpan StatementTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How much of a blob to describe rather than send. A cell is a table cell; nobody reads a
    /// megabyte of jpeg in one, and shipping it would cost the row it is in.
    /// </summary>
    private const int BlobPreview = 32;

    // ── Schema ──

    public static SqliteSchemaResponse ReadSchema(string fullPath)
    {
        using var connection = SqliteGateway.Open(fullPath);

        var names = new List<(string Name, string Kind)>();

        // sqlite_master rather than the pragma_table_list of newer SQLite: this has to answer for
        // whatever version wrote the file, and the two internal prefixes are the whole difference.
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT name, type
                FROM sqlite_master
                WHERE type IN ('table', 'view')
                  AND name NOT LIKE 'sqlite\_%' ESCAPE '\'
                ORDER BY type, name COLLATE NOCASE
                """;

            using var reader = command.ExecuteReader();
            while (reader.Read())
                names.Add((reader.GetString(0), reader.GetString(1)));
        }

        var tables = names
            .Select(x => new SqliteTableDescriptor
            {
                Name = x.Name,
                Kind = x.Kind,
                Columns = ReadColumns(connection, x.Name).Select(Describe).ToArray(),

                // A view has no indexes and cannot be given any. PRAGMA index_list answers for one
                // with an empty list rather than an error, so this is skipped for the answer it
                // would give rather than for the error it would raise.
                Indexes = x.Kind == "table" ? ReadIndexes(connection, x.Name) : [],
                HasRowId = HasRowId(connection, x.Name)
            })
            .ToArray();

        return new SqliteSchemaResponse
        {
            Tables = tables,
            FileSize = new FileInfo(fullPath).Length,
            SqliteVersion = connection.ServerVersion
        };
    }

    // ── Query ──

    public static SqliteQueryResponse Query(string fullPath, string sql, int maxRows)
    {
        var watch = Stopwatch.StartNew();

        try
        {
            using var connection = SqliteGateway.Open(fullPath);
            return Execute(connection, sql, maxRows, watch);
        }
        catch (OperationCanceledException)
        {
            return Failed(watch, TimedOut());
        }
        catch (SqliteException ex)
        {
            return Failed(watch, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            // what Open throws when the bytes are not a database
            return Failed(watch, ex.Message);
        }
    }

    private static SqliteQueryResponse Execute(SqliteConnection connection, string sql, int maxRows, Stopwatch watch)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        // sqlite3_interrupt, reached through the raw provider rather than through
        // SqliteCommand.Cancel - which is an empty method on this provider. A command that silently
        // does nothing when asked to stop is the difference between a timeout and a flat battery.
        var handle = connection.Handle;
        using var deadline = new CancellationTokenSource(StatementTimeout);
        using var registration = deadline.Token.Register(() => raw.sqlite3_interrupt(handle));

        using var reader = Interruptible(() => command.ExecuteReader());

        var columns = Array.Empty<string>();
        var rows = new List<string?[]>();
        var truncated = false;

        // ExecuteReader stops at the first statement that produces rows, so a script ending in a
        // SELECT arrives here already positioned on it. Walking result sets is what makes
        // "UPDATE …; SELECT * FROM …" - which is how anyone checks their own write - do what it
        // looks like.
        do
        {
            if (reader.FieldCount == 0)
                continue;

            columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();

            while (Interruptible(reader.Read))
            {
                if (rows.Count == maxRows)
                {
                    // Asked for, not read: there is one more row than the cap, which is what lets
                    // the client say "first 500 of more" rather than "500".
                    truncated = true;
                    break;
                }

                rows.Add(ReadRow(reader));
            }

            break;
        }
        while (Interruptible(reader.NextResult));

        // Everything after the result set that was shown still has to run - the reader is lazy, and
        // a trailing UPDATE that was never stepped is a write the user watched succeed and did not
        // get. Draining is also what settles RecordsAffected.
        while (Interruptible(reader.NextResult))
        {
        }

        reader.Close();

        // A query reports -1 rather than 0, because "0 rows affected" is a true and useful thing for
        // a DELETE to say, and "not that kind of statement" is not the same answer.
        var affected = columns.Length > 0 ? -1 : reader.RecordsAffected;

        return Result(columns, rows.ToArray(), affected, truncated, watch, null);
    }

    // ── Rows ──

    public static SqliteRowsResponse ReadRows(string fullPath, string table, int maxRows)
    {
        var watch = Stopwatch.StartNew();

        try
        {
            using var connection = SqliteGateway.Open(fullPath);

            // A view is selectable but not editable, and this route feeds the grid in both cases -
            // so it takes either, and the absence of rowids in the answer is what tells the client
            // which it got.
            var name = RequireSelectable(connection, table);
            var addressable = HasRowId(connection, name);

            using var command = connection.CreateCommand();

            // rowid first and by itself, rather than folded into the * that follows it. A table is
            // allowed a column of its own called "rowid", and then the select list has two columns
            // by that name and the ordinal is the only thing that still tells them apart - which is
            // why this reads column zero by position and never by name.
            command.CommandText = addressable
                ? $"SELECT rowid, * FROM {Quote(name)} LIMIT $take"
                : $"SELECT * FROM {Quote(name)} LIMIT $take";

            command.Parameters.AddWithValue("$take", maxRows + 1);

            using var reader = command.ExecuteReader();

            var offset = addressable ? 1 : 0;
            var columns = Enumerable.Range(offset, reader.FieldCount - offset).Select(reader.GetName).ToArray();
            var ids = new List<long>();
            var rows = new List<string?[]>();
            var truncated = false;

            while (reader.Read())
            {
                if (rows.Count == maxRows)
                {
                    truncated = true;
                    break;
                }

                if (addressable)
                    ids.Add(reader.GetInt64(0));

                rows.Add(ReadRow(reader, offset));
            }

            reader.Close();

            return new SqliteRowsResponse
            {
                Columns = columns,
                RowIds = ids.ToArray(),
                Rows = rows.ToArray(),
                Truncated = truncated,
                ElapsedMs = watch.ElapsedMilliseconds,
                Error = null
            };
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or OperationCanceledException)
        {
            return new SqliteRowsResponse
            {
                ElapsedMs = watch.ElapsedMilliseconds,
                Error = Describe(ex)
            };
        }
    }

    public static SqliteQueryResponse InsertRow(string fullPath, string table, IReadOnlyList<SqliteCellEdit> values)
    {
        var watch = Stopwatch.StartNew();

        try
        {
            using var connection = SqliteGateway.Open(fullPath);

            // The table and every column name are taken from the database's own schema rather than
            // from the request, because an identifier cannot be a parameter and the only safe
            // identifier is one the caller never chose.
            var name = RequireTable(connection, table);
            var columns = ReadColumns(connection, name);

            using var command = connection.CreateCommand();

            var quoted = new List<string>();
            var parameters = new List<string>();

            for (var i = 0; i < values.Count; i++)
            {
                var value = values[i];
                var column = FindColumn(columns, name, value.Column);

                var parameter = $"$v{i}";
                quoted.Add(Quote(column));
                parameters.Add(parameter);

                command.Parameters.AddWithValue(parameter, (object?)value.Value ?? DBNull.Value);
            }

            // DEFAULT VALUES rather than an empty column list, which is not valid SQL. It is the
            // honest statement for "one more record, all of it whatever the table says".
            command.CommandText = quoted.Count == 0
                ? $"INSERT INTO {Quote(name)} DEFAULT VALUES"
                : $"INSERT INTO {Quote(name)} ({string.Join(", ", quoted)}) VALUES ({string.Join(", ", parameters)})";

            var affected = command.ExecuteNonQuery();

            // Where the record landed, asked of the connection rather than worked out: the value is
            // the rowid of the last insert on this connection, and this connection has done exactly
            // one thing. It is also the only way to find a record whose key the table chose.
            var rowId = raw.sqlite3_last_insert_rowid(connection.Handle);

            // A WITHOUT ROWID table has nothing to read back by - the insert worked, and there is no
            // id that names what it wrote. The count is the whole answer, and the grid re-reads.
            return HasRowId(connection, name)
                ? ReadBack(connection, name, rowId, affected, watch)
                : Result([], [], affected, false, watch, null);
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or OperationCanceledException)
        {
            return Failed(watch, Describe(ex));
        }
    }

    public static SqliteQueryResponse UpdateRow(string fullPath, string table, long rowId, IReadOnlyList<SqliteCellEdit> changes)
    {
        var watch = Stopwatch.StartNew();

        if (changes.Count == 0)
            return Failed(watch, "Nothing was changed.");

        try
        {
            using var connection = SqliteGateway.Open(fullPath);

            var name = RequireTable(connection, table);
            var columns = ReadColumns(connection, name);

            var assignments = new List<string>();
            using var command = connection.CreateCommand();

            for (var i = 0; i < changes.Count; i++)
            {
                var change = changes[i];
                var column = FindColumn(columns, name, change.Column);

                var parameter = $"$v{i}";
                assignments.Add($"{Quote(column)} = {parameter}");

                // The text goes in as text and SQLite applies the column's affinity to it, so "42"
                // typed into an INTEGER column is stored as the number 42 - which is why the row is
                // read back below rather than assumed.
                command.Parameters.AddWithValue(parameter, (object?)change.Value ?? DBNull.Value);
            }

            command.CommandText =
                $"UPDATE {Quote(name)} SET {string.Join(", ", assignments)} WHERE rowid = $rowid";
            command.Parameters.AddWithValue("$rowid", rowId);

            var affected = command.ExecuteNonQuery();

            if (affected == 0)
            {
                // The row was deleted, or never existed. Saying so beats a silent success on a grid
                // that would then be showing a value nothing in the file agrees with.
                return Failed(watch, "That record is no longer there. Refresh the table.");
            }

            return ReadBack(connection, name, rowId, affected, watch);
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or OperationCanceledException)
        {
            return Failed(watch, Describe(ex));
        }
    }

    public static SqliteQueryResponse DeleteRow(string fullPath, string table, long rowId)
    {
        var watch = Stopwatch.StartNew();

        try
        {
            using var connection = SqliteGateway.Open(fullPath);
            var name = RequireTable(connection, table);

            using var command = connection.CreateCommand();
            command.CommandText = $"DELETE FROM {Quote(name)} WHERE rowid = $rowid";
            command.Parameters.AddWithValue("$rowid", rowId);

            var affected = command.ExecuteNonQuery();

            return affected == 0
                ? Failed(watch, "That record is no longer there. Refresh the table.")
                : Result([], [], affected, false, watch, null);
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or OperationCanceledException)
        {
            return Failed(watch, Describe(ex));
        }
    }

    /// <summary>
    /// The record as it stands after a write, read rather than echoed: SQLite applies the column's
    /// type affinity, a column left out takes its default, an INTEGER PRIMARY KEY takes the next
    /// rowid, and a trigger is free to have made it something else again.
    /// </summary>
    private static SqliteQueryResponse ReadBack(SqliteConnection connection, string table, long rowId, int affected, Stopwatch watch)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM {Quote(table)} WHERE rowid = $rowid";
        command.Parameters.AddWithValue("$rowid", rowId);

        using var reader = command.ExecuteReader();

        var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
        var rows = reader.Read() ? new[] { ReadRow(reader) } : [];

        reader.Close();

        return Result(columns, rows, affected, false, watch, null);
    }

    // ── Reading values ──

    /// <param name="from">
    /// The first column to read. Non-zero for the editable grid, whose select list carries the rowid
    /// in front of the table's own columns.
    /// </param>
    private static string?[] ReadRow(SqliteDataReader reader, int from = 0)
    {
        var values = new string?[reader.FieldCount - from];

        for (var i = 0; i < values.Length; i++)
        {
            var ordinal = i + from;

            if (reader.IsDBNull(ordinal))
                continue;

            values[i] = reader.GetFieldType(ordinal) == typeof(byte[])
                ? DescribeBlob(reader, ordinal)
                : reader.GetValue(ordinal).ToString();
        }

        return values;
    }

    /// <summary>
    /// A blob as a length and a few bytes of hex, because a cell cannot show one and a row should
    /// not cost what one weighs. Enough is shown to recognise a PNG header or a UUID.
    /// </summary>
    private static string DescribeBlob(SqliteDataReader reader, int ordinal)
    {
        var length = reader.GetBytes(ordinal, 0, null, 0, 0);
        var take = (int)Math.Min(length, BlobPreview);
        var buffer = new byte[take];
        reader.GetBytes(ordinal, 0, buffer, 0, take);

        var hex = Convert.ToHexString(buffer);
        var ellipsis = length > take ? "…" : "";

        return $"BLOB[{length}] {hex}{ellipsis}";
    }

    // ── Schema helpers ──

    private static List<SqliteColumn> ReadColumns(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({Quote(table)})";

        using var reader = command.ExecuteReader();
        var columns = new List<SqliteColumn>();

        while (reader.Read())
        {
            columns.Add(new SqliteColumn(
                reader.GetString(1),
                // A column in a view, or in a table declared without types, has none - and "" reads
                // as a missing value in the header rather than as the answer it is.
                reader.IsDBNull(2) || reader.GetString(2).Length == 0 ? "any" : reader.GetString(2),
                reader.GetBoolean(3),
                reader.GetInt32(5) > 0
            ));
        }

        return columns;
    }

    /// <summary>
    /// The indexes on a table, each with the columns it is over.
    /// </summary>
    /// <remarks>
    /// Two pragmas rather than a join on <c>sqlite_master</c>: the SQL of an index is null for the
    /// ones SQLite made itself, and <c>index_list</c> is the only thing that answers for unique,
    /// partial and automatic in one place. Read into a list before the second pragma runs - a pragma
    /// is a statement like any other, and this provider allows only one reader per connection.
    /// </remarks>
    private static SqliteIndexDescriptor[] ReadIndexes(SqliteConnection connection, string table)
    {
        var found = new List<(string Name, bool Unique, bool Automatic, bool Partial)>();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = $"PRAGMA index_list({Quote(table)})";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                // seq, name, unique, origin, partial - origin is "c" for a CREATE INDEX and "u" or
                // "pk" for the index a UNIQUE or PRIMARY KEY constraint brought with it.
                found.Add((
                    reader.GetString(1),
                    reader.GetBoolean(2),
                    !string.Equals(reader.GetString(3), "c", StringComparison.Ordinal),
                    reader.GetBoolean(4)
                ));
            }
        }

        return found
            .Select(x => new SqliteIndexDescriptor
            {
                Name = x.Name,
                Columns = ReadIndexColumns(connection, x.Name),
                Unique = x.Unique,
                Automatic = x.Automatic,
                Partial = x.Partial
            })
            .ToArray();
    }

    private static string[] ReadIndexColumns(SqliteConnection connection, string index)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA index_info({Quote(index)})";

        using var reader = command.ExecuteReader();
        var columns = new List<string>();

        while (reader.Read())
        {
            // seqno, cid, name - the name is null where the index is over an expression rather than
            // a column, which is a position in the index that has to be accounted for.
            columns.Add(reader.IsDBNull(2) ? "(expression)" : reader.GetString(2));
        }

        return columns.ToArray();
    }

    /// <summary>
    /// Whether rows in this table can be named individually.
    /// </summary>
    /// <remarks>
    /// Asked by preparing a statement rather than by reading the schema: a WITHOUT ROWID table and a
    /// view both fail to compile <c>SELECT rowid</c>, and there is no single flag covering both.
    /// <c>LIMIT 0</c> so this costs a prepare and never a scan.
    /// </remarks>
    private static bool HasRowId(SqliteConnection connection, string table)
    {
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT rowid FROM {Quote(table)} LIMIT 0";
            command.ExecuteNonQuery();
            return true;
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    /// <summary>
    /// The name of a real table, as the database spells it, or an error naming what was asked for.
    /// </summary>
    /// <remarks>
    /// Looked up rather than trusted, and the answer used in place of the request's own string -
    /// which is what makes it safe to put in a statement. Views are refused as well as missing
    /// tables, because this is the check a write goes through: a view has no rowid, so there is no
    /// record for an UPDATE to name.
    /// </remarks>
    private static string RequireTable(SqliteConnection connection, string requested)
        => Find(connection, requested, "type = 'table'")
            ?? throw new InvalidOperationException($"There is no table called '{requested}'.");

    /// <summary>The same, for reading, where a view is a perfectly good thing to be pointed at.</summary>
    private static string RequireSelectable(SqliteConnection connection, string requested)
        => Find(connection, requested, "type IN ('table', 'view')")
            ?? throw new InvalidOperationException($"There is nothing called '{requested}' in this database.");

    /// <summary>
    /// The table or view as <c>sqlite_master</c> spells it, matched the way SQLite itself matches
    /// identifiers.
    /// </summary>
    /// <remarks>
    /// NOCASE, not an exact match: SQLite resolves <c>PEOPLE</c> to <c>people</c>, so an exact match
    /// would reject a name the engine would have accepted. It is also what makes the answer useful -
    /// the point of looking the name up is to get the database's spelling rather than the caller's,
    /// and an exact match can only ever hand back what it was given.
    ///
    /// No ambiguity to worry about: SQLite refuses to create two tables whose names differ only by
    /// case, so at most one row can match.
    /// </remarks>
    private static string? Find(SqliteConnection connection, string requested, string kinds)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT name FROM sqlite_master WHERE {kinds} AND name = $name COLLATE NOCASE";
        command.Parameters.AddWithValue("$name", requested);

        return command.ExecuteScalar() as string;
    }

    /// <summary>
    /// The column as the database spells it, or an error naming what was asked for. What goes into
    /// the statement is this answer, never the caller's string.
    /// </summary>
    /// <remarks>
    /// Matched without regard to case, for the same reason <see cref="Find"/> is: SQLite resolves
    /// <c>NAME</c> to <c>Name</c>, so anything stricter would refuse an edit the engine would have
    /// accepted - and would hand back the caller's spelling rather than the table's.
    /// </remarks>
    private static string FindColumn(List<SqliteColumn> columns, string table, string? requested)
        => columns.FirstOrDefault(x => string.Equals(x.Name, requested, StringComparison.OrdinalIgnoreCase))?.Name
            ?? throw new InvalidOperationException($"'{table}' has no column called '{requested}'.");

    // ── Shared shapes ──

    private static SqliteQueryResponse Result(string[] columns, string?[][] rows, int affected, bool truncated, Stopwatch watch, string? error)
        => new()
        {
            Columns = columns,
            Rows = rows,
            RowsAffected = affected,
            Truncated = truncated,
            ElapsedMs = watch.ElapsedMilliseconds,
            Error = error
        };

    private static SqliteQueryResponse Failed(Stopwatch watch, string message)
        => Result([], [], -1, false, watch, message);

    private static string TimedOut()
        => $"The statement was still running after {StatementTimeout.TotalSeconds:0} seconds and was stopped.";

    private static string Describe(Exception ex)
        => ex is OperationCanceledException ? TimedOut() : ex.Message;

    private static T Interruptible<T>(Func<T> step)
    {
        try
        {
            return step();
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == raw.SQLITE_INTERRUPT)
        {
            throw new OperationCanceledException();
        }
    }

    /// <summary>
    /// An identifier, quoted. Every name that reaches this came out of <c>sqlite_master</c> or
    /// <c>PRAGMA table_info</c> a moment earlier rather than from a caller - the doubling is for the
    /// table genuinely called <c>my"table</c>, not for a caller trying to end the statement.
    /// </summary>
    private static string Quote(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";

    private static SqliteColumnDescriptor Describe(SqliteColumn column)
        => new()
        {
            Name = column.Name,
            Type = column.Type,
            NotNull = column.NotNull,
            PrimaryKey = column.PrimaryKey
        };
}

/// <param name="Value">Null is SQL NULL, which is why this is not simply an empty string.</param>
internal sealed record SqliteCellEdit(string? Column, string? Value);

internal sealed record SqliteColumn(string Name, string Type, bool NotNull, bool PrimaryKey);
