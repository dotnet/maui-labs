// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui.Cli.Errors;

namespace Microsoft.Maui.Cli.Commands;

internal static class ProfileSessionRunner
{
	internal static async Task<MauiProfileResult> RunAsync(ProfileSessionRequest request, CancellationToken cancellationToken)
	{
		var context = await ProfileSessionSetup.PrepareAsync(request, cancellationToken);
		var stopRequestedByUser = false;
		try
		{
			await ProfileSessionLaunch.StartAsync(context, cancellationToken);

			if (context.TraceProcess is not null)
			{
				stopRequestedByUser = await ProfileTraceLifecycle.WaitForCompletionAsync(
					context.TraceProcess,
					// Manual mode is driven by stdin signals, so we still listen for
					// a newline / Ctrl-C even in --json: scripted callers pipe a second
					// newline (or close stdin) to stop. Other modes keep the existing
					// "no manual stop in JSON" behavior.
					allowManualStop: !context.UseJson || context.ManualStart,
					context.Formatter,
					context.UseJson,
					context.Verbose,
					cancellationToken);
			}

			var postProcessingCancellationToken = ProfileCommand.ResolvePostProcessingCancellationToken(stopRequestedByUser, cancellationToken);

			if (!context.UseRuntimeOwnedTraceCollection
				&& context.ExitControlServer is not null)
				await TryRequestAppExitAsync(context, postProcessingCancellationToken);

			if (context.OutputFormat == TraceOutputFormat.Mibc)
			{
				await ProfileCommand.ConvertNetTraceToMibcAsync(
					context.Project,
					context.Framework,
					context.Configuration,
					context.OutputPath,
					context.PrimaryOutputPath,
					context.Formatter,
					context.UseJson,
					context.Verbose,
					postProcessingCancellationToken);
			}
		}
		finally
		{
			await CleanupAsync(context);
		}

		EnsurePrimaryOutputExists(context.PrimaryOutputPath);
		ProfileTraceOutputValidation.ValidateTraceOutput(context.PrimaryOutputPath, context.OutputPath, context.OutputFormat, context.Transport.Platform);

		return CreateResult(context);
	}

	static MauiProfileResult CreateResult(ProfileSessionContext context)
	{
		return new MauiProfileResult
		{
			ProjectPath = context.Project.ProjectPath,
			ProjectName = context.Project.ProjectName,
			Framework = context.Framework,
			Platform = context.Transport.Platform,
			DeviceId = context.Device.Id,
			DeviceName = context.Device.Name,
			Configuration = context.Configuration,
			Format = ProfileOutputResolver.FormatOutputFormat(context.OutputFormat),
			OutputPath = context.PrimaryOutputPath,
			RawTracePath = context.OutputFormat is TraceOutputFormat.Speedscope or TraceOutputFormat.Mibc ? context.OutputPath : null,
			DsrouterKind = context.DsrouterKind,
			DiagnosticAddress = context.DiagnosticAddress,
			DiagnosticPort = context.DiagnosticPort,
			UsedStoppingEvent = !string.IsNullOrWhiteSpace(context.StoppingEventProvider),
			StartedAtUtc = context.StartedAtUtc,
			CompletedAtUtc = DateTimeOffset.UtcNow
		};
	}

	static async Task CleanupAsync(ProfileSessionContext context)
	{
		context.ReservedPorts?.Dispose();
		context.ExitControlServer?.Dispose();

		if (context.TraceProcess is not null)
		{
			await ProfileTraceLifecycle.StopBackgroundProcessAsync(context.TraceProcess.Process, "dotnet-trace", context.Formatter, context.UseJson, context.Verbose);
			context.TraceProcess.Dispose();
		}

		if (context.Transport.RequiresManualExitControlPortRouting)
		{
			if (context.ReservedPorts is not null)
				await ProfileCommandPortRouter.RemoveAdbPortRoutingAsync(context.Device, context.Formatter, context.UseJson, context.Verbose, context.ReservedPorts.ExitControlPort);
			else
				await ProfileCommandPortRouter.RemoveAdbPortRoutingAsync(context.Device, context.Formatter, context.UseJson, context.Verbose, ProfileCommandPortRouter.GetExitControlPort(context.DiagnosticPort));
		}
	}

	internal static async Task TryRequestAppExitAsync(ProfileSessionContext context, CancellationToken cancellationToken)
	{
		var appExitRequested = await context.ExitControlServer!.TryRequestExitAsync(
			ProfileCommand.s_exitControlConnectTimeout,
			ProfileCommand.s_exitControlCommandTimeout,
			cancellationToken);

		if (!appExitRequested && !context.UseJson)
		{
			context.Formatter.WriteWarning(context.ManualStart
				? "The app did not connect to the profiling exit channel, so it may remain running. " +
				  "Ensure it references Microsoft.Maui.ProfilingHelper."
				: "The app did not connect to the startup profiling exit channel, so it may remain running and not flush PGO data. " +
				  "Ensure it references Microsoft.Maui.ProfilingHelper and loads that assembly during startup.");
		}
	}

	static void EnsurePrimaryOutputExists(string primaryOutputPath)
	{
		if (File.Exists(primaryOutputPath))
			return;

		throw new MauiToolException(
			ErrorCodes.InternalError,
			$"Trace collection completed, but '{primaryOutputPath}' was not created.");
	}
}
