// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.Cli.Commands;

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
