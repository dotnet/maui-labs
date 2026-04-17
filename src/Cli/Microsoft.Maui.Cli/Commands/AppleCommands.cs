// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using System.CommandLine.Parsing;
using Microsoft.Maui.Cli.Output;
using Microsoft.Maui.Cli.Providers.Apple;
using Microsoft.Maui.Cli.Utils;

namespace Microsoft.Maui.Cli.Commands;

/// <summary>
/// Implementation of 'maui apple' command group.
/// Sub-commands: xcode, runtime, simulator.
/// </summary>
public static class AppleCommands
{
	public static Command Create()
	{
		var command = new Command("apple", "Apple platform management (Xcode, simulators, runtimes)");

		command.Add(CreateXcodeCommand());
		command.Add(CreateRuntimeCommand());
		command.Add(CreateSimulatorCommand());

		return command;
	}

	static Command CreateXcodeCommand()
	{
		var xcodeCommand = new Command("xcode", "Manage Xcode installations");

		// maui apple xcode list
		var listCommand = new Command("list", "List installed Xcode versions");
		listCommand.SetAction((ParseResult parseResult) =>
		{
			var formatter = Program.GetFormatter(parseResult);

			if (!PlatformDetector.IsMacOS)
			{
				formatter.WriteWarning("Xcode is only available on macOS.");
				return 1;
			}

			var appleProvider = Program.AppleProvider;
			var useJson = parseResult.GetValue(GlobalOptions.JsonOption);

			var installations = appleProvider.GetXcodeInstallations();
			if (useJson)
			{
				formatter.Write(installations);
			}
			else
			{
				if (!installations.Any())
				{
					formatter.WriteWarning("No Xcode installations found.");
					return 0;
				}

				if (formatter is SpectreOutputFormatter spectre)
				{
					spectre.WriteTable(installations,
						("Version", x => x.Version ?? "?"),
						("Build", x => x.Build ?? "?"),
						("Path", x => x.Path),
						("Selected", x => x.IsSelected ? "✓" : ""));
				}
			}
			return 0;
		});

		xcodeCommand.Add(listCommand);
		return xcodeCommand;
	}

	static Command CreateRuntimeCommand()
	{
		var runtimeCommand = new Command("runtime", "Manage simulator runtimes");

		// maui apple runtime list [--platform ios]
		var platformOption = new Option<string?>("--platform") { Description = "Filter by platform (iOS, tvOS, watchOS, visionOS)" };
		var listCommand = new Command("list", "List installed simulator runtimes")
		{
			platformOption
		};

		listCommand.SetAction((ParseResult parseResult) =>
		{
			var formatter = Program.GetFormatter(parseResult);

			if (!PlatformDetector.IsMacOS)
			{
				formatter.WriteWarning("Runtimes are only available on macOS.");
				return 1;
			}

			var appleProvider = Program.AppleProvider;
			var useJson = parseResult.GetValue(GlobalOptions.JsonOption);
			var platform = parseResult.GetValue(platformOption);

			var runtimes = appleProvider.GetRuntimes(platform, availableOnly: false);
			if (useJson)
			{
				formatter.Write(runtimes);
			}
			else
			{
				if (!runtimes.Any())
				{
					formatter.WriteWarning("No simulator runtimes found.");
					return 0;
				}

				if (formatter is SpectreOutputFormatter spectre)
				{
					spectre.WriteTable(runtimes,
						("Name", r => r.Name),
						("Platform", r => r.Platform ?? "?"),
						("Version", r => r.Version ?? "?"),
						("Available", r => r.IsAvailable ? "✓" : "✗"),
						("Bundled", r => r.IsBundled ? "Yes" : "No"));
				}
			}
			return 0;
		});

		runtimeCommand.Add(listCommand);
		return runtimeCommand;
	}

	static Command CreateSimulatorCommand()
	{
		var simCommand = new Command("simulator", "Manage iOS simulators");

		// maui apple simulator list
		var listCommand = new Command("list", "List simulator devices");
		listCommand.SetAction((ParseResult parseResult) =>
		{
			var formatter = Program.GetFormatter(parseResult);

			if (!PlatformDetector.IsMacOS)
			{
				formatter.WriteWarning("Simulators are only available on macOS.");
				return 1;
			}

			var appleProvider = Program.AppleProvider;
			var useJson = parseResult.GetValue(GlobalOptions.JsonOption);

			var simulators = appleProvider.GetSimulators(availableOnly: false);
			if (useJson)
			{
				formatter.Write(simulators);
			}
			else
			{
				if (!simulators.Any())
				{
					formatter.WriteWarning("No simulators found.");
					return 0;
				}

				if (formatter is SpectreOutputFormatter spectre)
				{
					spectre.WriteTable(simulators,
						("Name", s => s.Name),
						("UDID", s => s.Udid),
						("OS", s => $"{s.Platform} {s.OSVersion}"),
						("State", s => s.IsBooted ? "Booted" : s.State ?? "Shutdown"),
						("Available", s => s.IsAvailable ? "✓" : "✗"));
				}
			}
			return 0;
		});

		// maui apple simulator start <name-or-udid>
		var startNameArg = new Argument<string>("name-or-udid") { Description = "Simulator name or UDID to boot" };
		var startCommand = new Command("start", "Boot a simulator") { startNameArg };
		startCommand.SetAction((ParseResult parseResult) =>
		{
			var formatter = Program.GetFormatter(parseResult);

			if (!PlatformDetector.IsMacOS)
			{
				formatter.WriteWarning("Simulators are only available on macOS.");
				return 1;
			}

			var appleProvider = Program.AppleProvider;
			var target = parseResult.GetValue(startNameArg);

			var success = appleProvider.BootSimulator(target!);
			if (success)
				formatter.WriteSuccess($"Simulator '{target}' booted.");
			else
				formatter.WriteWarning($"Failed to boot simulator '{target}'.");

			return success ? 0 : 1;
		});

		// maui apple simulator stop <name-or-udid>
		var stopNameArg = new Argument<string>("name-or-udid") { Description = "Simulator name or UDID to shut down (or 'all')" };
		var stopCommand = new Command("stop", "Shut down a simulator") { stopNameArg };
		stopCommand.SetAction((ParseResult parseResult) =>
		{
			var formatter = Program.GetFormatter(parseResult);

			if (!PlatformDetector.IsMacOS)
			{
				formatter.WriteWarning("Simulators are only available on macOS.");
				return 1;
			}

			var appleProvider = Program.AppleProvider;
			var target = parseResult.GetValue(stopNameArg);

			var success = appleProvider.ShutdownSimulator(target!);
			if (success)
				formatter.WriteSuccess($"Simulator '{target}' shut down.");
			else
				formatter.WriteWarning($"Failed to shut down simulator '{target}'.");

			return success ? 0 : 1;
		});

		// maui apple simulator delete <name-or-udid>
		var deleteNameArg = new Argument<string>("name-or-udid") { Description = "Simulator name or UDID to delete" };
		var deleteCommand = new Command("delete", "Delete a simulator") { deleteNameArg };
		deleteCommand.SetAction((ParseResult parseResult) =>
		{
			var formatter = Program.GetFormatter(parseResult);

			if (!PlatformDetector.IsMacOS)
			{
				formatter.WriteWarning("Simulators are only available on macOS.");
				return 1;
			}

			var appleProvider = Program.AppleProvider;
			var target = parseResult.GetValue(deleteNameArg);

			var success = appleProvider.DeleteSimulator(target!);
			if (success)
				formatter.WriteSuccess($"Simulator '{target}' deleted.");
			else
				formatter.WriteWarning($"Failed to delete simulator '{target}'.");

			return success ? 0 : 1;
		});

		simCommand.Add(listCommand);
		simCommand.Add(startCommand);
		simCommand.Add(stopCommand);
		simCommand.Add(deleteCommand);
		return simCommand;
	}
}
