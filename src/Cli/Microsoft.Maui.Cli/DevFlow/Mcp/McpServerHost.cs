using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using Microsoft.Maui.Cli.DevFlow.Mcp.Tools;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.Cli.DevFlow.Mcp;

public static class McpServerHost
{
	public static Task RunAsync() => RunAsync("full");

	internal static bool IsTestAgentProfile(string profile, string? enabled, string? killSwitches)
	{
		if (profile == "full")
			return false;
		if (profile != "test-agent")
			throw new ArgumentException("Unknown MCP profile. Use full or test-agent.", nameof(profile));
		if (!string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase) ||
			(killSwitches ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
				.Contains("agent-authoring", StringComparer.OrdinalIgnoreCase))
			throw new InvalidOperationException("The test-agent preview is disabled. Explicitly enable DEVFLOW_PREVIEW_AGENT_AUTHORING.");
		return true;
	}

	public static async Task RunAsync(string profile)
	{
		var testAgent = IsTestAgentProfile(profile,
			Environment.GetEnvironmentVariable("DEVFLOW_PREVIEW_AGENT_AUTHORING"),
			Environment.GetEnvironmentVariable("DEVFLOW_PREVIEW_KILL_SWITCHES"));
		var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

		var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings { Args = [] });

		// The MCP server uses stdio transport (stdin/stdout for JSON-RPC).
		// The default console logger writes to stdout, corrupting the protocol stream.
		// Redirect all logging to stderr so diagnostics are preserved without pollution.
		builder.Logging.ClearProviders();
		builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

		builder.Services.AddSingleton<McpAgentSession>();

		var server = builder.Services
			.AddMcpServer(options =>
			{
				options.ServerInfo = new() { Name = "maui", Version = version };
			})
			.WithStdioServerTransport()
			.WithRequestFilters(filters => filters.AddCallToolFilter(next => async (context, cancellationToken) =>
			{
				// A native (non-MAUI) agent answers 501 for capabilities its backend does not
				// implement. Surface that as an actionable tool error instead of letting the SDK
				// swallow it behind a generic "an error occurred" message.
				try
				{
					return await next(context, cancellationToken);
				}
				catch (NotSupportedByAgentException ex)
				{
					throw new McpException(
						$"{ex.Message} Call maui_capabilities to see what this agent supports.");
				}
			}));

		if (testAgent)
		{
			server.WithTools<PreviewTestAgentTools>();
		}
		else
		{
			server.WithTools<ScreenshotTool>()
			.WithTools<TreeTool>()
			.WithTools<LayoutDiagnosticsTool>()
			.WithTools<LogsTool>()
			.WithTools<NetworkTool>()
			.WithTools<InteractionTools>()
			.WithTools<PropertyTools>()
			.WithTools<NavigationTools>()
			.WithTools<QueryTools>()
			.WithTools<AgentTools>()
			.WithTools<CdpTools>()
			.WithTools<AssertTool>()
			.WithTools<RecordingTools>()
			.WithTools<PreferencesTools>()
			.WithTools<PlatformTools>()
			.WithTools<ThemeTools>()
			.WithTools<SensorTools>()
			.WithTools<JobTools>()
			.WithTools<FileTools>()
			.WithTools<BatchTools>()
			.WithTools<InvokeTools>()
			.WithTools<ExtensionTools>()
			.WithTools<Flows.FlowTools>()
			.WithTools<Flows.FlowRecordTools>();
		}

		await builder.Build().RunAsync();
	}
}
