// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui.Cli.Errors;
using Microsoft.Maui.Cli.Models;

namespace Microsoft.Maui.Cli.Commands;

internal static class ProfileTraceOutputValidation
{
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
}
