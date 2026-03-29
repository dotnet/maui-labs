using MauiIcons.FontAwesome;
using MauiIcons.FontAwesome.Solid;
using Platform.Maui.Linux.Gtk4.Hosting;
using Platform.Maui.Linux.Gtk4.Essentials.Hosting;
using Platform.Maui.Linux.Gtk4.BlazorWebView;
#if MAUIDEVFLOW
using MauiDevFlow.Agent.Gtk;
using MauiDevFlow.Blazor.Gtk;
#endif
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.LifecycleEvents;

namespace Platform.Maui.Linux.Gtk4.Sample;

public static class MauiProgram
{
#if MAUIDEVFLOW
	static bool _devFlowAgentStarted;
#endif

	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp
			.CreateBuilder()
			.UseMauiAppLinuxGtk4<App>()
			.AddLinuxGtk4Essentials()
			.UseFontAwesomeMauiIcons()
			.UseFontAwesomeSolidMauiIcons();

		builder.ConfigureFonts(fonts =>
		{
			fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
		});

		builder.Services.AddBlazorWebView();
		builder.Services.AddLinuxGtk4BlazorWebView();

		builder.ConfigureMauiHandlers(handlers =>
		{
			handlers.AddHandler<Microsoft.AspNetCore.Components.WebView.Maui.BlazorWebView, BlazorWebViewHandler>();
		});

#if MAUIDEVFLOW
		builder.AddMauiDevFlowAgent();
		builder.AddMauiBlazorDevFlowTools();

		builder.ConfigureLifecycleEvents(lifecycle =>
		{
			lifecycle.AddGtk(gtk =>
			{
				gtk.OnWindowCreated(_ =>
				{
					if (_devFlowAgentStarted)
						return;

					if (Microsoft.Maui.Controls.Application.Current is Microsoft.Maui.Controls.Application application)
					{
						application.StartDevFlowAgent();
						_devFlowAgentStarted = true;
					}
				});
			});
		});
#endif

		return builder.Build();
	}
}
