using System.Text;
using System.Web;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.Cli.DevFlow.Inspector;

/// <summary>
/// Generates an interactive HTML page from the DevFlow visual tree.
/// Each element becomes a positioned div with data-* attributes matching
/// the DevFlow ElementInfo property names (camelCase).
/// </summary>
public static class HtmlRenderer
{
    public static string Render(List<ElementInfo> tree, bool hasScreenshot)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html>");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset=\"utf-8\">");
        sb.AppendLine("  <title>DevFlow Inspector</title>");
        sb.AppendLine("  <style>");
        sb.AppendLine("    * { margin: 0; padding: 0; box-sizing: border-box; }");
        sb.AppendLine("    body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif; background: #1e1e1e; color: #fff; }");
        sb.AppendLine("    #devflow-toolbar { height: 40px; background: #2d2d2d; display: flex; align-items: center; padding: 0 12px; gap: 8px; border-bottom: 1px solid #444; }");
        sb.AppendLine("    #devflow-toolbar button { background: #3c3c3c; border: 1px solid #555; color: #fff; padding: 4px 10px; border-radius: 4px; cursor: pointer; font-size: 14px; }");
        sb.AppendLine("    #devflow-toolbar button:hover { background: #4c4c4c; }");
        sb.AppendLine("    #devflow-toolbar #connection-status { margin-left: auto; font-size: 12px; color: #4ec9b0; }");
        sb.AppendLine("    #app-viewport { position: relative; margin: 0 auto; overflow: hidden; }");
        sb.AppendLine("    #screenshot { position: absolute; top: 0; left: 0; width: 100%; height: 100%; pointer-events: none; user-select: none; }");
        sb.AppendLine("    .devflow-element { position: absolute; box-sizing: border-box; }");
        sb.AppendLine("    .devflow-element:hover { outline: 2px solid rgba(78, 201, 176, 0.5); }");
        sb.AppendLine("  </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");

        // Toolbar
        sb.AppendLine("  <nav id=\"devflow-toolbar\">");
        sb.AppendLine("    <button id=\"btn-back\" title=\"Navigate back\">←</button>");
        sb.AppendLine("    <button id=\"btn-refresh\" title=\"Refresh\">↻</button>");
        sb.AppendLine("    <span id=\"connection-status\">● Connected</span>");
        sb.AppendLine("  </nav>");

        // Determine viewport size from root element bounds
        double viewportWidth = 390;
        double viewportHeight = 844;
        var rootBounds = tree.Count > 0 ? tree[0].Bounds : null;
        if (rootBounds != null)
        {
            viewportWidth = rootBounds.Width;
            viewportHeight = rootBounds.Height;
        }

        sb.AppendLine($"  <div id=\"app-viewport\" style=\"width:{viewportWidth}px; height:{viewportHeight}px;\">");

        if (hasScreenshot)
        {
            sb.AppendLine("    <img id=\"screenshot\" src=\"/screenshot.png\" alt=\"App screenshot\">");
        }

        // Render element tree as nested divs
        foreach (var element in tree)
        {
            RenderElement(sb, element, 4);
        }

        sb.AppendLine("  </div>");
        sb.AppendLine("  <script src=\"/devflow.js\"></script>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
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
