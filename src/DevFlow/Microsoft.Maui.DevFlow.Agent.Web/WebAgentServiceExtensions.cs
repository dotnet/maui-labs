using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Agent.Core.Network;
using Microsoft.Maui.DevFlow.Logging;
using Microsoft.Maui.Hosting;

namespace Microsoft.Maui.DevFlow.Agent.Web;

/// <summary>
/// Extension methods for registering the Microsoft.Maui.DevFlow agent in MAUI apps that run on
/// WebAssembly in the browser.
/// </summary>
public static class WebAgentServiceExtensions
{
    /// <summary>
    /// Adds the Microsoft.Maui.DevFlow agent. It registers with the broker and starts serving once the
    /// web backend has created the MAUI application. A browser page cannot listen on a port, so the
    /// broker listens on the agent's port and relays requests over the agent's WebSocket; start the
    /// broker (any <c>maui devflow</c> command does) and serve the page from a loopback address.
    /// </summary>
    public static MauiAppBuilder AddMauiDevFlowAgent(this MauiAppBuilder builder, Action<AgentOptions>? configure = null)
    {
        var options = new AgentOptions { UseBrokerTunnel = true };
        configure?.Invoke(options);

        var service = new WebAgentService(options);
        builder.Services.AddSingleton<DevFlowAgentService>(service);
        builder.Services.AddSingleton<MauiDevFlowAgentService>(service);

        if (options.EnableFileLogging)
        {
            // WebAssembly's file system lives in memory, which is all the log API needs.
            var logDir = Path.Combine(Path.GetTempPath(), "mauidevflow-logs");
            var logProvider = new FileLogProvider(logDir, options.MaxLogFileSize, options.MaxLogFiles);
            service.SetLogProvider(logProvider);

            if (options.CaptureILogger)
                builder.Logging.AddProvider(logProvider);

            if (options.CaptureConsole || options.CaptureTrace)
            {
                var capture = new ConsoleLogCapture(logProvider.Writer);
                capture.Install(captureConsole: options.CaptureConsole, captureTrace: options.CaptureTrace);
            }
        }

        if (options.EnableNetworkMonitoring)
        {
            var store = service.NetworkStore;
            var maxBody = options.MaxNetworkBodySize;
            builder.Services.AddSingleton(store);
            builder.Services.ConfigureHttpClientDefaults(httpBuilder =>
                httpBuilder.AddHttpMessageHandler(() => new DevFlowHttpHandler(store, maxBody)));
        }

        // Initialization runs while the backend builds the MauiApp, before it creates the
        // application. Registration is asynchronous (WebAssembly cannot block), so the application
        // exists by the time it completes.
        builder.Services.AddSingleton<IMauiInitializeService>(
            new StartupInitializeService(() => _ = StartAsync(service, options)));

        return builder;
    }

    private static async Task StartAsync(WebAgentService service, AgentOptions options)
    {
        try
        {
            await WebAgentInterop.EnsureLoadedAsync();
            var context = await DevFlowAgentHost.ConfigureAsync(options, GetHostIdentity);
            context.AttachTo(service, options);

            var app = await WaitForApplicationAsync();
            if (app is null)
            {
                Console.WriteLine("[Microsoft.Maui.DevFlow] The MAUI application was not created; the agent is not running.");
                return;
            }

            service.Start(app, app.Dispatcher);
            Console.WriteLine($"[Microsoft.Maui.DevFlow] Agent started on port {options.Port}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Microsoft.Maui.DevFlow] Failed to start agent: {ex.Message}");
        }
    }

    private static async Task<Application?> WaitForApplicationAsync()
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (ResolveCurrentApplication() is { } app)
                return app;
            await Task.Delay(100);
        }
        return null;
    }

    private static Application? ResolveCurrentApplication()
    {
        if (Application.Current is { } current)
            return current;

        try
        {
            return IPlatformApplication.Current?.Application as Application;
        }
        catch
        {
            return null;
        }
    }

    private static (string Platform, string AppName) GetHostIdentity()
        => ("Browser", System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? "unknown");

    private sealed class StartupInitializeService(Action start) : IMauiInitializeService
    {
        public void Initialize(IServiceProvider services) => start();
    }
}
