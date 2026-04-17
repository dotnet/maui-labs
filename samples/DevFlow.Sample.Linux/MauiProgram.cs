using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Components.WebView.Maui;
using Platform.Maui.Linux.Gtk4.Hosting;
using Microsoft.Maui.DevFlow.Agent.Gtk;
using Microsoft.Maui.DevFlow.Blazor.Gtk;

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
			.UseMauiAppLinuxGtk4<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});

		// Blazor WebView — register services then override handler for GTK
		builder.Services.AddMauiBlazorWebView();
		builder.ConfigureMauiHandlers(handlers =>
		{
			handlers.AddHandler<IBlazorWebView, Platform.Maui.Linux.Gtk4.BlazorWebView.BlazorWebViewHandler>();
		});

		// Shared data
		builder.Services.AddSingleton<TodoService>();
		builder.Services.AddHttpClient();

		// Pages (DI-resolved by Shell's DataTemplate)
		builder.Services.AddTransient<MainPage>();
		builder.Services.AddTransient<BlazorTodoPage>();
		builder.Services.AddTransient<NetworkTestPage>();

#if DEBUG
		builder.Logging.AddDebug();
		builder.AddMauiDevFlowAgent(options =>
		{
			options.Port = ResolveAgentPort();
			options.EnableProfiler = true;
		});
		builder.AddMauiBlazorDevFlowTools();
#endif

		return builder.Build();
	}
}
