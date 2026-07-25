using Microsoft.Extensions.Logging;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Logging;

namespace Microsoft.Maui.DevFlow.Agent.Native;

/// <summary>
/// Entry point for hosting the DevFlow agent inside a plain .NET Android, iOS, Mac Catalyst or
/// macOS app — no .NET MAUI required.
/// </summary>
/// <remarks>
/// <para>Android — call from <c>MainActivity.OnCreate</c>:</para>
/// <code>
/// protected override void OnCreate(Bundle? savedInstanceState)
/// {
///     base.OnCreate(savedInstanceState);
///     SetContentView(Resource.Layout.activity_main);
///     DevFlowAgent.Start(this);
/// }
/// </code>
/// <para>iOS / Mac Catalyst — call from <c>AppDelegate.FinishedLaunching</c>. macOS — call from
/// <c>AppDelegate.DidFinishLaunching</c>:</para>
/// <code>
/// DevFlowAgent.Start();
/// </code>
/// </remarks>
public static class DevFlowAgent
{
    private static readonly object s_gate = new();
    private static NativeDevFlowAgentService? s_service;
    private static FileLogProvider? s_logProvider;

    /// <summary>
    /// The running agent, or <c>null</c> when <see cref="Start(AgentOptions)"/> has not been called.
    /// </summary>
    public static NativeDevFlowAgentService? Current => s_service;

    /// <summary>
    /// Starts the agent with default options.
    /// </summary>
    /// <returns>The running agent service.</returns>
    public static NativeDevFlowAgentService Start() => Start(new AgentOptions());

    /// <summary>
    /// Starts the agent with the supplied options. Calling this more than once returns the agent
    /// started by the first call — the HTTP server is never started twice.
    /// </summary>
    /// <param name="options">Agent configuration. Port, broker registration and feature switches.</param>
    /// <returns>The running agent service.</returns>
    public static NativeDevFlowAgentService Start(AgentOptions options)
        => Start(options, static o => new NativeDevFlowAgentService(o));

    /// <summary>
    /// Starts the agent, building the service with the supplied factory. Add-on packages use this
    /// to substitute a service that implements endpoints the base native agent reports as
    /// unsupported — see <c>Microsoft.Maui.DevFlow.Agent.Native.Essentials</c>.
    /// </summary>
    /// <param name="options">Agent configuration. Port, broker registration and feature switches.</param>
    /// <param name="serviceFactory">Creates the agent service from <paramref name="options"/>.</param>
    /// <returns>The running agent service.</returns>
    public static NativeDevFlowAgentService Start(
        AgentOptions options,
        Func<AgentOptions, NativeDevFlowAgentService> serviceFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(serviceFactory);

        lock (s_gate)
        {
            if (s_service != null) return s_service;

            var service = serviceFactory(options);

            if (options.EnableFileLogging)
            {
                var logDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "mauidevflow-logs");
                s_logProvider = new FileLogProvider(logDirectory, options.MaxLogFileSize, options.MaxLogFiles);
                service.SetLogProvider(s_logProvider);

                if (options.CaptureConsole || options.CaptureTrace)
                {
                    new ConsoleLogCapture(s_logProvider.Writer)
                        .Install(captureConsole: options.CaptureConsole, captureTrace: options.CaptureTrace);
                }
            }

            service.Start();
            s_service = service;
            return service;
        }
    }

    /// <summary>
    /// The <see cref="ILoggerProvider"/> that forwards app logs to the running agent so they show up
    /// in <c>maui devflow logs</c>. Register it with your logging builder to capture ILogger output.
    /// </summary>
    /// <returns>A provider bound to the running agent, or <c>null</c> when file logging is disabled.</returns>
    public static ILoggerProvider? LoggerProvider => s_logProvider;

    /// <summary>
    /// Stops the agent and releases its HTTP server.
    /// </summary>
    public static void Stop()
    {
        lock (s_gate)
        {
            s_service?.Dispose();
            s_service = null;
            s_logProvider = null;
        }
    }
}
