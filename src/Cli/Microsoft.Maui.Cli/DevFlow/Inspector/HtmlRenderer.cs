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

    public static string Render(List<ElementInfo> tree, bool hasScreenshot, int screenshotWidth = 0, int screenshotHeight = 0)
    {
        var template = GetTemplate();

        // Use screenshot dimensions as viewport size (most reliable),
        // fall back to root element bounds, then default
        double viewportWidth, viewportHeight;
        if (screenshotWidth > 0 && screenshotHeight > 0)
        {
            viewportWidth = screenshotWidth;
            viewportHeight = screenshotHeight;
        }
        else
        {
            var rootBounds = tree.Count > 0 ? tree[0].Bounds : null;
            viewportWidth = rootBounds is { Width: > 0 } ? rootBounds.Width : 800;
            viewportHeight = rootBounds is { Height: > 0 } ? rootBounds.Height : 600;
        }

        // Build the elements HTML
        var elements = new StringBuilder();
        foreach (var element in tree)
        {
            RenderElement(elements, element, 4);
        }

        // Build screenshot tag
        var screenshotHtml = hasScreenshot
            ? "<img id=\"screenshot\" src=\"/screenshot.png\" alt=\"App screenshot\">"
            : "";

        // Replace template placeholders
        var html = template
            .Replace("{{VIEWPORT_WIDTH}}", viewportWidth.ToString("F0"))
            .Replace("{{VIEWPORT_HEIGHT}}", viewportHeight.ToString("F0"))
            .Replace("{{SCREENSHOT}}", screenshotHtml)
            .Replace("{{ELEMENTS}}", elements.ToString());

        return html;
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

    private static void RenderElement(StringBuilder sb, ElementInfo element, int indent)
    {
        var pad = new string(' ', indent);

        // Build style for positioning
        var style = new StringBuilder("position:absolute;");
        if (element.Bounds != null)
        {
            style.Append($"left:{element.Bounds.X:F0}px;");
            style.Append($"top:{element.Bounds.Y:F0}px;");
            style.Append($"width:{element.Bounds.Width:F0}px;");
            style.Append($"height:{element.Bounds.Height:F0}px;");
        }

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

        var hasChildren = element.Children is { Count: > 0 };

        sb.Append($"{pad}<div class=\"devflow-element\"{attrs} style=\"{style}\">");

        if (hasChildren)
        {
            sb.AppendLine();
            foreach (var child in element.Children!)
            {
                RenderElement(sb, child, indent + 2);
            }
            sb.AppendLine($"{pad}</div>");
        }
        else
        {
            sb.AppendLine("</div>");
        }
    }

    private static string Escape(string value) => HttpUtility.HtmlAttributeEncode(value);
}
