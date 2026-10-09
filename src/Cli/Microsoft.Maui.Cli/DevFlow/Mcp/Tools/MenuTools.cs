using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Microsoft.Maui.Cli.DevFlow.Mcp;

namespace Microsoft.Maui.Cli.DevFlow.Mcp.Tools;

[McpServerToolType]
public sealed class MenuTools
{
	[McpServerTool(Name = "maui_menu_list"), Description("Inspect the application menu bar. Returns the cross-platform MAUI MenuBarItems tree plus, where supported, the platform's native application menu (macOS AppKit NSMenu, or Mac Catalyst responder-chain key commands). Use this to discover menu titles, paths, key equivalents, and enabled state — the native app menu is not part of the visual tree returned by maui_tree. All menu ids are positional and may change when menus change; refresh the listing before invoking by id. Prefer key + modifiers on Catalyst, whose native listing is a flat set of commands from the active responder chain.")]
	public static async Task<string> ListMenus(
		McpAgentSession session,
		[Description("Optional window index to scope the MAUI menu walk to a single window")] int? window = null,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var result = await agent.GetMenusAsync(window);
		return result.ValueKind == JsonValueKind.Undefined ? "Failed to list menus." : result.ToString();
	}

	[McpServerTool(Name = "maui_menu_invoke"), Description("Invoke an application menu item. Identify the item by id (from maui_menu_list), path (slash-joined titles such as 'File/Save'), title, or key + modifiers (e.g. key 'l', modifiers 'cmd,shift'). The target selects the menu layer: 'auto' (default — tries MAUI, then native only when no MAUI item matches), 'maui', or 'native'. Disabled items are never invoked. Copy paths from the listing: MAUI/AppKit title segments escape '%' as '%25', '/' as '%2F', and backslash as '%5C'; Catalyst native paths are flat titles, not hierarchical. Use window to scope MAUI path/title/key matching in multi-window apps.")]
	public static async Task<string> InvokeMenu(
		McpAgentSession session,
		[Description("Positional menu item id from a fresh maui_menu_list (e.g. 'maui:w0/1/2' or 'native:1/3'). Ids may change when menus change; prefer key + modifiers on Catalyst.")] string? id = null,
		[Description("Menu path copied from the listing, e.g. 'Account/Log Out'. MAUI/AppKit title segments escape %, / and backslash; Catalyst native paths are flat titles.")] string? path = null,
		[Description("Menu item title (first match)")] string? title = null,
		[Description("Key equivalent for the item, e.g. 'l' or 's'. Combine with modifiers.")] string? key = null,
		[Description("Comma or plus separated modifiers for the key, e.g. 'cmd,shift'")] string? modifiers = null,
		[Description("Menu layer to target: 'auto' (default), 'maui', or 'native'")] string? target = null,
		[Description("Optional window index to scope MAUI menu invocation; native menus use the active application responder chain")] int? window = null,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(path) &&
			string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(key))
		{
			return "Provide one of: id, path, title, or key.";
		}

		var agent = await session.GetAgentClientAsync(agentPort);
		var result = await agent.InvokeMenuAsync(id, path, title, key, modifiers, target, window);
		return result.ValueKind == JsonValueKind.Undefined
			? "Failed to invoke menu item (not found or unsupported)."
			: result.ToString();
	}
}
