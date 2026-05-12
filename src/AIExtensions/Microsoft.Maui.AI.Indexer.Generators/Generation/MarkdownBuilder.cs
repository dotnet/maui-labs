using System.Collections.Generic;
using System.Text;
using Microsoft.Maui.AI.Indexer.Generators.Models;

namespace Microsoft.Maui.AI.Indexer.Generators.Generation;

/// <summary>Builds markdown strings from the semantic UI model.</summary>
internal sealed class MarkdownBuilder
{
    private readonly StringBuilder _sb = new();
    private int _indent;

    public void AppendLine(string text = "")
    {
        if (text.Length > 0)
        {
            _sb.Append(new string(' ', _indent * 2));
            _sb.AppendLine(text);
        }
        else
        {
            _sb.AppendLine();
        }
    }

    public void AppendHeading(int level, string text)
    {
        _sb.Append(new string('#', level));
        _sb.Append(' ');
        _sb.AppendLine(text);
    }

    public void Indent() => _indent++;
    public void Dedent() => _indent = System.Math.Max(0, _indent - 1);

    public override string ToString() => _sb.ToString();

    /// <summary>Render a single UI element as a markdown list item.</summary>
    public void RenderElement(UiElement el)
    {
        // Determine the display text
        var displayText = GetElementDisplayText(el);
        var typeName = GetDisplayTypeName(el);
        var annotations = GetAnnotations(el);

        var line = $"- {typeName}{displayText}{annotations}";
        AppendLine(line);
    }

    /// <summary>Render a full page model to markdown.</summary>
    public static string RenderPage(PageModel page)
    {
        var mb = new MarkdownBuilder();

        mb.AppendHeading(1, page.ClassName);
        mb.AppendLine();

        if (page.Route != null)
            mb.AppendLine($"Route: {page.Route}");

        if (!string.IsNullOrEmpty(page.FilePath))
            mb.AppendLine($"File: {page.FilePath}");

        mb.AppendLine();

        RenderElements(mb, page.Elements);

        return mb.ToString().TrimEnd() + "\n";
    }

    public static void RenderElements(MarkdownBuilder mb, List<UiElement> elements)
    {
        foreach (var el in elements)
        {
            RenderElementTree(mb, el);
        }
    }

    private static void RenderElementTree(MarkdownBuilder mb, UiElement el)
    {
        // Handle collection views specially
        if (el.TypeName is "CollectionView" or "ListView" or "CarouselView")
        {
            RenderCollectionView(mb, el);
            return;
        }

        // Handle bindable layouts
        if (el.IsBindableLayout)
        {
            RenderBindableLayout(mb, el);
            return;
        }

        // Handle Shell elements
        if (el.TypeName is "ShellContent" or "Tab")
        {
            RenderShellElement(mb, el);
            return;
        }

        // Regular semantic element
        mb.RenderElement(el);

        // Render children (for promoted containers)
        if (el.Children.Count > 0)
        {
            mb.Indent();
            RenderElements(mb, el.Children);
            mb.Dedent();
        }
    }

    private static void RenderCollectionView(MarkdownBuilder mb, UiElement el)
    {
        var source = el.ItemsSourceBinding != null ? $": \"{{{el.ItemsSourceBinding}}}\"" : "";
        var grouped = el.IsGrouped ? ", grouped" : "";
        var cond = el.Condition != null ? $" [{el.Condition}]" : "";

        mb.AppendLine($"- {el.TypeName}{source}{(grouped.Length > 0 || cond.Length > 0 ? $" [{grouped.TrimStart(',', ' ')}{cond.TrimStart(' ')}]".Replace("[ ", "[").Replace("[]", "") : "")}");

        mb.Indent();

        if (el.HeaderTemplate != null && el.HeaderTemplate.Count > 0)
        {
            mb.AppendLine("- Header:");
            mb.Indent();
            RenderElements(mb, el.HeaderTemplate);
            mb.Dedent();
        }

        if (el.GroupHeaderTemplate != null && el.GroupHeaderTemplate.Count > 0)
        {
            mb.AppendLine("- Group header (each group):");
            mb.Indent();
            RenderElements(mb, el.GroupHeaderTemplate);
            mb.Dedent();
        }

        if (el.ItemTemplate != null && el.ItemTemplate.Count > 0)
        {
            mb.AppendLine("- Each item:");
            mb.Indent();
            RenderElements(mb, el.ItemTemplate);
            mb.Dedent();
        }

        if (el.TemplateVariants != null)
        {
            foreach (var variant in el.TemplateVariants)
            {
                mb.AppendLine($"- Template \"{variant.Name}\":");
                mb.Indent();
                RenderElements(mb, variant.Elements);
                mb.Dedent();
            }
        }

        if (el.GroupFooterTemplate != null && el.GroupFooterTemplate.Count > 0)
        {
            mb.AppendLine("- Group footer (each group):");
            mb.Indent();
            RenderElements(mb, el.GroupFooterTemplate);
            mb.Dedent();
        }

        if (el.FooterTemplate != null && el.FooterTemplate.Count > 0)
        {
            mb.AppendLine("- Footer:");
            mb.Indent();
            RenderElements(mb, el.FooterTemplate);
            mb.Dedent();
        }

        if (el.EmptyView != null && el.EmptyView.Count > 0)
        {
            mb.AppendLine("- Empty view:");
            mb.Indent();
            RenderElements(mb, el.EmptyView);
            mb.Dedent();
        }

        mb.Dedent();
    }

    private static void RenderBindableLayout(MarkdownBuilder mb, UiElement el)
    {
        var source = el.BindableLayoutItemsSource != null ? $" with items from \"{{{el.BindableLayoutItemsSource}}}\"" : "";
        var cond = el.Condition != null ? $" [{el.Condition}]" : "";

        mb.AppendLine($"- {el.TypeName}{source}{cond}:");

        mb.Indent();

        if (el.BindableLayoutItemTemplate != null && el.BindableLayoutItemTemplate.Count > 0)
        {
            mb.AppendLine("- Each item:");
            mb.Indent();
            RenderElements(mb, el.BindableLayoutItemTemplate);
            mb.Dedent();
        }

        mb.Dedent();
    }

    private static void RenderShellElement(MarkdownBuilder mb, UiElement el)
    {
        var route = el.CommandName != null ? $" [route: {el.CommandName}]" : "";
        mb.AppendLine($"- {el.TypeName}: \"{el.Text}\"{route}");

        if (el.Children.Count > 0)
        {
            mb.Indent();
            foreach (var child in el.Children)
            {
                RenderShellElement(mb, child);
            }
            mb.Dedent();
        }
    }

    private string GetDisplayTypeName(UiElement el)
    {
        // If heading level is set, use Heading instead of Label
        if (el.Semantics.HeadingLevel != null)
            return $"Heading (level {el.Semantics.HeadingLevel}): ";

        return el.TypeName switch
        {
            "Label" => "Label: ",
            "Button" => "Button: ",
            "ImageButton" => "ImageButton: ",
            "Entry" => "Entry: ",
            "Editor" => "Editor: ",
            "SearchBar" => "SearchBar: ",
            "Slider" => "Slider: ",
            "Stepper" => "Stepper: ",
            "Switch" => "Switch: ",
            "CheckBox" => "CheckBox: ",
            "RadioButton" => "RadioButton: ",
            "Picker" => "Picker: ",
            "DatePicker" => "DatePicker: ",
            "TimePicker" => "TimePicker: ",
            "Image" => "Image: ",
            "ActivityIndicator" => "ActivityIndicator",
            "ProgressBar" => "ProgressBar: ",
            _ => $"{el.TypeName}: ",
        };
    }

    private string GetElementDisplayText(UiElement el)
    {
        // SemanticProperties.Description overrides everything
        if (el.Semantics.Description != null && el.Semantics.Description.Length > 0)
            return $"\"{el.Semantics.Description}\"";

        // Slider/Stepper special format
        if (el.TypeName is "Slider" or "Stepper")
        {
            var range = $"{el.Minimum}–{el.Maximum}";
            if (el.ValueBinding != null)
                return $"{range} → \"{{{el.ValueBinding}}}\"";
            return range;
        }

        // Picker
        if (el.TypeName == "Picker")
        {
            var title = el.Title != null ? $"\"{el.Title}\"" : "";
            if (el.SelectedItemBinding != null)
                return $"{title} → \"{{{el.SelectedItemBinding}}}\"";
            return title;
        }

        // Entry/Editor — prefer placeholder if no text
        if (el.TypeName is "Entry" or "Editor" or "SearchBar")
        {
            if (el.TextBinding != null)
                return $"\"{el.TextBinding.ToDisplayString()}\"";
            if (el.Placeholder != null)
                return $"placeholder \"{el.Placeholder}\"";
            if (el.Text != null)
                return $"\"{el.Text}\"";
            return "";
        }

        // Image / ImageButton source
        if (el.TypeName is "Image" or "ImageButton")
        {
            if (el.Source != null)
                return $"\"{el.Source}\"";
            return "";
        }

        // Bound text
        if (el.TextBinding != null)
            return $"\"{el.TextBinding.ToDisplayString()}\"";

        // Literal text
        if (el.Text != null)
            return $"\"{el.Text}\"";

        return "";
    }

    private string GetAnnotations(UiElement el)
    {
        var parts = new List<string>();

        // Command
        if (el.CommandName != null)
            parts.Add($"→ {el.CommandName}");

        // Build bracket annotations
        var brackets = new List<string>();

        if (el.Semantics.Hint != null)
            brackets.Add($"hint: {el.Semantics.Hint}");

        if (el.Condition != null)
            brackets.Add(el.Condition.ToString());

        var result = "";
        if (parts.Count > 0)
            result = " " + string.Join(" ", parts);

        if (brackets.Count > 0)
            result += " [" + string.Join(", ", brackets) + "]";

        return result;
    }
}
