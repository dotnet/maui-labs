using System.Text.Json.Serialization;
using Microsoft.Maui.AI.GenerativeUI.Composition;

namespace GenerativeUI.Sample.Garden.Components;

public sealed record GardenCompositionToolResult(
    CompositionPlan Plan,
    CompositionPlanSource Source,
    int CorrectionCount,
    CompositionRenderDiff Render);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(GardenCompositionToolResult))]
internal partial class GardenCompositionJsonContext : JsonSerializerContext;
