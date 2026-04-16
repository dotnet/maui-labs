// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Reflection;
using Microsoft.Maui.Cli.Output;

namespace Microsoft.Maui.Cli.Commands;

/// <summary>
/// Implementation of 'maui version' command.
/// </summary>
public static class VersionCommand
{
	public static Command Create()
	{
		var command = new Command("version", "Display version information");

		command.SetAction((ParseResult parseResult) =>
		{
			var useJson = parseResult.GetValue(GlobalOptions.JsonOption);

			var assembly = Assembly.GetExecutingAssembly();
			var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
				?? assembly.GetName().Version?.ToString()
				?? "0.0.0";

			if (useJson)
			{
				var formatter = Program.GetFormatter(parseResult);
				formatter.WriteVersion(version, Environment.Version.ToString(), Environment.OSVersion.ToString());
			}
			else
			{
				var formatter = Program.GetFormatter(parseResult);
				formatter.WriteVersion(version, $".NET {Environment.Version}", Environment.OSVersion.ToString());
			}
		});

		return command;
	}
}
