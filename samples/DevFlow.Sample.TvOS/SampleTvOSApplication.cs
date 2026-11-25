using Foundation;
using Microsoft.Maui.Platform.TvOS.Hosting;
#if DEBUG
using Microsoft.Maui.DevFlow.Agent;
#endif

namespace DevFlow.Sample.TvOS;

[Register(nameof(SampleTvOSApplication))]
public class SampleTvOSApplication : TvOSMauiApplication
{
	static int ResolveAgentPort()
		=> int.TryParse(Environment.GetEnvironmentVariable("DEVFLOW_TEST_PORT"), out var envPort)
			? envPort
			: 9223;

	protected override MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder.UseTvOSMauiApp<App>();

#if DEBUG
		builder.AddMauiDevFlowAgent(options => options.Port = ResolveAgentPort());
#endif

		return builder.Build();
	}
}
