using System.ComponentModel;
using ModelContextProtocol.Server;
using Microsoft.Maui.Cli.DevFlow.Mcp;

namespace Microsoft.Maui.Cli.DevFlow.Mcp.Tools;

[McpServerToolType]
public sealed class ProfilerTools
{
	[McpServerTool(Name = "maui_profiler_capabilities"), Description("Get profiler capabilities for the connected agent. Call this first to check what profiling features are supported (FPS, memory, GC, CPU, jank detection, etc.) before starting a session.")]
	public static async Task<string> GetCapabilities(
		McpAgentSession session,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var capabilities = await agent.GetProfilerCapabilitiesAsync();
		if (capabilities == null)
			return "Failed to get profiler capabilities. The agent may not support profiling.";

		return CliJson.SerializeUntyped(capabilities, indented: false);
	}

	[McpServerTool(Name = "maui_profiler_start"), Description("Start a profiler session to collect performance samples (FPS, memory, GC, CPU). Returns session info with a sessionId for use with other profiler tools.")]
	public static async Task<string> Start(
		McpAgentSession session,
		[Description("Sample collection interval in milliseconds (default: server-defined, typically 500ms)")] int? sampleIntervalMs = null,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var sessionInfo = await agent.StartProfilerAsync(sampleIntervalMs);
		if (sessionInfo == null)
			return "Failed to start profiler session. Check maui_profiler_capabilities first.";

		return CliJson.SerializeUntyped(sessionInfo, indented: false);
	}

	[McpServerTool(Name = "maui_profiler_stop"), Description("Stop an active profiler session. Returns the final session info with isActive=false.")]
	public static async Task<string> Stop(
		McpAgentSession session,
		[Description("Session ID to stop (default: current active session)")] string? sessionId = null,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var sessionInfo = await agent.StopProfilerAsync(sessionId);
		if (sessionInfo == null)
			return "Failed to stop profiler session. No active session found.";

		return CliJson.SerializeUntyped(sessionInfo, indented: false);
	}

	[McpServerTool(Name = "maui_profiler_samples"), Description("Get profiler samples (FPS, memory, GC stats, CPU, jank events) from an active or stopped session. Uses cursor-based pagination — pass the returned cursor values in subsequent calls to get only new data.")]
	public static async Task<string> GetSamples(
		McpAgentSession session,
		[Description("Session ID (default: current session)")] string? sessionId = null,
		[Description("Sample cursor from previous response for incremental polling (default: 0 = from start)")] long sampleCursor = 0,
		[Description("Marker cursor from previous response for incremental polling (default: 0 = from start)")] long markerCursor = 0,
		[Description("Span cursor from previous response for incremental polling (default: 0 = from start)")] long spanCursor = 0,
		[Description("Maximum number of samples to return (default: 500)")] int limit = 500,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var batch = await agent.GetProfilerSamplesAsync(sessionId, sampleCursor, markerCursor, spanCursor, limit);
		if (batch == null)
			return "Failed to get profiler samples. No active session found.";

		return CliJson.SerializeUntyped(batch, indented: false);
	}

	[McpServerTool(Name = "maui_profiler_hotspots"), Description("Get profiler hotspots — aggregated slow operations sorted by impact. Identifies the most expensive UI operations, navigation events, and other spans that exceed the duration threshold.")]
	public static async Task<string> GetHotspots(
		McpAgentSession session,
		[Description("Maximum number of hotspots to return (default: 20, max: 200)")] int limit = 20,
		[Description("Minimum duration in milliseconds to include (default: 16ms = one frame budget)")] int minDurationMs = 16,
		[Description("Filter by span kind (e.g., 'ui.operation', 'navigation', 'http.request')")] string? kind = null,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var hotspots = await agent.GetProfilerHotspotsAsync(limit, minDurationMs, kind);
		if (hotspots.Count == 0)
			return "No hotspots found matching the criteria.";

		return CliJson.SerializeUntyped(hotspots, indented: false);
	}

	[McpServerTool(Name = "maui_profiler_marker"), Description("Publish a named marker into the profiler timeline. Use markers to annotate specific moments (e.g., 'user tapped login', 'data loaded') for correlation with performance data.")]
	public static async Task<string> PublishMarker(
		McpAgentSession session,
		[Description("Marker name (e.g., 'user_tapped_login', 'page_loaded')")] string name,
		[Description("Marker type category (default: 'user.action')")] string type = "user.action",
		[Description("Optional JSON payload to attach to the marker")] string? payloadJson = null,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var success = await agent.PublishProfilerMarkerAsync(name, type, payloadJson);
		return success
			? $"Marker '{name}' published successfully."
			: $"Failed to publish marker '{name}'.";
	}
}
