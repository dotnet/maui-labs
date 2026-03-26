using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using Microsoft.Maui.Cli.DevFlow.Mcp;

namespace Microsoft.Maui.Cli.DevFlow.Mcp.Tools;

[McpServerToolType]
public sealed class LogsTool
{
	[McpServerTool(Name = "maui_logs"), Description("Retrieve app logs (ILogger output and WebView console logs). Returns structured JSON log entries with timestamp, level, category, and message.")]
	public static async Task<string> Logs(
		McpAgentSession session,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null,
		[Description("Maximum number of log entries to return (default: 50)")] int limit = 50,
		[Description("Number of newest entries to skip (for pagination)")] int skip = 0,
		[Description("Minimum log level: trace, debug, info, warning, error, critical")] string? minLevel = null,
		[Description("Log source filter: native, webview, or all (default: all)")] string? source = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var response = await agent.GetLogsAsync(limit, skip, source);

		if (string.IsNullOrEmpty(minLevel))
			return response;

		var levelOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
		{
			["trace"] = 0, ["debug"] = 1, ["info"] = 2, ["information"] = 2,
			["warning"] = 3, ["warn"] = 3, ["error"] = 4, ["critical"] = 5, ["fatal"] = 5
		};

		if (!levelOrder.TryGetValue(minLevel, out var minOrd))
			return response;

		var entries = CliJson.ParseElement(response);
		if (entries.ValueKind != JsonValueKind.Array)
			return response;

		var filtered = new JsonArray();
		foreach (var entry in entries.EnumerateArray())
		{
			var level = entry.TryGetProperty("l", out var l) ? l.GetString() :
			            entry.TryGetProperty("level", out var lv) ? lv.GetString() : null;
			if (level == null || (levelOrder.TryGetValue(level, out var ord) && ord >= minOrd))
				filtered.Add(JsonNode.Parse(entry.GetRawText()));
		}

		return CliJson.SerializeUntyped(filtered, indented: false);
	}
}
