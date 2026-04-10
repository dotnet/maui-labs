// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.Tracing;
using System.Linq;

namespace Microsoft.Maui.StartupProfiling;

/// <summary>
/// EventSource that signals startup profiling milestones to dotnet-trace.
/// Provider name: <c>Microsoft.Maui.StartupProfiling</c>
/// </summary>
[EventSource(Name = StartupProfilingEventSource.ProviderName)]
internal sealed class StartupProfilingEventSource : EventSource
{
    /// <summary>The ETW/EventPipe provider name used with <c>dotnet-trace --stopping-event-provider-name</c>.</summary>
    internal const string ProviderName = "Microsoft.Maui.StartupProfiling";

    /// <summary>The event name used with <c>dotnet-trace --stopping-event-event-name</c>.</summary>
    internal const string StartupCompleteEventName = "StartupComplete";

    /// <summary>Diagnostic event emitted to help troubleshoot provider wiring and marker timing.</summary>
    internal const string DiagnosticEventName = "Diagnostic";

    internal static readonly StartupProfilingEventSource Log = new();

    private StartupProfilingEventSource()
    {
        StartupProfilingDiagnostics.Log("StartupProfilingEventSource created.");
    }

    protected override void OnEventCommand(EventCommandEventArgs command)
    {
        base.OnEventCommand(command);

        var arguments = command.Arguments is { Count: > 0 }
            ? string.Join(", ", command.Arguments.Select(pair => $"{pair.Key}={pair.Value}"))
            : "(none)";

        var message = $"EventSource command={command.Command}, enabled={IsEnabled()}, arguments={arguments}";
        StartupProfilingDiagnostics.Log(message);

        if (command.Command == EventCommand.Enable)
            Diagnostic(message);
    }

    /// <summary>
    /// Emitted when the app considers startup logically complete.
    /// dotnet-trace stops collection when it sees this event (if configured with
    /// <c>--stopping-event-provider-name Microsoft.Maui.StartupProfiling --stopping-event-event-name StartupComplete</c>).
    /// </summary>
    [Event(1, Level = EventLevel.Informational)]
    internal void StartupComplete() => WriteEvent(1);

    [Event(2, Level = EventLevel.Informational, Message = "{0}")]
    internal void Diagnostic(string message) => WriteEvent(2, message ?? string.Empty);
}
