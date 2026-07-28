// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;

namespace Microsoft.Maui.Cli.Commands;

public static partial class ProjectCommands
{
	internal static readonly Option<string> ProjectOption = new("--project", "-p")
	{
		Description = "Path to a .csproj file. If omitted, the current directory is searched.",
		Recursive = true,
	};

	public static Command Create()
	{
		var command = new Command("project", "Project-level .NET MAUI commands");
		command.Add(CreateVersionCommand());
		return command;
	}
}
