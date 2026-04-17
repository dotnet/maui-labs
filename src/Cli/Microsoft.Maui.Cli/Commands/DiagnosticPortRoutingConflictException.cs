// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.Cli.Commands;

internal sealed class DiagnosticPortRoutingConflictException(int port, string direction, string details)
	: Exception($"Diagnostic port {port} was unavailable during adb {direction} routing.")
{
	public int Port { get; } = port;
	public string Direction { get; } = direction;
	public string Details { get; } = details;
}
