using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Maui.DevFlow.Extensibility;

namespace Microsoft.Maui.DevFlow.Agent.Core;

/// <summary>Agent compatibility facade over the shared DevFlow JSON contracts.</summary>
public static class AgentJson
{
    private static readonly JsonSerializerOptions Defaults = new();

    static AgentJson() => DevFlowJson.RegisterContext(DevFlowAgentService.JsonContext);

    public static void RegisterContext(JsonSerializerContext context) => DevFlowJson.RegisterContext(context);

    public static string Serialize(object? value, JsonSerializerOptions? options = null)
        => DevFlowJson.Serialize(value, options ?? Defaults);

    public static JsonElement SerializeToElement(object? value)
        => DevFlowJson.SerializeToElement(value, Defaults);

    public static T? Deserialize<T>(string json, JsonSerializerOptions? options = null)
        => DevFlowJson.Deserialize<T>(json, options ?? Defaults);

    internal static bool IsUnsupportedJson(Exception exception) => DevFlowJson.IsUnsupportedJson(exception);

    internal static bool IsMissingMetadata(Exception exception) => DevFlowJson.IsMissingMetadata(exception);
}
