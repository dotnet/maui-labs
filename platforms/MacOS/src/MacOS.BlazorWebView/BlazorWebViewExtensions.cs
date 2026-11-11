using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Hosting;
#if !NET11_0_OR_GREATER
using Microsoft.Maui.Platforms.MacOS.Controls;
#endif
using Microsoft.Maui.Platforms.MacOS.Handlers;

namespace Microsoft.Maui.Platforms.MacOS.Hosting;

public static class BlazorWebViewExtensions
{
#if NET11_0_OR_GREATER
    /// <summary>
    /// Adds the shared MAUI BlazorWebView services and selects the AppKit handler.
    /// </summary>
    public static IMauiBlazorWebViewBuilder AddMacOSBlazorWebView(this IServiceCollection services)
        => services
            .AddMauiBlazorWebView()
            .UsePlatformHandler<Microsoft.Maui.Platforms.MacOS.Handlers.BlazorWebViewHandler>();
#endif

    /// <summary>
    /// Adds Blazor Hybrid support for macOS AppKit.
    /// </summary>
    public static MauiAppBuilder AddMacOSBlazorWebView(this MauiAppBuilder builder)
    {
#if NET11_0_OR_GREATER
        builder.Services.AddMacOSBlazorWebView();
#else
        builder.ConfigureMauiHandlers(handlers =>
        {
            handlers.AddHandler<MacOSBlazorWebView, Microsoft.Maui.Platforms.MacOS.Handlers.BlazorWebViewHandler>();
        });
#endif
        return builder;
    }
}
