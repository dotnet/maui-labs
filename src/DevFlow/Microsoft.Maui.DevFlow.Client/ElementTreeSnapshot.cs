using System.Text.Json.Serialization;

namespace Microsoft.Maui.DevFlow.Driver;

public sealed class ElementTreeSnapshot
{
    [JsonPropertyName("revision")]
    public string Revision { get; set; } = string.Empty;

    /// <summary>
    /// Opaque revision of observed layout inputs, including sizing changes without geometry changes.
    /// Null when the backend does not provide this signal. Stable across unchanged captures.
    /// </summary>
    [JsonPropertyName("diagnosticInputsRevision")]
    public string? DiagnosticInputsRevision { get; set; }

    [JsonPropertyName("elements")]
    public List<ElementInfo> Elements { get; set; } = [];
}
