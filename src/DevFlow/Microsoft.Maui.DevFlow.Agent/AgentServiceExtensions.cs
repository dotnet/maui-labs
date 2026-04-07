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

        // Read project identity from assembly metadata (injected by .targets)
        var project = ReadAssemblyMetadataProject() ?? "unknown";
        var tfm = ReadAssemblyMetadataTfm() ?? "unknown";

        // Always register with the broker for discoverability (must run on thread pool
        // to avoid deadlock with SynchronizationContext — AddMauiDevFlowAgent runs on
        // the main thread). When a custom port is set, we tell the broker our port so it
        // uses it instead of assigning from the pool; the agent stays discoverable via
        // `maui-devflow list` regardless of port configuration.
        BrokerRegistration? brokerReg = null;
        bool hasCustomPort = options.Port != AgentOptions.DefaultPort;
        try
        {
            string platform;
            string appName;
            try
            {
                platform = DeviceInfo.Platform.ToString();
                appName = AppInfo.Name ?? "unknown";
            }
            catch
            {
                // MAUI not fully initialized yet during DI registration
                platform = OperatingSystem.IsAndroid() ? "Android"
                    : OperatingSystem.IsIOS() ? "iOS"
                    : OperatingSystem.IsMacCatalyst() ? "MacCatalyst"
                    : OperatingSystem.IsMacOS() ? "macOS"
                    : OperatingSystem.IsWindows() ? "Windows"
                    : "Unknown";
                appName = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? "unknown";
            }
            brokerReg = new BrokerRegistration(project, tfm, platform, appName);
            // If the user set a custom port, tell the broker upfront so it registers
            // with that port instead of assigning one from the pool.
            if (hasCustomPort)
                brokerReg.CurrentPort = options.Port;
            // Task.Run avoids deadlock: TryRegisterAsync uses await internally,
            // and the main thread has a SynchronizationContext that would deadlock
            // if we called .GetAwaiter().GetResult() directly.
            var assignedPort = Task.Run(() => brokerReg.TryRegisterAsync(TimeSpan.FromSeconds(5))).GetAwaiter().GetResult();
            if (assignedPort.HasValue)
            {
                options.Port = assignedPort.Value;
                Console.WriteLine($"[Microsoft.Maui.DevFlow] Broker assigned port {assignedPort.Value}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Microsoft.Maui.DevFlow] Broker registration failed: {ex.Message}");
            brokerReg?.Dispose();
            brokerReg = null;
        }

        // Fall back to assembly metadata port if broker didn't assign one
        if (brokerReg?.AssignedPort == null)
        {
            var metaPort = ReadAssemblyMetadataPort();
            if (metaPort.HasValue)
                options.Port = metaPort.Value;
        }

        var service = new PlatformAgentService(options);
        if (brokerReg != null)
        {
            // Tell the broker registration what port we ended up on, so late
            // reconnections (broker started after app) register the correct port.
            brokerReg.CurrentPort = options.Port;
            service.SetBrokerRegistration(brokerReg);
        }
        builder.Services.AddSingleton<DevFlowAgentService>(service);

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
            var app = Application.Current;
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
        DevFlowAgentService service,
        AgentOptions options,
        IDispatcher? mainDispatcher)
    {
        for (int i = 0; i < 30; i++)
        {
            var app = Application.Current;
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
            var app = Application.Current;
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

    /// <summary>
    /// Reads Microsoft.Maui.DevFlow metadata from AssemblyMetadataAttributes injected by the .targets file.
    /// </summary>
    private static string? ReadAssemblyMetadata(string key)
    {
        try
        {
            // Try entry assembly first (works on Mac Catalyst, Windows)
            var entry = System.Reflection.Assembly.GetEntryAssembly();
            if (entry != null)
            {
                var value = FindMetadataInAssembly(entry, key);
                if (value != null) return value;
            }

            // GetEntryAssembly() returns null on Android/iOS — scan loaded assemblies
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.IsDynamic) continue;
                var value = FindMetadataInAssembly(asm, key);
                if (value != null) return value;
            }
        }
        catch { /* ignore reflection failures */ }
        return null;
    }

    private static int? ReadAssemblyMetadataPort()
    {
        var value = ReadAssemblyMetadata("Microsoft.Maui.DevFlowPort");
        return value != null && int.TryParse(value, out var port) ? port : null;
    }

    internal static string? ReadAssemblyMetadataProject() => ReadAssemblyMetadata("Microsoft.Maui.DevFlowProject");
    internal static string? ReadAssemblyMetadataTfm() => ReadAssemblyMetadata("Microsoft.Maui.DevFlowTfm");

    private static string? FindMetadataInAssembly(System.Reflection.Assembly assembly, string key)
    {
        try
        {
            var attrs = assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false);
            foreach (System.Reflection.AssemblyMetadataAttribute attr in attrs)
            {
                if (attr.Key == key)
                    return attr.Value;
            }
        }
        catch { /* ignore per-assembly reflection failures */ }
        return null;
    }
}
