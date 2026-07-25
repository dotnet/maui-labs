using Fizzler;

namespace Microsoft.Maui.DevFlow.Agent.Core.Css;

/// <summary>
/// Top-level API for running CSS selectors against an ElementInfo tree.
/// Wires the preprocessor, Fizzler parser, and ElementInfoOps together.
/// </summary>
public static class CssSelectorEngine
{
    /// <summary>
    /// Queries the ElementInfo tree using a CSS selector string.
    /// Returns a flat list of matching elements (without their children).
    /// </summary>
    public static List<ElementInfo> Query(List<ElementInfo> tree, string selector)
    {
        // Pre-process MAUI pseudo-classes into synthetic attributes
        var processed = SelectorPreprocessor.Preprocess(selector);

        // Build the element ops adapter from the tree
        var ops = new ElementInfoOps(tree);

        // Parse selector via Fizzler and get compiled selector
        var generator = new SelectorGenerator<ElementInfo>(ops);
        Parser.Parse(processed, generator);

        // Flatten tree to get all candidate elements
        var allElements = Flatten(tree);

        // Execute each selector in the group (comma-separated) and union results
        var results = new List<ElementInfo>();
        var seen = new HashSet<string>();

        foreach (var sel in generator.GetSelectors())
        {
            foreach (var match in sel(allElements))
            {
                if (seen.Add(match.Id))
                {
                    // Return matches without children (flat, like Query())
                    results.Add(StripChildren(match));
                }
            }
        }

        return results;
    }

    static List<ElementInfo> Flatten(List<ElementInfo> tree)
    {
        var result = new List<ElementInfo>();
        foreach (var root in tree)
            FlattenRecursive(root, result);
        return result;
    }

    static void FlattenRecursive(ElementInfo element, List<ElementInfo> result)
    {
        result.Add(element);
        if (element.Children == null) return;
        foreach (var child in element.Children)
            FlattenRecursive(child, result);
    }

    static ElementInfo StripChildren(ElementInfo el) => new()
    {
        Id = el.Id,
        ParentId = el.ParentId,
        Type = el.Type,
        FullType = el.FullType,
        AutomationId = el.AutomationId,
        Text = el.Text,
        Value = el.Value,
        IsVisible = el.IsVisible,
        IsEnabled = el.IsEnabled,
        IsFocused = el.IsFocused,
        Opacity = el.Opacity,
        Bounds = el.Bounds,
        Gestures = el.Gestures,
        StyleClass = el.StyleClass,
        NativeType = el.NativeType,
        NativeProperties = el.NativeProperties,
        Children = null,
    };
}
