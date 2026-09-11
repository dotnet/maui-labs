using Microsoft.Maui.DevFlow.Agent;

namespace DocumentExtractionSample;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder.UseMauiApp<App>();

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
