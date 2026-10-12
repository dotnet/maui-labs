using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.LifecycleEvents;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Logging;
using Microsoft.Maui.Platforms.Linux.Gtk4.Platform;
using System.Runtime.Versioning;

namespace Microsoft.Maui.DevFlow.Agent.Gtk;

/// <summary>
/// Extension methods for registering Microsoft.Maui.DevFlow Agent in Microsoft.Maui.Platforms.Linux.Gtk4 apps.
/// </summary>
[SupportedOSPlatform("linux")]
public static class GtkAgentServiceExtensions
{
    /// <summary>
    /// Adds the Microsoft.Maui.DevFlow Agent to a Microsoft.Maui.Platforms.Linux.Gtk4 app builder.
    /// The agent will start automatically when the first GTK window is created.
    /// </summary>
    public static MauiAppBuilder AddMauiDevFlowAgent(this MauiAppBuilder builder, Action<AgentOptions>? configure = null)
    {
        var options = new AgentOptions();
        configure?.Invoke(options);

        // Read project identity from assembly metadata (injected by .targets)
        var project = ReadAssemblyMetadata("Microsoft.Maui.DevFlowProject") ?? "unknown";
        var tfm = ReadAssemblyMetadata("Microsoft.Maui.DevFlowTfm") ?? "unknown";
        var sessionId = ReadAssemblyMetadata("Microsoft.Maui.DevFlowSessionId");

        // Always register with the broker for discoverability. When a custom port is
        // set, we tell the broker our port so it uses it instead of assigning from the pool.
        BrokerRegistration? brokerReg = null;
        bool hasCustomPort = options.Port != AgentOptions.DefaultPort;
        try
        {
            var platform = "Linux";
            var appName = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? "unknown";
            brokerReg = new BrokerRegistration(project, tfm, platform, appName, sessionId);
            if (hasCustomPort)
                brokerReg.CurrentPort = options.Port;
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
        if (!hasCustomPort && brokerReg?.AssignedPort == null)
        {
            var metaPort = ReadAssemblyMetadataPort();
            if (metaPort.HasValue)
                options.Port = metaPort.Value;
        }

        var nativeElementRegistry = new RegisteredNativeElementRegistry();
        var nativeElementDiagnosticSubscriber =
            new MauiNativeElementDiagnosticSubscriber(nativeElementRegistry);
        var service = new GtkAgentService(
            options,
            nativeElementRegistry,
            nativeElementDiagnosticSubscriber);
        service.SetSessionId(sessionId);
        if (brokerReg != null)
        {
            brokerReg.CurrentPort = options.Port;
            service.SetBrokerRegistration(brokerReg);
        }
        builder.Services.AddSingleton(nativeElementRegistry);
        builder.Services.AddSingleton<DevFlowAgentService>(service);
        builder.Services.AddSingleton<MauiDevFlowAgentService>(service);

        if (options.EnableFileLogging)
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "mauidevflow-logs");
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
            {
                httpBuilder.AddHttpMessageHandler(() => new Microsoft.Maui.DevFlow.Agent.Core.Network.DevFlowHttpHandler(store, maxBody));
            });
        }

        // GtkMauiApplication has assigned its Application and attached the handler
        // before raising the window-created event on the GTK thread.
        ConfigureStartup(builder, () => GtkMauiApplication.Current.StartDevFlowAgent());

        return builder;
    }

    internal static void ConfigureStartup(MauiAppBuilder builder, Action start)
    {
        bool started = false;
        builder.ConfigureLifecycleEvents(lifecycle =>
        {
            lifecycle.AddGtk(gtk =>
            {
                gtk.OnWindowCreated(_ =>
                {
                    if (started) return;
                    start();
                    started = true;
                });
            });
        });
    }

    /// <summary>
    /// Starts the Microsoft.Maui.DevFlow agent. Call this after the MAUI Application is available.
    /// Typically called after window creation. Normal builder registration starts it automatically.
    /// </summary>
    public static void StartDevFlowAgent(this Application app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var services = app.Handler?.MauiContext?.Services
            ?? throw new InvalidOperationException("The MAUI application handler is not initialized. Start DevFlow after window creation.");
        services.GetRequiredService<MauiDevFlowAgentService>().Start(app, app.Dispatcher);
    }

    /// <summary>
    /// Starts the registered agent from a GTK host, for example in GtkMauiApplication.OnStarted.
    /// AddMauiDevFlowAgent normally starts the agent automatically; this is an explicit startup hook.
    /// </summary>
    public static void StartDevFlowAgent(this GtkMauiApplication platformApp)
    {
        ArgumentNullException.ThrowIfNull(platformApp);
        if (platformApp.Application is not Application app)
            throw new InvalidOperationException("The GTK host has no MAUI Controls application. Start DevFlow from OnStarted or after window creation.");

        app.StartDevFlowAgent();
    }

    private static string? ReadAssemblyMetadata(string key)
    {
        try
        {
            var entry = System.Reflection.Assembly.GetEntryAssembly();
            if (entry != null)
            {
                var attrs = entry.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false);
                foreach (System.Reflection.AssemblyMetadataAttribute attr in attrs)
                {
                    if (attr.Key == key)
                        return attr.Value;
                }
            }
        }
        catch { }
        return null;
    }

    private static int? ReadAssemblyMetadataPort()
    {
        var value = ReadAssemblyMetadata("Microsoft.Maui.DevFlowPort");
        return value != null && int.TryParse(value, out var port) ? port : null;
    }
}
