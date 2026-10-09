using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.DevFlow.WebView;
using Microsoft.Maui.Hosting;

namespace Microsoft.Maui.DevFlow.Blazor;

public static class BlazorDevFlowExtensions
{
    /// <summary>Add Blazor tools once. Generic options remain independent; the first Blazor configuration wins.</summary>
    public static MauiAppBuilder AddMauiBlazorDevFlowTools(this MauiAppBuilder builder,
        Action<BlazorWebViewDebugOptions>? configure = null)
    {
        if (builder.Services.Any(d => d.ServiceType == typeof(BlazorWebViewDebugService)))
        {
            builder.AddMauiWebViewDevFlowTools();
            BlazorWebViewDebugService.ConfigureHandlers();
            return builder;
        }
        if (builder.Services.Any(d => d.ServiceType == typeof(Registration))) return builder;
        var options = new BlazorWebViewDebugOptions();
        configure?.Invoke(options);
        builder.Services.AddSingleton(new Registration(options));
        if (!options.Enabled) return builder;
        builder.AddMauiWebViewDevFlowTools();
        builder.Services.AddSingleton<BlazorWebViewDebugService>(_ =>
        {
            var service = new BlazorWebViewDebugService(options);
            service.LogCallback = message =>
            {
                if (options.EnableLogging) System.Diagnostics.Debug.WriteLine(message);
            };
            return service;
        });
        BlazorWebViewDebugService.ConfigureHandlers();
        return builder;
    }

    private sealed record Registration(BlazorWebViewDebugOptions Options);
}
