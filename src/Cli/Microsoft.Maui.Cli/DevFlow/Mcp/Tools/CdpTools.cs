using System.ComponentModel;
using System.Text.Json.Nodes;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using Microsoft.Maui.Cli.DevFlow.Mcp;

namespace Microsoft.Maui.Cli.DevFlow.Mcp.Tools;

[McpServerToolType]
public sealed class CdpTools
{
    [McpServerTool(Name = "maui_cdp_evaluate"), Description("Execute JavaScript in a Blazor WebView via Chrome DevTools Protocol. Returns the evaluation result.")]
    public static async Task<string> CdpEvaluate(
        McpAgentSession session,
        [Description("JavaScript expression to evaluate")] string expression,
        [Description("WebView ID or index to target (optional if only one WebView)")] string? webviewId = null,
        [Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
    {
        var agent = await session.GetAgentClientAsync(agentPort);
        var content = await agent.SendCdpCommandAsync("Runtime.evaluate", new JsonObject
        {
            ["expression"] = expression,
            ["returnByValue"] = true
        }, webviewId);

        try
        {
            if (content.TryGetProperty("result", out var result) &&
                result.TryGetProperty("result", out var inner) &&
                inner.TryGetProperty("value", out var value))
            {
                return value.ToString();
            }
            return content.ToString();
        }
        catch
        {
            return content.ToString();
        }
    }

    [McpServerTool(Name = "maui_cdp_screenshot"), Description("Capture a screenshot of a Blazor WebView via Chrome DevTools Protocol. Returns the image directly.")]
    public static async Task<ContentBlock[]> CdpScreenshot(
        McpAgentSession session,
        [Description("WebView ID or index to target (optional if only one WebView)")] string? webviewId = null,
        [Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
    {
        var agent = await session.GetAgentClientAsync(agentPort);
        var json = await agent.SendCdpCommandAsync("Page.captureScreenshot", new JsonObject
        {
            ["format"] = "png"
        }, webviewId);

        if (json.TryGetProperty("result", out var result) &&
            result.TryGetProperty("data", out var data))
        {
            var pngBytes = Convert.FromBase64String(data.GetString()!);
            return [
                new TextContentBlock { Text = $"WebView screenshot captured ({pngBytes.Length} bytes)" },
                ImageContentBlock.FromBytes(pngBytes, "image/png")
            ];
        }

        throw new McpException("Failed to capture WebView screenshot. Is a Blazor WebView active?");
    }

    [McpServerTool(Name = "maui_cdp_source"), Description("Get the HTML source of a Blazor WebView.")]
    public static async Task<string> CdpSource(
        McpAgentSession session,
        [Description("WebView ID or index to target (optional if only one WebView)")] string? webviewId = null,
        [Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
    {
        var agent = await session.GetAgentClientAsync(agentPort);
        var source = await agent.GetCdpSourceAsync(webviewId);
        return string.IsNullOrEmpty(source) ? "No WebView source available." : source;
    }

    [McpServerTool(Name = "maui_cdp_webviews"), Description("List all registered Blazor WebViews in the running app.")]
    public static async Task<string> CdpWebViews(
        McpAgentSession session,
        [Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
    {
        var agent = await session.GetAgentClientAsync(agentPort);
        var webviews = await agent.GetCdpWebViewsAsync();
        return webviews.ToString();
    }
}
