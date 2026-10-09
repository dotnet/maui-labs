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
    [McpServerTool(Name = "maui_cdp_evaluate"), Description("Execute JavaScript in a registered WebView, HybridWebView, or BlazorWebView via Chrome DevTools Protocol. Returns the evaluation result.")]
    public static async Task<string> CdpEvaluate(
        McpAgentSession session,
        [Description("JavaScript expression to evaluate")] string expression,
        [Description("WebView ID or index to target (optional if only one WebView)")] string? webviewId = null,
        [Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
    {
        using var agent = await session.GetAgentClientAsync(agentPort);
        var content = await agent.SendCdpCommandAsync("Runtime.evaluate", new JsonObject
        {
            ["expression"] = expression,
            ["returnByValue"] = true
        }, webviewId);

        if (content.TryGetProperty("result", out var result) &&
            result.TryGetProperty("result", out var inner) &&
            inner.TryGetProperty("value", out var value))
        {
            return value.ToString();
        }
        return content.ToString();
    }

    [McpServerTool(Name = "maui_cdp_screenshot"), Description("Capture a registered WebView, HybridWebView, or BlazorWebView using the agent's native-first screenshot path. Returns the image directly.")]
    public static async Task<ContentBlock[]> CdpScreenshot(
        McpAgentSession session,
        [Description("WebView ID or index to target (optional if only one WebView)")] string? webviewId = null,
        [Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
    {
        using var agent = await session.GetAgentClientAsync(agentPort);
        var pngBytes = await agent.GetWebViewScreenshotAsync(webviewId);
        return [
            new TextContentBlock { Text = $"WebView screenshot captured ({pngBytes.Length} bytes)" },
            ImageContentBlock.FromBytes(pngBytes, "image/png")
        ];
    }

    [McpServerTool(Name = "maui_cdp_source"), Description("Get the HTML source of a registered WebView, HybridWebView, or BlazorWebView.")]
    public static async Task<string> CdpSource(
        McpAgentSession session,
        [Description("WebView ID or index to target (optional if only one WebView)")] string? webviewId = null,
        [Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
    {
        using var agent = await session.GetAgentClientAsync(agentPort);
        var source = await agent.GetCdpSourceAsync(webviewId);
        if (string.IsNullOrEmpty(source))
            throw new McpException("The selected WebView returned no HTML source.");
        return source;
    }

    [McpServerTool(Name = "maui_cdp_webviews"), Description("List registered WebView, HybridWebView, and BlazorWebView contexts in the running app, including their readiness and active state.")]
    public static async Task<string> CdpWebViews(
        McpAgentSession session,
        [Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
    {
        using var agent = await session.GetAgentClientAsync(agentPort);
        var webviews = await agent.GetCdpWebViewsAsync();
        return webviews.ToString();
    }
}
