using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using Microsoft.Maui.Cli.DevFlow.Mcp;

namespace Microsoft.Maui.Cli.DevFlow.Mcp.Tools;

[McpServerToolType]
public sealed class ScreenshotTool
{
	[McpServerTool(Name = "maui_screenshot"), Description("Capture a screenshot of the running MAUI app. Returns the image directly for visual verification of layout, colors, contrast, and rendering.")]
	public static async Task<ContentBlock[]> Screenshot(
		McpAgentSession session,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null,
		[Description("Window index for multi-window apps (default: 0)")] int? window = null,
		[Description("Element ID to capture a specific element")] string? elementId = null,
		[Description("CSS selector to capture (first match, Blazor WebViews only)")] string? selector = null,
		[Description("Resize screenshot to this max width in pixels (overrides auto-scaling)")] int? maxWidth = null,
		[Description("Scale mode: 'native' keeps full HiDPI resolution, default auto-scales to 1x logical pixels")] string? scale = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var bytes = await agent.ScreenshotAsync(window, elementId, selector, maxWidth, scale);
		if (bytes == null || bytes.Length == 0)
			throw new McpException("Screenshot failed — no image data returned. Is the agent connected and the app visible?");

		return [
			new TextContentBlock { Text = $"Screenshot captured ({bytes.Length} bytes, PNG)" },
			ImageContentBlock.FromBytes(bytes, "image/png")
		];
	}
}
