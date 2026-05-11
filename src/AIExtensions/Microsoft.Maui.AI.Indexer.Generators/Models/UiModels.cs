using System.Collections.Generic;

namespace Microsoft.Maui.AI.Indexer.Generators.Models;

/// <summary>Semantic information extracted from SemanticProperties attached properties.</summary>
internal sealed class SemanticInfo
{
    public string? Description { get; set; }
    public string? Hint { get; set; }
    public int? HeadingLevel { get; set; }
}

/// <summary>Information about a data binding expression.</summary>
internal sealed class BindingInfo
{
    public string? Path { get; set; }
    public string? Mode { get; set; }
    public string? Converter { get; set; }
    public string? StringFormat { get; set; }
    public string? Raw { get; set; }

    public bool IsBound => Path != null;

    public string ToDisplayString()
    {
        if (Path != null) return "{" + Path + "}";
        return Raw ?? "";
    }
}

/// <summary>Represents a single UI element in the semantic tree.</summary>
internal sealed class UiElement
{
    public string TypeName { get; set; } = "";
    public string? Text { get; set; }
    public string? Placeholder { get; set; }
    public string? Source { get; set; }
    public BindingInfo? TextBinding { get; set; }
    public string? CommandName { get; set; }
    public string? CommandParameter { get; set; }
    public SemanticInfo Semantics { get; set; } = new();
    public List<UiElement> Children { get; set; } = new();
    public VisibilityCondition? Condition { get; set; }

    // Collection-specific
    public string? ItemsSourceBinding { get; set; }
    public bool IsGrouped { get; set; }
    public List<UiElement>? HeaderTemplate { get; set; }
    public List<UiElement>? FooterTemplate { get; set; }
    public List<UiElement>? ItemTemplate { get; set; }
    public List<UiElement>? GroupHeaderTemplate { get; set; }
    public List<UiElement>? GroupFooterTemplate { get; set; }
    public List<UiElement>? EmptyView { get; set; }
    public List<TemplateVariant>? TemplateVariants { get; set; }

    // BindableLayout
    public bool IsBindableLayout { get; set; }
    public string? BindableLayoutItemsSource { get; set; }
    public List<UiElement>? BindableLayoutItemTemplate { get; set; }

    // Slider-specific
    public string? Minimum { get; set; }
    public string? Maximum { get; set; }
    public string? ValueBinding { get; set; }

    // Picker-specific
    public string? Title { get; set; }
    public string? SelectedItemBinding { get; set; }
}

/// <summary>A named template variant from a DataTemplateSelector.</summary>
internal sealed class TemplateVariant
{
    public string Name { get; set; } = "";
    public List<UiElement> Elements { get; set; } = new();
}

/// <summary>Condition under which an element is visible.</summary>
internal sealed class VisibilityCondition
{
    public string Property { get; set; } = "";
    public string Value { get; set; } = "true";
    public bool IsInverted { get; set; }

    public override string ToString()
    {
        var val = IsInverted ? "false" : Value;
        return $"visible when {Property} = {val}";
    }
}

/// <summary>Parsed XAML document with semantic tree.</summary>
internal sealed class PageModel
{
    public string ClassName { get; set; } = "";
    public string Namespace { get; set; } = "";
    public string RootType { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string? Route { get; set; }
    public List<UiElement> Elements { get; set; } = new();
}

/// <summary>Aggregate project index across all pages.</summary>
internal sealed class ProjectIndex
{
    public List<PageModel> Pages { get; set; } = new();
}
