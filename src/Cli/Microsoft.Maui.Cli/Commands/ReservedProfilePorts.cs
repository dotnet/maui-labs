// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.Cli.Commands;

internal sealed class ReservedProfilePorts(
	int diagnosticPort,
	int exitControlPort,
	ReservedTcpPort diagnosticReservation,
	ReservedTcpPort exitControlReservation) : IDisposable
{
	public int DiagnosticPort { get; } = diagnosticPort;
	public int ExitControlPort { get; } = exitControlPort;
	public ReservedTcpPort DiagnosticReservation { get; } = diagnosticReservation;
	public ReservedTcpPort ExitControlReservation { get; } = exitControlReservation;

	public void Dispose()
	{
		DiagnosticReservation.Dispose();
		ExitControlReservation.Dispose();
	}
}
