using SQLitePCL;

namespace Microsoft.Maui.DevFlow.Agent.Core.Sqlite;

/// <summary>Host-owned SQLite initialization and per-connection configuration.
/// The agent never selects a SQLitePCLRaw provider automatically.</summary>
public sealed class SqliteBrowserOptions
{
    /// <summary>Optional explicit host initialization, called only when no provider is registered.
    /// Must not replace an existing provider.</summary>
    public Action? InitializeProvider { get; set; }

    /// <summary>Configure a new, agent-owned connection before header validation. Set encryption
    /// keys, functions and collations here. Do not return or close an app-owned connection.</summary>
    public Action<SqliteConnectionContext>? ConfigureConnection { get; set; }

    /// <summary>Execution budget, including host configuration. Must be positive and at most
    /// 25 seconds, leaving headroom under the client's 30-second HTTP deadline.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(20);
}

public sealed class SqliteConnectionContext
{
    private readonly SqliteDatabase _database;

    internal SqliteConnectionContext(SqliteDatabase database, string path, bool isReadOnly, bool isNew)
    {
        _database = database;
        Path = path;
        IsReadOnly = isReadOnly;
        IsNew = isNew;
    }

    /// <summary>Agent-owned SQLite handle. Its lifetime ends with this request.</summary>
    public sqlite3 Handle => _database.Handle;
    public string Path { get; }
    public bool IsReadOnly { get; }
    public bool IsNew { get; }
    public void Execute(string sql) => _database.Execute(sql);
}
