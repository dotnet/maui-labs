using System.Text.Json.Serialization;

namespace Microsoft.Maui.AI.GenerativeUI.Composition;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(ComponentLayoutDocument))]
[JsonSerializable(typeof(AdaptiveSurfaceCompositionRequest))]
[JsonSerializable(typeof(AdaptiveSurfaceContext))]
[JsonSerializable(typeof(AdaptiveComponentCatalogEntry[]))]
[JsonSerializable(typeof(AdaptiveDataDescriptor[]))]
public partial class ComponentLayoutJsonContext : JsonSerializerContext;
