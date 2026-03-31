using System.ComponentModel;
using ModelContextProtocol.Server;
using Microsoft.Maui.Cli.DevFlow.Mcp;

namespace Microsoft.Maui.Cli.DevFlow.Mcp.Tools;

[McpServerToolType]
public sealed class InteractionTools
{
	[McpServerTool(Name = "maui_tap"), Description("Tap a UI element by its visual tree ID. Use maui_tree to discover element IDs.")]
	public static async Task<string> Tap(
		McpAgentSession session,
		[Description("Element ID from the visual tree")] string elementId,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var success = await agent.TapAsync(elementId);
		return success
			? $"Tapped element '{elementId}' successfully."
			: $"Failed to tap element '{elementId}'. Element may not exist or is not tappable.";
	}

	[McpServerTool(Name = "maui_fill"), Description("Fill text into an Entry, Editor, or SearchBar element. Replaces existing text.")]
	public static async Task<string> Fill(
		McpAgentSession session,
		[Description("Element ID from the visual tree")] string elementId,
		[Description("Text to fill into the element")] string text,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var success = await agent.FillAsync(elementId, text);
		return success
			? $"Filled element '{elementId}' with text."
			: $"Failed to fill element '{elementId}'. Element may not exist or is not a text input.";
	}

	[McpServerTool(Name = "maui_clear"), Description("Clear text from an Entry, Editor, or SearchBar element.")]
	public static async Task<string> Clear(
		McpAgentSession session,
		[Description("Element ID from the visual tree")] string elementId,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var success = await agent.ClearAsync(elementId);
		return success
			? $"Cleared element '{elementId}' successfully."
			: $"Failed to clear element '{elementId}'.";
	}

	[McpServerTool(Name = "maui_scroll"), Description("Scroll a ScrollView, CollectionView, or ListView. Supports delta-based scrolling, scrolling to an item index, or scrolling an element into view.")]
	public static async Task<string> Scroll(
		McpAgentSession session,
		[Description("Element ID of the scroll container, or element to scroll into view")] string? elementId = null,
		[Description("Horizontal scroll delta in pixels")] double? x = null,
		[Description("Vertical scroll delta in pixels")] double? y = null,
		[Description("Whether to animate the scroll (default: true)")] bool? animated = null,
		[Description("Window index for multi-window apps")] int? window = null,
		[Description("Item index to scroll to (for CollectionView/ListView)")] int? itemIndex = null,
		[Description("Group index for grouped CollectionView")] int? groupIndex = null,
		[Description("Scroll position: MakeVisible (default), Start, Center, End")] string? scrollToPosition = null,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var success = await agent.ScrollAsync(elementId, x ?? 0, y ?? 0, animated ?? true, window, itemIndex, groupIndex, scrollToPosition);
		return success
			? elementId is not null ? $"Scrolled element '{elementId}' successfully." : "Scrolled successfully."
			: $"Failed to scroll element '{elementId}'. Element may not be a ScrollView.";
	}
}
