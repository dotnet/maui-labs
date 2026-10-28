using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Microsoft.Maui.Cli.DevFlow.Mcp;

namespace Microsoft.Maui.Cli.DevFlow.Mcp.Tools;

[McpServerToolType]
public sealed class JobTools
{
	[McpServerTool(Name = "maui_jobs_list"), Description("List background jobs registered on the device (Android Workers via WorkManager, iOS BGTasks via BGTaskScheduler).")]
	public static async Task<string> ListJobs(
		McpAgentSession session,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		using var agent = await session.GetAgentClientAsync(agentPort);
		var result = await agent.GetJobsAsync();
		return result.ValueKind == JsonValueKind.Undefined ? "Failed to list jobs." : result.ToString();
	}

	[McpServerTool(Name = "maui_jobs_run"), Description("Request a BGTask launch on physical iOS devices using a Debug-built agent library. Success acknowledges the launch request, not handler execution or completion; verify the handler's own outcome. Release-built NuGet agents cannot trigger it. Android forcing is available only through 'maui devflow ui jobs run' via host-side adb, not this tool.")]
	public static async Task<string> RunJob(
		McpAgentSession session,
		[Description("Job identifier returned by maui_jobs_list (Android WorkManager id or iOS BGTask identifier)")] string identifier,
		[Description("Optional iOS BGTask type: 'processing' or 'refresh'. If omitted, the agent resolves it from pending requests when possible.")] string? type = null,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
	{
		using var agent = await session.GetAgentClientAsync(agentPort);
		var result = await agent.RunJobAsync(identifier, type);
		return result.ValueKind == JsonValueKind.Undefined ? $"Failed to run job '{identifier}'." : result.ToString();
	}
}
