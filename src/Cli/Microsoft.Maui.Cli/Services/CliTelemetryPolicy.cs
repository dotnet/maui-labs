// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Parsing;

namespace Microsoft.Maui.Cli.Services;

internal static class CliTelemetryPolicy
{
	internal const string ConnectionStringVariable = "MAUI_CLI_TELEMETRY_CONNECTION_STRING";
	internal const string OptOutVariable = "MAUI_CLI_TELEMETRY_OPTOUT";

	internal static string? ResolveConnectionString(Func<string, string?> environment, string? defaultConnectionString)
	{
		if (IsTrue(environment(OptOutVariable)) ||
			IsTrue(environment("DOTNET_CLI_TELEMETRY_OPTOUT")) ||
			IsTrue(environment("OTEL_SDK_DISABLED")))
			return null;

		// Do not inherit APPLICATIONINSIGHTS_CONNECTION_STRING from a user's application.
		var connectionString = environment(ConnectionStringVariable) ?? defaultConnectionString;
		return string.IsNullOrWhiteSpace(connectionString) ? null : connectionString.Trim();
	}

	internal static string? GetCommandName(ParseResult parseResult)
	{
		if (parseResult.Errors.Count != 0 ||
			parseResult.CommandResult.Command is RootCommand ||
			parseResult.Action is HelpAction ||
			parseResult.GetValue(GlobalOptions.DryRunOption))
			return null;

		var names = new Stack<string>();
		for (var result = parseResult.CommandResult; result.Parent is CommandResult parent; result = parent)
			names.Push(result.Command.Name);

		var name = string.Join(" ", names);
		return name is "" or "version" ? null : name;
	}

	internal static bool ShouldShowNotice(ParseResult parseResult, string commandName,
		bool redirected, Func<string, string?> environment) =>
		commandName.Split(' ')[0] is not ("devflow" or "go") &&
		CliUpdatePolicy.IsInteractive(parseResult.GetValue(GlobalOptions.JsonOption),
			parseResult.GetValue(GlobalOptions.CiOption), redirected, environment);

	static bool IsTrue(string? value) =>
		value?.Trim() is { } text && (text == "1" || text.Equals("true", StringComparison.OrdinalIgnoreCase));
}
