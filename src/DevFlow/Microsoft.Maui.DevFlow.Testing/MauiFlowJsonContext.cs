using System.Text.Json.Serialization;

namespace Microsoft.Maui.DevFlow.Testing;

/// <summary>Source-generated JSON metadata for executable DevFlow flow contracts.</summary>
[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    PropertyNameCaseInsensitive = true,
    WriteIndented = true)]
[JsonSerializable(typeof(MauiFlow))]
[JsonSerializable(typeof(FlowAssert))]
[JsonSerializable(typeof(FlowSelector))]
[JsonSerializable(typeof(List<FlowAssert>))]
[JsonSerializable(typeof(MauiSelectorEvidence))]
public sealed partial class MauiFlowJsonContext : JsonSerializerContext;
