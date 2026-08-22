using System.Text.Json.Serialization;

namespace Microsoft.Maui.AI.GenerativeUI.Composition;

/// <summary>
/// A flat, non-recursive adaptive layout document authored by an AI model.
/// </summary>
public sealed record ComponentLayoutDocument
{
    public required string LayoutId { get; init; }

    public required int Revision { get; init; }

    public required string Surface { get; init; }

    public string? Explanation { get; init; }

    public required IReadOnlyList<AdaptiveRegionPlan> Regions { get; init; }

    public required IReadOnlyList<ComponentLayoutNode> Nodes { get; init; }
}

/// <summary>
/// Associates a named application-owned region with one root layout node.
/// </summary>
public sealed record AdaptiveRegionPlan
{
    public required string Region { get; init; }

    public required string RootNodeId { get; init; }
}

/// <summary>
/// A single layout or registered whole-component node in a flat node table.
/// </summary>
public sealed record ComponentLayoutNode
{
    public required string Id { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter<ComponentLayoutNodeKind>))]
    public required ComponentLayoutNodeKind Kind { get; init; }

    public string? ParentId { get; init; }

    public required int Order { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter<AdaptiveStackOrientation>))]
    public AdaptiveStackOrientation? Orientation { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter<AdaptiveGridPreset>))]
    public AdaptiveGridPreset? GridPreset { get; init; }

    public string? Title { get; init; }

    public string? Component { get; init; }

    public string? DataPath { get; init; }

    public string? Variant { get; init; }

    public required string Reason { get; init; }
}

public enum ComponentLayoutNodeKind
{
    Stack,
    Grid,
    Tabs,
    Section,
    Component,
}

public enum AdaptiveStackOrientation
{
    Vertical,
    Horizontal,
}

public enum AdaptiveGridPreset
{
    SingleColumn,
    TwoEqualColumns,
    PrimaryWithSidebar,
    SidebarWithPrimary,
}
