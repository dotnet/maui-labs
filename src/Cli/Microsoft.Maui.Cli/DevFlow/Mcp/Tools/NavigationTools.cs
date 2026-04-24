using System.ComponentModel;
using ModelContextProtocol.Server;
using Microsoft.Maui.Cli.DevFlow.Mcp;

namespace Microsoft.Maui.Cli.DevFlow.Mcp.Tools;

[McpServerToolType]
public sealed class NavigationTools
{
	[McpServerTool(Name = "maui_navigate"), Description("Navigate to a Shell route in the MAUI app (e.g., '//home', '//settings', '//blazor').")]
	public static async Task<string> Navigate(
		McpAgentSession session,
		[Description("Shell route to navigate to (e.g., '//home', '//blazor')")] string route,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var success = await agent.NavigateAsync(route);
		return success
			? $"Navigated to '{route}'."
			: $"Failed to navigate to '{route}'. Route may not exist in the Shell.";
	}

	[McpServerTool(Name = "maui_back"), Description("Go back in the app navigation stack. Equivalent to pressing the system back button.")]
	public static async Task<string> Back(
		McpAgentSession session,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var success = await agent.BackAsync();
		return success
			? "Navigated back successfully."
			: "Failed to navigate back. Navigation stack may be empty.";
	}

	[McpServerTool(Name = "maui_focus"), Description("Set focus to a UI element.")]
	public static async Task<string> Focus(
		McpAgentSession session,
		[Description("Element ID from the visual tree")] string elementId,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var success = await agent.FocusAsync(elementId);
		return success
			? $"Focused element '{elementId}'."
			: $"Failed to focus element '{elementId}'.";
	}

	[McpServerTool(Name = "maui_resize"), Description("Resize the app window.")]
	public static async Task<string> Resize(
		McpAgentSession session,
		[Description("New window width in pixels")] int width,
		[Description("New window height in pixels")] int height,
		[Description("Window index for multi-window apps")] int? window = null,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var success = await agent.ResizeAsync(width, height, window);
		return success
			? $"Resized window to {width}x{height}."
			: "Failed to resize window.";
	}
}
