// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using Microsoft.Maui.Cli.Errors;
using Microsoft.Maui.Cli.Models;
using Microsoft.Maui.Cli.Output;

namespace Microsoft.Maui.Cli.Commands;

internal static class DotnetTraceRunner
{
	// Match the provider mask/verbosity used by the known-good Android IBC flow in
	// dotnet-optimization so dotnet-pgo can see the richer JIT/R2R/profile payload.
	const string StartupPgoRuntimeProvider = "Microsoft-Windows-DotNETRuntime:0x1F000080018:5";

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
			args.Add(BuildProviderList(stoppingEventProvider));
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

	static string BuildProviderList(string stoppingEventProvider)
	{
		// dotnet-pgo create-mibc needs runtime JIT/R2R events in the trace.
		// Keep the startup-complete provider too so dotnet-trace can still stop automatically.
		return string.Join(
			",",
			[
				StartupPgoRuntimeProvider,
				$"{stoppingEventProvider}:ffffffffffffffff:5"
			]);
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
			"dotnet-trace exited before the app launch started.",
			nativeError: details);
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
