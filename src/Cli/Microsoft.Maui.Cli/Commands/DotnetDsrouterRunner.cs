// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Maui.Cli.Errors;
using Microsoft.Maui.Cli.Output;

namespace Microsoft.Maui.Cli.Commands;

internal static class DotnetDsrouterRunner
{
	/// <summary>
	/// Starts dotnet-dsrouter and waits for it to print its PID to stdout.
	/// dotnet-dsrouter always prints a line containing <c>pid=&lt;N&gt;</c> shortly after startup.
	/// </summary>
	internal static async Task<(MonitoredProcess DsrouterProcess, int DsrouterPid)> StartAsync(
		ProfileTransportConfiguration transport,
		int diagnosticPort,
		IOutputFormatter formatter,
		bool useJson,
		bool verbose,
		CancellationToken cancellationToken)
	{
		var startInfo = new ProcessStartInfo
		{
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			RedirectStandardInput = true,
			CreateNoWindow = true
		};

		var dsrouterArgs = ProfileCommandArguments.BuildDsrouterArguments(transport, diagnosticPort);
		ProfileCommandDiagnostics.ConfigureDotnetToolStartInfo(startInfo, "dotnet-dsrouter", dsrouterArgs, out var commandLine);
		ProfileCommandProcessHelpers.WriteVerbose(formatter, useJson, verbose, $"dsrouter command: {commandLine}");

		var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
		if (!process.Start())
			throw new MauiToolException(ErrorCodes.InternalError, "Failed to start dotnet-dsrouter via dnx.");

		var pidRegex = new Regex(@"\bpid=(\d+)\b");
		var pidTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

		var monitoredProcess = MonitoredProcess.Attach(
			process,
			formatter,
			useJson,
			verbose,
			"dsrouter",
			cancellationToken,
			onStdoutLine: line =>
			{
				var match = pidRegex.Match(line);
				if (match.Success && int.TryParse(match.Groups[1].Value, out var pid))
				{
					pidTcs.TrySetResult(pid);
				}
			});

		using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeoutCts.CancelAfter(ProfileCommand.s_dsrouterStartupTimeout);

		int dsrouterPid;
		try
		{
			dsrouterPid = await pidTcs.Task.WaitAsync(timeoutCts.Token);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			if (!process.HasExited)
				process.Kill(entireProcessTree: true);

			throw new MauiToolException(
				ErrorCodes.InternalError,
				$"dotnet-dsrouter did not report its PID within {ProfileCommand.s_dsrouterStartupTimeout.TotalSeconds}s.",
				nativeError: monitoredProcess.GetCombinedOutput());
		}

		return (monitoredProcess, dsrouterPid);
	}

	internal static async Task EnsureStartedAsync(MonitoredProcess dsrouterProcess, int diagnosticPort, CancellationToken cancellationToken)
	{
		await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
		if (!dsrouterProcess.Process.HasExited)
			return;

		await dsrouterProcess.WaitForExitAsync();
		var details = dsrouterProcess.GetCombinedOutput();
		var message = "dotnet-dsrouter exited before the app launch started.";
		var suggestions = details.Contains("Address already in use", StringComparison.OrdinalIgnoreCase)
			? new[]
			{
				$"Port {diagnosticPort} is already in use. Wait for the previous profile run to finish, or stop the stale dotnet-dsrouter process.",
				"If needed, retry with a different --diagnostic-port value."
			}
			: null;

		throw suggestions is null
			? new MauiToolException(ErrorCodes.InternalError, message, nativeError: details)
			: MauiToolException.UserActionRequired(ErrorCodes.InternalError, message, suggestions, nativeError: details);
	}
}
