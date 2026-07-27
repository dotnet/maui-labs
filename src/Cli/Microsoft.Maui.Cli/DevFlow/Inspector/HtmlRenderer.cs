using System.Globalization;
using System.Reflection;
using System.Text;
using System.Web;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.Cli.DevFlow.Inspector;

/// <summary>
/// Generates an interactive HTML page from the DevFlow visual tree.
/// Uses inspector.html as a template and injects the element tree.
/// Each element becomes a positioned div with data-* attributes matching
/// the DevFlow ElementInfo property names (camelCase).
/// </summary>
public static class HtmlRenderer
{
    private static readonly Lazy<string> _templateCache = new(LoadTemplate, LazyThreadSafetyMode.ExecutionAndPublication);

    public static string Render(
        List<ElementInfo> tree,
        bool hasScreenshot,
        int screenshotWidth = 0,
        int screenshotHeight = 0,
        double density = 1,
        double elementScale = 1,
        double rootOffsetX = 0,
        double rootOffsetY = 0,
        string screenshotUrl = "screenshot.png")
    {
        var template = _templateCache.Value;
        var (viewportWidth, viewportHeight) = ComputeViewportSize(tree, screenshotWidth, screenshotHeight);

        // Build the elements HTML (flat list — all elements use window-absolute bounds,
        // shifted by rootOffset so they align with the root page screenshot)
        var elementsHtml = RenderElements(tree, elementScale, rootOffsetX, rootOffsetY);

        // Build screenshot tag
        var screenshotHtml = hasScreenshot
            ? $"<img id=\"screenshot\" src=\"{Escape(screenshotUrl)}\" alt=\"App screenshot\">"
            : "";

        // Replace scalar placeholders first (all produce known-safe, non-user
        // strings). Then splice in {{ELEMENTS}} via StringBuilder so that any
        // "{{VIEWPORT_WIDTH}}" or similar text embedded in element attributes
        // (e.g., a Label whose title literally contains "{{DENSITY}}") is
        // never substituted — the element HTML is only concatenated in after
        // all Replace() calls have completed. This makes the ordering
        // invariant explicit and resistant to a future refactor that
        // reorders these replacements.
        var scalarsReplaced = template
            .Replace("{{VIEWPORT_WIDTH}}", viewportWidth.ToString("F0", CultureInfo.InvariantCulture))
            .Replace("{{VIEWPORT_HEIGHT}}", viewportHeight.ToString("F0", CultureInfo.InvariantCulture))
            .Replace("{{DENSITY}}", density.ToString("F1", CultureInfo.InvariantCulture))
            .Replace("{{ELEMENT_SCALE}}", elementScale.ToString("F4", CultureInfo.InvariantCulture))
            .Replace("{{ROOT_OFFSET_X}}", rootOffsetX.ToString("F4", CultureInfo.InvariantCulture))
            .Replace("{{ROOT_OFFSET_Y}}", rootOffsetY.ToString("F4", CultureInfo.InvariantCulture))
            .Replace("{{SCREENSHOT}}", screenshotHtml);

        const string ElementsMarker = "{{ELEMENTS}}";
        var markerIndex = scalarsReplaced.IndexOf(ElementsMarker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            // Marker missing from template — return as-is so the page still renders.
            return scalarsReplaced;
        }

        var sb = new StringBuilder(scalarsReplaced.Length + elementsHtml.Length);
        sb.Append(scalarsReplaced, 0, markerIndex);
        sb.Append(elementsHtml);
        sb.Append(scalarsReplaced, markerIndex + ElementsMarker.Length, scalarsReplaced.Length - markerIndex - ElementsMarker.Length);

        return sb.ToString();
    }

    /// <summary>
    /// Renders just the element divs (no template wrapping) for AJAX state updates.
    /// When rootOffsetX/Y are provided, element positions are shifted so that the
    /// root page's window origin aligns with the screenshot origin (coordinate-space fix
    /// for modals, sheets, and safe-area offsets).
    /// </summary>
    public static string RenderElements(List<ElementInfo> tree, double elementScale = 1, double rootOffsetX = 0, double rootOffsetY = 0)
    {
        var sb = new StringBuilder();
        // Drop inactive Shell tab/flyout pages (mounted but not on screen) so the overlay + visual
        // tree reflect only the displayed page instead of stacking every page's elements.
        var dropIds = PageFilter.InactivePageIds(tree);
        foreach (var element in tree)
        {
            RenderElementsFlat(sb, element, elementScale, rootOffsetX, rootOffsetY, parentRenderedId: null, dropIds);
        }
        return sb.ToString();
    }

    private static (double width, double height) ComputeViewportSize(List<ElementInfo> tree, int screenshotWidth, int screenshotHeight)
    {
        if (screenshotWidth > 0 && screenshotHeight > 0)
            return (screenshotWidth, screenshotHeight);

        var rootBounds = tree.Count > 0 ? tree[0].Bounds : null;
        return (
            rootBounds is { Width: > 0 } ? rootBounds.Width : 800,
            rootBounds is { Height: > 0 } ? rootBounds.Height : 600
        );
    }

    private static string LoadTemplate()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = "Microsoft.Maui.Cli.DevFlow.Inspector.Web.inspector.html";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource not found: {resourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Renders all elements as flat siblings (no nesting) using window-absolute bounds.
    /// <paramref name="parentRenderedId"/> is the id of the nearest ANCESTOR that actually
    /// produced a div (elements with no meaningful bounds are skipped). Threading it lets the
    /// client rebuild the visual hierarchy from the flat div list via data-parentId without
    /// orphaning children of a skipped parent.
    /// </summary>
    private static void RenderElementsFlat(StringBuilder sb, ElementInfo element, double scale, double rootOffsetX, double rootOffsetY, string? parentRenderedId, HashSet<string> dropIds)
    {
        // Skip an inactive Shell page (or its exclusive wrapper) and its entire subtree — it's mounted
        // but not on screen, so rendering it would stack a stale page's elements over the live one.
        if (element.Id is { Length: > 0 } id && dropIds.Contains(id))
            return;

        var rendered = RenderSingleElement(sb, element, scale, rootOffsetX, rootOffsetY, parentRenderedId);
        // Children attach to this element if it rendered a div, otherwise to the nearest
        // rendered ancestor (so a bounds-less container doesn't break the tree).
        var childParentId = rendered ? element.Id : parentRenderedId;
        if (element.Children != null)
        {
            foreach (var child in element.Children)
            {
                RenderElementsFlat(sb, child, scale, rootOffsetX, rootOffsetY, childParentId, dropIds);
            }
        }
    }

    private static bool RenderSingleElement(StringBuilder sb, ElementInfo element, double scale, double rootOffsetX, double rootOffsetY, string? parentRenderedId)
    {
        // Build style for positioning using window-absolute bounds, adjusted by the
        // root page offset so overlays align with the per-page screenshot.
        var bounds = element.WindowBounds ?? element.Bounds;
        if (bounds == null || (bounds.Width <= 0 && bounds.Height <= 0))
            return false; // Skip elements with no meaningful bounds

        var left = (bounds.X - rootOffsetX) * scale;
        var top = (bounds.Y - rootOffsetY) * scale;
        var style = string.Create(CultureInfo.InvariantCulture,
            $"position:absolute;left:{left:F0}px;top:{top:F0}px;width:{bounds.Width * scale:F0}px;height:{bounds.Height * scale:F0}px;");

        // Build data attributes
        var attrs = new StringBuilder();
        attrs.Append($" data-id=\"{Escape(element.Id)}\"");
        if (!string.IsNullOrEmpty(parentRenderedId))
            attrs.Append($" data-parentId=\"{Escape(parentRenderedId)}\"");
        attrs.Append($" data-type=\"{Escape(element.Type)}\"");

        if (!string.IsNullOrEmpty(element.FullType))
            attrs.Append($" data-fullType=\"{Escape(element.FullType)}\"");
        if (!string.IsNullOrEmpty(element.Framework))
            attrs.Append($" data-framework=\"{Escape(element.Framework)}\"");
        if (!string.IsNullOrEmpty(element.AutomationId))
            attrs.Append($" data-automationId=\"{Escape(element.AutomationId)}\"");
        if (!string.IsNullOrEmpty(element.Text))
            attrs.Append($" data-text=\"{Escape(element.Text)}\"");
        if (!string.IsNullOrEmpty(element.Value))
            attrs.Append($" data-value=\"{Escape(element.Value)}\"");
        if (!string.IsNullOrEmpty(element.Role))
            attrs.Append($" data-role=\"{Escape(element.Role)}\"");

        attrs.Append($" data-isVisible=\"{element.IsVisible.ToString().ToLowerInvariant()}\"");
        attrs.Append($" data-isEnabled=\"{element.IsEnabled.ToString().ToLowerInvariant()}\"");
        attrs.Append($" data-isFocused=\"{element.IsFocused.ToString().ToLowerInvariant()}\"");
        attrs.Append(CultureInfo.InvariantCulture, $" data-opacity=\"{element.Opacity:0.###}\"");

        var traits = element.Traits;
        var isInteractable = element.IsVisible && element.IsEnabled && traits?.Any(trait =>
            trait.Equals("interactive", StringComparison.OrdinalIgnoreCase) ||
            trait.Equals("scrollable", StringComparison.OrdinalIgnoreCase)) == true;
        attrs.Append($" data-interactable=\"{isInteractable.ToString().ToLowerInvariant()}\"");
        if (traits is { Count: > 0 })
            attrs.Append($" data-traits=\"{Escape(string.Join(",", traits))}\"");
        if (element.Gestures is { Count: > 0 })
            attrs.Append($" data-gestures=\"{Escape(string.Join(",", element.Gestures))}\"");
        if (element.StyleClass is { Count: > 0 })
            attrs.Append($" data-styleClass=\"{Escape(string.Join(",", element.StyleClass))}\"");
        if (!string.IsNullOrEmpty(element.NativeType))
            attrs.Append($" data-nativeType=\"{Escape(element.NativeType)}\"");
        // Boolean availability flag only — the absolute source path is fetched on demand via
        // /api/source resolves click-to-XAML locations on demand, so local filesystem paths are
        // never embedded in every div.
        if (!string.IsNullOrEmpty(element.SourceFile))
            attrs.Append(" data-hasSource=\"true\"");

        sb.AppendLine($"    <div class=\"devflow-element\"{attrs} style=\"{style}\"></div>");
        return true;
    }

    private static string Escape(string value) => HttpUtility.HtmlAttributeEncode(value);
}
