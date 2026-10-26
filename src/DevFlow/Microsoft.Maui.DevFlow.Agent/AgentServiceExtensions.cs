using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.LifecycleEvents;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Agent.Core.Network;
using Microsoft.Maui.DevFlow.Logging;

namespace Microsoft.Maui.DevFlow.Agent;

/// <summary>
/// Extension methods for registering Microsoft.Maui.DevFlow Agent in the MAUI DI container.
/// </summary>
public static class AgentServiceExtensions
{
    /// <summary>
    /// Adds the Microsoft.Maui.DevFlow Agent to the MAUI app builder.
    /// The agent will start automatically when the app starts.
    /// </summary>
    public static MauiAppBuilder AddMauiDevFlowAgent(this MauiAppBuilder builder, Action<AgentOptions>? configure = null)
    {
        var options = new AgentOptions();
        configure?.Invoke(options);

        var hostContext = DevFlowAgentHost.Configure(options, GetMauiHostIdentity);

        var nativeElementRegistry = new RegisteredNativeElementRegistry();
        var nativeElementDiagnosticSubscriber =
            new MauiNativeElementDiagnosticSubscriber(nativeElementRegistry);
        var service = new PlatformAgentService(
            options,
            nativeElementRegistry,
            nativeElementDiagnosticSubscriber);
        hostContext.AttachTo(service, options);
        builder.Services.AddSingleton(nativeElementRegistry);
        builder.Services.AddSingleton<DevFlowAgentService>(service);
        builder.Services.AddSingleton<MauiDevFlowAgentService>(service);

        if (options.EnableFileLogging)
        {
            var logDir = Path.Combine(FileSystem.CacheDirectory, "mauidevflow-logs");
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

        // Auto-inject network monitoring handler into all IHttpClientFactory-created clients
        if (options.EnableNetworkMonitoring)
        {
            var store = service.NetworkStore;
            var maxBody = options.MaxNetworkBodySize;
            builder.Services.AddSingleton(store);
            builder.Services.ConfigureHttpClientDefaults(httpBuilder =>
            {
                httpBuilder.AddHttpMessageHandler(() => new Microsoft.Maui.DevFlow.Agent.Core.Network.DevFlowHttpHandler(store, maxBody));
            });
        }

        var startupRequested = 0;

        void EnsureAgentStarted(IDispatcher? dispatcher = null)
        {
            var app = ResolveCurrentApplication();
            if (app != null)
            {
                if (!service.IsRunning)
                {
                    app.Dispatcher.Dispatch(() => service.Start(app, app.Dispatcher));
                    Console.WriteLine($"[Microsoft.Maui.DevFlow] Agent started on port {options.Port}");
                }
                else if (!service.IsAppBound)
                {
                    app.Dispatcher.Dispatch(() => service.BindApp(app));
                    Console.WriteLine("[Microsoft.Maui.DevFlow] Application bound to running agent after lifecycle event");
                }

                return;
            }

            if (service.IsRunning)
                return;

            dispatcher ??= Dispatching.Dispatcher.GetForCurrentThread();
            if (dispatcher == null)
            {
                Console.WriteLine("[Microsoft.Maui.DevFlow] Failed to start agent: Application.Current was null and no dispatcher available");
                return;
            }

            if (Interlocked.Exchange(ref startupRequested, 1) == 1)
                return;

            _ = Task.Run(async () =>
            {
                try
                {
                    await StartWhenApplicationAvailableAsync(service, options, dispatcher);
                }
                finally
                {
                    if (!service.IsRunning)
                        Interlocked.Exchange(ref startupRequested, 0);
                }
            });
        }

        builder.ConfigureLifecycleEvents(lifecycle =>
        {
#if ANDROID
            lifecycle.AddAndroid(android =>
            {
                android.OnResume(activity =>
                {
                    EnsureAgentStarted();
                });
            });
#elif IOS || MACCATALYST
            lifecycle.AddiOS(ios =>
            {
                ios.FinishedLaunching((_, _) =>
                {
                    var mainDispatcher = Dispatching.Dispatcher.GetForCurrentThread();
                    EnsureAgentStarted(mainDispatcher);
                    return true;
                });
            });
#elif WINDOWS
            lifecycle.AddWindows(windows =>
            {
                windows.OnActivated((window, args) =>
                {
                    EnsureAgentStarted();
                });
            });
#elif MACOS
            lifecycle.AddMacOS(macos =>
            {
                macos.DidFinishLaunching(_ =>
                {
                    var mainDispatcher = Dispatching.Dispatcher.GetForCurrentThread();
                    EnsureAgentStarted(mainDispatcher);
                });
            });
#endif
        });

        return builder;
    }

    private static async Task StartWhenApplicationAvailableAsync(
        MauiDevFlowAgentService service,
        AgentOptions options,
        IDispatcher? mainDispatcher)
    {
        for (int i = 0; i < 30; i++)
        {
            var app = ResolveCurrentApplication();
            if (app != null)
            {
                app.Dispatcher.Dispatch(() => service.Start(app, app.Dispatcher));
                Console.WriteLine($"[Microsoft.Maui.DevFlow] Agent started on port {options.Port}");
                return;
            }

            await Task.Delay(500);
        }

        if (mainDispatcher == null)
        {
            Console.WriteLine("[Microsoft.Maui.DevFlow] Failed to start agent: Application.Current was null and no dispatcher available");
            return;
        }

        // Application.Current never set during the initial window. Start the HTTP server
        // so DevFlow is reachable, then keep polling and bind once/if the app appears later.
        if (!service.IsRunning)
        {
            mainDispatcher.Dispatch(() => service.StartServerOnly(mainDispatcher));
            Console.WriteLine($"[Microsoft.Maui.DevFlow] Agent started on port {options.Port} (app-less mode — Application.Current was null)");
        }

        for (int i = 0; i < 30; i++)
        {
            var app = ResolveCurrentApplication();
            if (app != null)
            {
                app.Dispatcher.Dispatch(() => service.BindApp(app));
                Console.WriteLine("[Microsoft.Maui.DevFlow] Application bound to running agent after delayed startup");
                return;
            }

            await Task.Delay(500);
        }

        Console.WriteLine("[Microsoft.Maui.DevFlow] Application.Current was still null after late-bind retries; continuing in app-less mode");
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

    private static (string Platform, string AppName) GetMauiHostIdentity()
    {
        try
        {
            return (DeviceInfo.Platform.ToString(), AppInfo.Name ?? "unknown");
        }
        catch
        {
            // MAUI not fully initialized yet during DI registration.
            var platform = OperatingSystem.IsAndroid() ? "Android"
                : OperatingSystem.IsIOS() ? "iOS"
                : OperatingSystem.IsMacCatalyst() ? "MacCatalyst"
                : OperatingSystem.IsMacOS() ? "macOS"
                : OperatingSystem.IsWindows() ? "Windows"
                : "Unknown";
            var appName = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? "unknown";
            return (platform, appName);
        }
    }
}
