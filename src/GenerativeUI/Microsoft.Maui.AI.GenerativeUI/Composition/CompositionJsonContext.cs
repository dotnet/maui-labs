using System.Text.Json.Serialization;

namespace Microsoft.Maui.AI.GenerativeUI.Composition;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(CompositionPlan))]
internal partial class CompositionJsonContext : JsonSerializerContext;
