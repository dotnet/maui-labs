// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui.Cli.Models;
using Microsoft.Maui.Cli.Output;

namespace Microsoft.Maui.Cli.Commands;

internal sealed class ProfileSessionContext
{
	internal ProfileSessionContext(
		ProfileSessionRequest request,
		string primaryOutputPath,
		string profilePlatform,
		ProfileTransportConfiguration transport)
	{
		Request = request;
		PrimaryOutputPath = primaryOutputPath;
		ProfilePlatform = profilePlatform;
		Transport = transport;
		RequestedDiagnosticPort = request.DiagnosticPort;
		DiagnosticPort = request.DiagnosticPort;
		StartedAtUtc = DateTimeOffset.UtcNow;
	}

	internal ProfileSessionRequest Request { get; }

	internal ResolvedMauiProject Project => Request.Project;
	internal string Framework => Request.Framework;
	internal Device Device => Request.Device;
	internal string OutputPath => Request.OutputPath;
	internal TraceOutputFormat OutputFormat => Request.OutputFormat;
	internal string Configuration => Request.Configuration;
	internal string? TraceProfile => Request.TraceProfile;
	internal bool NoBuild => Request.NoBuild;
	internal TimeSpan? EffectiveDuration => Request.Duration;
	internal string? StoppingEventProvider => Request.StoppingEventProvider;
	internal string? StoppingEventName => Request.StoppingEventName;
	internal string? StoppingEventPayloadFilter => Request.StoppingEventPayloadFilter;
	internal bool AutoSelectedStoppingEvent => Request.AutoSelectedStoppingEvent;
	internal IOutputFormatter Formatter => Request.Formatter;
	internal bool UseJson => Request.UseJson;
	internal bool Verbose => Request.Verbose;

	internal string PrimaryOutputPath { get; }
	internal string ProfilePlatform { get; }
	internal ProfileTransportConfiguration Transport { get; }
	internal string DsrouterKind => Transport.DsrouterKind;
	internal string DiagnosticAddress => Transport.DiagnosticAddress;
	internal int RequestedDiagnosticPort { get; }
	internal int DiagnosticPort { get; set; }
	internal DateTimeOffset StartedAtUtc { get; }
	internal bool UseRuntimeOwnedTraceCollection { get; set; }
	internal string? RuntimeOwnedTraceDevicePath { get; set; }

	internal ReservedProfilePorts? ReservedPorts { get; set; }
	internal ExitControlServer? ExitControlServer { get; set; }
	internal ProfilingBuildInjection? BuildInjection { get; set; }
	internal MonitoredProcess? TraceProcess { get; set; }
}
