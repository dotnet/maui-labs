// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using Microsoft.Maui.Cli.Errors;
using Microsoft.Maui.Cli.Models;
using Microsoft.Maui.Cli.Output;
using Microsoft.Maui.Cli.Utils;

namespace Microsoft.Maui.Cli.Commands;

public static partial class ProfileCommand
{
	static async Task<MauiProfileResult> RunProfileAsync(
		ResolvedMauiProject project,
		string framework,
		Device device,
		string outputPath,
		TraceOutputFormat outputFormat,
		string configuration,
		string? traceProfile,
		bool noBuild,
		int diagnosticPort,
		TimeSpan? duration,
		string? stoppingEventProvider,
		string? stoppingEventName,
		string? stoppingEventPayloadFilter,
		bool autoSelectedStoppingEvent,
		IOutputFormatter formatter,
		bool useJson,
		bool verbose,
		CancellationToken cancellationToken)
	{
		var primaryOutputPath = GetPrimaryOutputPath(outputPath, outputFormat);
		var outputDirectory = Path.GetDirectoryName(outputPath);
		if (string.IsNullOrWhiteSpace(outputDirectory))
		{
			throw new MauiToolException(
				ErrorCodes.InvalidArgument,
				$"Could not determine the output directory for '{outputPath}'.");
		}

		Directory.CreateDirectory(outputDirectory);

		var profilePlatform = InferPlatformFromTargetFramework(framework) ?? device.Platform;
		var transport = ResolveProfileTransport(profilePlatform, device);
		var dsrouterKind = transport.DsrouterKind;
		var hasStartupProfilingHelper = MauiProjectResolver.HasPackageReference(project.ProjectPath, StartupProfilingPackageId);
		var requestedDiagnosticPort = diagnosticPort;
		var diagnosticAddress = transport.DiagnosticAddress;
		var startedAtUtc = DateTimeOffset.UtcNow;
		var effectiveDuration = duration;

		if (!useJson)
		{
			formatter.WriteInfo($"Project: {project.ProjectPath}");
			formatter.WriteInfo($"Framework: {framework}");
			formatter.WriteInfo($"Configuration: {configuration}");
			formatter.WriteInfo($"Device: {device.Name} ({device.Id})");
			formatter.WriteInfo($"Format: {FormatOutputFormat(outputFormat)}");
			formatter.WriteInfo($"Output: {primaryOutputPath}");
			if (!string.Equals(primaryOutputPath, outputPath, StringComparison.OrdinalIgnoreCase))
				formatter.WriteInfo($"Raw trace companion: {outputPath}");
			if (autoSelectedStoppingEvent)
			{
				formatter.WriteInfo(
					$"Stopping event: {StartupProfilingProviderName}/{StartupProfilingEventName} " +
					"(auto-detected from the app's startup profiling helper).");
			}
		}

		WriteVerbose(
			formatter,
			useJson,
			verbose,
			$"Profile settings: configuration={configuration}, noBuild={noBuild}, dsrouterKind={dsrouterKind}, " +
			$"diagnosticAddress={diagnosticAddress}, diagnosticListenMode={transport.DiagnosticListenMode}, diagnosticPort={diagnosticPort}, " +
			$"traceProfile={traceProfile ?? "(default)"}, outputFormat={FormatOutputFormat(outputFormat)}, duration={duration?.ToString() ?? "(manual stop)"}, " +
			$"stoppingEventProvider={stoppingEventProvider ?? "(none)"}, stoppingEventName={stoppingEventName ?? "(none)"}, " +
			$"stoppingEventPayloadFilter={stoppingEventPayloadFilter ?? "(none)"}");

		MonitoredProcess? dsrouterProcess = null;
		MonitoredProcess? traceProcess = null;
		ReservedProfilePorts? reservedPorts = null;
		ExitControlServer? exitControlServer = null;
		ProfilingBuildInjection? buildInjection = null;
		try
		{
			reservedPorts = await ReserveProfilePortsAndConfigureRoutingAsync(
				device,
				transport,
				diagnosticPort,
				formatter,
				useJson,
				verbose,
				cancellationToken);

			diagnosticPort = reservedPorts.DiagnosticPort;
			buildInjection = string.Equals(profilePlatform, Platforms.iOS, StringComparison.OrdinalIgnoreCase)
				? null
				: TryCreateBuildInjection(
					diagnosticAddress,
					reservedPorts.ExitControlPort,
					injectBootstrap: !hasStartupProfilingHelper);

			if (!useJson)
			{
				formatter.WriteInfo($"Diagnostic port: {diagnosticPort}");
				if (diagnosticPort != requestedDiagnosticPort)
					formatter.WriteInfo($"Port {requestedDiagnosticPort} was busy, so the profiler selected {diagnosticPort}.");

				if (buildInjection is null)
				{
					formatter.WriteWarning(
						"The CLI's startup profiling injection assets were not found next to the tool binaries, so automatic startup-complete and graceful app-exit injection are unavailable for this run.");
				}
			}

			if (!noBuild)
			{
				if (!useJson && formatter is not SpectreOutputFormatter)
					formatter.WriteInfo("Building the app...");

				var buildArgs = BuildCompileArguments(project.ProjectPath, framework, configuration, transport, diagnosticPort, buildInjection);
				WriteVerbose(formatter, useJson, verbose, $"Build command: {FormatCommandLine("dotnet", buildArgs)}");
				var buildResult = formatter is SpectreOutputFormatter spectreForBuild && !useJson
					? await spectreForBuild.StatusAsync(
						"Building the app...",
						() => ProcessRunner.RunAsync("dotnet", buildArgs, project.ProjectDirectory, timeout: s_buildLaunchTimeout, environmentVariablesToRemove: s_msbuildSdkEnvVars, cancellationToken: cancellationToken))
					: await ProcessRunner.RunAsync("dotnet", buildArgs, project.ProjectDirectory, timeout: s_buildLaunchTimeout, environmentVariablesToRemove: s_msbuildSdkEnvVars, cancellationToken: cancellationToken);

				if (!buildResult.Success)
					throw CreateProcessFailureException("dotnet build", buildResult);
			}
			else
			{
				WriteVerbose(formatter, useJson, verbose, "Skipping build because --no-build was specified.");
			}

			await TryForceStopRunningAndroidAppAsync(project, framework, configuration, device, formatter, useJson, verbose, cancellationToken);

			exitControlServer = ExitControlServer.Attach(reservedPorts.ExitControlReservation, formatter, useJson, verbose);
			reservedPorts.DiagnosticReservation.Dispose();

			WriteVerbose(formatter, useJson, verbose, $"Starting dotnet-dsrouter in '{dsrouterKind}' mode on port {diagnosticPort}.");
			var dsrouterStart = await StartDsrouterAsync(transport, diagnosticPort, formatter, useJson, verbose, cancellationToken);
			dsrouterProcess = dsrouterStart.DnxProcess;
			var dsrouterPid = dsrouterStart.DsrouterPid;
			WriteVerbose(formatter, useJson, verbose, $"dotnet-dsrouter reported PID {dsrouterPid}.");
			await EnsureDsrouterStartedAsync(dsrouterProcess, diagnosticPort, cancellationToken);
			var startTraceAfterLaunch = string.Equals(transport.Platform, Platforms.iOS, StringComparison.OrdinalIgnoreCase);
			if (!startTraceAfterLaunch)
			{
				traceProcess = StartTraceCollector(
					project.ProjectDirectory,
					outputPath,
					outputFormat,
					dsrouterPid,
					device.Id,
					traceProfile,
					effectiveDuration,
					stoppingEventProvider,
					stoppingEventName,
					stoppingEventPayloadFilter,
					formatter,
					useJson,
					verbose,
					cancellationToken);

				WriteVerbose(formatter, useJson, verbose, $"Waiting briefly for dotnet-trace (PID {traceProcess.Process.Id}) to connect.");
				await EnsureTraceCollectorStartedAsync(traceProcess, cancellationToken);
			}

			var launchArgs = BuildLaunchArguments(
				project.ProjectPath,
				framework,
				configuration,
				device,
				transport,
				diagnosticPort,
				buildInjection);
			WriteVerbose(formatter, useJson, verbose, $"Launch command: {FormatCommandLine("dotnet", launchArgs)}");

			if (!useJson && formatter is not SpectreOutputFormatter)
				formatter.WriteInfo("Deploying and launching the app with startup diagnostics enabled...");

			var launchResult = formatter is SpectreOutputFormatter spectre && !useJson
				? await spectre.StatusAsync(
					"Deploying and launching the app...",
					() => ProcessRunner.RunAsync("dotnet", launchArgs, project.ProjectDirectory, timeout: s_buildLaunchTimeout, environmentVariablesToRemove: s_msbuildSdkEnvVars, cancellationToken: cancellationToken))
				: await ProcessRunner.RunAsync("dotnet", launchArgs, project.ProjectDirectory, timeout: s_buildLaunchTimeout, environmentVariablesToRemove: s_msbuildSdkEnvVars, cancellationToken: cancellationToken);

			if (!launchResult.Success)
			{
				if (traceProcess is not null)
				{
					await RequestTraceStopAsync(traceProcess.Process, formatter, useJson, verbose);
					await traceProcess.WaitForExitAsync();
				}

				throw CreateProcessFailureException("dotnet build -t:Run", launchResult);
			}

			if (startTraceAfterLaunch)
			{
				traceProcess = await StartTraceCollectorWithRetryAsync(
					project.ProjectDirectory,
					outputPath,
					outputFormat,
					dsrouterPid,
					device.Id,
					traceProfile,
					effectiveDuration,
					stoppingEventProvider,
					stoppingEventName,
					stoppingEventPayloadFilter,
					formatter,
					useJson,
					verbose,
					cancellationToken);
			}

			if (!useJson)
			{
				if (traceProcess is not null && traceProcess.Process.HasExited)
				{
					formatter.WriteWarning(
						"Trace collection completed during app launch before a manual stop request. " +
						"This usually means the target process disconnected and the trace finalized early.");
				}
				else
				{
					var traceStatusMessage = !string.IsNullOrWhiteSpace(stoppingEventProvider)
						? "Waiting for the configured stopping event. Press Enter to stop early."
						: effectiveDuration is { } explicitDuration
							? $"Startup trace is running. It will stop automatically after {FormatDuration(explicitDuration)} unless you press Enter sooner."
							: "Startup trace is running. Press Enter to stop and finalize the trace output.";
					formatter.WriteInfo(traceStatusMessage);
				}
			}

			if (traceProcess is not null)
			{
				await WaitForTraceCompletionAsync(
					traceProcess,
					allowManualStop: !useJson,
					formatter,
					useJson,
					verbose,
					cancellationToken);
			}

			if (exitControlServer is not null)
			{
				var appExitRequested = await exitControlServer.TryRequestExitAsync(s_exitControlConnectTimeout, s_exitControlCommandTimeout, cancellationToken);
				if (!appExitRequested && !useJson)
				{
					formatter.WriteWarning(
						"The app did not connect to the startup profiling exit channel, so it may remain running and not flush PGO data. " +
						"Ensure it references Microsoft.Maui.StartupProfiling and loads that assembly during startup.");
				}
			}
		}
		finally
		{
			reservedPorts?.Dispose();
			exitControlServer?.Dispose();

			if (traceProcess is not null)
			{
				await StopBackgroundProcessAsync(traceProcess.Process, "dotnet-trace", formatter, useJson, verbose);
				traceProcess.Dispose();
			}

			if (dsrouterProcess is not null)
			{
				await StopBackgroundProcessAsync(dsrouterProcess.Process, "dotnet-dsrouter", formatter, useJson, verbose);
				dsrouterProcess.Dispose();
			}

			if (transport.RequiresManualExitControlPortRouting)
			{
				if (reservedPorts is not null)
					await RemoveAdbPortRoutingAsync(device, formatter, useJson, verbose, reservedPorts.ExitControlPort);
				else
					await RemoveAdbPortRoutingAsync(device, formatter, useJson, verbose, GetExitControlPort(diagnosticPort));
			}
		}

		if (!File.Exists(primaryOutputPath))
		{
			throw new MauiToolException(
				ErrorCodes.InternalError,
				$"Trace collection completed, but '{primaryOutputPath}' was not created.");
		}

		ValidateTraceOutput(primaryOutputPath, outputPath, outputFormat, transport.Platform);

		return new MauiProfileResult
		{
			ProjectPath = project.ProjectPath,
			ProjectName = project.ProjectName,
			Framework = framework,
			Platform = transport.Platform,
			DeviceId = device.Id,
			DeviceName = device.Name,
			Configuration = configuration,
			Format = FormatOutputFormat(outputFormat),
			OutputPath = primaryOutputPath,
			RawTracePath = outputFormat == TraceOutputFormat.Speedscope ? outputPath : null,
			DsrouterKind = dsrouterKind,
			DiagnosticAddress = diagnosticAddress,
			DiagnosticPort = diagnosticPort,
			UsedStoppingEvent = !string.IsNullOrWhiteSpace(stoppingEventProvider),
			StartedAtUtc = startedAtUtc,
			CompletedAtUtc = DateTimeOffset.UtcNow
		};
	}

	static string FormatDuration(TimeSpan duration)
	{
		var positiveDuration = duration < TimeSpan.Zero ? duration.Negate() : duration;
		return $"{(int)positiveDuration.TotalDays:00}:{positiveDuration.Hours:00}:{positiveDuration.Minutes:00}:{positiveDuration.Seconds:00}";
	}

	internal static void ValidateTraceOutput(string primaryOutputPath, string collectorOutputPath, TraceOutputFormat outputFormat, string platform)
	{
		var primaryFile = new FileInfo(primaryOutputPath);
		if (primaryFile.Length > 0)
			return;

		if (outputFormat == TraceOutputFormat.Speedscope &&
			!string.Equals(primaryOutputPath, collectorOutputPath, StringComparison.OrdinalIgnoreCase) &&
			File.Exists(collectorOutputPath) &&
			new FileInfo(collectorOutputPath).Length > 0)
		{
			throw MauiToolException.UserActionRequired(
				ErrorCodes.InternalError,
				$"Trace collection produced a raw .nettrace at '{collectorOutputPath}', but the converted output '{primaryOutputPath}' is empty.",
				[
					"Rerun with `--format nettrace` to keep the raw trace without conversion.",
					"Or rerun with `--verbose` to inspect any dotnet-trace conversion errors."
				]);
		}

		var suggestions = string.Equals(platform, Platforms.iOS, StringComparison.OrdinalIgnoreCase)
			? new[]
			{
				"Rerun with `--verbose` to capture the full dotnet-trace and dotnet-dsrouter output.",
				"Ensure the global `dotnet-trace` and `dotnet-dsrouter` tools are installed and up to date so the CLI can use the supported diagnostics toolchain.",
				"If Release iOS simulator tracing still produces an empty artifact, treat it as a Mono/EventPipe diagnostics issue to investigate rather than switching to Debug."
			}
			: new[]
			{
				"Rerun with `--verbose` to inspect the full dotnet-trace output.",
				"If the app exited immediately, retry with a longer `--duration` or stop the trace manually after the app finishes loading."
			};

		throw MauiToolException.UserActionRequired(
			ErrorCodes.InternalError,
			$"Trace collection completed, but '{primaryOutputPath}' is empty.",
			suggestions);
	}

	static async Task WaitForTraceCompletionAsync(
		MonitoredProcess traceProcess,
		bool allowManualStop,
		IOutputFormatter formatter,
		bool useJson,
		bool verbose,
		CancellationToken cancellationToken)
	{
		WriteVerbose(
			formatter,
			useJson,
			verbose,
			allowManualStop
				? $"Waiting for dotnet-trace (PID {traceProcess.Process.Id}) to exit or for a manual stop request."
				: $"Waiting for dotnet-trace (PID {traceProcess.Process.Id}) to complete in non-interactive mode.");

		var processWaitTask = traceProcess.WaitForExitAsync();
		var stopRequested = false;
		if (!allowManualStop)
		{
			await processWaitTask;
		}
		else
		{
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
				while (true)
				{
					var completedTask = await Task.WhenAny(processWaitTask, manualStopSignal.Task);
					if (completedTask == processWaitTask)
					{
						WriteVerbose(formatter, useJson, verbose, "dotnet-trace exited before any manual stop request was needed.");
						break;
					}

					if (completedTask == manualStopSignal.Task && !traceProcess.Process.HasExited)
					{
						formatter.WriteInfo("Stopping trace and finalizing output...");
						WriteVerbose(formatter, useJson, verbose, "Manual stop requested from the console.");
						stopRequested = true;
						await RequestTraceStopAsync(traceProcess.Process, formatter, useJson, verbose);
						break;
					}
				}
			}
			finally
			{
				Console.CancelKeyPress -= cancelHandler;
				manualStopSignal.TrySetResult(true);
				try
				{
					await readLineTask.WaitAsync(TimeSpan.FromMilliseconds(100));
				}
				catch
				{
					// Ignore any late console-read completions.
				}
			}
		}

		if (stopRequested)
		{
			try
			{
				await processWaitTask.WaitAsync(s_traceStopTimeout);
			}
			catch (TimeoutException)
			{
				throw new MauiToolException(
					ErrorCodes.InternalError,
					$"dotnet-trace did not exit within {s_traceStopTimeout.TotalSeconds:0}s after the stop request.",
					nativeError: traceProcess.GetCombinedOutput());
			}
		}

		WriteVerbose(formatter, useJson, verbose, $"dotnet-trace exited with code {traceProcess.Process.ExitCode}.");

		if (stopRequested && traceProcess.Process.ExitCode == 130)
		{
			WriteVerbose(
				formatter,
				useJson,
				verbose,
				"dotnet-trace exited with SIGINT after the stop request; treating the canceled collector exit as a successful finalized trace.");
			return;
		}

		if (traceProcess.Process.ExitCode != 0)
		{
			throw new MauiToolException(
				ErrorCodes.InternalError,
				$"dotnet-trace exited with code {traceProcess.Process.ExitCode}.",
				nativeError: traceProcess.GetCombinedOutput());
		}
	}

	static async Task RequestTraceStopAsync(Process traceProcess, IOutputFormatter formatter, bool useJson, bool verbose)
	{
		if (traceProcess.HasExited)
		{
			WriteVerbose(formatter, useJson, verbose, "dotnet-trace had already exited before the stop request was sent.");
			return;
		}

		WriteVerbose(formatter, useJson, verbose, $"Sending a stop newline to dotnet-trace stdin (PID {traceProcess.Id}).");
		try
		{
			await traceProcess.StandardInput.WriteLineAsync();
			await traceProcess.StandardInput.FlushAsync();
		}
		catch (ObjectDisposedException)
		{
			WriteVerbose(formatter, useJson, verbose, "dotnet-trace stdin was already closed before the stop request.");
		}

		try
		{
			traceProcess.StandardInput.Close();
		}
		catch (ObjectDisposedException)
		{
			// Already closed.
		}

		WriteVerbose(formatter, useJson, verbose, "Closed dotnet-trace stdin after the stop request.");

		await Task.Delay(s_traceStopInterruptDelay);
		if (!traceProcess.HasExited)
		{
			WriteVerbose(
				formatter,
				useJson,
				verbose,
				$"dotnet-trace was still running {s_traceStopInterruptDelay.TotalSeconds:0.#}s after the stdin stop request; sending SIGINT to the process tree.");
			await SendInterruptToProcessTreeAsync(traceProcess, formatter, useJson, verbose);
		}
	}

	static async Task SendInterruptToProcessTreeAsync(Process rootProcess, IOutputFormatter formatter, bool useJson, bool verbose)
	{
		if (rootProcess.HasExited)
			return;

		if (OperatingSystem.IsWindows())
		{
			WriteVerbose(formatter, useJson, verbose, "Skipping SIGINT fallback on Windows.");
			return;
		}

		var pids = await GetDescendantProcessIdsAsync(rootProcess.Id);
		pids.Add(rootProcess.Id);

		foreach (var pid in pids.Distinct().OrderByDescending(pid => pid))
		{
			WriteVerbose(formatter, useJson, verbose, $"Sending SIGINT to PID {pid}.");
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

	static async Task StopBackgroundProcessAsync(Process? process, string processName, IOutputFormatter formatter, bool useJson, bool verbose)
	{
		if (process is null || process.HasExited)
			return;

		WriteVerbose(formatter, useJson, verbose, $"Stopping {processName} (PID {process.Id}).");
		try
		{
			process.Kill(entireProcessTree: true);
			await process.WaitForExitAsync();
			WriteVerbose(formatter, useJson, verbose, $"{processName} exited with code {process.ExitCode} during cleanup.");
		}
		catch (InvalidOperationException)
		{
			// The process already exited between the check and the kill call.
		}
	}
}
