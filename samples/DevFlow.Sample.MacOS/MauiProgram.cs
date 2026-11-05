using Microsoft.Extensions.Logging;
using Microsoft.Maui.Platforms.MacOS.Hosting;
using Microsoft.Maui.Platforms.MacOS.Essentials;
using Microsoft.Maui.DevFlow.Agent;
using Microsoft.Maui.DevFlow.Blazor;

namespace DevFlow.Sample;

public static partial class MauiProgram
{
	static int ResolveAgentPort()
		=> int.TryParse(Environment.GetEnvironmentVariable("DEVFLOW_TEST_PORT"), out var envPort)
			? envPort
			: 9223;

	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiAppMacOS<App>()
			.AddMacOSEssentials()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});

		// Blazor WebView
		builder.Services.AddMauiBlazorWebView();
		builder.AddMacOSBlazorWebView();

		// Shared data
		builder.Services.AddSingleton<TodoService>();

		// Pages (DI-resolved by Shell's DataTemplate)
		builder.Services.AddTransient<MainPage>();
		builder.Services.AddTransient<BlazorTodoPage>();

#if DEBUG
		builder.Logging.AddDebug();
		builder.AddMauiDevFlowAgent(options =>
		{
			options.Port = ResolveAgentPort();
			options.EnableProfiler = true;
			options.EnableLayoutDiagnostics = true;
			var diagnostics = options.RegisterExtension(
				"com.example.diagnostics",
				"Sample diagnostics extension",
				"1.0.0",
				new[] { "seed_pref" });
			PreferenceDiagnostics.Register(diagnostics);
		});
		builder.AddMauiBlazorDevFlowTools();
#endif

		return builder.Build();
	}
}
