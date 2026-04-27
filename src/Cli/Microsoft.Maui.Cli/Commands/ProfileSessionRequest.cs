// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui.Cli.Models;
using Microsoft.Maui.Cli.Output;

namespace Microsoft.Maui.Cli.Commands;

internal sealed record ProfileSessionRequest(
	ResolvedMauiProject Project,
	string Framework,
	Device Device,
	string OutputPath,
	TraceOutputFormat OutputFormat,
	string Configuration,
	string? TraceProfile,
	bool NoBuild,
	int DiagnosticPort,
	TimeSpan? Duration,
	string? StoppingEventProvider,
	string? StoppingEventName,
	string? StoppingEventPayloadFilter,
	bool AutoSelectedStoppingEvent,
	IOutputFormatter Formatter,
	bool UseJson,
	bool Verbose,
	bool ManualStart = false);
