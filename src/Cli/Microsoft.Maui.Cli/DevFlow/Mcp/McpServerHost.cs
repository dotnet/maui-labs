using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using Microsoft.Maui.Cli.DevFlow.Mcp.Tools;

namespace Microsoft.Maui.Cli.DevFlow.Mcp;

public static class McpServerHost
{
	public static async Task RunAsync()
	{
		var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

		var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings { Args = [] });

		// The MCP server uses stdio transport (stdin/stdout for JSON-RPC).
		// The default console logger writes to stdout, corrupting the protocol stream.
		// Redirect all logging to stderr so diagnostics are preserved without pollution.
		builder.Logging.ClearProviders();
		builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

		builder.Services.AddSingleton<McpAgentSession>();

		builder.Services
			.AddMcpServer(options =>
			{
				options.ServerInfo = new() { Name = "maui", Version = version };
			})
			.WithStdioServerTransport()
			.WithTools<ScreenshotTool>()
			.WithTools<TreeTool>()
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
			.WithTools<SensorTools>()
			.WithTools<JobTools>()
			.WithTools<FileTools>()
			.WithTools<BleTools>()
			.WithTools<BatchTools>();

		await builder.Build().RunAsync();
	}
}
