// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using Microsoft.Maui.Cli.Errors;
using Microsoft.Maui.Cli.Output;
using Microsoft.Maui.Cli.Utils;

namespace Microsoft.Maui.Cli.Commands;

internal static class ProfileTraceLifecycle
{
	enum StopSignalReason
	{
		None,
		Manual,
		Timed,
		ExternalCompletion
	}

	static readonly Task s_neverCompletes = Task.Delay(Timeout.InfiniteTimeSpan, CancellationToken.None);
	static readonly TimeSpan s_consoleReadJoinTimeout = TimeSpan.FromMilliseconds(100);

	internal static async Task<bool> WaitForCompletionAsync(
		MonitoredProcess traceProcess,
		bool allowManualStop,
		IOutputFormatter formatter,
		bool useJson,
		bool verbose,
		CancellationToken cancellationToken)
	{
		ProfileCommandProcessHelpers.WriteVerbose(
			formatter,
			useJson,
			verbose,
			allowManualStop
				? $"Waiting for dotnet-trace (PID {traceProcess.Process.Id}) to exit or for a manual stop request."
				: $"Waiting for dotnet-trace (PID {traceProcess.Process.Id}) to complete in non-interactive mode.");

		var processWaitTask = traceProcess.WaitForExitAsync();
		var stopRequested = false;

		var stopReason = await WaitForStopSignalCoreAsync(
			externalCompletionTask: processWaitTask,
			duration: null,
			allowManualStop: allowManualStop,
			formatter,
			useJson,
			verbose,
			cancellationToken);

		if (stopReason == StopSignalReason.ExternalCompletion)
		{
			ProfileCommandProcessHelpers.WriteVerbose(
				formatter,
				useJson,
				verbose,
				"dotnet-trace exited before any manual stop request was needed.");
		}
		else if (stopReason is StopSignalReason.Manual or StopSignalReason.Timed)
		{
			if (!useJson)
				formatter.WriteInfo("Stopping trace and finalizing output...");
			stopRequested = true;
			await RequestStopAsync(traceProcess.Process, formatter, useJson, verbose);
		}

		if (stopRequested)
		{
			try
			{
				await processWaitTask.WaitAsync(ProfileCommand.s_traceStopTimeout);
			}
			catch (TimeoutException)
			{
				throw new MauiToolException(
					ErrorCodes.InternalError,
					$"dotnet-trace did not exit within {ProfileCommand.s_traceStopTimeout.TotalSeconds:0}s after the stop request.",
					nativeError: traceProcess.GetCombinedOutput());
			}
		}

		ProfileCommandProcessHelpers.WriteVerbose(formatter, useJson, verbose, $"dotnet-trace exited with code {traceProcess.Process.ExitCode}.");

		if (stopRequested && traceProcess.Process.ExitCode == 130)
		{
			ProfileCommandProcessHelpers.WriteVerbose(
				formatter,
				useJson,
				verbose,
				"dotnet-trace exited with SIGINT after the stop request; treating the canceled collector exit as a successful finalized trace.");
			return stopRequested;
		}

		if (traceProcess.Process.ExitCode != 0)
		{
			throw new MauiToolException(
				ErrorCodes.InternalError,
				$"dotnet-trace exited with code {traceProcess.Process.ExitCode}.",
				nativeError: traceProcess.GetCombinedOutput());
		}

		return stopRequested;
	}

	internal static async Task WaitForStopSignalAsync(
		TimeSpan? duration,
		bool allowManualStop,
		IOutputFormatter formatter,
		bool useJson,
		bool verbose,
		CancellationToken cancellationToken,
		string? completionMessage = "Stopping trace and finalizing output...")
	{
		var stopReason = await WaitForStopSignalCoreAsync(
			externalCompletionTask: null,
			duration,
			allowManualStop,
			formatter,
			useJson,
			verbose,
			cancellationToken);

		if (stopReason is StopSignalReason.Manual or StopSignalReason.Timed
			&& !useJson
			&& !string.IsNullOrEmpty(completionMessage))
			formatter.WriteInfo(completionMessage);
	}

	// Stdin-only wait used by the manual-start attach prompt. Unlike WaitForStopSignalAsync,
	// this does NOT swallow Ctrl+C: cancellation propagates up via cancellationToken so the
	// command terminates cleanly. The orphaned-ReadLine race in WaitForStopSignalCoreAsync's
	// CancelKeyPress path therefore can't affect a subsequent stop-phase reader.
	internal static async Task WaitForStdinNewlineOrEofAsync(CancellationToken cancellationToken)
	{
		var newlineSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var readLineTask = Task.Run(() =>
		{
			try
			{
				Console.ReadLine();
			}
			finally
			{
				newlineSignal.TrySetResult(true);
			}
		}, CancellationToken.None);

		try
		{
			await newlineSignal.Task.WaitAsync(cancellationToken);
		}
		finally
		{
			try
			{
				await readLineTask.WaitAsync(s_consoleReadJoinTimeout);
			}
			catch (TimeoutException)
			{
				// Console.ReadLine() can outlive the wait; ignore.
			}
		}

		// Drain any extra buffered keystrokes (e.g. an accidental fast double-Enter)
		// so the next stdin reader doesn't immediately consume them as a stop signal.
		if (!Console.IsInputRedirected)
		{
			try
			{
				while (Console.KeyAvailable)
					Console.ReadKey(intercept: true);
			}
			catch (InvalidOperationException)
			{
				// Console.KeyAvailable throws when stdin is redirected to a file; ignore.
			}
		}
	}

	internal static bool ShouldRequestManualStop(Task completedTask, Task processWaitTask, bool processHasExited)
		=> completedTask != processWaitTask && !processHasExited;

	static async Task<StopSignalReason> WaitForStopSignalCoreAsync(
		Task? externalCompletionTask,
		TimeSpan? duration,
		bool allowManualStop,
		IOutputFormatter formatter,
		bool useJson,
		bool verbose,
		CancellationToken cancellationToken)
	{
		var completionTask = externalCompletionTask ?? s_neverCompletes;

		if (!allowManualStop)
		{
			if (duration is { } nonInteractiveDuration)
			{
				ProfileCommandProcessHelpers.WriteVerbose(
					formatter,
					useJson,
					verbose,
					$"Waiting {nonInteractiveDuration} before requesting app exit for runtime-owned trace finalization.");
				var completedTask = await Task.WhenAny(completionTask, Task.Delay(nonInteractiveDuration, cancellationToken));
				return completedTask == completionTask ? StopSignalReason.ExternalCompletion : StopSignalReason.Timed;
			}

			if (externalCompletionTask is null)
				return StopSignalReason.None;

			await completionTask;
			return StopSignalReason.ExternalCompletion;
		}

		var manualStopSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		ConsoleCancelEventHandler? cancelHandler = null;
		cancelHandler = (_, e) =>
		{
			e.Cancel = true;
			manualStopSignal.TrySetResult(true);
		};

		Console.CancelKeyPress += cancelHandler;
		var readLineTask = Task.Run(() =>
		{
			try
			{
				Console.ReadLine();
			}
			finally
			{
				manualStopSignal.TrySetResult(true);
			}
		}, CancellationToken.None);

		try
		{
			var durationTask = duration is { } waitDuration
				? Task.Delay(waitDuration, cancellationToken)
				: s_neverCompletes;
			var completedTask = await Task.WhenAny(completionTask, durationTask, manualStopSignal.Task);

			if (externalCompletionTask is not null && completedTask == completionTask)
				return StopSignalReason.ExternalCompletion;

			if (duration is not null && completedTask == durationTask)
			{
				ProfileCommandProcessHelpers.WriteVerbose(
					formatter,
					useJson,
					verbose,
					$"Trace duration {duration} elapsed.");
				return StopSignalReason.Timed;
			}

			ProfileCommandProcessHelpers.WriteVerbose(formatter, useJson, verbose, "Manual stop requested from the console.");
			return StopSignalReason.Manual;
		}
		finally
		{
			Console.CancelKeyPress -= cancelHandler;
			manualStopSignal.TrySetResult(true);
			try
			{
				await readLineTask.WaitAsync(s_consoleReadJoinTimeout);
			}
			catch (TimeoutException)
			{
				// Console.ReadLine() can outlive the profiling stop path; ignore that case.
			}
		}
	}

	internal static async Task RequestStopAsync(Process traceProcess, IOutputFormatter formatter, bool useJson, bool verbose)
	{
		if (traceProcess.HasExited)
		{
			ProfileCommandProcessHelpers.WriteVerbose(formatter, useJson, verbose, "dotnet-trace had already exited before the stop request was sent.");
			return;
		}

		ProfileCommandProcessHelpers.WriteVerbose(formatter, useJson, verbose, $"Sending a stop newline to dotnet-trace stdin (PID {traceProcess.Id}).");
		try
		{
			await traceProcess.StandardInput.WriteLineAsync();
			await traceProcess.StandardInput.FlushAsync();
		}
		catch (ObjectDisposedException)
		{
			ProfileCommandProcessHelpers.WriteVerbose(formatter, useJson, verbose, "dotnet-trace stdin was already closed before the stop request.");
		}

		try
		{
			traceProcess.StandardInput.Close();
		}
		catch (ObjectDisposedException)
		{
			// Already closed.
		}

		ProfileCommandProcessHelpers.WriteVerbose(formatter, useJson, verbose, "Closed dotnet-trace stdin after the stop request.");

		await Task.Delay(ProfileCommand.s_traceStopInterruptDelay);
		if (!traceProcess.HasExited)
		{
			ProfileCommandProcessHelpers.WriteVerbose(
				formatter,
				useJson,
				verbose,
				$"dotnet-trace was still running {ProfileCommand.s_traceStopInterruptDelay.TotalSeconds:0.#}s after the stdin stop request; sending SIGINT to the process tree.");
			await SendInterruptToProcessTreeAsync(traceProcess, formatter, useJson, verbose);
		}
	}

	internal static async Task StopBackgroundProcessAsync(Process? process, string processName, IOutputFormatter formatter, bool useJson, bool verbose)
	{
		if (process is null || process.HasExited)
			return;

		ProfileCommandProcessHelpers.WriteVerbose(formatter, useJson, verbose, $"Stopping {processName} (PID {process.Id}).");
		try
		{
			process.Kill(entireProcessTree: true);
			await process.WaitForExitAsync();
			ProfileCommandProcessHelpers.WriteVerbose(formatter, useJson, verbose, $"{processName} exited with code {process.ExitCode} during cleanup.");
		}
		catch (InvalidOperationException)
		{
			// The process already exited between the check and the kill call.
		}
	}

	static async Task SendInterruptToProcessTreeAsync(Process rootProcess, IOutputFormatter formatter, bool useJson, bool verbose)
	{
		if (rootProcess.HasExited)
			return;

		if (OperatingSystem.IsWindows())
		{
			ProfileCommandProcessHelpers.WriteVerbose(formatter, useJson, verbose, "Skipping SIGINT fallback on Windows.");
			return;
		}

		var pids = await GetDescendantProcessIdsAsync(rootProcess.Id);
		pids.Add(rootProcess.Id);

		foreach (var pid in pids.Distinct().OrderByDescending(pid => pid))
		{
			ProfileCommandProcessHelpers.WriteVerbose(formatter, useJson, verbose, $"Sending SIGINT to PID {pid}.");
			_ = await ProcessRunner.RunAsync(
				"kill",
				["-INT", pid.ToString()],
				timeout: TimeSpan.FromSeconds(5),
				cancellationToken: CancellationToken.None);
		}
	}

	static async Task<List<int>> GetDescendantProcessIdsAsync(int rootPid)
	{
		if (OperatingSystem.IsWindows())
			return [];

		var result = await ProcessRunner.RunAsync(
			"ps",
			["-eo", "pid=,ppid="],
			timeout: TimeSpan.FromSeconds(5),
			cancellationToken: CancellationToken.None);

		if (!result.Success)
			return [];

		var childrenByParent = new Dictionary<int, List<int>>();
		var lines = result.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		foreach (var line in lines)
		{
			var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
			if (parts.Length != 2
				|| !int.TryParse(parts[0], out var pid)
				|| !int.TryParse(parts[1], out var parentPid))
			{
				continue;
			}

			if (!childrenByParent.TryGetValue(parentPid, out var children))
			{
				children = [];
				childrenByParent[parentPid] = children;
			}

			children.Add(pid);
		}

		var descendants = new List<int>();
		var queue = new Queue<int>();
		queue.Enqueue(rootPid);

		while (queue.Count > 0)
		{
			var parent = queue.Dequeue();
			if (!childrenByParent.TryGetValue(parent, out var children))
				continue;

			foreach (var child in children)
			{
				descendants.Add(child);
				queue.Enqueue(child);
			}
		}

		return descendants;
	}
}
