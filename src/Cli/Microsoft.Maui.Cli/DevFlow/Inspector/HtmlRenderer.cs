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
    private static string? _templateCache;

    public static string Render(List<ElementInfo> tree, bool hasScreenshot, int screenshotWidth = 0, int screenshotHeight = 0, double density = 1, double elementScale = 1)
    {
        var template = GetTemplate();
        var (viewportWidth, viewportHeight) = ComputeViewportSize(tree, screenshotWidth, screenshotHeight);

        // Build the elements HTML (flat list — all elements use window-absolute bounds)
        var elementsHtml = RenderElements(tree, elementScale);

        // Build screenshot tag
        var screenshotHtml = hasScreenshot
            ? "<img id=\"screenshot\" src=\"screenshot.png\" alt=\"App screenshot\">"
            : "";

        // Replace template placeholders
        var html = template
            .Replace("{{VIEWPORT_WIDTH}}", viewportWidth.ToString("F0"))
            .Replace("{{VIEWPORT_HEIGHT}}", viewportHeight.ToString("F0"))
            .Replace("{{DENSITY}}", density.ToString("F1"))
            .Replace("{{ELEMENT_SCALE}}", elementScale.ToString("F4"))
            .Replace("{{SCREENSHOT}}", screenshotHtml)
            .Replace("{{ELEMENTS}}", elementsHtml);

        return html;
    }

    /// <summary>
    /// Renders just the element divs (no template wrapping) for AJAX state updates.
    /// </summary>
    public static string RenderElements(List<ElementInfo> tree, double elementScale = 1)
    {
        var sb = new StringBuilder();
        foreach (var element in tree)
        {
            RenderElementsFlat(sb, element, elementScale);
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

    private static string GetTemplate()
    {
        if (_templateCache != null) return _templateCache;

        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = "Microsoft.Maui.Cli.DevFlow.Inspector.Web.inspector.html";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource not found: {resourceName}");
        using var reader = new StreamReader(stream);
        _templateCache = reader.ReadToEnd();
        return _templateCache;
    }

    /// <summary>
    /// Renders all elements as flat siblings (no nesting) using window-absolute bounds.
    /// </summary>
    private static void RenderElementsFlat(StringBuilder sb, ElementInfo element, double scale)
    {
        RenderSingleElement(sb, element, scale);
        if (element.Children != null)
        {
            foreach (var child in element.Children)
            {
                RenderElementsFlat(sb, child, scale);
            }
        }
    }

    private static void RenderSingleElement(StringBuilder sb, ElementInfo element, double scale)
    {
        // Build style for positioning using window-absolute bounds
        // (windowBounds is absolute within the window; bounds is relative to parent)
        var bounds = element.WindowBounds ?? element.Bounds;
        if (bounds == null || (bounds.Width <= 0 && bounds.Height <= 0))
            return; // Skip elements with no meaningful bounds

        var style = $"position:absolute;left:{bounds.X * scale:F0}px;top:{bounds.Y * scale:F0}px;width:{bounds.Width * scale:F0}px;height:{bounds.Height * scale:F0}px;";

        // Build data attributes
        var attrs = new StringBuilder();
        attrs.Append($" data-id=\"{Escape(element.Id)}\"");
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
        attrs.Append($" data-opacity=\"{element.Opacity}\"");

        if (element.Traits is { Count: > 0 })
            attrs.Append($" data-traits=\"{Escape(string.Join(",", element.Traits))}\"");
        if (element.Gestures is { Count: > 0 })
            attrs.Append($" data-gestures=\"{Escape(string.Join(",", element.Gestures))}\"");
        if (element.StyleClass is { Count: > 0 })
            attrs.Append($" data-styleClass=\"{Escape(string.Join(",", element.StyleClass))}\"");
        if (!string.IsNullOrEmpty(element.NativeType))
            attrs.Append($" data-nativeType=\"{Escape(element.NativeType)}\"");

        sb.AppendLine($"    <div class=\"devflow-element\"{attrs} style=\"{style}\"></div>");
    }

    private static string Escape(string value) => HttpUtility.HtmlAttributeEncode(value);
}
