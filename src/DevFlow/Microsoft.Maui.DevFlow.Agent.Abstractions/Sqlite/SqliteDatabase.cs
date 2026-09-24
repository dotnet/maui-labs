using SQLitePCL;

namespace Microsoft.Maui.DevFlow.Agent.Core.Sqlite;

/// <summary>
/// One open database file, for the length of one request, with a deadline over everything run on it.
/// </summary>
/// <remarks>
/// <para>
/// Straight onto SQLitePCLRaw rather than through Microsoft.Data.Sqlite. Its <c>SqliteConnection</c>
/// registers the batteries bundle from a static constructor the first time one is made, which replaces
/// whatever provider the app set up - a debugging tool quietly changing how the app talks to its own
/// database. See <see cref="SqliteGateway.EnsureProvider"/>.
/// </para>
/// <para>
/// The deadline is the connection's rather than any one statement's, so a schema read, a grid, and a
/// write with a slow trigger behind it are all held to it - not only the query pane.
/// </para>
/// </remarks>
internal sealed class SqliteDatabase : IDisposable
{
    /// <summary>
    /// How long a statement waits on a lock the app is holding. Short of the deadline, so a busy file
    /// answers "database is locked" rather than spending the whole request waiting to.
    /// </summary>
    private const int BusyTimeoutMilliseconds = 5000;

    private readonly CancellationTokenSource _deadline;
    private readonly CancellationTokenRegistration _interrupt;

    private SqliteDatabase(sqlite3 handle, TimeSpan timeout)
    {
        Handle = handle;
        _deadline = new CancellationTokenSource(timeout);

        // sqlite3_interrupt stops whatever is running when the deadline passes. It does nothing for a
        // statement started afterwards, which is why every prepare and step checks the deadline as well.
        _interrupt = _deadline.Token.Register(() => raw.sqlite3_interrupt(handle));
    }

    public sqlite3 Handle { get; }

    public static string Version => raw.sqlite3_libversion().utf8_to_string() ?? "";

    public long LastInsertRowId => raw.sqlite3_last_insert_rowid(Handle);

    /// <summary>Rows changed by the most recently completed INSERT, UPDATE or DELETE.</summary>
    public int Changes => raw.sqlite3_changes(Handle);

    /// <summary>Rows changed since the connection opened - which is how to tell a statement that changed nothing from one that did.</summary>
    public int TotalChanges => raw.sqlite3_total_changes(Handle);

    /// <exception cref="InvalidOperationException">SQLite would not open the file.</exception>
    public static SqliteDatabase Open(string fullPath, int flags, TimeSpan timeout)
    {
        var rc = raw.sqlite3_open_v2(fullPath, out var handle, flags, null);
        if (rc != raw.SQLITE_OK)
        {
            var message = handle is null || handle.IsInvalid
                ? raw.sqlite3_errstr(rc).utf8_to_string()
                : raw.sqlite3_errmsg(handle).utf8_to_string();

            handle?.Dispose();
            throw new InvalidOperationException(message);
        }

        raw.sqlite3_busy_timeout(handle, BusyTimeoutMilliseconds);
        return new SqliteDatabase(handle, timeout);
    }

    /// <summary>Prepares one statement, which must be all of <paramref name="sql"/>.</summary>
    /// <exception cref="SqliteError">The SQL did not compile, or the authorizer refused it.</exception>
    public SqliteStatement Prepare(string sql)
    {
        ThrowIfExpired();

        var rc = raw.sqlite3_prepare_v2(Handle, sql, out var statement);
        if (rc != raw.SQLITE_OK)
        {
            statement?.Dispose();
            throw Error(rc);
        }

        return new SqliteStatement(this, statement);
    }

    /// <summary>
    /// Prepares the next statement of a script and moves <paramref name="sql"/> past it. Null once only
    /// whitespace and comments are left.
    /// </summary>
    public SqliteStatement? PrepareNext(ref string sql)
    {
        while (!string.IsNullOrWhiteSpace(sql))
        {
            ThrowIfExpired();

            var rc = raw.sqlite3_prepare_v2(Handle, sql, out var statement, out string tail);
            if (rc != raw.SQLITE_OK)
            {
                statement?.Dispose();
                throw Error(rc);
            }

            // A stray semicolon or a trailing comment compiles to nothing, and is skipped rather than
            // stepped. The length check is only a guard against a tail that did not move.
            var consumed = tail is null || tail.Length < sql.Length;
            sql = tail ?? "";

            if (statement is not null && !statement.IsInvalid)
                return new SqliteStatement(this, statement);

            statement?.Dispose();
            if (!consumed)
                break;
        }

        return null;
    }

    /// <summary>Runs a statement that returns nothing worth reading.</summary>
    public void Execute(string sql)
    {
        using var statement = Prepare(sql);
        while (statement.Step())
        {
        }
    }

    internal void ThrowIfExpired()
    {
        if (_deadline.IsCancellationRequested)
            throw new OperationCanceledException();
    }

    internal Exception Error(int rc)
    {
        var code = rc & 0xFF;

        // An interrupted statement is the deadline, whatever step it happened to be caught at.
        if (code == raw.SQLITE_INTERRUPT)
            return new OperationCanceledException();

        return new SqliteError(code, raw.sqlite3_errmsg(Handle).utf8_to_string() ?? raw.sqlite3_errstr(rc).utf8_to_string() ?? "");
    }

    public void Dispose()
    {
        // The registration first: disposing it waits out an interrupt already in flight, so the handle
        // is never interrupted after it has been closed.
        _interrupt.Dispose();
        _deadline.Dispose();
        Handle.Dispose();
    }
}

/// <summary>One prepared statement. Every step is held to its connection's deadline.</summary>
internal sealed class SqliteStatement : IDisposable
{
    private readonly SqliteDatabase _database;
    private readonly sqlite3_stmt _handle;

    internal SqliteStatement(SqliteDatabase database, sqlite3_stmt handle)
    {
        _database = database;
        _handle = handle;
    }

    public int ColumnCount => raw.sqlite3_column_count(_handle);

    /// <summary>Whether the statement cannot change the file - which is how a script tells its writes from its reads.</summary>
    public bool IsReadOnly => raw.sqlite3_stmt_readonly(_handle) != 0;

    /// <returns>True with a row to read, false once the statement has finished.</returns>
    /// <exception cref="SqliteError">The statement failed - a constraint, a lock, a type in a STRICT table.</exception>
    /// <exception cref="OperationCanceledException">The deadline passed.</exception>
    public bool Step()
    {
        _database.ThrowIfExpired();

        var rc = raw.sqlite3_step(_handle);
        return rc switch
        {
            raw.SQLITE_ROW => true,
            raw.SQLITE_DONE => false,
            _ => throw _database.Error(rc)
        };
    }

    /// <summary>Binds text, or SQL NULL for null - which SQLite then applies the column's affinity to.</summary>
    public void Bind(string name, string? value)
    {
        var index = ParameterIndex(name);
        Check(value is null
            ? raw.sqlite3_bind_null(_handle, index)
            : raw.sqlite3_bind_text(_handle, index, value));
    }

    public void Bind(string name, long value)
        => Check(raw.sqlite3_bind_int64(_handle, ParameterIndex(name), value));

    public string ColumnName(int index) => raw.sqlite3_column_name(_handle, index).utf8_to_string() ?? "";

    public int ColumnType(int index) => raw.sqlite3_column_type(_handle, index);

    public bool IsNull(int index) => ColumnType(index) == raw.SQLITE_NULL;

    public long GetInt64(int index) => raw.sqlite3_column_int64(_handle, index);

    public int GetInt32(int index) => raw.sqlite3_column_int(_handle, index);

    public bool GetBoolean(int index) => GetInt64(index) != 0;

    public double GetDouble(int index) => raw.sqlite3_column_double(_handle, index);

    public string GetString(int index) => raw.sqlite3_column_text(_handle, index).utf8_to_string() ?? "";

    public ReadOnlySpan<byte> GetBlob(int index) => raw.sqlite3_column_blob(_handle, index);

    private int ParameterIndex(string name)
    {
        var index = raw.sqlite3_bind_parameter_index(_handle, name);
        return index > 0 ? index : throw new ArgumentException($"The statement has no parameter called '{name}'.", nameof(name));
    }

    private void Check(int rc)
    {
        if (rc != raw.SQLITE_OK)
            throw _database.Error(rc);
    }

    public void Dispose() => _handle.Dispose();
}

/// <summary>
/// SQLite refused a statement: it did not compile, broke a constraint, or was not authorized. Worded the
/// way Microsoft.Data.Sqlite words it, so an error reads the same as it always has.
/// </summary>
internal sealed class SqliteError(int code, string message) : Exception($"SQLite Error {code}: '{message}'.")
{
    /// <summary>The primary result code - <c>SQLITE_AUTH</c>, <c>SQLITE_NOTADB</c> and so on.</summary>
    public int Code { get; } = code;
}
