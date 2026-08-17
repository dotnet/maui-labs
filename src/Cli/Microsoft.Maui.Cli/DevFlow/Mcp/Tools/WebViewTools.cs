using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using Microsoft.Maui.Cli.DevFlow.Mcp;

namespace Microsoft.Maui.Cli.DevFlow.Mcp.Tools;

[McpServerToolType]
public sealed class WebViewTools
{
    [McpServerTool(Name = "maui_webview_navigate"), Description("Navigate a Blazor WebView to a URL. Use this for high-level WebView navigation instead of low-level CDP commands.")]
    public static async Task<string> WebViewNavigate(
        McpAgentSession session,
        [Description("URL to navigate the WebView to (e.g., '/counter', 'https://example.com')")] string url,
        [Description("WebView context ID (optional if only one WebView)")] string? contextId = null,
        [Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
    {
        var agent = await session.GetAgentClientAsync(agentPort);
        var success = await agent.NavigateWebViewAsync(url, contextId);
        return success
            ? $"WebView navigated to '{url}'."
            : $"Failed to navigate WebView to '{url}'. Is a Blazor WebView active?";
    }

    [McpServerTool(Name = "maui_webview_click"), Description("Click an element in a Blazor WebView by CSS selector. Scrolls the element into view, focuses it, and clicks it.")]
    public static async Task<string> WebViewClick(
        McpAgentSession session,
        [Description("CSS selector of the element to click (e.g., 'button', '#submit-btn', '.nav-link')")] string selector,
        [Description("WebView context ID (optional if only one WebView)")] string? contextId = null,
        [Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
    {
        var agent = await session.GetAgentClientAsync(agentPort);
        var success = await agent.ClickWebViewAsync(selector, contextId);
        return success
            ? $"Clicked WebView element '{selector}'."
            : $"Failed to click WebView element '{selector}'. Element may not exist or WebView is not active.";
    }

    [McpServerTool(Name = "maui_webview_fill"), Description("Fill text into an input element in a Blazor WebView by CSS selector. Replaces existing value, focuses the element, and dispatches input/change events.")]
    public static async Task<string> WebViewFill(
        McpAgentSession session,
        [Description("CSS selector of the input element to fill (e.g., 'input#email', 'textarea.comment')")] string selector,
        [Description("Text to fill into the element")] string text,
        [Description("WebView context ID (optional if only one WebView)")] string? contextId = null,
        [Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
    {
        var agent = await session.GetAgentClientAsync(agentPort);
        var success = await agent.FillWebViewAsync(selector, text, contextId);
        return success
            ? $"Filled WebView element '{selector}' with text."
            : $"Failed to fill WebView element '{selector}'. Element may not exist or is not an input.";
    }

    [McpServerTool(Name = "maui_webview_type"), Description("Type text into the currently focused element in a Blazor WebView using CDP Input.insertText. Use maui_webview_click to focus an element first.")]
    public static async Task<string> WebViewType(
        McpAgentSession session,
        [Description("Text to type into the focused element")] string text,
        [Description("WebView context ID (optional if only one WebView)")] string? contextId = null,
        [Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
    {
        var agent = await session.GetAgentClientAsync(agentPort);
        var success = await agent.InsertWebViewTextAsync(text, contextId);
        return success
            ? "Text typed into WebView."
            : "Failed to type text into WebView. Is an element focused?";
    }

    [McpServerTool(Name = "maui_webview_console"), Description("Get console log messages from a Blazor WebView. Returns log entries filtered to the WebView source.")]
    public static async Task<string> WebViewConsole(
        McpAgentSession session,
        [Description("Maximum number of log entries to return (default: 100)")] int limit = 100,
        [Description("WebView context ID (optional if only one WebView)")] string? contextId = null,
        [Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
    {
        var agent = await session.GetAgentClientAsync(agentPort);
        var logs = await agent.GetWebViewConsoleAsync(limit, contextId);
        return string.IsNullOrEmpty(logs) || logs == "[]"
            ? "No WebView console messages captured."
            : logs;
    }

    [McpServerTool(Name = "maui_webview_dom"), Description("Get the DOM tree (HTML source) of a Blazor WebView. Returns the full outerHTML of the document element.")]
    public static async Task<string> WebViewDom(
        McpAgentSession session,
        [Description("WebView context ID (optional if only one WebView)")] string? contextId = null,
        [Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
    {
        var agent = await session.GetAgentClientAsync(agentPort);
        var dom = await agent.GetWebViewDomAsync(contextId);
        return string.IsNullOrEmpty(dom)
            ? "No WebView DOM available. Is a Blazor WebView active?"
            : dom;
    }

    [McpServerTool(Name = "maui_webview_dom_query"), Description("Query the WebView DOM by CSS selector. Returns matching elements with their tag name, id, class, text content, and outer HTML.")]
    public static async Task<string> WebViewDomQuery(
        McpAgentSession session,
        [Description("CSS selector to query (e.g., 'button', '.nav-link', '#main-content h1')")] string selector,
        [Description("WebView context ID (optional if only one WebView)")] string? contextId = null,
        [Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
    {
        var agent = await session.GetAgentClientAsync(agentPort);
        var result = await agent.QueryWebViewDomAsync(selector, contextId);
        return string.IsNullOrEmpty(result) || result == "[]"
            ? $"No elements found matching '{selector}'."
            : result;
    }

    [McpServerTool(Name = "maui_webview_network"), Description("Get captured network requests from a Blazor WebView.")]
    public static async Task<string> WebViewNetwork(
        McpAgentSession session,
        [Description("WebView context ID (optional if only one WebView)")] string? contextId = null,
        [Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
    {
        var agent = await session.GetAgentClientAsync(agentPort);
        var requests = await agent.GetWebViewNetworkAsync(contextId);
        return string.IsNullOrEmpty(requests) || requests == "[]"
            ? "No WebView network requests captured."
            : requests;
    }

    [McpServerTool(Name = "maui_webview_screenshot"), Description("Capture a screenshot of a Blazor WebView. Returns the image directly. This captures only the WebView content, not the native MAUI UI.")]
    public static async Task<ContentBlock[]> WebViewScreenshot(
        McpAgentSession session,
        [Description("WebView context ID (optional if only one WebView)")] string? contextId = null,
        [Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
    {
        var agent = await session.GetAgentClientAsync(agentPort);
        var bytes = await agent.GetWebViewScreenshotAsync(contextId);
        if (bytes == null || bytes.Length == 0)
            throw new McpException("Failed to capture WebView screenshot. Is a Blazor WebView active?");

        return [
            new TextContentBlock { Text = $"WebView screenshot captured ({bytes.Length} bytes)" },
            ImageContentBlock.FromBytes(bytes, "image/png")
        ];
    }
}
