using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Microsoft.Maui.DevFlow.Driver;

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AgentStatus))]
[JsonSerializable(typeof(ElementInfo))]
[JsonSerializable(typeof(List<ElementInfo>))]
[JsonSerializable(typeof(NetworkRequest))]
[JsonSerializable(typeof(List<NetworkRequest>))]
[JsonSerializable(typeof(ProfilerCapabilities))]
[JsonSerializable(typeof(ProfilerSessionInfo))]
[JsonSerializable(typeof(ProfilerBatch))]
[JsonSerializable(typeof(List<ProfilerHotspot>))]
[JsonSerializable(typeof(RecordingState))]
[JsonSerializable(typeof(AgentClient.ProfilerSessionEnvelope))]
[JsonSerializable(typeof(AgentClient.ActionResponse))]
[JsonSerializable(typeof(InvokeResult))]
internal sealed partial class DevFlowDriverJsonContext : JsonSerializerContext;

internal static class DriverJson
{
    private static readonly JsonSerializerOptions s_nodeIndentedOptions = new()
    {
        WriteIndented = true
    };

    private static readonly JsonSerializerOptions s_nodeCompactOptions = new()
    {
        WriteIndented = false
    };

    public static T? Deserialize<T>(string json) where T : class
        => (T?)JsonSerializer.Deserialize(json, typeof(T), DevFlowDriverJsonContext.Default);

    public static string SerializeUntyped(object? value, bool indented = false)
    {
        if (value is null)
            return "null";

        return value switch
        {
            JsonNode node => node.ToJsonString(indented ? s_nodeIndentedOptions : s_nodeCompactOptions),
            JsonElement element => PrettyPrint(element, indented),
            JsonDocument document => PrettyPrint(document.RootElement, indented),
            _ => SerializeTyped(value, indented),
        };
    }

    public static StringContent CreateJsonContent(JsonNode body)
        => new(SerializeUntyped(body), Encoding.UTF8, "application/json");

    public static JsonElement ParseElement(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    public static string PrettyPrint(string json)
    {
        using var document = JsonDocument.Parse(json);
        return PrettyPrint(document.RootElement, indented: true);
    }

    public static string PrettyPrint(JsonElement element, bool indented = true)
    {
        if (!indented)
            return element.GetRawText();

        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        element.WriteTo(writer);
        writer.Flush();
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string SerializeTyped(object value, bool indented)
    {
        var json = JsonSerializer.Serialize(value, value.GetType(), DevFlowDriverJsonContext.Default);
        return indented ? PrettyPrint(json) : json;
    }
}
