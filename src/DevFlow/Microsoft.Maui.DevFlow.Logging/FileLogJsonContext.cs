using System.Text.Json.Serialization;

namespace Microsoft.Maui.DevFlow.Logging;

[JsonSerializable(typeof(FileLogEntry))]
internal partial class FileLogJsonContext : JsonSerializerContext
{
}
