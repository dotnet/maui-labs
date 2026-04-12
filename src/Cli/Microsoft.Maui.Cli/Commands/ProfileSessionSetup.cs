// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui.Cli.Errors;
using Microsoft.Maui.Cli.Models;

namespace Microsoft.Maui.Cli.Commands;

internal static class ProfileSessionSetup
{
	internal static async Task<ProfileSessionContext> PrepareAsync(ProfileSessionRequest request, CancellationToken cancellationToken)
	{
		var primaryOutputPath = ProfileOutputResolver.GetPrimaryOutputPath(request.OutputPath, request.OutputFormat);
		var outputDirectory = Path.GetDirectoryName(request.OutputPath);
		if (string.IsNullOrWhiteSpace(outputDirectory))
		{
			throw new MauiToolException(
				ErrorCodes.InvalidArgument,
				$"Could not determine the output directory for '{request.OutputPath}'.");
		}

		Directory.CreateDirectory(outputDirectory);

		var profilePlatform = ProfileTargetResolver.InferPlatformFromTargetFramework(request.Framework) ?? request.Device.Platform;
		var transport = ProfileCommand.ResolveProfileTransport(profilePlatform, request.Device);
		var context = new ProfileSessionContext(request, primaryOutputPath, profilePlatform, transport);

		WriteSessionHeader(context);
		WriteVerboseSettings(context);

		context.ReservedPorts = await ProfileCommandPortRouter.ReserveProfilePortsAndConfigureRoutingAsync(
			context.Device,
			context.Transport,
			context.DiagnosticPort,
			context.Formatter,
			context.UseJson,
			context.Verbose,
			cancellationToken);

		context.DiagnosticPort = context.ReservedPorts.DiagnosticPort;

		var hasStartupProfilingHelper = MauiProjectResolver.HasPackageReference(context.Project.ProjectPath, ProfileCommand.StartupProfilingPackageId);
		context.BuildInjection = string.Equals(profilePlatform, Platforms.iOS, StringComparison.OrdinalIgnoreCase)
			? null
			: ProfileCommandBuildInjectionResolver.TryCreateBuildInjection(
				context.DiagnosticAddress,
				context.ReservedPorts.ExitControlPort,
				injectBootstrap: !hasStartupProfilingHelper);

		WriteDiagnosticPortInfo(context);
		return context;
	}

	static void WriteSessionHeader(ProfileSessionContext context)
	{
		if (context.UseJson)
			return;

		context.Formatter.WriteInfo($"Project: {context.Project.ProjectPath}");
		context.Formatter.WriteInfo($"Framework: {context.Framework}");
		context.Formatter.WriteInfo($"Configuration: {context.Configuration}");
		context.Formatter.WriteInfo($"Device: {context.Device.Name} ({context.Device.Id})");
		context.Formatter.WriteInfo($"Format: {ProfileOutputResolver.FormatOutputFormat(context.OutputFormat)}");
		context.Formatter.WriteInfo($"Output: {context.PrimaryOutputPath}");
		if (!string.Equals(context.PrimaryOutputPath, context.OutputPath, StringComparison.OrdinalIgnoreCase))
			context.Formatter.WriteInfo($"Raw trace companion: {context.OutputPath}");
		if (context.AutoSelectedStoppingEvent)
		{
			context.Formatter.WriteInfo(
				$"Stopping event: {ProfileCommand.StartupProfilingProviderName}/{ProfileCommand.StartupProfilingEventName} " +
				"(auto-detected from the app's startup profiling helper).");
		}
	}

	static void WriteVerboseSettings(ProfileSessionContext context)
	{
		ProfileCommandProcessHelpers.WriteVerbose(
			context.Formatter,
			context.UseJson,
			context.Verbose,
			$"Profile settings: configuration={context.Configuration}, noBuild={context.NoBuild}, dsrouterKind={context.DsrouterKind}, " +
			$"diagnosticAddress={context.DiagnosticAddress}, diagnosticListenMode={context.Transport.DiagnosticListenMode}, diagnosticPort={context.DiagnosticPort}, " +
			$"traceProfile={context.TraceProfile ?? "(default)"}, outputFormat={ProfileOutputResolver.FormatOutputFormat(context.OutputFormat)}, duration={context.EffectiveDuration?.ToString() ?? "(manual stop)"}, " +
			$"stoppingEventProvider={context.StoppingEventProvider ?? "(none)"}, stoppingEventName={context.StoppingEventName ?? "(none)"}, " +
			$"stoppingEventPayloadFilter={context.StoppingEventPayloadFilter ?? "(none)"}");
	}

	static void WriteDiagnosticPortInfo(ProfileSessionContext context)
	{
		if (context.UseJson)
			return;

		context.Formatter.WriteInfo($"Diagnostic port: {context.DiagnosticPort}");
		if (context.DiagnosticPort != context.RequestedDiagnosticPort)
			context.Formatter.WriteInfo($"Port {context.RequestedDiagnosticPort} was busy, so the profiler selected {context.DiagnosticPort}.");

		if (context.BuildInjection is null)
		{
			context.Formatter.WriteWarning(
				"The CLI's startup profiling injection assets were not found next to the tool binaries, so automatic startup-complete and graceful app-exit injection are unavailable for this run.");
		}
	}
}
