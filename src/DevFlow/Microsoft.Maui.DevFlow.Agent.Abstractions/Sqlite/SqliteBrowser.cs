using System.Diagnostics;
using System.Globalization;
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
/// <b>Nothing runs past the deadline.</b> Every operation here - schema, grid, insert, update,
/// delete, not only the query pane - runs on a connection whose deadline interrupts it. A view is a
/// query and a trigger is a script, so "just reading a table" or "just changing one cell" can cost
/// as much as anything typed into the query pane.
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
    /// How much of a blob to describe rather than send. A cell is a table cell; nobody reads a
    /// megabyte of jpeg in one, and shipping it would cost the row it is in.
    /// </summary>
    private const int BlobPreview = 32;

    /// <summary>
    /// The three names SQLite answers to for a table's rowid, in the order they are tried.
    /// </summary>
    private static readonly string[] RowIdAliases = ["rowid", "_rowid_", "oid"];

    // ── Schema ──

    /// <exception cref="InvalidOperationException">The file could not be read as a database, or the read ran past the deadline.</exception>
    public static SqliteSchemaResponse ReadSchema(string fullPath, TimeSpan? timeout = null)
    {
        try
        {
            using var database = SqliteGateway.Open(fullPath, timeout);

            var names = new List<(string Name, string Kind)>();

            // sqlite_master rather than the pragma_table_list of newer SQLite: this has to answer for
            // whatever version wrote the file, and the two internal prefixes are the whole difference.
            using (var statement = database.Prepare(
                """
                SELECT name, type
                FROM sqlite_master
                WHERE type IN ('table', 'view')
                  AND name NOT LIKE 'sqlite\_%' ESCAPE '\'
                ORDER BY type, name COLLATE NOCASE
                """))
            {
                while (statement.Step())
                    names.Add((statement.GetString(0), statement.GetString(1)));
            }

            var tables = names
                .Select(x => new SqliteTableDescriptor
                {
                    Name = x.Name,
                    Kind = x.Kind,
                    Columns = ReadColumns(database, x.Name).Select(Describe).ToArray(),

                    // A view has no indexes and cannot be given any. PRAGMA index_list answers for one
                    // with an empty list rather than an error, so this is skipped for the answer it
                    // would give rather than for the error it would raise.
                    Indexes = x.Kind == "table" ? ReadIndexes(database, x.Name) : [],
                    HasRowId = RowIdAlias(database, x.Name) is not null
                })
                .ToArray();

            return new SqliteSchemaResponse
            {
                Tables = tables,
                FileSize = new FileInfo(fullPath).Length,
                SqliteVersion = SqliteDatabase.Version
            };
        }
        catch (OperationCanceledException)
        {
            throw new InvalidOperationException(TimedOut(timeout));
        }
        catch (SqliteError ex)
        {
            throw new InvalidOperationException(ex.Message, ex);
        }
    }

    // ── Query ──

    public static SqliteQueryResponse Query(string fullPath, string sql, int maxRows, TimeSpan? timeout = null)
    {
        var watch = Stopwatch.StartNew();

        try
        {
            using var database = SqliteGateway.Open(fullPath, timeout);
            return RunScript(database, sql, maxRows, watch);
        }
        catch (Exception ex) when (IsAnswer(ex))
        {
            return Failed(watch, Describe(ex, timeout));
        }
    }

    private static SqliteQueryResponse RunScript(SqliteDatabase database, string sql, int maxRows, Stopwatch watch)
    {
        var columns = Array.Empty<string>();
        var rows = new List<string?[]>();
        var truncated = false;
        var shown = false;

        // -1 until a statement that can write has run: "0 rows affected" is a true and useful thing for
        // a DELETE to say, and "not that kind of statement" is not the same answer.
        var affected = -1;

        // Every statement runs, in order. The first that produces columns is the one shown - which is
        // what makes "UPDATE …; SELECT * FROM …", how anyone checks their own write, do what it looks
        // like - and everything after it still runs: a trailing UPDATE that was never stepped is a write
        // the user watched succeed and did not get.
        var remaining = sql;
        while (database.PrepareNext(ref remaining) is { } statement)
        {
            using (statement)
            {
                var changesBefore = database.TotalChanges;

                if (!shown && statement.ColumnCount > 0)
                {
                    shown = true;
                    columns = Enumerable.Range(0, statement.ColumnCount).Select(statement.ColumnName).ToArray();

                    while (statement.Step())
                    {
                        if (rows.Count == maxRows)
                        {
                            // Asked for, not read: there is one more row than the cap, which is what lets
                            // the client say "first 500 of more" rather than "500".
                            truncated = true;
                            break;
                        }

                        rows.Add(ReadRow(statement));
                    }
                }
                else
                {
                    while (statement.Step())
                    {
                    }
                }

                if (!statement.IsReadOnly)
                {
                    // Changes reports the last INSERT, UPDATE or DELETE on the connection even after a
                    // CREATE TABLE that changed no rows at all, so it is only counted when the total moved.
                    affected = Math.Max(affected, 0)
                        + (database.TotalChanges != changesBefore ? database.Changes : 0);
                }
            }
        }

        return Result(columns, rows.ToArray(), shown ? -1 : affected, truncated, watch, null);
    }

    // ── Rows ──

    public static SqliteRowsResponse ReadRows(string fullPath, string table, int maxRows, TimeSpan? timeout = null)
    {
        var watch = Stopwatch.StartNew();

        try
        {
            using var database = SqliteGateway.Open(fullPath, timeout);

            // A view is selectable but not editable, and this route feeds the grid in both cases -
            // so it takes either, and the absence of rowids in the answer is what tells the client
            // which it got.
            var name = RequireSelectable(database, table);
            var alias = RowIdAlias(database, name);

            // The rowid first and by itself, rather than folded into the * that follows it, under a
            // name no column of the table has taken - see RowIdAlias.
            using var statement = database.Prepare(alias is null
                ? $"SELECT * FROM {Quote(name)} LIMIT $take"
                : $"SELECT {alias}, * FROM {Quote(name)} LIMIT $take");

            statement.Bind("$take", maxRows + 1L);

            var offset = alias is null ? 0 : 1;
            var columns = Enumerable.Range(offset, statement.ColumnCount - offset).Select(statement.ColumnName).ToArray();
            var ids = new List<long>();
            var rows = new List<string?[]>();
            var truncated = false;

            while (statement.Step())
            {
                if (rows.Count == maxRows)
                {
                    truncated = true;
                    break;
                }

                if (alias is not null)
                    ids.Add(statement.GetInt64(0));

                rows.Add(ReadRow(statement, offset));
            }

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
        catch (Exception ex) when (IsAnswer(ex))
        {
            return new SqliteRowsResponse
            {
                ElapsedMs = watch.ElapsedMilliseconds,
                Error = Describe(ex, timeout)
            };
        }
    }

    public static SqliteQueryResponse InsertRow(string fullPath, string table, IReadOnlyList<SqliteCellEdit> values, TimeSpan? timeout = null)
    {
        var watch = Stopwatch.StartNew();

        try
        {
            using var database = SqliteGateway.Open(fullPath, timeout);

            // The table and every column name are taken from the database's own schema rather than
            // from the request, because an identifier cannot be a parameter and the only safe
            // identifier is one the caller never chose.
            var name = RequireTable(database, table);
            var columns = ReadColumns(database, name);

            var quoted = new List<string>();
            var parameters = new List<(string Name, string? Value)>();

            for (var i = 0; i < values.Count; i++)
            {
                var column = FindColumn(columns, name, values[i].Column);
                quoted.Add(Quote(column));
                parameters.Add(($"$v{i}", values[i].Value));
            }

            // DEFAULT VALUES rather than an empty column list, which is not valid SQL. It is the
            // honest statement for "one more record, all of it whatever the table says".
            using (var statement = database.Prepare(quoted.Count == 0
                ? $"INSERT INTO {Quote(name)} DEFAULT VALUES"
                : $"INSERT INTO {Quote(name)} ({string.Join(", ", quoted)}) VALUES ({string.Join(", ", parameters.Select(x => x.Name))})"))
            {
                foreach (var (parameter, value) in parameters)
                    statement.Bind(parameter, value);

                while (statement.Step())
                {
                }
            }

            var affected = database.Changes;

            // Where the record landed, asked of the connection rather than worked out: the value is
            // the rowid of the last insert on this connection, and this connection has done exactly
            // one thing. It is also the only way to find a record whose key the table chose.
            var rowId = database.LastInsertRowId;

            // A table with no rowid to name has nothing to read back by - the insert worked, and there
            // is no id that names what it wrote. The count is the whole answer, and the grid re-reads.
            return RowIdAlias(database, name) is { } alias
                ? ReadBack(database, name, alias, rowId, affected, watch)
                : Result([], [], affected, false, watch, null);
        }
        catch (Exception ex) when (IsAnswer(ex))
        {
            return Failed(watch, Describe(ex, timeout));
        }
    }

    public static SqliteQueryResponse UpdateRow(string fullPath, string table, long rowId, IReadOnlyList<SqliteCellEdit> changes, TimeSpan? timeout = null)
    {
        var watch = Stopwatch.StartNew();

        if (changes.Count == 0)
            return Failed(watch, "Nothing was changed.");

        try
        {
            using var database = SqliteGateway.Open(fullPath, timeout);

            var name = RequireTable(database, table);
            var columns = ReadColumns(database, name);
            var alias = RequireRowIdAlias(database, name);

            var assignments = new List<string>();
            var parameters = new List<(string Name, string? Value)>();

            for (var i = 0; i < changes.Count; i++)
            {
                var column = FindColumn(columns, name, changes[i].Column);
                assignments.Add($"{Quote(column)} = $v{i}");
                parameters.Add(($"$v{i}", changes[i].Value));
            }

            using (var statement = database.Prepare(
                $"UPDATE {Quote(name)} SET {string.Join(", ", assignments)} WHERE {alias} = $rowid"))
            {
                // The text goes in as text and SQLite applies the column's affinity to it, so "42"
                // typed into an INTEGER column is stored as the number 42 - which is why the row is
                // read back below rather than assumed.
                foreach (var (parameter, value) in parameters)
                    statement.Bind(parameter, value);

                statement.Bind("$rowid", rowId);

                while (statement.Step())
                {
                }
            }

            var affected = database.Changes;

            if (affected == 0)
            {
                // The row was deleted, or never existed. Saying so beats a silent success on a grid
                // that would then be showing a value nothing in the file agrees with.
                return Failed(watch, "That record is no longer there. Refresh the table.");
            }

            return ReadBack(database, name, alias, rowId, affected, watch);
        }
        catch (Exception ex) when (IsAnswer(ex))
        {
            return Failed(watch, Describe(ex, timeout));
        }
    }

    public static SqliteQueryResponse DeleteRow(string fullPath, string table, long rowId, TimeSpan? timeout = null)
    {
        var watch = Stopwatch.StartNew();

        try
        {
            using var database = SqliteGateway.Open(fullPath, timeout);
            var name = RequireTable(database, table);
            var alias = RequireRowIdAlias(database, name);

            using (var statement = database.Prepare($"DELETE FROM {Quote(name)} WHERE {alias} = $rowid"))
            {
                statement.Bind("$rowid", rowId);

                while (statement.Step())
                {
                }
            }

            var affected = database.Changes;

            return affected == 0
                ? Failed(watch, "That record is no longer there. Refresh the table.")
                : Result([], [], affected, false, watch, null);
        }
        catch (Exception ex) when (IsAnswer(ex))
        {
            return Failed(watch, Describe(ex, timeout));
        }
    }

    /// <summary>
    /// The record as it stands after a write, read rather than echoed: SQLite applies the column's
    /// type affinity, a column left out takes its default, an INTEGER PRIMARY KEY takes the next
    /// rowid, and a trigger is free to have made it something else again.
    /// </summary>
    private static SqliteQueryResponse ReadBack(SqliteDatabase database, string table, string alias, long rowId, int affected, Stopwatch watch)
    {
        using var statement = database.Prepare($"SELECT * FROM {Quote(table)} WHERE {alias} = $rowid");
        statement.Bind("$rowid", rowId);

        var columns = Enumerable.Range(0, statement.ColumnCount).Select(statement.ColumnName).ToArray();
        var rows = statement.Step() ? new[] { ReadRow(statement) } : [];

        return Result(columns, rows, affected, false, watch, null);
    }

    // ── Reading values ──

    /// <param name="from">
    /// The first column to read. Non-zero for the editable grid, whose select list carries the rowid
    /// in front of the table's own columns.
    /// </param>
    private static string?[] ReadRow(SqliteStatement statement, int from = 0)
    {
        var values = new string?[statement.ColumnCount - from];

        for (var i = 0; i < values.Length; i++)
        {
            var ordinal = i + from;

            // Numbers in the invariant culture, and a REAL in its round-trip form. The text is what the
            // grid shows and what an edit sends back: "1,5" from a German phone is TEXT when it returns,
            // and an error in a STRICT table.
            values[i] = statement.ColumnType(ordinal) switch
            {
                raw.SQLITE_NULL => null,
                raw.SQLITE_INTEGER => statement.GetInt64(ordinal).ToString(CultureInfo.InvariantCulture),
                raw.SQLITE_FLOAT => statement.GetDouble(ordinal).ToString("R", CultureInfo.InvariantCulture),
                raw.SQLITE_BLOB => DescribeBlob(statement.GetBlob(ordinal)),
                _ => statement.GetString(ordinal)
            };
        }

        return values;
    }

    /// <summary>
    /// A blob as a length and a few bytes of hex, because a cell cannot show one and a row should
    /// not cost what one weighs. Enough is shown to recognise a PNG header or a UUID.
    /// </summary>
    private static string DescribeBlob(ReadOnlySpan<byte> blob)
    {
        var take = Math.Min(blob.Length, BlobPreview);
        var hex = Convert.ToHexString(blob[..take]);
        var ellipsis = blob.Length > take ? "…" : "";

        return $"BLOB[{blob.Length}] {hex}{ellipsis}";
    }

    // ── Schema helpers ──

    private static List<SqliteColumn> ReadColumns(SqliteDatabase database, string table)
    {
        using var statement = database.Prepare($"PRAGMA table_info({Quote(table)})");
        var columns = new List<SqliteColumn>();

        while (statement.Step())
        {
            columns.Add(new SqliteColumn(
                statement.GetString(1),
                // A column in a view, or in a table declared without types, has none - and "" reads
                // as a missing value in the header rather than as the answer it is.
                statement.IsNull(2) || statement.GetString(2).Length == 0 ? "any" : statement.GetString(2),
                statement.GetBoolean(3),
                statement.GetInt32(5) > 0
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
    /// partial and automatic in one place.
    /// </remarks>
    private static SqliteIndexDescriptor[] ReadIndexes(SqliteDatabase database, string table)
    {
        var found = new List<(string Name, bool Unique, bool Automatic, bool Partial)>();

        using (var statement = database.Prepare($"PRAGMA index_list({Quote(table)})"))
        {
            while (statement.Step())
            {
                // seq, name, unique, origin, partial - origin is "c" for a CREATE INDEX and "u" or
                // "pk" for the index a UNIQUE or PRIMARY KEY constraint brought with it.
                found.Add((
                    statement.GetString(1),
                    statement.GetBoolean(2),
                    !string.Equals(statement.GetString(3), "c", StringComparison.Ordinal),
                    statement.GetBoolean(4)
                ));
            }
        }

        return found
            .Select(x => new SqliteIndexDescriptor
            {
                Name = x.Name,
                Columns = ReadIndexColumns(database, x.Name),
                Unique = x.Unique,
                Automatic = x.Automatic,
                Partial = x.Partial
            })
            .ToArray();
    }

    private static string[] ReadIndexColumns(SqliteDatabase database, string index)
    {
        using var statement = database.Prepare($"PRAGMA index_info({Quote(index)})");
        var columns = new List<string>();

        while (statement.Step())
        {
            // seqno, cid, name - the name is null where the index is over an expression rather than
            // a column, which is a position in the index that has to be accounted for.
            columns.Add(statement.IsNull(2) ? "(expression)" : statement.GetString(2));
        }

        return columns.ToArray();
    }

    /// <summary>
    /// The name that reaches this table's real rowid, or null when nothing does: a view, a WITHOUT
    /// ROWID table, or a table whose own columns have taken every name the rowid answers to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A table may declare a column called <c>rowid</c>, and from then on <c>rowid</c> in a statement
    /// means that column - which need not be unique, so an UPDATE naming one record by it can change
    /// several. SQLite answers to three names for the real rowid and a column can shadow each one, so
    /// the first that no column has claimed is the one used, and a table that has claimed all three
    /// is read-only here.
    /// </para>
    /// <para>
    /// Whether there is a rowid at all is asked by preparing a statement rather than by reading the
    /// schema: a view and a WITHOUT ROWID table both fail to compile one, and there is no single flag
    /// covering both. Prepared and never stepped, so this never costs a scan.
    /// </para>
    /// </remarks>
    private static string? RowIdAlias(SqliteDatabase database, string table)
    {
        var taken = ReadColumns(database, table)
            .Select(x => x.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var alias = RowIdAliases.FirstOrDefault(x => !taken.Contains(x));
        if (alias is null)
            return null;

        try
        {
            using var _ = database.Prepare($"SELECT {alias} FROM {Quote(table)} LIMIT 0");
            return alias;
        }
        catch (SqliteError)
        {
            return null;
        }
    }

    private static string RequireRowIdAlias(SqliteDatabase database, string table)
        => RowIdAlias(database, table)
            ?? throw new InvalidOperationException(
                $"'{table}' has no rowid to name a record by, so its records cannot be edited here. Use the query pane.");

    /// <summary>
    /// The name of a real table, as the database spells it, or an error naming what was asked for.
    /// </summary>
    /// <remarks>
    /// Looked up rather than trusted, and the answer used in place of the request's own string -
    /// which is what makes it safe to put in a statement. Views are refused as well as missing
    /// tables, because this is the check a write goes through: a view has no rowid, so there is no
    /// record for an UPDATE to name.
    /// </remarks>
    private static string RequireTable(SqliteDatabase database, string requested)
        => Find(database, requested, "type = 'table'")
            ?? throw new InvalidOperationException($"There is no table called '{requested}'.");

    /// <summary>The same, for reading, where a view is a perfectly good thing to be pointed at.</summary>
    private static string RequireSelectable(SqliteDatabase database, string requested)
        => Find(database, requested, "type IN ('table', 'view')")
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
    private static string? Find(SqliteDatabase database, string requested, string kinds)
    {
        using var statement = database.Prepare($"SELECT name FROM sqlite_master WHERE {kinds} AND name = $name COLLATE NOCASE");
        statement.Bind("$name", requested);

        return statement.Step() ? statement.GetString(0) : null;
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

    /// <summary>
    /// What a request can fail with that is an answer for the person asking: SQL that SQLite refused,
    /// a file that is not a database, a name that is not there, or the deadline.
    /// </summary>
    private static bool IsAnswer(Exception ex)
        => ex is SqliteError or InvalidOperationException or OperationCanceledException;

    private static string TimedOut(TimeSpan? timeout)
        => $"The statement was still running after {(timeout ?? SqliteGateway.DefaultTimeout).TotalSeconds:0.#} seconds and was stopped.";

    private static string Describe(Exception ex, TimeSpan? timeout)
        => ex is OperationCanceledException ? TimedOut(timeout) : ex.Message;

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
