using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Hosting;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.BlazorWebView;

public static class BlazorWebViewExtensions
{
	/// <summary>
	/// Adds the shared MAUI BlazorWebView services and selects the GTK4 handler.
	/// </summary>
#if NET11_0_OR_GREATER
	public static IMauiBlazorWebViewBuilder AddLinuxGtk4BlazorWebView(this IServiceCollection services)
	{
		return services
			.AddMauiBlazorWebView()
			.UsePlatformHandler<BlazorWebViewHandler>();
	}
#else
	public static IServiceCollection AddLinuxGtk4BlazorWebView(this IServiceCollection services)
	{
		services.AddMauiBlazorWebView();
		services.ConfigureMauiHandlers(handlers =>
			handlers.AddHandler<Microsoft.AspNetCore.Components.WebView.Maui.BlazorWebView, BlazorWebViewHandler>());
		return services;
	}
#endif
}
