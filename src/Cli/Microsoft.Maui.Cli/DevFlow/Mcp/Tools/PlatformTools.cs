using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Microsoft.Maui.Cli.DevFlow.Mcp;

namespace Microsoft.Maui.Cli.DevFlow.Mcp.Tools;

[McpServerToolType]
public sealed class PlatformTools
{
	[McpServerTool(Name = "maui_app_info"), Description("Get app name, version, package name, build number, and theme.")]
	public static async Task<string> GetAppInfo(
		McpAgentSession session,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var result = await agent.GetPlatformInfoAsync("app-info");
		return result.ValueKind == JsonValueKind.Undefined ? "Failed to get app info." : result.ToString();
	}

	[McpServerTool(Name = "maui_device_info"), Description("Get device manufacturer, model, OS version, platform, and idiom.")]
	public static async Task<string> GetDeviceInfo(
		McpAgentSession session,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var result = await agent.GetPlatformInfoAsync("device-info");
		return result.ValueKind == JsonValueKind.Undefined ? "Failed to get device info." : result.ToString();
	}

	[McpServerTool(Name = "maui_display_info"), Description("Get screen width, height, density, orientation, rotation, and refresh rate.")]
	public static async Task<string> GetDisplayInfo(
		McpAgentSession session,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var result = await agent.GetPlatformInfoAsync("device-display");
		return result.ValueKind == JsonValueKind.Undefined ? "Failed to get display info." : result.ToString();
	}

	[McpServerTool(Name = "maui_battery_info"), Description("Get battery level, charging state, power source, and energy saver status.")]
	public static async Task<string> GetBatteryInfo(
		McpAgentSession session,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var result = await agent.GetPlatformInfoAsync("battery");
		return result.ValueKind == JsonValueKind.Undefined ? "Failed to get battery info." : result.ToString();
	}

	[McpServerTool(Name = "maui_connectivity"), Description("Get network access status and connection profiles (WiFi, Cellular, etc.).")]
	public static async Task<string> GetConnectivity(
		McpAgentSession session,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var result = await agent.GetPlatformInfoAsync("connectivity");
		return result.ValueKind == JsonValueKind.Undefined ? "Failed to get connectivity info." : result.ToString();
	}

	[McpServerTool(Name = "maui_geolocation"), Description("Get current GPS coordinates (latitude, longitude, altitude, accuracy).")]
	public static async Task<string> GetGeolocation(
		McpAgentSession session,
		[Description("Accuracy: default, coarse, fine")] string? accuracy = null,
		[Description("Timeout in seconds (default: 10)")] int? timeoutSeconds = null,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var result = await agent.GetGeolocationAsync(accuracy, timeoutSeconds);
		return result.ValueKind == JsonValueKind.Undefined ? "Failed to get geolocation." : result.ToString();
	}
}
