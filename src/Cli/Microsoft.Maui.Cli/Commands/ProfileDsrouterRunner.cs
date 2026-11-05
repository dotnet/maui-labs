// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using Microsoft.Maui.Cli.Errors;
using Microsoft.Maui.Cli.Models;
using Microsoft.Maui.Cli.Output;

namespace Microsoft.Maui.Cli.Commands;

internal static class ProfileDsrouterRunner
{
	internal static string CreateIpcEndpoint()
	{
		var endpointName = $"maui-profile-dsrouter-{Guid.NewGuid():N}";
		if (OperatingSystem.IsWindows())
			return endpointName;

		var ipcRoot = Directory.Exists("/tmp") ? "/tmp" : Path.GetTempPath();
		return Path.Combine(ipcRoot, $"{endpointName}-socket");
	}

	internal static MonitoredProcess Start(
		string workingDirectory,
		string ipcEndpoint,
		int tcpPort,
		Device device,
		IOutputFormatter formatter,
		bool useJson,
		bool verbose,
		CancellationToken cancellationToken)
	{
		var startInfo = new ProcessStartInfo
		{
			WorkingDirectory = workingDirectory,
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			RedirectStandardInput = true,
			CreateNoWindow = true
		};

		var args = BuildArguments(ipcEndpoint, tcpPort);
		ProfileCommandDiagnostics.ConfigureDotnetToolStartInfo(startInfo, "dotnet-dsrouter", args, out var commandLine);
		startInfo.EnvironmentVariables["ANDROID_SERIAL"] = device.Id;
		ProfileCommandProcessHelpers.WriteVerbose(formatter, useJson, verbose, $"Dsrouter command: {commandLine}");

		var process = new Process
		{
			StartInfo = startInfo,
			EnableRaisingEvents = true
		};

		if (!process.Start())
		{
			throw new MauiToolException(
				ErrorCodes.InternalError,
				"Failed to start dotnet-dsrouter.");
		}

		return MonitoredProcess.Attach(process, formatter, useJson, verbose, "dsrouter", cancellationToken);
	}

	internal static string[] BuildArguments(string ipcEndpoint, int tcpPort) =>
	[
		"server-server",
		"--ipc-server", ipcEndpoint,
		"--tcp-server", $"127.0.0.1:{tcpPort}",
		"--forward-port", "Android"
	];

	internal static async Task EnsureStartedAsync(MonitoredProcess dsrouterProcess, CancellationToken cancellationToken)
	{
		await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
		if (!dsrouterProcess.Process.HasExited)
			return;

		await dsrouterProcess.WaitForExitAsync();
		throw new MauiToolException(
			ErrorCodes.InternalError,
			"dotnet-dsrouter exited before the profiling session could be established.",
			nativeError: dsrouterProcess.GetCombinedOutput());
	}

	internal static void DeleteIpcEndpoint(string? ipcEndpoint)
	{
		if (OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(ipcEndpoint))
			return;

		try
		{
			File.Delete(ipcEndpoint);
		}
		catch (IOException)
		{
			// Best-effort cleanup only.
		}
		catch (UnauthorizedAccessException)
		{
			// Best-effort cleanup only.
		}
	}
}
