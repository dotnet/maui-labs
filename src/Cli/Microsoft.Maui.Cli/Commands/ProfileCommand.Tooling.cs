// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Maui.Cli.Errors;
using Microsoft.Maui.Cli.Output;
using Microsoft.Maui.Cli.Utils;

namespace Microsoft.Maui.Cli.Commands;

public static partial class ProfileCommand
{
	static void ValidateDnxAvailable()
	{
		var hasDnx = ProcessRunner.GetCommandPath("dnx") is not null;
		var hasDotnetTrace = CanResolveDiagnosticsTool(
			FindInstalledDotnetToolCommand("dotnet-trace"),
			FindCachedDotnetToolDll("dotnet-trace"));
		var hasDotnetDsrouter = CanResolveDiagnosticsTool(
			FindInstalledDotnetToolCommand("dotnet-dsrouter"),
			FindCachedDotnetToolDll("dotnet-dsrouter"));

		if (CanUseDiagnosticsTooling(hasDnx, hasDotnetTrace, hasDotnetDsrouter))
		{
			return;
		}

		throw MauiToolException.UserActionRequired(
			ErrorCodes.DiagnosticsToolNotFound,
			"Neither 'dnx' nor the required dotnet diagnostics tools were found.",
			[
				"Install the global tools: `dotnet tool install -g dotnet-trace` and `dotnet tool install -g dotnet-dsrouter`.",
				"Or use a .NET 10 SDK with `dnx` available on PATH: https://dot.net/download"
			]);
	}

	static void ConfigureDotnetToolStartInfo(ProcessStartInfo startInfo, string packageId, IReadOnlyList<string> toolArgs, out string commandLine)
	{
		var installedToolPath = FindInstalledDotnetToolCommand(packageId);
		if (installedToolPath is not null)
		{
			startInfo.FileName = installedToolPath;
			foreach (var arg in toolArgs)
				startInfo.ArgumentList.Add(arg);

			commandLine = FormatCommandLine(installedToolPath, [.. toolArgs]);
			return;
		}

		var cachedToolDll = FindCachedDotnetToolDll(packageId);
		if (cachedToolDll is not null)
		{
			startInfo.FileName = "dotnet";
			startInfo.ArgumentList.Add(cachedToolDll);
			foreach (var arg in toolArgs)
				startInfo.ArgumentList.Add(arg);

			commandLine = FormatCommandLine("dotnet", [cachedToolDll, .. toolArgs]);
			return;
		}

		startInfo.FileName = "dnx";
		startInfo.ArgumentList.Add("-y");
		startInfo.ArgumentList.Add(packageId);
		startInfo.ArgumentList.Add("--");
		foreach (var arg in toolArgs)
			startInfo.ArgumentList.Add(arg);

		commandLine = FormatCommandLine("dnx", ["-y", packageId, "--", .. toolArgs]);
	}

	internal static bool CanResolveDiagnosticsTool(string? installedToolPath, string? cachedToolDll)
		=> !string.IsNullOrWhiteSpace(installedToolPath) || !string.IsNullOrWhiteSpace(cachedToolDll);

	internal static bool CanUseDiagnosticsTooling(bool hasDnx, bool hasDotnetTrace, bool hasDotnetDsrouter)
		=> hasDnx || (hasDotnetTrace && hasDotnetDsrouter);

	static string? FindInstalledDotnetToolCommand(string packageId)
	{
		var commandPath = ProcessRunner.GetCommandPath(packageId);
		if (!string.IsNullOrWhiteSpace(commandPath))
			return commandPath;

		var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		if (string.IsNullOrWhiteSpace(userProfile))
			return null;

		var extension = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
		var candidate = Path.Combine(userProfile, ".dotnet", "tools", packageId + extension);
		return File.Exists(candidate) ? candidate : null;
	}

	/// <summary>
	/// Starts dotnet-dsrouter via <c>dnx</c> and waits for it to print its PID to stdout.
	/// dotnet-dsrouter always prints a line containing <c>pid=&lt;N&gt;</c> shortly after startup.
	/// </summary>
	static async Task<(MonitoredProcess DnxProcess, int DsrouterPid)> StartDsrouterAsync(
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

		var dsrouterArgs = BuildDsrouterArguments(transport, diagnosticPort);
		ConfigureDotnetToolStartInfo(startInfo, "dotnet-dsrouter", dsrouterArgs, out var commandLine);
		WriteVerbose(formatter, useJson, verbose, $"dsrouter command: {commandLine}");

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
				var m = pidRegex.Match(line);
				if (m.Success && int.TryParse(m.Groups[1].Value, out var pid))
				{
					pidTcs.TrySetResult(pid);
				}
			});

		using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeoutCts.CancelAfter(s_dsrouterStartupTimeout);

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
				$"dotnet-dsrouter did not report its PID within {s_dsrouterStartupTimeout.TotalSeconds}s.",
				nativeError: monitoredProcess.GetCombinedOutput());
		}

		return (monitoredProcess, dsrouterPid);
	}

	static MonitoredProcess StartTraceCollector(
		string workingDirectory,
		string outputPath,
		TraceOutputFormat outputFormat,
		int dsrouterPid,
		string androidSerial,
		string? traceProfile,
		TimeSpan? duration,
		string? stoppingEventProvider,
		string? stoppingEventName,
		string? stoppingEventPayloadFilter,
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

		var traceArgs = BuildTraceArguments(outputPath, outputFormat, dsrouterPid, traceProfile, duration, stoppingEventProvider, stoppingEventName, stoppingEventPayloadFilter).ToArray();
		ConfigureDotnetToolStartInfo(startInfo, "dotnet-trace", traceArgs, out var commandLine);
		WriteVerbose(formatter, useJson, verbose, $"Trace command: {commandLine}");

		startInfo.EnvironmentVariables["ANDROID_SERIAL"] = androidSerial;

		var process = new Process
		{
			StartInfo = startInfo,
			EnableRaisingEvents = true
		};

		if (!process.Start())
		{
			throw new MauiToolException(
				ErrorCodes.InternalError,
				"Failed to start dotnet-trace.");
		}

		return MonitoredProcess.Attach(process, formatter, useJson, verbose, "trace", cancellationToken);
	}

	static string? FindCachedDotnetToolDll(string packageId)
	{
		var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		if (string.IsNullOrWhiteSpace(userProfile))
			return null;

		var packageRoot = Path.Combine(userProfile, ".nuget", "packages", packageId.ToLowerInvariant());
		if (!Directory.Exists(packageRoot))
			return null;

		var versionDirectories = Directory
			.GetDirectories(packageRoot)
			.OrderByDescending(path => TryParsePackageVersion(Path.GetFileName(path), out var version) ? version : new Version(0, 0))
			.ThenByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase);

		foreach (var versionDirectory in versionDirectories)
		{
			var directPath = Path.Combine(versionDirectory, "tools", "net8.0", "any", $"{packageId}.dll");
			if (File.Exists(directPath))
				return directPath;

			var candidate = Directory
				.EnumerateFiles(versionDirectory, $"{packageId}.dll", SearchOption.AllDirectories)
				.FirstOrDefault(path =>
					path.Contains($"{Path.DirectorySeparatorChar}tools{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
					path.Contains($"{Path.AltDirectorySeparatorChar}tools{Path.AltDirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
			if (candidate is not null)
				return candidate;
		}

		return null;
	}

	static bool TryParsePackageVersion(string? value, out Version version)
	{
		var success = Version.TryParse(value, out var parsedVersion);
		version = parsedVersion ?? new Version(0, 0);
		return success;
	}

	internal static IEnumerable<string> BuildTraceArguments(
		string outputPath,
		TraceOutputFormat outputFormat,
		int dsrouterPid,
		string? traceProfile,
		TimeSpan? duration,
		string? stoppingEventProvider,
		string? stoppingEventName,
		string? stoppingEventPayloadFilter)
	{
		var args = new List<string>
		{
			"collect",
			"--process-id",
			dsrouterPid.ToString(),
			"--format",
			outputFormat switch
			{
				TraceOutputFormat.Speedscope => "Speedscope",
				_ => "NetTrace"
			},
			"--output",
			outputPath,
			"--resume-runtime"
		};

		if (!string.IsNullOrWhiteSpace(traceProfile))
		{
			args.Add("--profile");
			args.Add(traceProfile);
		}
		else if (!string.IsNullOrWhiteSpace(stoppingEventProvider))
		{
			args.Add("--profile");
			args.Add("dotnet-common,dotnet-sampled-thread-time");
		}

		if (duration is { } durationValue)
		{
			args.Add("--duration");
			args.Add(FormatDuration(durationValue));
		}

		if (!string.IsNullOrWhiteSpace(stoppingEventProvider))
		{
			args.Add("--providers");
			args.Add($"{stoppingEventProvider}:ffffffffffffffff:5");
		}

		if (!string.IsNullOrWhiteSpace(stoppingEventProvider))
		{
			args.Add("--stopping-event-provider-name");
			args.Add(stoppingEventProvider);
		}

		if (!string.IsNullOrWhiteSpace(stoppingEventName))
		{
			args.Add("--stopping-event-event-name");
			args.Add(stoppingEventName);
		}

		if (!string.IsNullOrWhiteSpace(stoppingEventPayloadFilter))
		{
			args.Add("--stopping-event-payload-filter");
			args.Add(stoppingEventPayloadFilter);
		}

		return args;
	}

	static async Task EnsureTraceCollectorStartedAsync(MonitoredProcess traceProcess, CancellationToken cancellationToken)
	{
		await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
		if (!traceProcess.Process.HasExited)
			return;

		await traceProcess.WaitForExitAsync();
		var details = traceProcess.GetCombinedOutput();
		throw new MauiToolException(
			ErrorCodes.InternalError,
			"dotnet-trace exited before the app launch started.",
			nativeError: details);
	}

	static async Task<MonitoredProcess> StartTraceCollectorWithRetryAsync(
		string workingDirectory,
		string outputPath,
		TraceOutputFormat outputFormat,
		int dsrouterPid,
		string androidSerial,
		string? traceProfile,
		TimeSpan? duration,
		string? stoppingEventProvider,
		string? stoppingEventName,
		string? stoppingEventPayloadFilter,
		IOutputFormatter formatter,
		bool useJson,
		bool verbose,
		CancellationToken cancellationToken)
	{
		var startedAt = Stopwatch.GetTimestamp();
		MauiToolException? lastFailure = null;

		while (Stopwatch.GetElapsedTime(startedAt) < s_traceStartupRetryTimeout)
		{
			var traceProcess = StartTraceCollector(
				workingDirectory,
				outputPath,
				outputFormat,
				dsrouterPid,
				androidSerial,
				traceProfile,
				duration,
				stoppingEventProvider,
				stoppingEventName,
				stoppingEventPayloadFilter,
				formatter,
				useJson,
				verbose,
				cancellationToken);

			try
			{
				WriteVerbose(formatter, useJson, verbose, $"Waiting briefly for dotnet-trace (PID {traceProcess.Process.Id}) to connect after launching the suspended iOS app.");
				await EnsureTraceCollectorStartedAsync(traceProcess, cancellationToken);
				return traceProcess;
			}
			catch (MauiToolException ex) when (IsRetryableTraceStartupFailure(ex.NativeError))
			{
				lastFailure = ex;
				traceProcess.Dispose();
				WriteVerbose(
					formatter,
					useJson,
					verbose,
					$"dotnet-trace could not connect yet; retrying in {s_traceStartupRetryDelay.TotalSeconds:0.#}s while the iOS runtime finishes opening its diagnostics channel.");
				await Task.Delay(s_traceStartupRetryDelay, cancellationToken);
			}
		}

		throw lastFailure ?? new MauiToolException(
			ErrorCodes.InternalError,
			$"dotnet-trace could not connect to the iOS app within {s_traceStartupRetryTimeout.TotalSeconds:0}s.");
	}

	internal static bool IsRetryableTraceStartupFailure(string? details)
	{
		if (string.IsNullOrWhiteSpace(details))
			return false;

		return details.Contains("EndOfStreamException", StringComparison.OrdinalIgnoreCase) ||
			details.Contains("ServerNotAvailableException", StringComparison.OrdinalIgnoreCase) ||
			details.Contains("Unable to connect to the server", StringComparison.OrdinalIgnoreCase) ||
			details.Contains("Connection refused", StringComparison.OrdinalIgnoreCase) ||
			details.Contains("Can't assign requested address", StringComparison.OrdinalIgnoreCase);
	}

	static async Task EnsureDsrouterStartedAsync(MonitoredProcess dsrouterProcess, int diagnosticPort, CancellationToken cancellationToken)
	{
		await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
		if (!dsrouterProcess.Process.HasExited)
			return;

		await dsrouterProcess.WaitForExitAsync();
		var details = dsrouterProcess.GetCombinedOutput();
		var message = $"dotnet-dsrouter exited before the app launch started.";
		var suggestions = details.Contains("Address already in use", StringComparison.OrdinalIgnoreCase)
			? new[]
			{
				$"Port {diagnosticPort} is already in use. Wait for the previous profile run to finish, or stop the stale dotnet-dsrouter process.",
				$"If needed, retry with a different --diagnostic-port value."
			}
			: null;

		throw suggestions is null
			? new MauiToolException(ErrorCodes.InternalError, message, nativeError: details)
			: MauiToolException.UserActionRequired(ErrorCodes.InternalError, message, suggestions, nativeError: details);
	}

	static string GetProcessFailureDetails(ProcessResult result) =>
		string.IsNullOrWhiteSpace(result.StandardError)
			? result.StandardOutput.Trim()
			: result.StandardError.Trim();

	static void WriteVerbose(IOutputFormatter formatter, bool useJson, bool verbose, string message)
	{
		if (verbose && !useJson)
			formatter.WriteProgress($"[debug] {message}");
	}

	static string FormatCommandLine(string fileName, IEnumerable<string> arguments) =>
		string.Join(" ", [QuoteForDisplay(fileName), .. arguments.Select(QuoteForDisplay)]);

	static string QuoteForDisplay(string value) =>
		value.Any(ch => char.IsWhiteSpace(ch) || ch is '"' or '\'')
			? $"\"{value.Replace("\"", "\\\"")}\""
			: value;

	static Exception CreateProcessFailureException(string commandName, ProcessResult result)
	{
		var details = string.IsNullOrWhiteSpace(result.StandardError)
			? result.StandardOutput.Trim()
			: result.StandardError.Trim();

		return new MauiToolException(
			ErrorCodes.InternalError,
			$"{commandName} failed with exit code {result.ExitCode}.",
			nativeError: details);
	}
}
