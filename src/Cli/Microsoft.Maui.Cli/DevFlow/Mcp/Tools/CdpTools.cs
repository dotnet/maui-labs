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
    [McpServerTool(Name = "maui_cdp_evaluate"), Description("Execute JavaScript in a registered standard, Hybrid or Blazor WebView via Chrome DevTools Protocol. Returns the evaluation result.")]
    public static async Task<string> CdpEvaluate(
        McpAgentSession session,
        [Description("JavaScript expression to evaluate")] string expression,
        [Description("Canonical context ID from maui_cdp_webviews (webview-<index>; omitted selects the active host)")] string? contextId = null,
        [Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
    {
        using var agent = await session.GetAgentClientAsync(agentPort);
        try
        {
            var content = await agent.SendCdpCommandAsync("Runtime.evaluate", new JsonObject
            {
                ["expression"] = expression,
                ["returnByValue"] = true
            }, contextId);

            if (content.TryGetProperty("result", out var result) &&
                result.TryGetProperty("result", out var inner) &&
                inner.TryGetProperty("value", out var value))
            {
                return value.ToString();
            }
            return content.ToString();
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or System.Text.Json.JsonException)
        {
            throw new McpException(ex.Message);
        }
    }

    [McpServerTool(Name = "maui_cdp_screenshot"), Description("Capture a registered WebView using the agent's native-first screenshot path. Returns the PNG image directly.")]
    public static async Task<ContentBlock[]> CdpScreenshot(
        McpAgentSession session,
        [Description("Canonical context ID from maui_cdp_webviews (webview-<index>; omitted selects the active host)")] string? contextId = null,
        [Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
    {
        using var agent = await session.GetAgentClientAsync(agentPort);
        try
        {
            var pngBytes = await agent.GetWebViewScreenshotAsync(contextId);
            return [
                new TextContentBlock { Text = $"WebView screenshot captured ({pngBytes.Length} bytes)" },
                ImageContentBlock.FromBytes(pngBytes, "image/png")
            ];
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or System.Text.Json.JsonException)
        {
            throw new McpException(ex.Message);
        }
    }

    [McpServerTool(Name = "maui_cdp_source"), Description("Get the HTML source of a registered WebView.")]
    public static async Task<string> CdpSource(
        McpAgentSession session,
        [Description("Canonical context ID from maui_cdp_webviews (webview-<index>; omitted selects the active host)")] string? contextId = null,
        [Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
    {
        using var agent = await session.GetAgentClientAsync(agentPort);
        try
        {
            var source = await agent.GetCdpSourceAsync(contextId);
            if (string.IsNullOrEmpty(source))
                throw new McpException("The selected WebView returned no HTML source.");
            return source;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or System.Text.Json.JsonException)
        {
            throw new McpException(ex.Message);
        }
    }

    [McpServerTool(Name = "maui_cdp_webviews"), Description("List all registered WebView contexts with unique canonical id, ready and active fields and descriptive hostKind metadata.")]
    public static async Task<string> CdpWebViews(
        McpAgentSession session,
        [Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
    {
        using var agent = await session.GetAgentClientAsync(agentPort);
        var webviews = await agent.GetCdpWebViewsAsync();
        return webviews.ToString();
    }
}
