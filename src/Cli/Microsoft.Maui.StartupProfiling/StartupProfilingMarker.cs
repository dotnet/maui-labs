// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;

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
        string.Equals(
            Environment.GetEnvironmentVariable("MAUI_STARTUP_PROFILING"),
            "1",
            StringComparison.OrdinalIgnoreCase)
        || string.Equals(
            Environment.GetEnvironmentVariable("MAUI_STARTUP_PROFILING"),
            "true",
            StringComparison.OrdinalIgnoreCase);

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
        StartupProfilingEventSource.Log.StartupComplete();

        if (string.Equals(
                Environment.GetEnvironmentVariable("MAUI_STARTUP_PROFILING_AUTO_EXIT"),
                "1",
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                Environment.GetEnvironmentVariable("MAUI_STARTUP_PROFILING_AUTO_EXIT"),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            Environment.Exit(0);
        }
    }
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
    }
}
