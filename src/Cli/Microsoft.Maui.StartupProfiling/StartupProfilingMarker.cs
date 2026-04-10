// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace Microsoft.Maui.StartupProfiling;

/// <summary>
/// Public API for signalling the end of MAUI startup to an attached dotnet-trace collector.
/// </summary>
/// <remarks>
/// <para>
/// Add the <c>Microsoft.Maui.StartupProfiling</c> NuGet package to your MAUI app project,
/// then call <see cref="Complete"/> at the logical end of startup — for example after the
/// first page is shown or after <c>Application.Current.MainPage</c> is fully constructed.
/// </para>
/// <para>
/// When running under <c>maui profile</c> (or any dotnet-trace session configured with
/// <c>--stopping-event-provider-name Microsoft.Maui.StartupProfiling
/// --stopping-event-event-name StartupComplete</c>), the trace will stop automatically
/// when this method is called.
/// </para>
/// <para>
/// Set the environment variable <c>MAUI_STARTUP_PROFILING_AUTO_EXIT=1</c> to have the app
/// terminate immediately after emitting the marker — useful in automated CI profiling pipelines.
/// </para>
/// </remarks>
public static class StartupProfilingMarker
{
    internal const string ProfilingEnvironmentVariable = "MAUI_STARTUP_PROFILING";
    internal const string AutoExitEnvironmentVariable = "MAUI_STARTUP_PROFILING_AUTO_EXIT";
    internal const string ExitControlHostEnvironmentVariable = "MAUI_STARTUP_PROFILING_EXIT_HOST";
    internal const string ExitControlPortEnvironmentVariable = "MAUI_STARTUP_PROFILING_EXIT_PORT";
    internal const string DiagnosticPortsEnvironmentVariable = "DOTNET_DiagnosticPorts";
    internal const string LegacyDiagnosticPortsEnvironmentVariable = "COMPlus_DiagnosticPorts";

    /// <summary>
    /// The EventSource provider name to use with
    /// <c>dotnet-trace --stopping-event-provider-name</c>.
    /// </summary>
    public const string ProviderName = StartupProfilingEventSource.ProviderName;

    /// <summary>
    /// The event name to use with <c>dotnet-trace --stopping-event-event-name</c>.
    /// </summary>
    public const string EventName = StartupProfilingEventSource.StartupCompleteEventName;

    /// <summary>
    /// Returns <see langword="true"/> when the <c>MAUI_STARTUP_PROFILING</c> environment
    /// variable is set to <c>1</c> or <c>true</c>, indicating the app was launched by
    /// <c>maui profile</c> for profiling purposes.
    /// </summary>
    public static bool IsProfilingSession =>
        IsEnabledEnvironmentVariable(ProfilingEnvironmentVariable)
        || StartupProfilingExitChannel.TryGetEndpoint(out _, out _);

    /// <summary>
    /// Signals that startup is complete.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item>Emits the <c>StartupComplete</c> event on the
    ///     <c>Microsoft.Maui.StartupProfiling</c> EventSource, which causes an attached
    ///     dotnet-trace session to stop collection automatically.</item>
    ///   <item>If the <c>MAUI_STARTUP_PROFILING_AUTO_EXIT</c> environment variable is
    ///     <c>1</c> or <c>true</c>, the process exits immediately with exit code 0 after
    ///     emitting the event — useful for automated CI profiling pipelines.</item>
    /// </list>
    /// </remarks>
    public static void Complete()
    {
        var profilingSession = IsProfilingSession;
        var autoExitEnabled = IsEnabledEnvironmentVariable(AutoExitEnvironmentVariable);
        var diagnosticMessage =
            $"Complete() called. IsProfilingSession={profilingSession}, AutoExit={autoExitEnabled}, EventSourceEnabled={StartupProfilingEventSource.Log.IsEnabled()}";

        StartupProfilingDiagnostics.Log(diagnosticMessage);
        StartupProfilingEventSource.Log.Diagnostic(diagnosticMessage);
        StartupProfilingEventSource.Log.StartupComplete();
        StartupProfilingDiagnostics.Log("StartupComplete event emitted.");

        if (autoExitEnabled)
        {
            StartupProfilingDiagnostics.Log("Auto-exit requested; terminating the app process.");
            StartupProfilingDiagnostics.Log($"Calling Environment.Exit(0) now from Complete(). PID={Environment.ProcessId}");
            StartupProfilingDiagnostics.Flush();
            Environment.Exit(0);
        }
    }

    internal static bool IsEnabledEnvironmentVariable(string variableName) =>
        string.Equals(
            Environment.GetEnvironmentVariable(variableName),
            "1",
            StringComparison.OrdinalIgnoreCase)
        || string.Equals(
            Environment.GetEnvironmentVariable(variableName),
            "true",
            StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Module initializer that eagerly instantiates the <see cref="StartupProfilingEventSource"/>
/// so the provider is visible to dotnet-trace from the very first moment the assembly loads —
/// before any app code runs. This is important for startup profiling because the collector
/// must see the provider before it can register the stopping-event filter.
/// </summary>
internal static class StartupProfilingInitializer
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        // Accessing Log forces the static field initializer to run, which calls
        // EventSource's constructor and registers the provider with the runtime.
        _ = StartupProfilingEventSource.Log;

        var diagnosticMessage =
            $"Module initializer ran. IsProfilingSession={StartupProfilingMarker.IsProfilingSession}, AutoExit={StartupProfilingMarker.IsEnabledEnvironmentVariable(StartupProfilingMarker.AutoExitEnvironmentVariable)}";

        StartupProfilingDiagnostics.Log(diagnosticMessage);
        StartupProfilingEventSource.Log.Diagnostic(diagnosticMessage);
        StartupProfilingExitChannel.TryStart();
    }
}

internal static class StartupProfilingDiagnostics
{
    const string Prefix = "[maui-profile]";

    internal static void Log(string message)
    {
        var formatted = $"{Prefix} {DateTime.UtcNow:O} {message}";
        TryLogToAndroidLogcat(formatted);
        Debug.WriteLine(formatted);
        Trace.WriteLine(formatted);
        Console.WriteLine(formatted);
    }

    internal static void Flush()
    {
        try
        {
            Console.Out.Flush();
            Console.Error.Flush();
            Trace.Flush();
        }
        catch
        {
            // Best-effort only.
        }
    }

    static void TryLogToAndroidLogcat(string message)
    {
        try
        {
            var logType = Type.GetType("Android.Util.Log, Mono.Android", throwOnError: false);
            var infoMethod = logType?.GetMethod(
                "Info",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: [typeof(string), typeof(string)],
                modifiers: null);

            _ = infoMethod?.Invoke(null, ["maui-profile", message]);
        }
        catch
        {
            // Android logcat tagging is opportunistic only.
        }
    }
}

internal static class StartupProfilingExitChannel
{
    const int ExitControlPortOffset = 1;
    const int MaxConnectAttempts = 20;
    static int s_started;

    internal static void TryStart()
    {
        if (Interlocked.Exchange(ref s_started, 1) != 0)
            return;

        if (!TryGetEndpoint(out var host, out var port))
        {
            StartupProfilingDiagnostics.Log("No exit control endpoint was detected for this profiling session.");
            return;
        }

        _ = Task.Run(() => RunAsync(host, port));
    }

    internal static bool TryGetEndpoint(out string host, out int port)
    {
        host = "127.0.0.1";
        port = 0;

        var explicitHost = Environment.GetEnvironmentVariable(StartupProfilingMarker.ExitControlHostEnvironmentVariable);
        var explicitPort = Environment.GetEnvironmentVariable(StartupProfilingMarker.ExitControlPortEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(explicitPort)
            && int.TryParse(explicitPort, out var parsedPort)
            && parsedPort > 0
            && parsedPort <= IPEndPoint.MaxPort)
        {
            host = string.IsNullOrWhiteSpace(explicitHost) ? "127.0.0.1" : explicitHost.Trim();
            port = parsedPort;
            return true;
        }

        var diagnosticPorts = Environment.GetEnvironmentVariable(StartupProfilingMarker.DiagnosticPortsEnvironmentVariable)
            ?? Environment.GetEnvironmentVariable(StartupProfilingMarker.LegacyDiagnosticPortsEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(diagnosticPorts))
            return false;

        return TryDeriveFromDiagnosticPorts(diagnosticPorts, out host, out port);
    }

    static bool TryDeriveFromDiagnosticPorts(string diagnosticPorts, out string host, out int port)
    {
        host = "127.0.0.1";
        port = 0;

        var firstEndpoint = diagnosticPorts
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(firstEndpoint))
            return false;

        var endpointOnly = firstEndpoint.Split(',', 2, StringSplitOptions.TrimEntries)[0];
        if (!Uri.TryCreate($"tcp://{endpointOnly}", UriKind.Absolute, out var uri) || uri.Port <= 0)
            return false;

        if (uri.Port >= IPEndPoint.MaxPort)
            return false;

        host = uri.Host;
        port = checked(uri.Port + ExitControlPortOffset);
        return true;
    }

    static async Task RunAsync(string host, int port)
    {
        for (var attempt = 1; attempt <= MaxConnectAttempts; attempt++)
        {
            try
            {
                using var client = new TcpClient();
                StartupProfilingDiagnostics.Log($"Connecting exit control channel to {host}:{port} (attempt {attempt}/{MaxConnectAttempts}).");
                await client.ConnectAsync(host, port);

                using var stream = client.GetStream();
                using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);

                await writer.WriteLineAsync($"ready pid={Environment.ProcessId}");
                StartupProfilingDiagnostics.Log("Exit control channel connected; waiting for the host exit command.");

                while (true)
                {
                    var message = await reader.ReadLineAsync();
                    if (message is null)
                    {
                        StartupProfilingDiagnostics.Log("Exit control channel closed before an exit command arrived.");
                        return;
                    }

                    StartupProfilingDiagnostics.Log($"Exit control channel message received: '{message}'.");
                    if (string.Equals(message.Trim(), "exit", StringComparison.OrdinalIgnoreCase))
                    {
                        StartupProfilingDiagnostics.Log("Graceful exit requested by maui profile; terminating the app process.");
                        StartupProfilingDiagnostics.Log($"Calling Environment.Exit(0) now from exit control channel. PID={Environment.ProcessId}");
                        StartupProfilingDiagnostics.Flush();
                        Environment.Exit(0);
                    }
                }
            }
            catch (Exception ex)
            {
                StartupProfilingDiagnostics.Log($"Exit control connection attempt {attempt} failed: {ex.Message}");
                if (attempt == MaxConnectAttempts)
                    return;

                await Task.Delay(TimeSpan.FromMilliseconds(500));
            }
        }
    }

}
