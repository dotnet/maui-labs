using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls.Hosting.WPF;
#if MAUIDEVFLOW
using Microsoft.Maui.DevFlow.Agent.WPF;
#endif
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
			.UseWPFEssentials();

#if MAUIDEVFLOW
		builder.AddMauiDevFlowAgent(options =>
		{
			options.EnableLayoutDiagnostics = true;
			options.EnableFileLogging = false;
		});
#endif

		builder.ConfigureFonts(SampleFontRegistration.Configure);

		// Blazor hybrid
		builder.Services.AddMauiBlazorWebView();
		builder.Services.AddWpfBlazorWebView();

		return builder.Build();
	}
}
