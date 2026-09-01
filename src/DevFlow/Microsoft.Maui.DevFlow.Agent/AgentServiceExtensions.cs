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
        var enabledMetadata = ReadAssemblyMetadata("Microsoft.Maui.DevFlowEnabled");
        var buildMode = ReadAssemblyMetadata("Microsoft.Maui.DevFlowMode");
        options.ApplyBuildMetadata(enabledMetadata, buildMode);
        options.ApplyPortMetadata(ReadAssemblyMetadataPort());
        options.ValidateForRegistration();

        // Read project identity from assembly metadata (injected by .targets)
        var project = ReadAssemblyMetadataProject() ?? "unknown";
        var tfm = ReadAssemblyMetadataTfm() ?? "unknown";
        var sessionId = ReadAssemblyMetadataSessionId();
        var packageId = ReadAssemblyMetadataPackageId();

        // Always register with the broker for discoverability (must run on thread pool
        // to avoid deadlock with SynchronizationContext — AddMauiDevFlowAgent runs on
        // the main thread). When a custom port is set, we tell the broker our port so it
        // uses it instead of assigning from the pool; the agent stays discoverable via
        // `maui devflow list` regardless of port configuration.
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
                packageId ??= AppInfo.PackageName;
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
            brokerReg = new BrokerRegistration(
                project, tfm, platform, appName, sessionId, packageId, ResolveDeviceIdentity());
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

        options.ValidateForRegistration();

        var service = new PlatformAgentService(options);
        service.SetSessionId(sessionId);
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
    /// Resolves the identity of the virtual device this app is running on, so the broker can pair
    /// the app agent with the device around it.
    /// <para>
    /// This lives in the platform-specific package because the strongest signals need platform
    /// APIs. Each platform contributes what its own tooling addresses devices by, so the value
    /// can be matched exactly rather than guessed:
    /// </para>
    /// <list type="bullet">
    /// <item>Android — the serial adb uses, plus the AVD name as a weaker fallback.</item>
    /// <item>iOS and Mac Catalyst — the simulator UDID, read from the environment by the core
    /// resolver since the simulator runtime injects it.</item>
    /// <item>Windows and macOS — nothing; a desktop app has no virtual device around it.</item>
    /// </list>
    /// </summary>
    private static string? ResolveDeviceIdentity()
    {
        try
        {
#if ANDROID
            var parts = new List<string> { "platform=android" };

            // The AVD name is the primary Android join key. adb addresses an emulator as
            // "emulator-<consolePort>", which is derived from the console port and is NOT
            // ro.serialno — so the serial below is a secondary signal that matches only on the
            // configurations where the two happen to agree.
            var avd = GetAndroidSystemProperty("ro.boot.qemu.avd_name")
                ?? GetAndroidSystemProperty("ro.kernel.qemu.avd_name");
            if (!string.IsNullOrWhiteSpace(avd))
                parts.Add($"avd={Sanitize(avd)}");

            var serial = GetAndroidSystemProperty("ro.serialno");
            if (!string.IsNullOrWhiteSpace(serial) && !serial.Equals("unknown", StringComparison.OrdinalIgnoreCase))
                parts.Add($"serial={Sanitize(serial)}");

            return parts.Count > 1 ? string.Join(";", parts) : null;
#else
            // Everything else is either handled by the environment-based core resolver (the iOS
            // simulator injects SIMULATOR_UDID) or has no virtual device at all.
            return DeviceIdentityProvider.Resolve();
#endif
        }
        catch
        {
            // Pairing is an enhancement. An app must still register and be inspectable when we
            // cannot work out what it is running on.
            return null;
        }
    }

#if ANDROID
    /// <summary>
    /// Strips the wire format's own separators from a value.
    /// <para>
    /// The identity is <c>key=value;key=value</c>, so a value containing <c>;</c> or <c>=</c>
    /// would split into fields that parse as something else. AVD names should not contain either,
    /// but a value read from a system property is not ours to trust.
    /// </para>
    /// </summary>
    private static string Sanitize(string value) =>
        value.Replace(";", "").Replace("=", "").Trim();

    /// <summary>
    /// Reads an Android system property. The emulator exposes its AVD name this way, and there is
    /// no managed API for it.
    /// </summary>
    private static string? GetAndroidSystemProperty(string key)
    {
        global::Java.Lang.Process? process = null;
        try
        {
            process = new global::Java.Lang.ProcessBuilder("/system/bin/getprop", key).Start();
            using var reader = new StreamReader(process!.InputStream!);
            var value = reader.ReadToEnd()?.Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch
        {
            return null;
        }
        finally
        {
            // The subprocess holds a file descriptor until it is reaped; leaking one per property
            // read would accumulate over an app's lifetime.
            try { process?.Destroy(); } catch { }
            process?.Dispose();
        }
    }
#endif

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
        => AgentOptions.ParsePortMetadata(ReadAssemblyMetadata("Microsoft.Maui.DevFlowPort"));

    internal static string? ReadAssemblyMetadataProject() => ReadAssemblyMetadata("Microsoft.Maui.DevFlowProject");
    internal static string? ReadAssemblyMetadataTfm() => ReadAssemblyMetadata("Microsoft.Maui.DevFlowTfm");
    internal static string? ReadAssemblyMetadataSessionId() => ReadAssemblyMetadata("Microsoft.Maui.DevFlowSessionId");
    internal static string? ReadAssemblyMetadataPackageId() => ReadAssemblyMetadata("Microsoft.Maui.DevFlowPackageId");

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
