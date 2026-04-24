// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using System.CommandLine.Parsing;
using Microsoft.Maui.Cli.Errors;
using Microsoft.Maui.Cli.Output;
using Spectre.Console;

namespace Microsoft.Maui.Cli.Commands;

internal static class ProfileOutputResolver
{
	internal static bool WasOptionExplicitlySpecified<T>(ParseResult parseResult, Option<T> option)
		=> parseResult.GetResult(option)?.Tokens.Count > 0;

	internal static string ResolveProfileConfiguration(string? requestedConfiguration, bool explicitlySpecified, string platform)
	{
		return string.IsNullOrWhiteSpace(requestedConfiguration)
			? "Release"
			: requestedConfiguration.Trim();
	}

	internal static TraceOutputFormat ResolveTraceOutputFormat(
		string? requestedFormat,
		bool explicitlySpecified,
		bool nonInteractive,
		SpectreOutputFormatter? spectre)
	{
		if (explicitlySpecified || nonInteractive || spectre is null)
			return ResolveTraceOutputFormat(requestedFormat);

		return spectre.Prompt(
			new SelectionPrompt<TraceOutputFormat>()
				.Title("[bold]Select the trace output format[/]")
				.HighlightStyle(new Style(Color.DodgerBlue1))
				.UseConverter(FormatTraceOutputPromptChoice)
				.AddChoices([TraceOutputFormat.NetTrace, TraceOutputFormat.Speedscope, TraceOutputFormat.Mibc]));
	}

	internal static TraceOutputFormat ResolveTraceOutputFormat(string? requestedFormat) => requestedFormat?.Trim().ToLowerInvariant() switch
	{
		null or "" or "nettrace" => TraceOutputFormat.NetTrace,
		"speedscope" => TraceOutputFormat.Speedscope,
		"mibc" => TraceOutputFormat.Mibc,
		_ => throw new MauiToolException(
			ErrorCodes.InvalidArgument,
			$"Unsupported output format '{requestedFormat}'. Supported values are: nettrace, speedscope, mibc.")
	};

	internal static string ResolveOutputPath(string projectName, string? requestedOutput, TraceOutputFormat outputFormat)
	{
		if (string.IsNullOrWhiteSpace(requestedOutput))
		{
			var safeProjectName = string.IsNullOrWhiteSpace(projectName) ? "maui-startup-profile" : projectName;
			var defaultName = $"{safeProjectName}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.nettrace";
			return Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, defaultName));
		}

		var fullPath = Path.GetFullPath(requestedOutput);
		if (outputFormat == TraceOutputFormat.Speedscope &&
			fullPath.EndsWith(ProfileCommand.SpeedscopeExtension, StringComparison.OrdinalIgnoreCase))
		{
			fullPath = fullPath[..^ProfileCommand.SpeedscopeExtension.Length];
		}
		else if (outputFormat == TraceOutputFormat.Mibc &&
			fullPath.EndsWith(ProfileCommand.MibcExtension, StringComparison.OrdinalIgnoreCase))
		{
			fullPath = Path.ChangeExtension(fullPath, "nettrace");
		}

		if (string.IsNullOrWhiteSpace(Path.GetExtension(fullPath)))
			fullPath += ".nettrace";
		return fullPath;
	}

	internal static string GetPrimaryOutputPath(string collectorOutputPath, TraceOutputFormat outputFormat) => outputFormat switch
	{
		TraceOutputFormat.Speedscope => collectorOutputPath.EndsWith(".nettrace", StringComparison.OrdinalIgnoreCase)
			? collectorOutputPath[..^".nettrace".Length] + ProfileCommand.SpeedscopeExtension
			: collectorOutputPath + ProfileCommand.SpeedscopeExtension,
		TraceOutputFormat.Mibc => Path.ChangeExtension(collectorOutputPath, "mibc"),
		_ => collectorOutputPath
	};

	internal static string FormatOutputFormat(TraceOutputFormat outputFormat) => outputFormat switch
	{
		TraceOutputFormat.NetTrace => "nettrace",
		TraceOutputFormat.Speedscope => "speedscope",
		TraceOutputFormat.Mibc => "mibc",
		_ => outputFormat.ToString().ToLowerInvariant()
	};

	internal static void ValidateStoppingEventOptions(string? providerName, string? eventName, string? payloadFilter)
	{
		var hasProviderName = !string.IsNullOrWhiteSpace(providerName);
		var hasEventName = !string.IsNullOrWhiteSpace(eventName);
		var hasPayloadFilter = !string.IsNullOrWhiteSpace(payloadFilter);

		if (!hasProviderName && (hasEventName || hasPayloadFilter))
		{
			throw new MauiToolException(
				ErrorCodes.InvalidArgument,
				"--stopping-event-provider-name is required when using --stopping-event-event-name or --stopping-event-payload-filter.");
		}

		if (!hasEventName && hasPayloadFilter)
		{
			throw new MauiToolException(
				ErrorCodes.InvalidArgument,
				"--stopping-event-event-name is required when using --stopping-event-payload-filter.");
		}
	}

	internal static StoppingEventConfiguration ResolveStoppingEventConfiguration(
		TimeSpan? duration,
		string? providerName,
		string? eventName,
		string? payloadFilter)
		=> new(providerName, eventName, payloadFilter, AutoSelected: false);

	static string FormatTraceOutputPromptChoice(TraceOutputFormat outputFormat) => outputFormat switch
	{
		TraceOutputFormat.NetTrace => "[bold]nettrace[/] [dim](raw EventPipe trace for PerfView / Visual Studio)[/]",
		TraceOutputFormat.Speedscope => "[bold]speedscope[/] [dim](browser-friendly flame chart; also keeps the raw .nettrace)[/]",
		TraceOutputFormat.Mibc => "[bold]mibc[/] [dim](creates a reusable PGO profile and keeps the raw .nettrace)[/]",
		_ => $"[bold]{Markup.Escape(FormatOutputFormat(outputFormat))}[/]"
	};
}
