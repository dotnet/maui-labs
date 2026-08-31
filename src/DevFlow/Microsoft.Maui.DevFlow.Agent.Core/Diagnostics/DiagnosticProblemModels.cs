using System.Text.Json.Serialization;

namespace Microsoft.Maui.DevFlow.Agent.Core.Diagnostics;

public sealed class DiagnosticProblem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "";

    [JsonPropertyName("severity")]
    public string Severity { get; set; } = "warning";

    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("count")]
    public int Count { get; set; } = 1;

    [JsonPropertyName("firstSeenUtc")]
    public DateTime FirstSeenUtc { get; set; }

    [JsonPropertyName("lastSeenUtc")]
    public DateTime LastSeenUtc { get; set; }

    [JsonPropertyName("elementId")]
    public string? ElementId { get; set; }

    [JsonPropertyName("elementType")]
    public string? ElementType { get; set; }

    [JsonPropertyName("property")]
    public string? Property { get; set; }

    [JsonPropertyName("bindingType")]
    public string? BindingType { get; set; }

    [JsonPropertyName("bindingPath")]
    public string? BindingPath { get; set; }

    [JsonPropertyName("bindingMode")]
    public string? BindingMode { get; set; }

    [JsonPropertyName("sourceType")]
    public string? SourceType { get; set; }

    [JsonPropertyName("converterType")]
    public string? ConverterType { get; set; }

    [JsonPropertyName("sourceFile")]
    public string? SourceFile { get; set; }

    [JsonPropertyName("sourceLine")]
    public int? SourceLine { get; set; }

    [JsonPropertyName("sourceColumn")]
    public int? SourceColumn { get; set; }
}

public sealed class DiagnosticProblemBatch
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("revision")]
    public long Revision { get; set; }

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("evicted")]
    public long Evicted { get; set; }

    [JsonPropertyName("problems")]
    public List<DiagnosticProblem> Problems { get; set; } = new();
}
