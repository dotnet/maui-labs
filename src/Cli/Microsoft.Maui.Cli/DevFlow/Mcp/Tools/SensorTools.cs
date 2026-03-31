using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Microsoft.Maui.Cli.DevFlow.Mcp;

namespace Microsoft.Maui.Cli.DevFlow.Mcp.Tools;

[McpServerToolType]
public sealed class SensorTools
{
	[McpServerTool(Name = "maui_sensors_list"), Description("List available device sensors and their current status (active/inactive).")]
	public static async Task<string> ListSensors(
		McpAgentSession session,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var result = await agent.GetSensorsAsync();
		return result.ValueKind == JsonValueKind.Undefined ? "Failed to list sensors." : result.ToString();
	}

	[McpServerTool(Name = "maui_sensors_start"), Description("Start a device sensor (e.g., Accelerometer, Gyroscope, Magnetometer, Barometer, Compass, OrientationSensor).")]
	public static async Task<string> StartSensor(
		McpAgentSession session,
		[Description("Sensor name (e.g., Accelerometer, Gyroscope, Magnetometer, Barometer, Compass)")] string sensor,
		[Description("Reading speed: UI, Default, Game, Fastest (default: Default)")] string? speed = null,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var success = await agent.StartSensorAsync(sensor, speed);
		return success ? $"Sensor '{sensor}' started." : $"Failed to start sensor '{sensor}'.";
	}

	[McpServerTool(Name = "maui_sensors_stop"), Description("Stop a running device sensor.")]
	public static async Task<string> StopSensor(
		McpAgentSession session,
		[Description("Sensor name to stop")] string sensor,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		var agent = await session.GetAgentClientAsync(agentPort);
		var success = await agent.StopSensorAsync(sensor);
		return success ? $"Sensor '{sensor}' stopped." : $"Failed to stop sensor '{sensor}'.";
	}
}
