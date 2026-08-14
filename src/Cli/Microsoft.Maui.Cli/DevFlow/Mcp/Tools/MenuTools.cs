using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Microsoft.Maui.Cli.DevFlow.Mcp;

namespace Microsoft.Maui.Cli.DevFlow.Mcp.Tools;

[McpServerToolType]
public sealed class MenuTools
{
	[McpServerTool(Name = "maui_menu_list"), Description("Inspect the application menu bar. Returns the cross-platform MAUI MenuBarItems tree plus, where supported, the platform's native application menu (macOS AppKit NSMenu, or Mac Catalyst responder-chain key commands). Use this to discover menu titles, paths, key equivalents, and enabled state — the native app menu is not part of the visual tree returned by maui_tree. On Mac Catalyst, native 'native:N' ids are ephemeral (derived from responder-chain order at call time) and should only be used within the same automation step; prefer key + modifiers for stable invocation on Catalyst. macOS AppKit ids are stable.")]
	public static async Task<string> ListMenus(
		McpAgentSession session,
		[Description("Optional window index to scope the MAUI menu walk to a single window")] int? window = null,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var result = await agent.GetMenusAsync(window);
		return result.ValueKind == JsonValueKind.Undefined ? "Failed to list menus." : result.ToString();
	}

	[McpServerTool(Name = "maui_menu_invoke"), Description("Invoke an application menu item. Identify the item by id (from maui_menu_list), path (slash-joined titles such as 'File/Save'), title, or key + modifiers (e.g. key 'l', modifiers 'cmd,shift'). The target selects the menu layer: 'auto' (default — tries the MAUI menu, then the native menu), 'maui', or 'native'. On macOS this performs the native NSMenu item; cross-platform MAUI menu items execute their Command and fire Clicked. Note: paths are slash-joined titles and do not escape '/', so a menu title that itself contains '/' is ambiguous for path matching - identify such items by their stable id from maui_menu_list instead.")]
	public static async Task<string> InvokeMenu(
		McpAgentSession session,
		[Description("Menu item id from maui_menu_list (e.g. 'maui:w0/1/2' or 'native:1/3'). MAUI and macOS ids are stable; Mac Catalyst 'native:N' ids are ephemeral - prefer key + modifiers there.")] string? id = null,
		[Description("Slash-joined title path, e.g. 'Account/Log Out'. If a title itself contains '/', use id instead.")] string? path = null,
		[Description("Menu item title (first match)")] string? title = null,
		[Description("Key equivalent for the item, e.g. 'l' or 's'. Combine with modifiers.")] string? key = null,
		[Description("Comma or plus separated modifiers for the key, e.g. 'cmd,shift'")] string? modifiers = null,
		[Description("Menu layer to target: 'auto' (default), 'maui', or 'native'")] string? target = null,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(path) &&
			string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(key))
		{
			return "Provide one of: id, path, title, or key.";
		}

		var agent = await session.GetAgentClientAsync(agentPort);
		var result = await agent.InvokeMenuAsync(id, path, title, key, modifiers, target);
		return result.ValueKind == JsonValueKind.Undefined
			? "Failed to invoke menu item (not found or unsupported)."
			: result.ToString();
	}
}
