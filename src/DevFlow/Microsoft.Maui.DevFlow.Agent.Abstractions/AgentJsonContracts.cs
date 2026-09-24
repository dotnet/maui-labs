using System.Text.Json.Serialization;

namespace Microsoft.Maui.DevFlow.Agent.Core;

// The wire shapes for the storage and SQLite routes, as types rather than anonymous objects, so
// every one of them is serialized by the source generator in AgentJson rather than by reflecting
// over it at runtime. Names are camel-cased by the context's options - see AgentJson.

// ── Storage: roots and files ──

public sealed class StorageRootsResponse
{
    public StorageRootDescriptor[] Roots { get; set; } = [];
}

public sealed class StorageRootDescriptor
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Kind { get; set; } = "";
    public bool IsWritable { get; set; }
    public bool IsReadOnly { get; set; }
    public bool IsPersistent { get; set; }
    public bool IsBackedUp { get; set; }
    public bool MayBeClearedBySystem { get; set; }
    public bool IsUserVisible { get; set; }
    public string[] SupportedOperations { get; set; } = [];
}

public sealed class FileListResponse
{
    public string Root { get; set; } = "";
    public string Path { get; set; } = "";
    public FileEntryDescriptor[] Entries { get; set; } = [];
}

public sealed class FileEntryDescriptor
{
    public string Name { get; set; } = "";

    /// <summary>Root-relative, forward-slashed, so a client need not rebuild it from the parent.</summary>
    public string Path { get; set; } = "";

    /// <summary>"file" or "directory".</summary>
    public string Type { get; set; } = "";

    /// <summary>Always present, and zero for a directory - a client reading a number should get one.</summary>
    public long Size { get; set; }

    public string LastModified { get; set; } = "";
}

public sealed class FileContentResponse
{
    public string Root { get; set; } = "";
    public string Path { get; set; } = "";
    public long Size { get; set; }
    public string LastModified { get; set; } = "";
    public string ContentBase64 { get; set; } = "";
}

public sealed class FileWriteResponse
{
    public bool Success { get; set; }
    public string Root { get; set; } = "";
    public string Path { get; set; } = "";
    public long Size { get; set; }
    public string LastModified { get; set; } = "";
}

public sealed class FileRemovedResponse
{
    public bool Success { get; set; }
    public string Root { get; set; } = "";
    public string Path { get; set; } = "";
    public string Message { get; set; } = "";
}

public sealed class DirectoryCreatedResponse
{
    public bool Success { get; set; }
    public string Root { get; set; } = "";
    public string Path { get; set; } = "";

    /// <summary>False when the directory was already there, which is a success but not a change.</summary>
    public bool Created { get; set; }
}

public sealed class FileMovedResponse
{
    public bool Success { get; set; }
    public string Root { get; set; } = "";
    public string From { get; set; } = "";
    public string Path { get; set; } = "";
    public string Type { get; set; } = "";
}

// ── Storage: request bodies ──

public sealed class FileMoveRequest
{
    public string? From { get; set; }
    public string? To { get; set; }
    public bool Overwrite { get; set; }
}

// ── SQLite ──

public sealed class SqliteSchemaResponse
{
    public SqliteTableDescriptor[] Tables { get; set; } = [];
    public long FileSize { get; set; }
    public string SqliteVersion { get; set; } = "";
}

/// <param name="Kind">"table" or "view" - the two things sqlite_master holds that can be selected from.</param>
public sealed class SqliteTableDescriptor
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public SqliteColumnDescriptor[] Columns { get; set; } = [];
    public SqliteIndexDescriptor[] Indexes { get; set; } = [];

    /// <summary>
    /// Whether a row in this table can be named for editing. A view has no rowid, and neither does a
    /// WITHOUT ROWID table, so for those the client can only show what is there - and it is told
    /// which it is looking at rather than finding out by having a save fail.
    /// </summary>
    public bool HasRowId { get; set; }
}

public sealed class SqliteColumnDescriptor
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public bool NotNull { get; set; }
    public bool PrimaryKey { get; set; }
}

public sealed class SqliteIndexDescriptor
{
    public string Name { get; set; } = "";

    /// <summary>In index order. An expression index reads "(expression)" for that position.</summary>
    public string[] Columns { get; set; } = [];

    public bool Unique { get; set; }

    /// <summary>SQLite made it for a constraint rather than a CREATE INDEX. It cannot be dropped.</summary>
    public bool Automatic { get; set; }

    /// <summary>It has a WHERE clause, so it covers only some of the rows.</summary>
    public bool Partial { get; set; }
}

/// <summary>
/// What one statement did.
/// </summary>
/// <remarks>
/// <see cref="Error"/> is a field rather than a thrown exception, and that is the whole shape of
/// this type. A syntax error is not the agent failing - it is the normal outcome of typing SQL, and
/// it happens more often than success does while a query is being written.
/// </remarks>
public sealed class SqliteQueryResponse
{
    public string[] Columns { get; set; } = [];

    /// <summary>
    /// Every value is a string, including the numbers - what the pane does with a cell is show it.
    /// Null stays null, because "no value" and the word "NULL" are different cells.
    /// </summary>
    public string?[][] Rows { get; set; } = [];

    /// <summary>What a write did. -1 when the statement was a query rather than a change.</summary>
    public int RowsAffected { get; set; }

    public bool Truncated { get; set; }
    public long ElapsedMs { get; set; }
    public string? Error { get; set; }
}

/// <summary>One table's rows, each carrying the rowid that names it.</summary>
public sealed class SqliteRowsResponse
{
    public string[] Columns { get; set; } = [];

    /// <summary>Parallel to <see cref="Rows"/>. Empty when the table has no rowid to address by.</summary>
    public long[] RowIds { get; set; } = [];

    public string?[][] Rows { get; set; } = [];
    public bool Truncated { get; set; }
    public long ElapsedMs { get; set; }
    public string? Error { get; set; }
}

public sealed class SqliteCreatedResponse
{
    public bool Success { get; set; }
    public string Root { get; set; } = "";
    public string Path { get; set; } = "";
    public long Size { get; set; }
}

// ── SQLite: request bodies ──

public class SqliteRequestBody
{
    public string? Path { get; set; }
}

public sealed class SqliteQueryBody : SqliteRequestBody
{
    public string? Sql { get; set; }
    public int? MaxRows { get; set; }
}

public sealed class SqliteRowBody : SqliteRequestBody
{
    public string? Table { get; set; }
    public long? RowId { get; set; }
    public SqliteCellBody[]? Values { get; set; }
}

public sealed class SqliteCellBody
{
    public string? Column { get; set; }

    /// <summary>Null is SQL NULL, which is why this is not simply an empty string.</summary>
    public string? Value { get; set; }
}

// ── Generic response shapes ──

public sealed class SuccessResponse
{
    public bool Success { get; set; } = true;
    public string? Message { get; set; }
}

public sealed class ErrorResponse
{
    public bool Success { get; set; }
    public string Error { get; set; } = "";
    public string? Reason { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(StorageRootsResponse))]
[JsonSerializable(typeof(FileListResponse))]
[JsonSerializable(typeof(FileContentResponse))]
[JsonSerializable(typeof(FileWriteResponse))]
[JsonSerializable(typeof(FileRemovedResponse))]
[JsonSerializable(typeof(DirectoryCreatedResponse))]
[JsonSerializable(typeof(FileMovedResponse))]
[JsonSerializable(typeof(FileUploadRequest))]
[JsonSerializable(typeof(FileMoveRequest))]
[JsonSerializable(typeof(SqliteSchemaResponse))]
[JsonSerializable(typeof(SqliteQueryResponse))]
[JsonSerializable(typeof(SqliteRowsResponse))]
[JsonSerializable(typeof(SqliteCreatedResponse))]
[JsonSerializable(typeof(SqliteQueryBody))]
[JsonSerializable(typeof(SqliteRowBody))]
[JsonSerializable(typeof(SqliteRequestBody))]
[JsonSerializable(typeof(SuccessResponse))]
[JsonSerializable(typeof(ErrorResponse))]
public sealed partial class AgentJsonContext : JsonSerializerContext;
