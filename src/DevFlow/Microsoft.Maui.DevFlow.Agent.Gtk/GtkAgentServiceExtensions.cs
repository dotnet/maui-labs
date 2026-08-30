using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.LifecycleEvents;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Logging;

namespace Microsoft.Maui.DevFlow.Agent.Gtk;

/// <summary>
/// Extension methods for registering Microsoft.Maui.DevFlow Agent in Maui.Gtk apps.
/// </summary>
public static class GtkAgentServiceExtensions
{
    /// <summary>
    /// Adds the Microsoft.Maui.DevFlow Agent to a Maui.Gtk app builder.
    /// The agent will start automatically when the first GTK window is created.
    /// </summary>
    public static MauiAppBuilder AddMauiDevFlowAgent(this MauiAppBuilder builder, Action<AgentOptions>? configure = null)
    {
        var options = new AgentOptions();
        configure?.Invoke(options);
        options.ApplyBuildMetadata(
            ReadAssemblyMetadata("Microsoft.Maui.DevFlowEnabled"),
            ReadAssemblyMetadata("Microsoft.Maui.DevFlowMode"));
        options.ApplyPortMetadata(ReadAssemblyMetadataPort());
        options.ValidateForRegistration();

        // Read project identity from assembly metadata (injected by .targets)
        var project = ReadAssemblyMetadata("Microsoft.Maui.DevFlowProject") ?? "unknown";
        var tfm = ReadAssemblyMetadata("Microsoft.Maui.DevFlowTfm") ?? "unknown";
        var sessionId = ReadAssemblyMetadata("Microsoft.Maui.DevFlowSessionId");
        var packageId = ReadAssemblyMetadata("Microsoft.Maui.DevFlowPackageId");

        // Always register with the broker for discoverability. When a custom port is
        // set, we tell the broker our port so it uses it instead of assigning from the pool.
        BrokerRegistration? brokerReg = null;
        bool hasCustomPort = options.Port != AgentOptions.DefaultPort;
        try
        {
            var platform = "Linux";
            var appName = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? "unknown";
            brokerReg = new BrokerRegistration(
                project, tfm, platform, appName, sessionId, packageId);
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

        options.ValidateForRegistration();

        var service = new GtkAgentService(options);
        service.SetSessionId(sessionId);
        if (brokerReg != null)
        {
            brokerReg.CurrentPort = options.Port;
            service.SetBrokerRegistration(brokerReg);
        }
        builder.Services.AddSingleton<DevFlowAgentService>(service);

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

        // Auto-start agent when the first GTK window is created
        bool started = false;
        builder.ConfigureLifecycleEvents(lifecycle =>
        {
            lifecycle.AddGtk(gtk =>
            {
                gtk.OnWindowCreated(_ =>
                {
                    if (started) return;
                    started = true;

                    Task.Run(async () =>
                    {
                        // Wait for Application.Current to be available
                        Application? app = null;
                        for (int i = 0; i < 50 && app == null; i++)
                        {
                            await Task.Delay(200);
                            app = Application.Current;
                        }
                        if (app != null)
                            app.StartDevFlowAgent();
                    });
                });
            });
        });

        return builder;
    }

    /// <summary>
    /// Starts the Microsoft.Maui.DevFlow agent. Call this after the MAUI Application is available.
    /// Typically called from GtkMauiApplication.OnActivate or after window creation.
    /// </summary>
    public static void StartDevFlowAgent(this Application app)
    {
        var service = GetAgentService(app);
        if (service != null)
        {
            service.Start(app, app.Dispatcher);
        }
    }

    private static DevFlowAgentService? GetAgentService(Application app)
    {
        try
        {
            return app.Handler?.MauiContext?.Services.GetService<DevFlowAgentService>();
        }
        catch
        {
            return null;
        }
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
        => AgentOptions.ParsePortMetadata(ReadAssemblyMetadata("Microsoft.Maui.DevFlowPort"));
}
