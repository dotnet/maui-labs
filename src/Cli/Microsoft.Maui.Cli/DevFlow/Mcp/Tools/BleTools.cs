using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Microsoft.Maui.Cli.DevFlow.Mcp;

namespace Microsoft.Maui.Cli.DevFlow.Mcp.Tools;

[McpServerToolType]
public sealed class BleTools
{
	[McpServerTool(Name = "maui_ble_status"), Description("Get BLE monitor status including whether scanning is active, event count, and subscriber count.")]
	public static async Task<string> GetBleStatus(
		McpAgentSession session,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var result = await agent.GetBleStatusAsync();
		return result.ValueKind == JsonValueKind.Undefined ? "Failed to get BLE status." : result.ToString();
	}

	[McpServerTool(Name = "maui_ble_events"), Description("Get recent BLE events (scan results, connections, disconnections, reads, writes, notifications). Optionally filter by event type.")]
	public static async Task<string> GetBleEvents(
		McpAgentSession session,
		[Description("Max events to return (default 100)")] int? limit = null,
		[Description("Filter by event type: scan_result, connected, disconnected, characteristic_read, characteristic_write, notification, service_discovered, mtu_changed, descriptor_write, bond_state_changed")] string? type = null,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var result = await agent.GetBleEventsAsync(limit ?? 100, type);
		return result.ValueKind == JsonValueKind.Undefined ? "Failed to get BLE events." : result.ToString();
	}

	[McpServerTool(Name = "maui_ble_stream_url"), Description("Get the WebSocket URL for streaming live BLE events. The stream can replay buffered events on connect without consuming them.")]
	public static async Task<string> GetBleStreamUrl(
		McpAgentSession session,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null,
		[Description("Start BLE scanning automatically for the lifetime of this stream when supported (default false)")] bool scan = false,
		[Description("Number of buffered events to replay when the stream opens. Use 0 for live-only streaming. Default is 100.")] int replay = 100,
		[Description("Optional event type filter, such as scan_result, connected, disconnected, notification, or a custom app-defined event type.")] string? type = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		return agent.GetBleWebSocketUrl(scan, replay, type);
	}

	[McpServerTool(Name = "maui_ble_scan_start"), Description("Start a BLE scan to discover nearby Bluetooth Low Energy devices. Scan results appear as BLE events.")]
	public static async Task<string> StartBleScan(
		McpAgentSession session,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var success = await agent.StartBleScanAsync();
		return success ? "BLE scan started." : "Failed to start BLE scan.";
	}

	[McpServerTool(Name = "maui_ble_scan_stop"), Description("Stop an active BLE scan.")]
	public static async Task<string> StopBleScan(
		McpAgentSession session,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var success = await agent.StopBleScanAsync();
		return success ? "BLE scan stopped." : "Failed to stop BLE scan.";
	}

	[McpServerTool(Name = "maui_ble_events_clear"), Description("Clear all recorded BLE events.")]
	public static async Task<string> ClearBleEvents(
		McpAgentSession session,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var success = await agent.ClearBleEventsAsync();
		return success ? "BLE events cleared." : "Failed to clear BLE events.";
	}
}
