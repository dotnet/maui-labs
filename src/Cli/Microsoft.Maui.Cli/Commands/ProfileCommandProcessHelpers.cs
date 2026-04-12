// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui.Cli.Errors;
using Microsoft.Maui.Cli.Output;
using Microsoft.Maui.Cli.Utils;

namespace Microsoft.Maui.Cli.Commands;

internal static class ProfileCommandProcessHelpers
{
	internal static string GetProcessFailureDetails(ProcessResult result) =>
		string.IsNullOrWhiteSpace(result.StandardError)
			? result.StandardOutput.Trim()
			: result.StandardError.Trim();

	internal static void WriteVerbose(IOutputFormatter formatter, bool useJson, bool verbose, string message)
	{
		if (verbose && !useJson)
			formatter.WriteProgress($"[debug] {message}");
	}

	internal static string FormatCommandLine(string fileName, IEnumerable<string> arguments) =>
		string.Join(" ", [QuoteForDisplay(fileName), .. arguments.Select(QuoteForDisplay)]);

	static string QuoteForDisplay(string value) =>
		value.Any(ch => char.IsWhiteSpace(ch) || ch is '"' or '\'')
			? $"\"{value.Replace("\"", "\\\"")}\""
			: value;

	internal static Exception CreateProcessFailureException(string commandName, ProcessResult result)
	{
		var details = string.IsNullOrWhiteSpace(result.StandardError)
			? result.StandardOutput.Trim()
			: result.StandardError.Trim();

		return new MauiToolException(
			ErrorCodes.InternalError,
			$"{commandName} failed with exit code {result.ExitCode}.",
			nativeError: details);
	}
}
