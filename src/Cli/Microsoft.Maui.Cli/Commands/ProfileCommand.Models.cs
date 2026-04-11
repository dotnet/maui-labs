// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.Cli.Commands;

internal sealed record ResolvedMauiProject
{
	public required string ProjectPath { get; init; }
	public required string ProjectDirectory { get; init; }
	public required string ProjectName { get; init; }
	public required IReadOnlyList<string> TargetFrameworks { get; init; }
}

internal sealed record StoppingEventConfiguration(
	string? ProviderName,
	string? EventName,
	string? PayloadFilter,
	bool AutoSelected);

internal enum TraceOutputFormat
{
	NetTrace,
	Speedscope
}

internal sealed record ProfilingBuildInjection(
	string TargetsPath,
	string AssemblyPath,
	string ExitControlHost,
	int ExitControlPort,
	bool InjectBootstrap);

internal sealed record ProfileTransportConfiguration(
	string Platform,
	string DiagnosticAddress,
	string DiagnosticListenMode,
	string DsrouterKind,
	string DsrouterRuntimeEndpointOption,
	string? DsrouterForwardPort,
	bool RequiresManualExitControlPortRouting);

internal sealed record MauiProfileResult
{
	public required string ProjectPath { get; init; }
	public required string ProjectName { get; init; }
	public required string Framework { get; init; }
	public required string Platform { get; init; }
	public required string DeviceId { get; init; }
	public required string DeviceName { get; init; }
	public required string Configuration { get; init; }
	public required string Format { get; init; }
	public required string OutputPath { get; init; }
	public string? RawTracePath { get; init; }
	public required string DsrouterKind { get; init; }
	public required string DiagnosticAddress { get; init; }
	public required int DiagnosticPort { get; init; }
	public required bool UsedStoppingEvent { get; init; }
	public required DateTimeOffset StartedAtUtc { get; init; }
	public required DateTimeOffset CompletedAtUtc { get; init; }
}
