using System.Text.Json.Serialization;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Agent.Core;

internal sealed class SuccessResponse
{
    public bool Success { get; set; } = true;
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Message { get; set; }
}

internal sealed class ErrorResponse
{
    public bool Success { get; set; }
    public string Error { get; set; } = "";
    public string? Reason { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
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
[JsonSerializable(typeof(SqliteValue[]))]
[JsonSerializable(typeof(SqlitePageCursor))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(SuccessResponse))]
[JsonSerializable(typeof(ErrorResponse))]
internal sealed partial class StorageAgentJsonContext : JsonSerializerContext;

internal sealed class SqlitePageCursor
{
    public string Database { get; set; } = "";
    public string Table { get; set; } = "";
    public int SchemaVersion { get; set; }
    public string? AfterRowId { get; set; }
    public long Offset { get; set; }
}
