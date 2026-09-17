using System.Text.Json.Serialization;

namespace Microsoft.Maui.DevFlow.Driver;

/// <summary>
/// Result of a structural edit: <see cref="AgentClient.AddElementAsync"/>,
/// <see cref="AgentClient.RemoveElementAsync"/> or <see cref="AgentClient.MoveElementAsync"/>.
/// Failures, including a backend without the <c>ui.edit</c> capability, are reported here rather
/// than thrown.
/// </summary>
public sealed class ElementEditResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    /// <summary>The added or moved element as it now appears in the tree.</summary>
    [JsonPropertyName("element")]
    public ElementInfo? Element { get; set; }

    /// <summary>The removed element's id.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>The parent the element was added to, moved to, or removed from.</summary>
    [JsonPropertyName("parentId")]
    public string? ParentId { get; set; }

    /// <summary>Position among the parent's children; null for single-content parents.</summary>
    [JsonPropertyName("index")]
    public int? Index { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    /// <summary>Machine-readable failure code, e.g. <c>not-a-container</c>, <c>content-occupied</c>, <c>invalid-xaml</c>.</summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonIgnore]
    public int? StatusCode { get; set; }
}

/// <summary>Result of <see cref="AgentClient.ReloadXamlAsync"/>.</summary>
public sealed class XamlReloadResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("className")]
    public string? ClassName { get; set; }

    /// <summary>Number of live instances that were re-inflated.</summary>
    [JsonPropertyName("reloaded")]
    public int Reloaded { get; set; }

    [JsonPropertyName("elementIds")]
    public string[]? ElementIds { get; set; }

    /// <summary>Content hash of the refreshed source map; matches <see cref="ElementInfo.SourceHash"/> after the reload.</summary>
    [JsonPropertyName("sourceHash")]
    public string? SourceHash { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    /// <summary>Parse error position, when the XAML could not be parsed.</summary>
    [JsonPropertyName("details")]
    public XamlErrorLocation? Details { get; set; }

    [JsonIgnore]
    public int? StatusCode { get; set; }
}

public sealed class XamlErrorLocation
{
    [JsonPropertyName("line")]
    public int? Line { get; set; }

    [JsonPropertyName("column")]
    public int? Column { get; set; }
}

/// <summary>An element selected in the app through pick mode.</summary>
public sealed record PickedElement(string ElementId, string? ElementType);
