using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.DevFlow.Driver;
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
    internal const int MaxRows = 1000;
    internal const int MaxCellBytes = 64 * 1024;
    internal const int MaxResponseBytes = 1024 * 1024;

    /// <summary>
    /// The three names SQLite answers to for a table's rowid, in the order they are tried.
    /// </summary>
    private static readonly string[] RowIdAliases = ["rowid", "_rowid_", "oid"];

    // ── Schema ──

    /// <exception cref="InvalidOperationException">The file could not be read as a database, or the read ran past the deadline.</exception>
    public static SqliteSchemaResponse ReadSchema(string fullPath, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default, SqliteBrowserOptions? options = null)
    {
        try
        {
            using var database = SqliteGateway.Open(fullPath, timeout, cancellationToken, options, readOnly: true);

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
                    Columns = ReadColumns(database, x.Name).Select(c => Describe(c, x.Kind == "table")).ToArray(),

                    // A view has no indexes and cannot be given any. PRAGMA index_list answers for one
                    // with an empty list rather than an error, so this is skipped for the answer it
                    // would give rather than for the error it would raise.
                    Indexes = x.Kind == "table" ? ReadIndexes(database, x.Name) : [],
                    HasRowId = x.Kind == "table" && RowIdAlias(database, x.Name) is not null
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
            throw new InvalidOperationException(cancellationToken.IsCancellationRequested
                ? "The database request was cancelled." : TimedOut(timeout ?? options?.Timeout));
        }
        catch (SqliteError ex)
        {
            throw new InvalidOperationException(ex.Message, ex);
        }
    }

    // ── Query ──

    public static SqliteQueryResponse Query(string fullPath, string sql, int maxRows, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default, SqliteBrowserOptions? options = null)
    {
        var watch = Stopwatch.StartNew();

        try
        {
            using var database = SqliteGateway.Open(fullPath, timeout, cancellationToken, options);
            return RunScript(database, sql, LimitRows(maxRows), watch);
        }
        catch (Exception ex) when (IsAnswer(ex))
        {
            return Failed(watch, Describe(ex, timeout ?? options?.Timeout, cancellationToken), ErrorCode(ex, cancellationToken));
        }
    }

    private static SqliteQueryResponse RunScript(SqliteDatabase database, string sql, int maxRows, Stopwatch watch)
    {
        var columns = Array.Empty<string>();
        var rows = new List<string?[]>();
        var cells = new List<SqliteValue[]>();
        var budget = new ResultBudget();
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
                    budget.AddColumns(columns);

                    while (statement.Step())
                    {
                        if (truncated || rows.Count == maxRows || !budget.TryAddRow(ReadRow(statement), out var row))
                        {
                            // Asked for, not read: there is one more row than the cap, which is what lets
                            // the client say "first 500 of more" rather than "500".
                            truncated = true;
                            if (statement.IsReadOnly)
                                break;
                            continue;
                        }

                        rows.Add(row.Display);
                        cells.Add(row.Cells);
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

        var result = Result(columns, rows.ToArray(), shown ? -1 : affected, truncated, watch, null);
        result.Cells = cells.ToArray();
        return result;
    }

    // ── Rows ──

    public static SqliteRowsResponse ReadRows(string fullPath, string table, int maxRows, TimeSpan? timeout = null,
        string? cursor = null, CancellationToken cancellationToken = default, SqliteBrowserOptions? options = null)
    {
        var watch = Stopwatch.StartNew();

        try
        {
            using var database = SqliteGateway.Open(fullPath, timeout, cancellationToken, options, readOnly: true);
            maxRows = LimitRows(maxRows);

            // A view is selectable but not editable, and this route feeds the grid in both cases -
            // so it takes either, and the absence of rowids in the answer is what tells the client
            // which it got.
            var name = RequireSelectable(database, table);
            var alias = RowIdAlias(database, name);
            var schemaVersion = SchemaVersion(database);
            var page = DecodeCursor(cursor, fullPath, name, schemaVersion);
            var order = alias ?? string.Join(", ", ReadColumns(database, name).Where(x => !x.IsHidden)
                .Select(x => Quote(x.Name)));
            var where = alias is not null && page.AfterRowId is not null ? $" WHERE {alias} > $after" : "";
            var projection = alias is null ? "*" : $"{alias}, *";

            using var statement = database.Prepare(
                $"SELECT {projection} FROM {Quote(name)}{where} ORDER BY {order} LIMIT $take OFFSET $skip");

            statement.Bind("$take", maxRows + 1L);
            statement.Bind("$skip", alias is null ? page.Offset : 0);
            if (where.Length > 0)
                statement.Bind("$after", ParseRowId(page.AfterRowId!));

            var offset = alias is null ? 0 : 1;
            var columns = Enumerable.Range(offset, statement.ColumnCount - offset).Select(statement.ColumnName).ToArray();
            var ids = new List<string>();
            var versions = new List<string>();
            var rows = new List<string?[]>();
            var cells = new List<SqliteValue[]>();
            var truncated = false;
            var budget = new ResultBudget();
            budget.AddColumns(columns);

            while (statement.Step())
            {
                if (rows.Count == maxRows || !budget.TryAddRow(ReadRow(statement, offset), out var row))
                {
                    truncated = true;
                    break;
                }

                if (alias is not null)
                    ids.Add(statement.GetInt64(0).ToString(CultureInfo.InvariantCulture));

                rows.Add(row.Display);
                cells.Add(row.Cells);
                versions.Add(row.Version);
            }

            return new SqliteRowsResponse
            {
                Columns = columns,
                RowIds = ids.ToArray(),
                Rows = rows.ToArray(),
                Cells = cells.ToArray(),
                RowVersions = versions.ToArray(),
                NextCursor = truncated ? EncodeCursor(new SqlitePageCursor
                {
                    Database = DatabaseKey(fullPath), Table = name, SchemaVersion = schemaVersion,
                    AfterRowId = ids.LastOrDefault(), Offset = checked(page.Offset + rows.Count)
                }) : null,
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
                Error = Describe(ex, timeout ?? options?.Timeout, cancellationToken),
                ErrorCode = ErrorCode(ex, cancellationToken)
            };
        }
    }

    public static SqliteQueryResponse InsertRow(string fullPath, string table, IReadOnlyList<SqliteCellEdit> values, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default, SqliteBrowserOptions? options = null)
    {
        var watch = Stopwatch.StartNew();

        try
        {
            using var database = SqliteGateway.Open(fullPath, timeout, cancellationToken, options);
            database.Execute("BEGIN IMMEDIATE");

            // The table and every column name are taken from the database's own schema rather than
            // from the request, because an identifier cannot be a parameter and the only safe
            // identifier is one the caller never chose.
            var name = RequireTable(database, table);
            var columns = ReadColumns(database, name);

            var quoted = new List<string>();
            var parameters = new List<(string Name, SqliteCellEdit Edit)>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < values.Count; i++)
            {
                var column = FindColumn(columns, name, values[i].Column);
                if (!seen.Add(column))
                    throw new InvalidOperationException($"Column '{column}' is repeated.");
                quoted.Add(Quote(column));
                parameters.Add(($"$v{i}", values[i]));
            }

            // DEFAULT VALUES rather than an empty column list, which is not valid SQL. It is the
            // honest statement for "one more record, all of it whatever the table says".
            using (var statement = database.Prepare(quoted.Count == 0
                ? $"INSERT INTO {Quote(name)} DEFAULT VALUES"
                : $"INSERT INTO {Quote(name)} ({string.Join(", ", quoted)}) VALUES ({string.Join(", ", parameters.Select(x => x.Name))})"))
            {
                foreach (var (parameter, edit) in parameters)
                    BindEdit(statement, parameter, edit);

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
            var result = RowIdAlias(database, name) is { } alias
                ? ReadBack(database, name, alias, rowId, affected, watch)
                : Result([], [], affected, false, watch, null);
            database.Execute("COMMIT");
            return result;
        }
        catch (Exception ex) when (IsAnswer(ex))
        {
            return Failed(watch, Describe(ex, timeout ?? options?.Timeout, cancellationToken), ErrorCode(ex, cancellationToken));
        }
    }

    public static SqliteQueryResponse UpdateRow(string fullPath, string table, long rowId, IReadOnlyList<SqliteCellEdit> changes, TimeSpan? timeout = null,
        string? expectedVersion = null, CancellationToken cancellationToken = default, SqliteBrowserOptions? options = null)
    {
        var watch = Stopwatch.StartNew();

        if (changes.Count == 0)
            return Failed(watch, "Nothing was changed.");

        try
        {
            using var database = SqliteGateway.Open(fullPath, timeout, cancellationToken, options);
            database.Execute("BEGIN IMMEDIATE");

            var name = RequireTable(database, table);
            var columns = ReadColumns(database, name);
            var alias = RequireRowIdAlias(database, name);
            CheckVersion(database, name, alias, rowId, expectedVersion);

            var assignments = new List<string>();
            var parameters = new List<(string Name, SqliteCellEdit Edit)>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; i < changes.Count; i++)
            {
                var column = FindColumn(columns, name, changes[i].Column);
                if (!seen.Add(column))
                    throw new InvalidOperationException($"Column '{column}' is repeated.");
                assignments.Add($"{Quote(column)} = $v{i}");
                parameters.Add(($"$v{i}", changes[i]));
            }

            using (var statement = database.Prepare(
                $"UPDATE {Quote(name)} SET {string.Join(", ", assignments)} WHERE {alias} = $rowid RETURNING {alias}"))
            {
                // The text goes in as text and SQLite applies the column's affinity to it, so "42"
                // typed into an INTEGER column is stored as the number 42 - which is why the row is
                // read back below rather than assumed.
                foreach (var (parameter, edit) in parameters)
                    BindEdit(statement, parameter, edit);

                statement.Bind("$rowid", rowId);

                while (statement.Step())
                {
                    rowId = statement.GetInt64(0);
                }
            }

            var affected = database.Changes;

            if (affected == 0)
            {
                // The row was deleted, or never existed. Saying so beats a silent success on a grid
                // that would then be showing a value nothing in the file agrees with.
                return Failed(watch, "That record is no longer there. Refresh the table.");
            }

            var result = ReadBack(database, name, alias, rowId, affected, watch);
            database.Execute("COMMIT");
            return result;
        }
        catch (Exception ex) when (IsAnswer(ex))
        {
            return Failed(watch, Describe(ex, timeout ?? options?.Timeout, cancellationToken), ErrorCode(ex, cancellationToken));
        }
    }

    public static SqliteQueryResponse DeleteRow(string fullPath, string table, long rowId, TimeSpan? timeout = null,
        string? expectedVersion = null, CancellationToken cancellationToken = default, SqliteBrowserOptions? options = null)
    {
        var watch = Stopwatch.StartNew();

        try
        {
            using var database = SqliteGateway.Open(fullPath, timeout, cancellationToken, options);
            database.Execute("BEGIN IMMEDIATE");
            var name = RequireTable(database, table);
            var alias = RequireRowIdAlias(database, name);
            CheckVersion(database, name, alias, rowId, expectedVersion);

            using (var statement = database.Prepare($"DELETE FROM {Quote(name)} WHERE {alias} = $rowid"))
            {
                statement.Bind("$rowid", rowId);

                while (statement.Step())
                {
                }
            }

            var affected = database.Changes;
            database.Execute("COMMIT");

            return affected == 0
                ? Failed(watch, "That record is no longer there. Refresh the table.")
                : Result([], [], affected, false, watch, null);
        }
        catch (Exception ex) when (IsAnswer(ex))
        {
            return Failed(watch, Describe(ex, timeout ?? options?.Timeout, cancellationToken), ErrorCode(ex, cancellationToken));
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
        var row = statement.Step() ? ReadRow(statement) : null;
        var budget = new ResultBudget();
        budget.AddColumns(columns);
        if (row is not null)
            budget.TryAddRow(row, out _);
        var result = Result(columns, row is null ? [] : [row.Display], affected, false, watch, null);
        result.Cells = row is null ? [] : [row.Cells];
        result.RowId = row is null ? null : rowId.ToString(CultureInfo.InvariantCulture);
        result.RowVersion = row?.Version;
        return result;
    }

    // ── Reading values ──

    /// <param name="from">
    /// The first column to read. Non-zero for the editable grid, whose select list carries the rowid
    /// in front of the table's own columns.
    /// </param>
    private static RowData ReadRow(SqliteStatement statement, int from = 0)
    {
        var values = new string?[statement.ColumnCount - from];
        var cells = new SqliteValue[values.Length];
        if (values.Length > 1024)
            throw new InvalidOperationException("A browser result cannot have more than 1024 columns.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        for (var i = 0; i < values.Length; i++)
        {
            var ordinal = i + from;
            var type = statement.ColumnType(ordinal);
            var name = Encoding.UTF8.GetBytes(statement.ColumnName(ordinal));
            hash.AppendData(BitConverter.GetBytes(name.Length));
            hash.AppendData(name);
            hash.AppendData(BitConverter.GetBytes(type));
            var bytes = type switch
            {
                raw.SQLITE_INTEGER => BitConverter.GetBytes(statement.GetInt64(ordinal)),
                raw.SQLITE_FLOAT => BitConverter.GetBytes(statement.GetDouble(ordinal)),
                _ => Array.Empty<byte>()
            };
            var data = type is raw.SQLITE_TEXT or raw.SQLITE_BLOB ? statement.GetBlob(ordinal) : bytes.AsSpan();
            hash.AppendData(BitConverter.GetBytes(data.Length));
            hash.AppendData(data);
            var cell = new SqliteValue
            {
                Type = type switch
                {
                    raw.SQLITE_INTEGER => "integer", raw.SQLITE_FLOAT => "real",
                    raw.SQLITE_TEXT => "text", raw.SQLITE_BLOB => "blob", _ => "null"
                },
                ByteLength = data.Length,
                Truncated = data.Length > MaxCellBytes
            };

            // Numbers in the invariant culture, and a REAL in its round-trip form. The text is what the
            // grid shows and what an edit sends back: "1,5" from a German phone is TEXT when it returns,
            // and an error in a STRICT table.
            values[i] = type switch
            {
                raw.SQLITE_NULL => null,
                raw.SQLITE_INTEGER => statement.GetInt64(ordinal).ToString(CultureInfo.InvariantCulture),
                raw.SQLITE_FLOAT => statement.GetDouble(ordinal).ToString("R", CultureInfo.InvariantCulture),
                raw.SQLITE_BLOB => DescribeBlob(statement.GetBlob(ordinal)),
                _ => cell.Truncated ? $"TEXT[{data.Length} bytes]" : statement.GetString(ordinal)
            };
            cell.Value = cell.Truncated ? null : type == raw.SQLITE_BLOB
                ? Convert.ToBase64String(data) : values[i];
            cells[i] = cell;
        }

        return new RowData(values, cells, Convert.ToHexString(hash.GetHashAndReset()));
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
        using var statement = database.Prepare($"PRAGMA table_xinfo({Quote(table)})");
        var columns = new List<SqliteColumn>();

        while (statement.Step())
        {
            columns.Add(new SqliteColumn(
                statement.GetString(1),
                // A column in a view, or in a table declared without types, has none - and "" reads
                // as a missing value in the header rather than as the answer it is.
                statement.IsNull(2) || statement.GetString(2).Length == 0 ? "any" : statement.GetString(2),
                statement.GetBoolean(3),
                statement.GetInt32(5) > 0,
                statement.GetInt32(6)
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
            .Select(FoldIdentifier)
            .ToHashSet(StringComparer.Ordinal);

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
    {
        var column = columns.FirstOrDefault(x => FoldIdentifier(x.Name) == FoldIdentifier(requested ?? ""));
        if (column is null)
            throw new InvalidOperationException($"'{table}' has no column called '{requested}'.");
        if (column.Hidden != 0)
            throw new InvalidOperationException($"Column '{column.Name}' is generated or hidden and cannot be written.");
        return column.Name;
    }

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

    private static SqliteQueryResponse Failed(Stopwatch watch, string message, string? errorCode = null)
    {
        var result = Result([], [], -1, false, watch, message);
        result.ErrorCode = errorCode;
        return result;
    }

    /// <summary>
    /// What a request can fail with that is an answer for the person asking: SQL that SQLite refused,
    /// a file that is not a database, a name that is not there, or the deadline.
    /// </summary>
    private static bool IsAnswer(Exception ex)
        => ex is SqliteError or InvalidOperationException or OperationCanceledException;

    private static string TimedOut(TimeSpan? timeout)
        => $"The statement was still running after {(timeout ?? SqliteGateway.DefaultTimeout).TotalSeconds:0.#} seconds and was stopped.";

    private static string Describe(Exception ex, TimeSpan? timeout, CancellationToken cancellationToken)
        => ex is OperationCanceledException
            ? cancellationToken.IsCancellationRequested ? "The database request was cancelled." : TimedOut(timeout)
            : ex.Message;

    private static string ErrorCode(Exception ex, CancellationToken cancellationToken)
        => ex switch
        {
            RowConflictException => "conflict",
            OperationCanceledException => cancellationToken.IsCancellationRequested ? "cancelled" : "timeout",
            SqliteError sql => $"sqlite_{sql.Code}",
            _ => "invalid_request"
        };

    internal static int LimitRows(int requested) => requested <= 0 ? 500 : Math.Min(requested, MaxRows);

    internal static long ParseRowId(string text)
        => long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var id)
            && id.ToString(CultureInfo.InvariantCulture) == text
                ? id : throw new InvalidOperationException("rowId must be a canonical signed 64-bit decimal string.");

    private static string FoldIdentifier(string identifier)
        => string.Concat(identifier.Select(c => c is >= 'A' and <= 'Z' ? (char)(c + ('a' - 'A')) : c));

    private static void BindEdit(SqliteStatement statement, string parameter, SqliteCellEdit edit)
    {
        if (edit.Truncated)
            throw new InvalidOperationException("A truncated cell cannot be saved. Supply its complete value.");
        if (edit.Type is null && edit.Value is not null && Encoding.UTF8.GetByteCount(edit.Value) > MaxCellBytes)
            throw new InvalidOperationException("An edited cell cannot exceed 65536 bytes.");
        switch (edit.Type)
        {
            case null:
                statement.Bind(parameter, edit.Value);
                break;
            case "null" when edit.Value is null:
                statement.Bind(parameter, (string?)null);
                break;
            case "text" when edit.Value is not null:
                if (Encoding.UTF8.GetByteCount(edit.Value) > MaxCellBytes)
                    throw new InvalidOperationException("An edited cell cannot exceed 65536 bytes.");
                statement.Bind(parameter, edit.Value);
                break;
            case "integer" when edit.Value is not null:
                statement.Bind(parameter, ParseRowId(edit.Value));
                break;
            case "real" when double.TryParse(edit.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var real)
                && !double.IsNaN(real):
                statement.Bind(parameter, real);
                break;
            case "blob" when edit.Value is not null:
                if (edit.Value.Length > ((MaxCellBytes + 2) / 3) * 4)
                    throw new InvalidOperationException("An edited blob cannot exceed 65536 bytes.");
                try
                {
                    var blob = Convert.FromBase64String(edit.Value);
                    if (blob.Length > MaxCellBytes)
                        throw new InvalidOperationException("An edited blob cannot exceed 65536 bytes.");
                    statement.BindBlob(parameter, blob);
                }
                catch (FormatException ex)
                {
                    throw new InvalidOperationException("A blob value must be base64.", ex);
                }
                break;
            default:
                throw new InvalidOperationException("A cell must contain a valid null, integer, real, text or blob value.");
        }
    }

    private static void CheckVersion(SqliteDatabase database, string table, string alias, long rowId, string? expectedVersion)
    {
        if (expectedVersion is null)
            return;
        using var statement = database.Prepare($"SELECT * FROM {Quote(table)} WHERE {alias} = $rowid");
        statement.Bind("$rowid", rowId);
        if (!statement.Step() || !string.Equals(ReadRow(statement).Version, expectedVersion, StringComparison.Ordinal))
            throw new RowConflictException("That record changed since it was read. Refresh the table before saving.");
    }

    private static int SchemaVersion(SqliteDatabase database)
    {
        using var statement = database.Prepare("PRAGMA schema_version");
        return statement.Step() ? statement.GetInt32(0) : 0;
    }

    private static string DatabaseKey(string path) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)));

    private static string EncodeCursor(SqlitePageCursor page)
    {
        var cursor = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(page, StorageAgentJsonContext.Default.SqlitePageCursor));
        return cursor.Length <= 8192 ? cursor
            : throw new InvalidOperationException("The table name is too long for a paging cursor.");
    }

    private static SqlitePageCursor DecodeCursor(string? cursor, string path, string table, int schemaVersion)
    {
        if (cursor is null)
            return new SqlitePageCursor();
        if (cursor.Length > 8192)
            throw new InvalidOperationException("Invalid database page cursor.");
        try
        {
            var page = JsonSerializer.Deserialize(Convert.FromBase64String(cursor), StorageAgentJsonContext.Default.SqlitePageCursor);
            if (page is null || page.Database != DatabaseKey(path) || page.Table != table
                || page.SchemaVersion != schemaVersion || page.Offset < 0 || page.Offset > long.MaxValue - MaxRows)
                throw new InvalidOperationException("This page cursor is invalid or the database schema changed. Refresh the table.");
            return page;
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            throw new InvalidOperationException("Invalid database page cursor.", ex);
        }
    }

    private sealed record RowData(string?[] Display, SqliteValue[] Cells, string Version);

    private sealed class ResultBudget
    {
        // Includes envelope fields, versions, IDs and the bounded continuation cursor.
        private int _bytes = 16 * 1024;
        private int _rows;

        public void AddColumns(string[] columns)
        {
            _bytes += JsonSerializer.SerializeToUtf8Bytes(columns, StorageAgentJsonContext.Default.StringArray).Length;
            if (_bytes > MaxResponseBytes)
                throw new InvalidOperationException("The result's column metadata exceeds the 1 MiB response limit.");
        }

        public bool TryAddRow(RowData row, out RowData accepted)
        {
            accepted = row;
            var size = JsonSerializer.SerializeToUtf8Bytes(row.Display, StorageAgentJsonContext.Default.StringArray).Length
                + JsonSerializer.SerializeToUtf8Bytes(row.Cells, StorageAgentJsonContext.Default.SqliteValueArray).Length + 256;
            if (_bytes + size > MaxResponseBytes)
            {
                if (_rows == 0)
                    throw new InvalidOperationException("One row exceeds the 1 MiB response limit. Select fewer or smaller columns.");
                return false;
            }
            _bytes += size;
            _rows++;
            return true;
        }
    }

    private sealed class RowConflictException(string message) : InvalidOperationException(message);

    /// <summary>
    /// An identifier, quoted. Every name that reaches this came out of <c>sqlite_master</c> or
    /// <c>PRAGMA table_info</c> a moment earlier rather than from a caller - the doubling is for the
    /// table genuinely called <c>my"table</c>, not for a caller trying to end the statement.
    /// </summary>
    private static string Quote(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";

    private static SqliteColumnDescriptor Describe(SqliteColumn column, bool table)
        => new()
        {
            Name = column.Name,
            Type = column.Type,
            NotNull = column.NotNull,
            PrimaryKey = column.PrimaryKey,
            IsGenerated = column.Hidden is 2 or 3,
            IsHidden = column.Hidden == 1,
            IsWritable = table && column.Hidden == 0
        };
}

/// <param name="Value">Null is SQL NULL, which is why this is not simply an empty string.</param>
internal sealed record SqliteCellEdit(string? Column, string? Value, string? Type = null, bool Truncated = false);

internal sealed record SqliteColumn(string Name, string Type, bool NotNull, bool PrimaryKey, int Hidden)
{
    public bool IsHidden => Hidden == 1;
}
