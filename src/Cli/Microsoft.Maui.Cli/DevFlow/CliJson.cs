using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Microsoft.Maui.Cli.DevFlow;

internal static class CliJson
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
        => (T?)JsonSerializer.Deserialize(json, typeof(T), DevFlowCliJsonContext.Default);

    public static string SerializeUntyped(object? value, bool indented = true)
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

    public static JsonElement ParseElement(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    public static JsonNode? ParseNode(string json) => JsonNode.Parse(json);

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
        var json = JsonSerializer.Serialize(value, value.GetType(), DevFlowCliJsonContext.Default);
        return indented ? PrettyPrint(json) : json;
    }
}
