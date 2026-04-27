using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.MacOS.Controls;
using Microsoft.Maui.Platforms.MacOS.Handlers;

namespace Microsoft.Maui.Platforms.MacOS.Hosting;

public static class BlazorWebViewExtensions
{
    /// <summary>
    /// Adds Blazor Hybrid support for macOS AppKit. Registers the BlazorWebView handler
    /// that hosts Blazor components in a WKWebView.
    /// </summary>
    public static MauiAppBuilder AddMacOSBlazorWebView(this MauiAppBuilder builder)
    {
        builder.ConfigureMauiHandlers(handlers =>
        {
            handlers.AddHandler<MacOSBlazorWebView, BlazorWebViewHandler>();
        });
        return builder;
    }
}
