using Microsoft.Maui.DevFlow.Agent;
using Microsoft.Maui.Platforms.MacOS.Essentials;
using Microsoft.Maui.Platforms.MacOS.Hosting;

namespace DocumentExtractionSample;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiAppMacOS<App>()
			.AddMacOSEssentials();

#if DEBUG
		builder.AddMauiDevFlowAgent(options =>
		{
			options.Port = ResolveAgentPort();
			options.EnableLayoutDiagnostics = true;
		});
#endif

		return builder.Build();
	}

	private static int ResolveAgentPort() =>
		int.TryParse(
			Environment.GetEnvironmentVariable("DEVFLOW_TEST_PORT"),
			out var port)
			? port
			: 9240;
}
