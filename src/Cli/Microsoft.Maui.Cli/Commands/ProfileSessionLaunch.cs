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

		context.ExitControlServer = ExitControlServer.Attach(context.ReservedPorts!.ExitControlReservation, context.Formatter, context.UseJson, context.Verbose);
		context.ReservedPorts.DiagnosticReservation.Dispose();

		ProfileCommandProcessHelpers.WriteVerbose(context.Formatter, context.UseJson, context.Verbose, $"Starting dotnet-dsrouter in '{context.DsrouterKind}' mode on port {context.DiagnosticPort}.");
		var dsrouterStart = await DotnetDsrouterRunner.StartAsync(context.Transport, context.DiagnosticPort, context.Formatter, context.UseJson, context.Verbose, cancellationToken);
		context.DsrouterProcess = dsrouterStart.DsrouterProcess;
		var dsrouterPid = dsrouterStart.DsrouterPid;
		ProfileCommandProcessHelpers.WriteVerbose(context.Formatter, context.UseJson, context.Verbose, $"dotnet-dsrouter reported PID {dsrouterPid}.");
		await DotnetDsrouterRunner.EnsureStartedAsync(context.DsrouterProcess, context.DiagnosticPort, cancellationToken);

		if (!context.StartTraceAfterLaunch)
		{
			context.TraceProcess = DotnetTraceRunner.StartCollector(
				context.Project.ProjectDirectory,
				context.OutputPath,
				context.OutputFormat,
				dsrouterPid,
				context.Device.Id,
				context.TraceProfile,
				context.EffectiveDuration,
				context.StoppingEventProvider,
				context.StoppingEventName,
				context.StoppingEventPayloadFilter,
				context.Formatter,
				context.UseJson,
				context.Verbose,
				cancellationToken);

			ProfileCommandProcessHelpers.WriteVerbose(context.Formatter, context.UseJson, context.Verbose, $"Waiting briefly for dotnet-trace (PID {context.TraceProcess.Process.Id}) to connect.");
			await DotnetTraceRunner.EnsureStartedAsync(context.TraceProcess, cancellationToken);
		}

		await LaunchAppAsync(context, cancellationToken);

		if (context.StartTraceAfterLaunch)
		{
			context.TraceProcess = await DotnetTraceRunner.StartWithRetryAsync(
				context.Project.ProjectDirectory,
				context.OutputPath,
				context.OutputFormat,
				dsrouterPid,
				context.Device.Id,
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
			context.BuildInjection);
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
			context.BuildInjection);
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
				"Trace collection completed during app launch before a manual stop request. " +
				"This usually means the target process disconnected and the trace finalized early.");
			return;
		}

		var traceStatusMessage = !string.IsNullOrWhiteSpace(context.StoppingEventProvider)
			? "Waiting for the configured stopping event. Press Enter to stop early."
			: context.EffectiveDuration is { } explicitDuration
				? $"Startup trace is running. It will stop automatically after {FormatDuration(explicitDuration)} unless you press Enter sooner."
				: "Startup trace is running. Press Enter to stop and finalize the trace output.";
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
