using System.Text.Json.Serialization;

namespace Microsoft.Maui.DevFlow.Agent.Core;

public partial class MauiDevFlowAgentService
{
    [JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
    [JsonSerializable(typeof(LayoutInspectionRequest))]
    [JsonSerializable(typeof(LayoutInspectionResult))]
    [JsonSerializable(typeof(LayoutRuleCatalog))]
    [JsonSerializable(typeof(ThemeInfoPayload))]
    private partial class MauiAgentJsonContext : JsonSerializerContext;
}
