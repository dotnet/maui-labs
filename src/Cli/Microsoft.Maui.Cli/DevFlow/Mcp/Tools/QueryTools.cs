using System.ComponentModel;
using ModelContextProtocol.Server;
using Microsoft.Maui.Cli.DevFlow.Mcp;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.Cli.DevFlow.Mcp.Tools;

[McpServerToolType]
public sealed class QueryTools
{
    [McpServerTool(Name = "maui_query"), Description("Query visual tree elements by type, AutomationId, or text content. Returns matching elements with their IDs and properties.")]
    public static async Task<string> Query(
        McpAgentSession session,
        [Description("Element type filter (e.g., 'Button', 'Label', 'Entry')")] string? type = null,
        [Description("AutomationId to search for")] string? automationId = null,
        [Description("Text content to search for")] string? text = null,
        [Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
    {
        if (type == null && automationId == null && text == null)
            return "At least one filter must be specified: type, automationId, or text.";

        var agent = await session.GetAgentClientAsync(agentPort);
        var results = await agent.QueryAsync(type, automationId, text);
        if (results == null || results.Count == 0)
            return "No matching elements found.";

        return CliJson.SerializeUntyped(results, indented: false);
    }

    [McpServerTool(Name = "maui_query_css"), Description("Query Blazor WebView elements using CSS selectors. Returns matching elements.")]
    public static async Task<string> QueryCss(
        McpAgentSession session,
        [Description("CSS selector (e.g., '.my-class', '#myId', 'button.primary')")] string selector,
        [Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
    {
        var agent = await session.GetAgentClientAsync(agentPort);
        var results = await agent.QueryCssAsync(selector);
        if (results == null || results.Count == 0)
            return $"No elements matching selector '{selector}'.";

        return CliJson.SerializeUntyped(results, indented: false);
    }

    [McpServerTool(Name = "maui_element"), Description("Get detailed info about a single element by its visual tree ID.")]
    public static async Task<string> Element(
        McpAgentSession session,
        [Description("Element ID from the visual tree")] string elementId,
        [Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
    {
        var agent = await session.GetAgentClientAsync(agentPort);
        var element = await agent.GetElementAsync(elementId);
        if (element == null)
            return $"Element '{elementId}' not found.";

        return CliJson.SerializeUntyped(element, indented: false);
    }

    [McpServerTool(Name = "maui_hittest"), Description("Find which element is at specific screen coordinates (hit test).")]
    public static async Task<string> HitTest(
        McpAgentSession session,
        [Description("X coordinate in pixels")] double x,
        [Description("Y coordinate in pixels")] double y,
        [Description("Window index for multi-window apps")] int? window = null,
        [Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
    {
        var agent = await session.GetAgentClientAsync(agentPort);
        var elementId = await agent.HitTestAsync(x, y, window);
        if (string.IsNullOrEmpty(elementId))
            return $"No element found at ({x}, {y}).";

        return $"Element at ({x}, {y}): {elementId}";
    }
}
