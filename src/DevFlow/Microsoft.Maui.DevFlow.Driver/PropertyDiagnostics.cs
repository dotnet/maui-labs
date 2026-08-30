using System.Text.Json.Serialization;

namespace Microsoft.Maui.DevFlow.Driver;

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
    public string ValueSource { get; set; } = "unknown";

    [JsonPropertyName("valueSourceConfidence")]
    public string ValueSourceConfidence { get; set; } = "unknown";

    [JsonPropertyName("mutationSafety")]
    public string MutationSafety { get; set; } = "unknown";

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
    public string ValueSource { get; set; } = "unknown";

    [JsonPropertyName("mutationSafety")]
    public string MutationSafety { get; set; } = "unknown";

    [JsonPropertyName("warning")]
    public string? Warning { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}
