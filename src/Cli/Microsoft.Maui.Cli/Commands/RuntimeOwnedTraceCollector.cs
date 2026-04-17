// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using Microsoft.Maui.Cli.Errors;
using Microsoft.Maui.Cli.Utils;

namespace Microsoft.Maui.Cli.Commands;

internal static class RuntimeOwnedTraceCollector
{
	static readonly TimeSpan s_traceWaitTimeout = TimeSpan.FromMinutes(2);
	static readonly TimeSpan s_pollDelay = TimeSpan.FromSeconds(2);
	static readonly TimeSpan s_postExitFlushDelay = TimeSpan.FromSeconds(3);
	static readonly TimeSpan s_pullTimeout = TimeSpan.FromMinutes(2);
	const int RequiredStableSamples = 2;

	internal static async Task PrepareAsync(ProfileSessionContext context, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(context.RuntimeOwnedTraceDevicePath))
			return;

		if (UsesAppInternalStorage(context.RuntimeOwnedTraceDevicePath))
			return;

		var adbPath = RequireAdbPath();
		var deviceDirectory = Path.GetDirectoryName(context.RuntimeOwnedTraceDevicePath.Replace('\\', '/'))?.Replace('\\', '/');
		if (!string.IsNullOrWhiteSpace(deviceDirectory))
		{
			_ = await ProcessRunner.RunAsync(
				adbPath,
				["-s", context.Device.Id, "shell", "mkdir", "-p", deviceDirectory],
				timeout: ProfileCommand.s_adbPortForwardTimeout,
				cancellationToken: cancellationToken);
		}

		_ = await ProcessRunner.RunAsync(
			adbPath,
			["-s", context.Device.Id, "shell", "rm", "-f", context.RuntimeOwnedTraceDevicePath],
			timeout: ProfileCommand.s_adbPortForwardTimeout,
			cancellationToken: cancellationToken);
	}

	internal static async Task WaitForAndPullAsync(ProfileSessionContext context, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(context.RuntimeOwnedTraceDevicePath))
		{
			throw new MauiToolException(
				ErrorCodes.InternalError,
				"Runtime-owned trace collection was selected, but no Android trace output path was configured.");
		}

		var adbPath = RequireAdbPath();
		var applicationId = RequireAndroidApplicationId(context);
		await WaitForAppExitAsync(adbPath, context, applicationId, cancellationToken);
		await Task.Delay(s_postExitFlushDelay, cancellationToken);

		if (UsesAppInternalStorage(context.RuntimeOwnedTraceDevicePath))
		{
			await WaitForStableTraceAsync(
				cancellationToken => ProbeInternalTraceAsync(adbPath, context, applicationId, cancellationToken),
				$"The Android runtime-owned EventPipe trace at '{context.RuntimeOwnedTraceDevicePath}' never stabilized after the app exited.",
				cancellationToken);
			await PullInternalTraceAsync(adbPath, context, applicationId, cancellationToken);
			await DeleteInternalTraceAsync(adbPath, context, applicationId, CancellationToken.None);
			return;
		}

		await WaitForStableTraceAsync(
			cancellationToken => ProbeExternalTraceAsync(adbPath, context, cancellationToken),
			$"The Android runtime-owned EventPipe trace at '{context.RuntimeOwnedTraceDevicePath}' never stabilized after the app exited.",
			cancellationToken);

		var pullResult = await ProcessRunner.RunAsync(
			adbPath,
			["-s", context.Device.Id, "pull", context.RuntimeOwnedTraceDevicePath, context.OutputPath],
			timeout: s_pullTimeout,
			cancellationToken: cancellationToken);

		if (!pullResult.Success || !File.Exists(context.OutputPath))
		{
			throw new MauiToolException(
				ErrorCodes.InternalError,
				$"The Android runtime-owned EventPipe trace could not be pulled from '{context.RuntimeOwnedTraceDevicePath}'.",
				nativeError: ProfileCommandProcessHelpers.GetProcessFailureDetails(pullResult));
		}

		_ = await ProcessRunner.RunAsync(
			adbPath,
			["-s", context.Device.Id, "shell", "rm", "-f", context.RuntimeOwnedTraceDevicePath],
			timeout: ProfileCommand.s_adbPortForwardTimeout,
			cancellationToken: CancellationToken.None);
	}

	static string RequireAdbPath()
		=> ProfileCommandPortRouter.ResolveAdbPath()
			?? throw MauiToolException.UserActionRequired(
				ErrorCodes.AndroidAdbNotFound,
				"ADB was not found, so the Android runtime-owned EventPipe trace could not be retrieved.",
				[
					"Install Android platform-tools so adb is available.",
					"Or add adb to PATH and rerun `maui profile startup`."
				]);

	internal static long TryParseLongListingSize(string output)
	{
		var line = output
			.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.FirstOrDefault();
		if (string.IsNullOrWhiteSpace(line))
			return 0;

		var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (parts.Length < 5)
			return 0;

		return long.TryParse(parts[4], out var size) ? size : 0;
	}

	static bool UsesAppInternalStorage(string devicePath)
		=> devicePath.StartsWith("/data/user/0/", StringComparison.OrdinalIgnoreCase)
			|| devicePath.StartsWith("/data/data/", StringComparison.OrdinalIgnoreCase);

	static string RequireAndroidApplicationId(ProfileSessionContext context)
		=> MauiProjectResolver.GetAndroidApplicationId(context.Project.ProjectPath, context.Framework, context.Configuration)
			?? throw new MauiToolException(
				ErrorCodes.InternalError,
				$"Could not resolve the Android application ID for '{context.Project.ProjectPath}'.");

	static async Task WaitForStableTraceAsync(
		Func<CancellationToken, Task<TraceProbeResult>> probeAsync,
		string timeoutMessage,
		CancellationToken cancellationToken)
	{
		var startedAt = Stopwatch.GetTimestamp();
		long? previousSize = null;
		var stableSamples = 0;
		string? lastFailure = null;

		while (Stopwatch.GetElapsedTime(startedAt) < s_traceWaitTimeout)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var probe = await probeAsync(cancellationToken);
			if (!string.IsNullOrWhiteSpace(probe.FailureDetails))
				lastFailure = probe.FailureDetails;

			if (probe.Size > 0)
			{
				stableSamples = previousSize == probe.Size ? stableSamples + 1 : 0;
				previousSize = probe.Size;
				if (stableSamples >= RequiredStableSamples)
					return;
			}

			await Task.Delay(s_pollDelay, cancellationToken);
		}

		throw new MauiToolException(
			ErrorCodes.InternalError,
			timeoutMessage,
			nativeError: lastFailure);
	}

	static async Task PullInternalTraceAsync(string adbPath, ProfileSessionContext context, string applicationId, CancellationToken cancellationToken)
	{
		var startInfo = new ProcessStartInfo(adbPath)
		{
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true
		};

		startInfo.ArgumentList.Add("-s");
		startInfo.ArgumentList.Add(context.Device.Id);
		startInfo.ArgumentList.Add("exec-out");
		startInfo.ArgumentList.Add("run-as");
		startInfo.ArgumentList.Add(applicationId);
		startInfo.ArgumentList.Add("cat");
		startInfo.ArgumentList.Add(context.RuntimeOwnedTraceDevicePath!);

		using var process = new Process { StartInfo = startInfo };
		process.Start();

		await using var outputStream = File.Create(context.OutputPath);
		var stderrTask = process.StandardError.ReadToEndAsync();

		try
		{
			await process.StandardOutput.BaseStream.CopyToAsync(outputStream, cancellationToken);
			await process.WaitForExitAsync(cancellationToken);
			var stderr = await stderrTask;

			if (process.ExitCode != 0 || !File.Exists(context.OutputPath) || new FileInfo(context.OutputPath).Length == 0)
			{
				if (File.Exists(context.OutputPath))
					File.Delete(context.OutputPath);

				throw new MauiToolException(
					ErrorCodes.InternalError,
					$"The Android runtime-owned EventPipe trace could not be copied from '{context.RuntimeOwnedTraceDevicePath}'.",
					nativeError: string.IsNullOrWhiteSpace(stderr) ? $"adb exited with code {process.ExitCode}." : stderr);
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			try
			{
				if (!process.HasExited)
				{
					process.Kill(entireProcessTree: true);
					await process.WaitForExitAsync(CancellationToken.None);
				}
			}
			catch (InvalidOperationException)
			{
				// The process exited while cleanup was in progress.
			}

			if (File.Exists(context.OutputPath))
				File.Delete(context.OutputPath);

			throw;
		}
	}

	static async Task DeleteInternalTraceAsync(string adbPath, ProfileSessionContext context, string applicationId, CancellationToken cancellationToken)
	{
		_ = await ProcessRunner.RunAsync(
			adbPath,
			["-s", context.Device.Id, "shell", "run-as", applicationId, "rm", "-f", context.RuntimeOwnedTraceDevicePath!],
			timeout: ProfileCommand.s_adbPortForwardTimeout,
			cancellationToken: cancellationToken);
	}

	static async Task WaitForAppExitAsync(string adbPath, ProfileSessionContext context, string applicationId, CancellationToken cancellationToken)
	{
		var startedAt = Stopwatch.GetTimestamp();
		var sawRunning = false;
		string? lastFailure = null;

		while (Stopwatch.GetElapsedTime(startedAt) < s_traceWaitTimeout)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var result = await ProcessRunner.RunAsync(
				adbPath,
				["-s", context.Device.Id, "shell", "pidof", applicationId],
				timeout: ProfileCommand.s_adbPortForwardTimeout,
				cancellationToken: cancellationToken);

			if (result.Success && !string.IsNullOrWhiteSpace(result.StandardOutput))
			{
				sawRunning = true;
			}
			else if (ShouldTreatAppAsExited(sawRunning, result))
			{
				return;
			}
			else if (!result.Success)
			{
				lastFailure = ProfileCommandProcessHelpers.GetProcessFailureDetails(result);
			}

			await Task.Delay(s_pollDelay, cancellationToken);
		}

		throw new MauiToolException(
			ErrorCodes.InternalError,
			sawRunning
				? $"The Android app '{applicationId}' did not exit in time, so the runtime-owned startup trace could not be finalized."
				: $"The Android app '{applicationId}' was never observed running after launch, so the runtime-owned startup trace could not be finalized.",
			nativeError: lastFailure);
	}

	internal static bool ShouldTreatAppAsExited(bool sawRunning, ProcessResult result)
		=> sawRunning
			&& result.Success
			&& string.IsNullOrWhiteSpace(result.StandardOutput);

	static async Task<TraceProbeResult> ProbeInternalTraceAsync(string adbPath, ProfileSessionContext context, string applicationId, CancellationToken cancellationToken)
	{
		var result = await ProcessRunner.RunAsync(
			adbPath,
			["-s", context.Device.Id, "shell", "run-as", applicationId, "ls", "-ln", context.RuntimeOwnedTraceDevicePath!],
			timeout: ProfileCommand.s_adbPortForwardTimeout,
			cancellationToken: cancellationToken);

		if (result.StandardError.Contains("package not debuggable", StringComparison.OrdinalIgnoreCase))
		{
			throw new MauiToolException(
				ErrorCodes.InternalError,
				$"The Android profiling app '{applicationId}' was installed without debug access, so the runtime-owned trace could not be retrieved.",
				nativeError: result.StandardError);
		}

		return new TraceProbeResult(
			Size: result.Success ? TryParseLongListingSize(result.StandardOutput) : 0,
			FailureDetails: result.Success ? null : ProfileCommandProcessHelpers.GetProcessFailureDetails(result));
	}

	static async Task<TraceProbeResult> ProbeExternalTraceAsync(string adbPath, ProfileSessionContext context, CancellationToken cancellationToken)
	{
		var result = await ProcessRunner.RunAsync(
			adbPath,
			["-s", context.Device.Id, "shell", "ls", "-ln", context.RuntimeOwnedTraceDevicePath!],
			timeout: ProfileCommand.s_adbPortForwardTimeout,
			cancellationToken: cancellationToken);

		return new TraceProbeResult(
			Size: result.Success ? TryParseLongListingSize(result.StandardOutput) : 0,
			FailureDetails: result.Success ? null : ProfileCommandProcessHelpers.GetProcessFailureDetails(result));
	}

	readonly record struct TraceProbeResult(long Size, string? FailureDetails);
}
