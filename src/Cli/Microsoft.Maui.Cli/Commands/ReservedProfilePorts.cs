// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.Cli.Commands;

internal sealed class ReservedProfilePorts(
	int diagnosticPort,
	int? dsrouterTcpPort,
	int exitControlPort,
	ReservedTcpPort diagnosticReservation,
	ReservedTcpPort? dsrouterTcpReservation,
	ReservedTcpPort exitControlReservation) : IDisposable
{
	public int DiagnosticPort { get; } = diagnosticPort;
	public int? DsrouterTcpPort { get; } = dsrouterTcpPort;
	public int ExitControlPort { get; } = exitControlPort;
	public ReservedTcpPort DiagnosticReservation { get; } = diagnosticReservation;
	public ReservedTcpPort? DsrouterTcpReservation { get; } = dsrouterTcpReservation;
	public ReservedTcpPort ExitControlReservation { get; } = exitControlReservation;

	public void Dispose()
	{
		DiagnosticReservation.Dispose();
		DsrouterTcpReservation?.Dispose();
		ExitControlReservation.Dispose();
	}
}
