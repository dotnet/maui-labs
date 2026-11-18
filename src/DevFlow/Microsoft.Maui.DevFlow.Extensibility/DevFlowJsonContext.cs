using System.Collections;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Microsoft.Maui.DevFlow.Extensibility;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(object))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(byte))]
[JsonSerializable(typeof(sbyte))]
[JsonSerializable(typeof(short))]
[JsonSerializable(typeof(ushort))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(uint))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(ulong))]
[JsonSerializable(typeof(float))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(decimal))]
[JsonSerializable(typeof(char))]
[JsonSerializable(typeof(Half))]
[JsonSerializable(typeof(Int128))]
[JsonSerializable(typeof(UInt128))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(DateTimeOffset))]
[JsonSerializable(typeof(DateOnly))]
[JsonSerializable(typeof(TimeOnly))]
[JsonSerializable(typeof(TimeSpan))]
[JsonSerializable(typeof(Guid))]
[JsonSerializable(typeof(Uri))]
[JsonSerializable(typeof(Version))]
[JsonSerializable(typeof(byte[]))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(JsonDocument))]
[JsonSerializable(typeof(JsonNode))]
[JsonSerializable(typeof(JsonObject))]
[JsonSerializable(typeof(JsonArray))]
[JsonSerializable(typeof(JsonValue))]
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSerializable(typeof(object[]))]
internal partial class DevFlowJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(ReadOnlyDictionary<string, object>))]
[JsonSerializable(typeof(IEnumerable))]
[JsonSerializable(typeof(IDictionary))]
[JsonSerializable(typeof(IDictionary<string, object>))]
[JsonSerializable(typeof(IReadOnlyDictionary<string, object>))]
internal partial class DevFlowCollectionJsonContext : JsonSerializerContext;
