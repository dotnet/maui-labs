using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol;
using Microsoft.Maui.Cli.DevFlow.Mcp.Tools;

namespace Microsoft.Maui.Cli.DevFlow.Mcp;

public static class McpServerHost
{
	public static async Task RunAsync()
	{
		var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

		var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings { Args = [] });

		builder.Services.AddSingleton<McpAgentSession>();

		builder.Services
			.AddMcpServer(options =>
			{
				options.ServerInfo = new() { Name = "maui-devflow", Version = version };
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
			.WithTools<SensorTools>();

		await builder.Build().RunAsync();
	}
}
