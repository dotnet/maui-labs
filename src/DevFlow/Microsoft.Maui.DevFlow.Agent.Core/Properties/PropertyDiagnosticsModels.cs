using System.Text.Json.Serialization;

namespace Microsoft.Maui.DevFlow.Agent.Core.Properties;

public static class PropertyValueSources
{
    public const string Unknown = "unknown";
    public const string Default = "default";
    public const string Local = "local";
    public const string Binding = "binding";
    public const string DynamicResource = "dynamicResource";
    public const string Style = "style";
    public const string Trigger = "trigger";
    public const string Handler = "handler";
    public const string Clr = "clr";
}

public static class PropertyMutationSafetyKinds
{
    public const string Safe = "safe";
    public const string BindingWouldBeReplaced = "bindingWouldBeReplaced";
    public const string DynamicResourceWouldBeReplaced = "dynamicResourceWouldBeReplaced";
    public const string Unknown = "unknown";
}

public static class PropertyDiagnosticConfidenceKinds
{
    public const string Runtime = "runtime";
    public const string Heuristic = "heuristic";
    public const string Unknown = "unknown";
}

public sealed class ElementPropertyDescriptorSet
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("properties")]
    public List<ElementPropertyDescriptor> Properties { get; set; } = new();
}

public sealed class ElementPropertyDescriptor
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "";

    [JsonPropertyName("value")]
    public string? Value { get; set; }

    [JsonPropertyName("writable")]
    public bool Writable { get; set; }

    [JsonPropertyName("forceWritable")]
    public bool ForceWritable { get; set; }

    [JsonPropertyName("choices")]
    public string[]? Choices { get; set; }

    [JsonPropertyName("min")]
    public double? Min { get; set; }

    [JsonPropertyName("max")]
    public double? Max { get; set; }

    [JsonPropertyName("step")]
    public double? Step { get; set; }

    [JsonPropertyName("valueSource")]
    public string ValueSource { get; set; } = PropertyValueSources.Unknown;

    [JsonPropertyName("valueSourceConfidence")]
    public string ValueSourceConfidence { get; set; } = PropertyDiagnosticConfidenceKinds.Unknown;

    [JsonPropertyName("mutationSafety")]
    public string MutationSafety { get; set; } = PropertyMutationSafetyKinds.Unknown;

    [JsonPropertyName("mutationWarning")]
    public string? MutationWarning { get; set; }
}

public sealed class PropertyMutationResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("property")]
    public string? Property { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }

    [JsonPropertyName("valueSource")]
    public string ValueSource { get; set; } = PropertyValueSources.Unknown;

    [JsonPropertyName("mutationSafety")]
    public string MutationSafety { get; set; } = PropertyMutationSafetyKinds.Unknown;

    [JsonPropertyName("warning")]
    public string? Warning { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

internal readonly record struct PropertyDiagnosticSnapshot(
    string ValueSource,
    string Confidence,
    string MutationSafety,
    string? Warning)
{
    public bool CanWriteSafely => MutationSafety == PropertyMutationSafetyKinds.Safe;

    public static PropertyDiagnosticSnapshot Safe(string valueSource, string confidence)
        => new(valueSource, confidence, PropertyMutationSafetyKinds.Safe, null);

    public static PropertyDiagnosticSnapshot Unknown(string? warning = null)
        => new(
            PropertyValueSources.Unknown,
            PropertyDiagnosticConfidenceKinds.Unknown,
            PropertyMutationSafetyKinds.Unknown,
            warning ?? "DevFlow cannot prove that this property can be changed without altering its value source.");
}
