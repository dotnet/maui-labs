// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.Tracing;

namespace Microsoft.Maui.ProfilingHelper;

/// <summary>
/// EventSource that signals startup profiling milestones to dotnet-trace.
/// Provider name: <c>Microsoft.Maui.ProfilingHelper</c>
/// </summary>
[EventSource(Name = MauiProfilingEventSource.ProviderName)]
internal sealed class MauiProfilingEventSource : EventSource
{
	/// <summary>The ETW/EventPipe provider name used with <c>dotnet-trace --stopping-event-provider-name</c>.</summary>
	internal const string ProviderName = "Microsoft.Maui.ProfilingHelper";

	/// <summary>The event name used with <c>dotnet-trace --stopping-event-event-name</c>.</summary>
	internal const string StartupCompleteEventName = "StartupComplete";

	internal static readonly MauiProfilingEventSource Log = new();

	/// <summary>
	/// Emitted when the app considers startup logically complete.
	/// dotnet-trace stops collection when it sees this event (if configured with
	/// <c>--stopping-event-provider-name Microsoft.Maui.ProfilingHelper --stopping-event-event-name StartupComplete</c>).
	/// </summary>
	[Event(1, Level = EventLevel.Informational)]
	internal void StartupComplete() => WriteEvent(1);
}
