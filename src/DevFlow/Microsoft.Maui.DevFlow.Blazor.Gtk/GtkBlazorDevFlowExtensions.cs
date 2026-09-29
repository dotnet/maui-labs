using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.LifecycleEvents;
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
using System.Runtime.Versioning;

namespace Microsoft.Maui.DevFlow.Blazor.Gtk;

/// <summary>
/// Extension methods for registering DevFlow Blazor debug tools in Microsoft.Maui.Platforms.Linux.Gtk4 apps.
/// </summary>
[SupportedOSPlatform("linux")]
public static class GtkBlazorDevFlowExtensions
{
    /// <summary>
    /// Adds WebKitGTK CDP debugging. Wires the registered agent and starts WebView discovery
    /// after the first GTK window is created, regardless of how long app startup takes.
    /// </summary>
    public static MauiAppBuilder AddMauiBlazorDevFlowTools(this MauiAppBuilder builder, bool enableLogging = false)
    {
        builder.Services.AddSingleton(_ =>
        {
            var service = new GtkBlazorWebViewDebugService();
            if (enableLogging)
                service.LogCallback = msg => System.Diagnostics.Debug.WriteLine(msg);
            return service;
        });
        ConfigureStartup(builder, () => GtkMauiApplication.Current.Services);
        return builder;
    }

    internal static void ConfigureStartup(MauiAppBuilder builder, Func<IServiceProvider> getServices)
    {
        builder.ConfigureLifecycleEvents(lifecycle => lifecycle.AddGtk(gtk =>
            gtk.OnWindowCreated(_ =>
            {
                var services = getServices();
                var service = services.GetRequiredService<GtkBlazorWebViewDebugService>();
                service.WireToAgent(services.GetRequiredService<DevFlowAgentService>());
                service.StartWebViewDiscovery();
            })));
    }

    /// <summary>
    /// Explicitly wires CDP after GTK startup. Repeated calls are safe; normal builder
    /// registration already wires CDP and starts discovery on the first window.
    /// </summary>
    public static void WireBlazorCdpToAgent(this GtkBlazorWebViewDebugService blazorService)
    {
        ArgumentNullException.ThrowIfNull(blazorService);
        blazorService.WireToAgent(
            GtkMauiApplication.Current.Services.GetRequiredService<DevFlowAgentService>());
    }
}
