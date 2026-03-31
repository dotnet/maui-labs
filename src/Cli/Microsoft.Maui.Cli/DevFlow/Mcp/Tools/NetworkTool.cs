using System.ComponentModel;
using ModelContextProtocol.Server;
using Microsoft.Maui.Cli.DevFlow.Mcp;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.Cli.DevFlow.Mcp.Tools;

[McpServerToolType]
public sealed class NetworkTool
{
	[McpServerTool(Name = "maui_network"), Description("List captured HTTP network requests from the running app. Returns structured data with method, URL, status code, duration, and sizes.")]
	public static async Task<string> NetworkList(
		McpAgentSession session,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null,
		[Description("Maximum number of requests to return (default: 50)")] int limit = 50,
		[Description("Filter by host name")] string? host = null,
		[Description("Filter by HTTP method (GET, POST, etc.)")] string? method = null,
		[Description("Filter by status: '4xx', '5xx', '200', etc.")] string? statusFilter = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var requests = await agent.GetNetworkRequestsAsync(limit, host, method);
		if (requests == null || requests.Count == 0)
			return "No network requests captured. Ensure DevFlowHttpHandler is configured in the app.";

		if (!string.IsNullOrEmpty(statusFilter))
		{
			requests = statusFilter.ToLowerInvariant() switch
			{
				"4xx" => requests.Where(r => r.StatusCode >= 400 && r.StatusCode < 500).ToList(),
				"5xx" => requests.Where(r => r.StatusCode >= 500 && r.StatusCode < 600).ToList(),
				"2xx" => requests.Where(r => r.StatusCode >= 200 && r.StatusCode < 300).ToList(),
				"3xx" => requests.Where(r => r.StatusCode >= 300 && r.StatusCode < 400).ToList(),
				_ when int.TryParse(statusFilter, out var code) => requests.Where(r => r.StatusCode == code).ToList(),
				_ => requests
			};
		}

		return CliJson.SerializeUntyped(requests, indented: false);
	}

	[McpServerTool(Name = "maui_network_detail"), Description("Get full details of a captured HTTP request including headers and body.")]
	public static async Task<string> NetworkDetail(
		McpAgentSession session,
		[Description("The request ID from maui_network results")] string requestId,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var detail = await agent.GetNetworkRequestDetailAsync(requestId);
		if (detail == null)
			return $"Network request '{requestId}' not found.";

		return CliJson.SerializeUntyped(detail, indented: false);
	}

	[McpServerTool(Name = "maui_network_clear"), Description("Clear all captured network requests from the buffer.")]
	public static async Task<string> NetworkClear(
		McpAgentSession session,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var success = await agent.ClearNetworkRequestsAsync();
		return success ? "Network request buffer cleared." : "Failed to clear network requests.";
	}
}
