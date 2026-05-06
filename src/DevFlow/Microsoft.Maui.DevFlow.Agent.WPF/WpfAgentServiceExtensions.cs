using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.LifecycleEvents;
using Microsoft.Maui.LifecycleEvents.WPF;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Logging;

namespace Microsoft.Maui.DevFlow.Agent.WPF;

/// <summary>
/// Extension methods for registering Microsoft.Maui.DevFlow Agent in MAUI WPF apps.
/// </summary>
public static class WpfAgentServiceExtensions
{
    /// <summary>
    /// Adds the Microsoft.Maui.DevFlow Agent to a MAUI WPF app builder.
    /// The agent will start automatically when the first WPF window is activated.
    /// </summary>
    public static MauiAppBuilder AddMauiDevFlowAgent(this MauiAppBuilder builder, Action<AgentOptions>? configure = null)
    {
        var options = new AgentOptions();
        configure?.Invoke(options);

        var project = ReadAssemblyMetadata("Microsoft.Maui.DevFlowProject") ?? "unknown";
        var tfm = ReadAssemblyMetadata("Microsoft.Maui.DevFlowTfm") ?? "unknown";

        BrokerRegistration? brokerReg = null;
        bool hasCustomPort = options.Port != AgentOptions.DefaultPort;
        try
        {
            var platform = "WPF";
            var appName = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? "unknown";
            brokerReg = new BrokerRegistration(project, tfm, platform, appName);
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

        if (!hasCustomPort && brokerReg?.AssignedPort == null)
        {
            var metaPort = ReadAssemblyMetadataPort();
            if (metaPort.HasValue)
                options.Port = metaPort.Value;
        }

        var service = new WpfAgentService(options);
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

        bool started = false;
        builder.ConfigureLifecycleEvents(lifecycle =>
        {
            lifecycle.AddWPF(wpf =>
            {
                wpf.OnActivated((window, args) =>
                {
                    if (started) return;
                    started = true;

                    Task.Run(async () =>
                    {
                        Application? app = null;
                        for (int i = 0; i < 50 && app == null; i++)
                        {
                            await Task.Delay(200);
                            app = Application.Current;
                        }
                        app?.StartDevFlowAgent();
                    });
                });
            });
        });
        return builder;
    }

    /// <summary>
    /// Starts the Microsoft.Maui.DevFlow agent. Call after the MAUI Application is available.
    /// </summary>
    public static void StartDevFlowAgent(this Application app)
    {
        var service = GetAgentService(app);
        service?.Start(app, app.Dispatcher);
    }

    private static DevFlowAgentService? GetAgentService(Application app)
    {
        try { return app.Handler?.MauiContext?.Services.GetService<DevFlowAgentService>(); }
        catch { return null; }
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
                    if (attr.Key == key) return attr.Value;
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
