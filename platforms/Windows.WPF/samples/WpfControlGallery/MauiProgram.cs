using Microsoft.Extensions.DependencyInjection;
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
			.AddMauiDevFlowAgent();

		builder.ConfigureFonts(fonts =>
		{
			fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
		});

		// Blazor hybrid
		builder.Services.AddMauiBlazorWebView();
		builder.Services.AddWpfBlazorWebView();

		return builder.Build();
	}
}
