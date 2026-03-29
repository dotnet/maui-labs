using Microsoft.Extensions.DependencyInjection;

namespace Platform.Maui.Linux.Gtk4.BlazorWebView;

public static class BlazorWebViewExtensions
{
	/// <summary>
	/// Adds Linux-specific BlazorWebView services.
	/// </summary>
	public static IServiceCollection AddLinuxGtk4BlazorWebView(this IServiceCollection services)
	{
		// Register any BlazorWebView-specific services here
		return services;
	}
}
