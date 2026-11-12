using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Extensions.DependencyInjection;

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
		// MAUI 10 has no public UsePlatformHandler contract. Existing MAUI 10
		// applications continue to register the handler with ConfigureMauiHandlers.
		return services;
	}
#endif
}
