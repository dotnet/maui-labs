// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using Microsoft.Maui.Cli.Errors;
using Microsoft.Maui.Cli.Models;
using Microsoft.Maui.Cli.Output;

namespace Microsoft.Maui.Cli.Commands;

internal static class DotnetTraceRunner
{
	internal static MonitoredProcess StartCollector(
		string workingDirectory,
		string outputPath,
		TraceOutputFormat outputFormat,
		ProfileTransportConfiguration transport,
		Device device,
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

		var traceArgs = BuildTraceArguments(
			outputPath,
			outputFormat,
			transport,
			traceProfile,
			duration,
			stoppingEventProvider,
			stoppingEventName,
			stoppingEventPayloadFilter).ToArray();
		ProfileCommandDiagnostics.ConfigureDotnetToolStartInfo(startInfo, "dotnet-trace", traceArgs, out var commandLine);
		ProfileCommandProcessHelpers.WriteVerbose(formatter, useJson, verbose, $"Trace command: {commandLine}");

		if (string.Equals(transport.Platform, Platforms.Android, StringComparison.OrdinalIgnoreCase))
			startInfo.EnvironmentVariables["ANDROID_SERIAL"] = device.Id;

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

	internal static IEnumerable<string> BuildTraceArguments(
		string outputPath,
		TraceOutputFormat outputFormat,
		ProfileTransportConfiguration transport,
		string? traceProfile,
		TimeSpan? duration,
		string? stoppingEventProvider,
		string? stoppingEventName,
		string? stoppingEventPayloadFilter)
	{
		var args = new List<string>
		{
			"collect",
			"--dsrouter",
			transport.DsrouterKind,
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

		var requiresExtraRuntimeProviders = outputFormat == TraceOutputFormat.Mibc;

		if (!string.IsNullOrWhiteSpace(traceProfile))
		{
			args.Add("--profile");
			args.Add(traceProfile);
		}
		else if (!string.IsNullOrWhiteSpace(stoppingEventProvider) || requiresExtraRuntimeProviders)
		{
			args.Add("--profile");
			args.Add("dotnet-common,dotnet-sampled-thread-time");
		}

		if (duration is { } durationValue)
		{
			args.Add("--duration");
			args.Add(FormatDuration(durationValue));
		}

		var providers = new List<string>();
		if (requiresExtraRuntimeProviders || !string.IsNullOrWhiteSpace(stoppingEventProvider))
		{
			// Keep the runtime provider enabled for event-based stop conditions and MIBC traces
			// so the raw EventPipe data contains the JIT/profile payload dotnet-pgo expects.
			providers.Add(ProfileCommand.MibcDotnetRuntimeProvider);
		}

		if (!string.IsNullOrWhiteSpace(stoppingEventProvider))
		{
			providers.Add($"{stoppingEventProvider}:ffffffffffffffff:5");
		}

		if (providers.Count > 0)
		{
			args.Add("--providers");
			args.Add(string.Join(",", providers));
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
	internal static async Task EnsureStartedAsync(MonitoredProcess traceProcess, CancellationToken cancellationToken)
	{
		await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
		if (!traceProcess.Process.HasExited)
			return;

		await traceProcess.WaitForExitAsync();
		var details = traceProcess.GetCombinedOutput();
		throw new MauiToolException(
			ErrorCodes.InternalError,
			"dotnet-trace exited before the profiling session could be established.",
			nativeError: details);
	}

	internal static async Task<MonitoredProcess> StartWithRetryAsync(
		string workingDirectory,
		string outputPath,
		TraceOutputFormat outputFormat,
		ProfileTransportConfiguration transport,
		Device device,
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

		while (Stopwatch.GetElapsedTime(startedAt) < ProfileCommand.s_traceStartupRetryTimeout)
		{
			var traceProcess = StartCollector(
				workingDirectory,
				outputPath,
				outputFormat,
				transport,
				device,
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
				ProfileCommandProcessHelpers.WriteVerbose(
					formatter,
					useJson,
					verbose,
					$"Waiting briefly for dotnet-trace (PID {traceProcess.Process.Id}) to connect after launching the suspended app.");
				await EnsureStartedAsync(traceProcess, cancellationToken);
				return traceProcess;
			}
			catch (MauiToolException ex) when (IsRetryableStartupFailure(ex.NativeError))
			{
				lastFailure = ex;
				traceProcess.Dispose();
				ProfileCommandProcessHelpers.WriteVerbose(
					formatter,
					useJson,
					verbose,
					$"dotnet-trace could not connect yet; retrying in {ProfileCommand.s_traceStartupRetryDelay.TotalSeconds:0.#}s while the app runtime finishes opening its diagnostics channel.");
				await Task.Delay(ProfileCommand.s_traceStartupRetryDelay, cancellationToken);
			}
		}

		throw lastFailure ?? new MauiToolException(
			ErrorCodes.InternalError,
			$"dotnet-trace could not connect to the app within {ProfileCommand.s_traceStartupRetryTimeout.TotalSeconds:0}s.");
	}
	internal static bool IsRetryableStartupFailure(string? details)
	{
		if (string.IsNullOrWhiteSpace(details))
			return false;

		return details.Contains("EndOfStreamException", StringComparison.OrdinalIgnoreCase) ||
			details.Contains("ServerNotAvailableException", StringComparison.OrdinalIgnoreCase) ||
			details.Contains("Unable to connect to the server", StringComparison.OrdinalIgnoreCase) ||
			details.Contains("Connection refused", StringComparison.OrdinalIgnoreCase) ||
			details.Contains("Can't assign requested address", StringComparison.OrdinalIgnoreCase);
	}

	static string FormatDuration(TimeSpan duration)
	{
		var positiveDuration = duration < TimeSpan.Zero ? duration.Negate() : duration;
		return $"{(int)positiveDuration.TotalDays:00}:{positiveDuration.Hours:00}:{positiveDuration.Minutes:00}:{positiveDuration.Seconds:00}";
	}
}
