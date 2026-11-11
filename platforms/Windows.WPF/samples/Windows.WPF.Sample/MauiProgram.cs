using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Maui.Controls.Hosting.WPF;
using Microsoft.Maui.DevFlow.Agent.WPF;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Windows.WPF.Essentials;

namespace Microsoft.Maui.Platforms.Windows.WPF.Sample;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp
			.CreateBuilder()
			.UseMauiAppWPF<MainApp>()
			.UseWPFEssentials()
			.AddMauiDevFlowAgent(options =>
			{
				options.EnableLayoutDiagnostics = true;
				options.EnableFileLogging = false;
			});

		builder.ConfigureFonts(SampleFontRegistration.Configure);

		// Blazor hybrid
		builder.Services
			.AddMauiBlazorWebView()
			.UsePlatformHandler<Microsoft.AspNetCore.Components.WebView.Maui.WPF.BlazorWebViewHandler>();

		return builder.Build();
	}
}
