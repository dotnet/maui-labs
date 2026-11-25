using System.ComponentModel;
using ModelContextProtocol.Server;
using Microsoft.Maui.Cli.DevFlow.Mcp;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.Cli.DevFlow.Mcp.Tools;

[McpServerToolType]
public sealed class RecordingTools
{
	[McpServerTool(Name = "maui_recording_start"), Description("Start screen recording of the running app. Uses simctl for iOS/tvOS simulators, xcrun for Mac Catalyst, and scrcpy for Android.")]
	public static async Task<string> RecordingStart(
		McpAgentSession session,
		[Description("Output file path (default: recording_<timestamp>.mp4)")] string? output = null,
		[Description("Max recording duration in seconds (default: 30)")] int timeout = 30,
		[Description("Agent HTTP port (optional, used to detect platform)")] int? agentPort = null)
	{
		try
		{
			using var agent = await session.GetAgentClientAsync(agentPort);
			var status = await agent.GetStatusAsync();
			var platform = status?.Platform ?? throw new InvalidOperationException("The selected agent did not report its platform.");
			var device = HostOperations.IsAppleSimulatorPlatform(platform)
				? await HostOperations.ResolveUdidAsync(null, status)
				: session.GetAndroidDeviceSerial(new Uri(agent.BaseUrl).Port);

			var filename = output ?? $"recording_{DateTime.Now:yyyyMMdd_HHmmss}.mp4";
			var operations = new RecordingOperations(DevFlowCommands.CreateRecordingDriver, RecordingStateManager.Load);
			await operations.StartAsync(platform, device, filename, timeout);

			return $"Recording started (timeout: {timeout}s). Output: {Path.GetFullPath(filename)}";
		}
		catch (Exception ex)
		{
			return $"Error starting recording: {ex.Message}";
		}
	}

	[McpServerTool(Name = "maui_recording_stop"), Description("Stop the active screen recording and save the video file.")]
	public static async Task<string> RecordingStop(
		McpAgentSession session,
		[Description("Agent HTTP port (optional, used to detect platform)")] int? agentPort = null)
	{
		try
		{
			var operations = new RecordingOperations(DevFlowCommands.CreateRecordingDriver, RecordingStateManager.Load);
			var (outputFile, size) = await operations.StopAsync();

			return $"Recording saved: {outputFile} ({size} bytes)";
		}
		catch (Exception ex)
		{
			return $"Error stopping recording: {ex.Message}";
		}
	}

	[McpServerTool(Name = "maui_recording_status"), Description("Check if a screen recording is currently in progress.")]
	public static Task<string> RecordingStatus()
	{
		var state = Microsoft.Maui.DevFlow.Driver.RecordingStateManager.Load();
		if (state == null || !Microsoft.Maui.DevFlow.Driver.RecordingStateManager.IsRecording())
			return Task.FromResult("No active recording.");

		var elapsed = DateTimeOffset.UtcNow - state.StartedAt;
		return Task.FromResult(
			$"Recording in progress: platform={state.Platform}, output={state.OutputFile}, " +
			$"elapsed={elapsed.TotalSeconds:F0}s/{state.TimeoutSeconds}s, pid={state.RecordingPid}");
	}
}
