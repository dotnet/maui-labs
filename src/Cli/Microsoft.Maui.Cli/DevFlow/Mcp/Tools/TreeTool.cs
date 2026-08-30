using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Microsoft.Maui.Cli.DevFlow.Mcp;
using Microsoft.Maui.Cli.DevFlow.Inspector;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.Cli.DevFlow.Mcp.Tools;

[McpServerToolType]
public sealed class TreeTool
{
	[McpServerTool(Name = "maui_tree"), Description("Inspect the visual tree of the running MAUI app. Returns structured JSON element hierarchy with IDs, types, bounds, visibility, and properties. Use element IDs from this tree for tap, fill, scroll, and other interaction commands.")]
	public static async Task<CallToolResult> Tree(
		McpAgentSession session,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null,
		[Description("Window index for multi-window apps (default: 0)")] int? window = null,
		[Description("Max tree depth to return (default: 50)")] int depth = 50,
		[Description("Filter to a specific element type, e.g. 'Label', 'Button', 'Entry'")] string? filter = null,
		[Description("Return only the subtree rooted at this element ID")] string? elementId = null,
		[Description("Tree projection: activeVisual (default, same tree as Inspector hosts) or raw (low-level agent tree)")] string projection = "activeVisual",
		RequestContext<CallToolRequestParams>? requestContext = null)
	{
		List<ElementInfo>? tree;
		if (string.Equals(projection, "activeVisual", StringComparison.OrdinalIgnoreCase))
		{
			if (window is not null)
				return McpAppMetadata.Result(
					"The activeVisual Inspector projection does not accept a window index. Use projection='raw' for a window-specific agent tree.",
					new { kind = "mauiTree", available = false, error = "window-not-supported" },
					includeUi: false);
			tree = await session.GetInspectorTreeAsync(agentPort);
			if (tree is not null)
				InspectorSnapshotService.TrimDepth(tree, depth);
		}
		else if (string.Equals(projection, "raw", StringComparison.OrdinalIgnoreCase))
		{
			using var agent = await session.GetAgentClientAsync(agentPort);
			tree = await agent.GetTreeAsync(depth, window);
		}
		else
		{
			return McpAppMetadata.Result(
				"projection must be 'activeVisual' or 'raw'.",
				new { kind = "mauiTree", available = false, error = "invalid-projection" },
				includeUi: false);
		}

		if (tree == null || tree.Count == 0)
		{
			var text = projection.Equals("activeVisual", StringComparison.OrdinalIgnoreCase)
				? "No canonical Inspector tree is available. Is the broker running and the app connected?"
				: "No visual tree available. Is the agent connected and the app running?";
			return McpAppMetadata.Result(
				text,
				new { kind = "mauiTree", available = false, projection },
				includeUi: false);
		}

		IEnumerable<ElementInfo> result = tree;

		if (elementId != null)
		{
			var subtree = FindElement(tree, elementId);
			if (subtree == null)
				return McpAppMetadata.Result(
					$"Element '{elementId}' not found in the visual tree.",
					new { kind = "mauiTree", available = false, projection, elementId },
					includeUi: false);
			result = [subtree];
		}

		if (filter != null)
		{
			result = FilterByType(result.ToList(), filter);
			if (!result.Any())
				return McpAppMetadata.Result(
					$"No elements of type '{filter}' found in the visual tree.",
					new { kind = "mauiTree", available = false, projection, filter },
					includeUi: false);
		}

		var elements = result.ToList();
		return McpAppMetadata.Result(
			CliJson.SerializeUntyped(elements, indented: false),
			new
			{
				kind = "mauiTree",
				available = true,
				projection,
				elements,
			},
			McpAppMetadata.IsNegotiated(requestContext?.Server.ClientCapabilities));
	}

	private static ElementInfo? FindElement(IEnumerable<ElementInfo> elements, string id)
	{
		foreach (var el in elements)
		{
			if (el.Id == id) return el;
			if (el.Children != null)
			{
				var found = FindElement(el.Children, id);
				if (found != null) return found;
			}
		}
		return null;
	}

	private static List<ElementInfo> FilterByType(List<ElementInfo> elements, string type)
	{
		var result = new List<ElementInfo>();
		foreach (var el in elements)
		{
			if (el.Type.Equals(type, StringComparison.OrdinalIgnoreCase))
				result.Add(el);
			else if (el.Children != null)
			{
				var filtered = FilterByType(el.Children, type);
				if (filtered.Count > 0)
					result.AddRange(filtered);
			}
		}
		return result;
	}
}
