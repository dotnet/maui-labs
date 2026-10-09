using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Hosting;

namespace Microsoft.Maui.DevFlow.WebView;

public static class WebViewDevFlowExtensions
{
    /// <summary>
    /// Add shared CDP/console tools for WebView and HybridWebView. The first explicit configuration
    /// wins; an earlier default registration (including Blazor's) can still be explicitly configured.
    /// Service instances are created lazily and mapper hooks are installed once.
    /// </summary>
    public static MauiAppBuilder AddMauiWebViewDevFlowTools(this MauiAppBuilder builder,
        Action<WebViewDebugOptions>? configure = null)
    {
        var registration = builder.Services.LastOrDefault(d => d.ServiceType == typeof(Registration))
            ?.ImplementationInstance as Registration;
        if (registration is null)
        {
            // Respect manually registered services without creating a second, unused instance.
            if (builder.Services.Any(d => d.ServiceType == typeof(WebViewDebugService)))
            {
                WebViewDebugService.ConfigureHandlers();
                return builder;
            }
            registration = new Registration();
            builder.Services.AddSingleton(registration);
        }
        if (configure is not null && !registration.ExplicitlyConfigured)
        {
            configure(registration.Options);
            registration.ExplicitlyConfigured = true;
        }
        if (!registration.Options.Enabled)
        {
            if (registration.ServiceDescriptor is not null) builder.Services.Remove(registration.ServiceDescriptor);
            return builder;
        }
        if (!builder.Services.Any(d => d.ServiceType == typeof(WebViewDebugService)))
        {
            var options = registration.Options;
            registration.ServiceDescriptor = ServiceDescriptor.Singleton<WebViewDebugService>(_ =>
            {
                var service = new WebViewDebugService(options);
                service.LogCallback = message =>
                {
                    if (options.EnableLogging) System.Diagnostics.Debug.WriteLine(message);
                };
                return service;
            });
            builder.Services.Add(registration.ServiceDescriptor);
        }
        WebViewDebugService.ConfigureHandlers();
        return builder;
    }

    private sealed class Registration
    {
        public WebViewDebugOptions Options { get; } = new();
        public bool ExplicitlyConfigured { get; set; }
        public ServiceDescriptor? ServiceDescriptor { get; set; }
    }
}
