using System.Collections.Generic;

namespace Microsoft.Maui.AI.Indexer.Generators.Models;

/// <summary>Represents a single node in the semantic UI tree.</summary>
internal sealed class SemanticNode
{
    public string TypeName { get; set; } = "";
    public string? Text { get; set; }
    public string? Placeholder { get; set; }
    public string? Source { get; set; }
    public BindingInfo? TextBinding { get; set; }
    public string? CommandName { get; set; }
    public string? CommandParameter { get; set; }
    public SemanticInfo Semantics { get; set; } = new();
    public List<SemanticNode> Children { get; set; } = new();
    public VisibilityCondition? Condition { get; set; }

    // Cross-file user control reference
    public bool IsUserControlReference { get; set; }

    // Shell navigation: the page class this ShellContent/Tab hosts (from ContentTemplate),
    // and whether it is the app's entry/home screen (first ShellContent in the Shell).
    public string? NavigationTarget { get; set; }
    public bool IsEntry { get; set; }

    // Condition group (structural container with visibility condition wrapping children)
    public bool IsConditionGroup { get; set; }

    // Collection-specific
    public string? ItemsSourceBinding { get; set; }
    public bool IsGrouped { get; set; }
    public List<SemanticNode>? HeaderTemplate { get; set; }
    public List<SemanticNode>? FooterTemplate { get; set; }
    public List<SemanticNode>? ItemTemplate { get; set; }
    public List<SemanticNode>? GroupHeaderTemplate { get; set; }
    public List<SemanticNode>? GroupFooterTemplate { get; set; }
    public List<SemanticNode>? EmptyView { get; set; }
    public List<TemplateVariant>? TemplateVariants { get; set; }

    // BindableLayout
    public bool IsBindableLayout { get; set; }
    public string? BindableLayoutItemsSource { get; set; }
    public List<SemanticNode>? BindableLayoutItemTemplate { get; set; }

    // Slider-specific
    public string? Minimum { get; set; }
    public string? Maximum { get; set; }
    public string? ValueBinding { get; set; }

    // Picker-specific
    public string? Title { get; set; }
    public string? SelectedItemBinding { get; set; }
}
