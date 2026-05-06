// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui.Cli.Output;
using Microsoft.Maui.Cli.Utils;
using Spectre.Console;

namespace Microsoft.Maui.Cli.Commands;

internal static class ProfileSessionLaunch
{
	internal static async Task StartAsync(ProfileSessionContext context, CancellationToken cancellationToken)
	{
		await BuildIfNeededAsync(context, cancellationToken);

		await ProfileCommandPortRouter.TryForceStopRunningAndroidAppAsync(
			context.Project,
			context.Framework,
			context.Configuration,
			context.Device,
			context.Formatter,
			context.UseJson,
			context.Verbose,
			cancellationToken);

		if (context.UseRuntimeOwnedTraceCollection)
		{
			context.ExitControlServer = ExitControlServer.Attach(context.ReservedPorts!.ExitControlReservation, context.Formatter, context.UseJson, context.Verbose);
			context.ReservedPorts.DiagnosticReservation.Dispose();

			await RuntimeOwnedTraceCollector.PrepareAsync(context, cancellationToken);
			await LaunchAppAsync(context, cancellationToken);
			WriteTraceStatusMessage(context);

			await ProfileTraceLifecycle.WaitForStopSignalAsync(
				context.EffectiveDuration,
				allowManualStop: !context.UseJson,
				context.Formatter,
				context.UseJson,
				context.Verbose,
				cancellationToken);
			await ProfileSessionRunner.TryRequestAppExitAsync(context, cancellationToken);

			await RuntimeOwnedTraceCollector.WaitForAndPullAsync(context, cancellationToken);
			WriteTraceStatusMessage(context);
			return;
		}

		context.ExitControlServer = ExitControlServer.Attach(context.ReservedPorts!.ExitControlReservation, context.Formatter, context.UseJson, context.Verbose);
		context.ReservedPorts.DiagnosticReservation.Dispose();

		if (!context.StartTraceAfterLaunch)
		{
			ProfileCommandProcessHelpers.WriteVerbose(
				context.Formatter,
				context.UseJson,
				context.Verbose,
				$"Starting dotnet-trace with built-in dsrouter mode '{context.DsrouterKind}' on port {context.DiagnosticPort}.");
			context.TraceProcess = DotnetTraceRunner.StartCollector(
				context.Project.ProjectDirectory,
				context.OutputPath,
				context.OutputFormat,
				context.Transport,
				context.Device,
				context.TraceProfile,
				context.EffectiveDuration,
				context.StoppingEventProvider,
				context.StoppingEventName,
				context.StoppingEventPayloadFilter,
				context.Formatter,
				context.UseJson,
				context.Verbose,
				cancellationToken);

			ProfileCommandProcessHelpers.WriteVerbose(
				context.Formatter,
				context.UseJson,
				context.Verbose,
				$"Waiting briefly for dotnet-trace (PID {context.TraceProcess.Process.Id}) to initialize.");
			await DotnetTraceRunner.EnsureStartedAsync(context.TraceProcess, cancellationToken);
		}

		await LaunchAppAsync(context, cancellationToken);

		if (context.StartTraceAfterLaunch)
		{
			if (context.ManualStart)
				await WaitForManualStartSignalAsync(context, cancellationToken);

			ProfileCommandProcessHelpers.WriteVerbose(
				context.Formatter,
				context.UseJson,
				context.Verbose,
				$"Starting dotnet-trace with built-in dsrouter mode '{context.DsrouterKind}' on port {context.DiagnosticPort} after the {(context.ManualStart ? "non-suspended" : "suspended")} app launch.");
			context.TraceProcess = await DotnetTraceRunner.StartWithRetryAsync(
				context.Project.ProjectDirectory,
				context.OutputPath,
				context.OutputFormat,
				context.Transport,
				context.Device,
				context.TraceProfile,
				context.EffectiveDuration,
				context.StoppingEventProvider,
				context.StoppingEventName,
				context.StoppingEventPayloadFilter,
				context.Formatter,
				context.UseJson,
				context.Verbose,
				cancellationToken);
		}

		WriteTraceStatusMessage(context);
	}

	static async Task WaitForManualStartSignalAsync(ProfileSessionContext context, CancellationToken cancellationToken)
	{
		// Both interactive and non-interactive callers wait for an Enter / newline on stdin
		// before attaching dotnet-trace. Scripted callers can pipe a newline at the right
		// moment (after navigating the app, finishing setup, etc.). Stdin EOF also unblocks
		// so a closed pipe attaches gracefully instead of hanging forever.
		var nonInteractive = context.UseJson || Console.IsInputRedirected;
		if (nonInteractive)
		{
			ProfileCommandProcessHelpers.WriteVerbose(
				context.Formatter,
				context.UseJson,
				context.Verbose,
				"Manual profiling: waiting for a newline on stdin (or stdin close) to attach dotnet-trace.");
		}
		else
		{
			context.Formatter.WriteInfo("App is running. Press Enter to attach dotnet-trace and start profiling.");
		}

		await ProfileTraceLifecycle.WaitForStdinNewlineOrEofAsync(cancellationToken);
	}

	static async Task BuildIfNeededAsync(ProfileSessionContext context, CancellationToken cancellationToken)
	{
		if (context.NoBuild)
		{
			ProfileCommandProcessHelpers.WriteVerbose(context.Formatter, context.UseJson, context.Verbose, "Skipping build because --no-build was specified.");
			return;
		}

		if (!context.UseJson && context.Formatter is not SpectreOutputFormatter)
			context.Formatter.WriteInfo("Building the app...");

		var buildArgs = ProfileCommandArguments.BuildCompileArguments(
			context.Project.ProjectPath,
			context.Framework,
			context.Configuration,
			context.Transport,
			context.DiagnosticPort,
			context.BuildInjection,
			context.DiagnosticSuspend);
		ProfileCommandProcessHelpers.WriteVerbose(context.Formatter, context.UseJson, context.Verbose, $"Build command: {ProfileCommandProcessHelpers.FormatCommandLine("dotnet", buildArgs)}");
		var buildResult = await RunDotnetCommandAsync(context, "Building the app...", buildArgs, cancellationToken);

		if (!buildResult.Success)
			throw ProfileCommandProcessHelpers.CreateProcessFailureException("dotnet build", buildResult);
	}

	static async Task LaunchAppAsync(ProfileSessionContext context, CancellationToken cancellationToken)
	{
		var launchArgs = ProfileCommandArguments.BuildLaunchArguments(
			context.Project.ProjectPath,
			context.Framework,
			context.Configuration,
			context.Device,
			context.Transport,
			context.DiagnosticPort,
			context.BuildInjection,
			context.DiagnosticSuspend);
		ProfileCommandProcessHelpers.WriteVerbose(context.Formatter, context.UseJson, context.Verbose, $"Launch command: {ProfileCommandProcessHelpers.FormatCommandLine("dotnet", launchArgs)}");

		if (!context.UseJson && context.Formatter is not SpectreOutputFormatter)
			context.Formatter.WriteInfo("Deploying and launching the app with startup diagnostics enabled...");

		var launchResult = await RunDotnetCommandAsync(context, "Deploying and launching the app...", launchArgs, cancellationToken);

		if (launchResult.Success)
			return;

		if (context.TraceProcess is not null)
		{
			await ProfileTraceLifecycle.RequestStopAsync(context.TraceProcess.Process, context.Formatter, context.UseJson, context.Verbose);
			await context.TraceProcess.WaitForExitAsync();
		}

		throw ProfileCommandProcessHelpers.CreateProcessFailureException("dotnet build -t:Run", launchResult);
	}

	static void WriteTraceStatusMessage(ProfileSessionContext context)
	{
		if (context.UseJson)
			return;

		if (context.TraceProcess is not null && context.TraceProcess.Process.HasExited)
		{
			context.Formatter.WriteWarning(
				"Trace collection completed before a manual stop request. " +
				"This usually means the target process disconnected and the trace finalized early.");
			return;
		}

		if (context.UseRuntimeOwnedTraceCollection)
		{
			if (File.Exists(context.PrimaryOutputPath))
			{
				context.Formatter.WriteInfo("Runtime-owned Android startup trace finalized and copied from the device.");
			}
			else
			{
				var runtimeOwnedStatusMessage = context.EffectiveDuration is { } runtimeDuration
					? $"Trace is running. It will stop automatically after {FormatDuration(runtimeDuration)} unless you press Enter sooner."
					: "Trace is running. Press Enter to stop and finalize the trace output.";
				context.Formatter.WriteInfo(runtimeOwnedStatusMessage);
			}
			return;
		}

		var traceStatusMessage = !string.IsNullOrWhiteSpace(context.StoppingEventProvider)
			? "Waiting for the configured stopping event. Press Enter to stop early."
			: context.EffectiveDuration is { } explicitDuration
				? $"Trace is running. It will stop automatically after {FormatDuration(explicitDuration)} unless you press Enter sooner."
				: "Trace is running. Press Enter to stop and finalize the trace output.";
		context.Formatter.WriteInfo(traceStatusMessage);
	}

	static string FormatDuration(TimeSpan duration)
	{
		var positiveDuration = duration < TimeSpan.Zero ? duration.Negate() : duration;
		return $"{(int)positiveDuration.TotalDays:00}:{positiveDuration.Hours:00}:{positiveDuration.Minutes:00}:{positiveDuration.Seconds:00}";
	}

	static Task<ProcessResult> RunDotnetCommandAsync(
		ProfileSessionContext context,
		string statusMessage,
		IReadOnlyList<string> arguments,
		CancellationToken cancellationToken)
		=> context.Formatter is SpectreOutputFormatter spectre && !context.UseJson
			? spectre.StatusAsync(
				statusMessage,
				() => ProcessRunner.RunAsync("dotnet", [.. arguments], context.Project.ProjectDirectory, timeout: ProfileCommand.s_buildLaunchTimeout, environmentVariablesToRemove: ProfileCommand.s_msbuildSdkEnvVars, cancellationToken: cancellationToken))
			: ProcessRunner.RunAsync("dotnet", [.. arguments], context.Project.ProjectDirectory, timeout: ProfileCommand.s_buildLaunchTimeout, environmentVariablesToRemove: ProfileCommand.s_msbuildSdkEnvVars, cancellationToken: cancellationToken);
}
