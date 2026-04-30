// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using Microsoft.Maui.Cli.Errors;
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
		command.Add(CreateInstallCommand());

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

	static Command CreateInstallCommand()
	{
		var platformOption = new Option<string[]>("--platform")
		{
			Description = "Platform(s) to ensure runtimes for (iOS, tvOS, watchOS, visionOS, all). Defaults to iOS only; use 'all' to install all available runtimes.",
			AllowMultipleArgumentsPerToken = true,
			DefaultValueFactory = _ => new[] { "iOS" }
		};

		var installCommand = new Command("install", "Set up Apple development environment (CLT, runtimes)")
		{
			platformOption
		};

		installCommand.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
		{
			var formatter = Program.GetFormatter(parseResult);

			if (!PlatformDetector.IsMacOS)
			{
				formatter.WriteWarning("Apple install is only available on macOS.");
				return 1;
			}

			var appleProvider = Program.AppleProvider;
			var useJson = parseResult.GetValue(GlobalOptions.JsonOption);
			var platforms = parseResult.GetValue(platformOption);
			var dryRun = parseResult.GetValue(GlobalOptions.DryRunOption);

			if (dryRun && !useJson)
				formatter.WriteInfo("Dry run mode — no changes will be made.");

			try
			{
				// "all" means no filter — install runtimes for every available platform
				var platformFilter = platforms is { Length: > 0 } && !platforms.Any(p => string.Equals(p, "all", StringComparison.OrdinalIgnoreCase))
					? platforms
					: null;

				var result = await appleProvider.InstallEnvironmentAsync(
					platformFilter,
					dryRun,
					ct);

				if (useJson)
				{
					formatter.Write(result);
				}
				else
				{
					if (result.XcodeVersion is not null)
						formatter.WriteSuccess($"Xcode: {result.XcodeVersion}");
					else
						formatter.WriteWarning("Xcode: not found");

					formatter.WriteInfo($"Command Line Tools: {(result.CommandLineToolsInstalled ? "installed" : "not installed")}");

					if (result.Platforms.Count > 0)
						formatter.WriteInfo($"Platforms: {string.Join(", ", result.Platforms)}");

					if (result.Runtimes.Count > 0)
						formatter.WriteInfo($"Runtimes: {string.Join(", ", result.Runtimes)}");

					formatter.WriteInfo($"Status: {result.Status}");
				}

				return result.Status is "ok" or "skipped" ? 0 : 1;
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				formatter.WriteError(new MauiToolException(ErrorCodes.AppleSetupFailed, "Apple install failed.", ex));
				return 1;
			}
			catch (Exception ex)
			{
				return Program.HandleCommandException(formatter, ex);
			}
		});

		return installCommand;
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

		// maui apple simulator start <name-or-udid> [--no-open]
		var startNameArg = new Argument<string>("name-or-udid") { Description = "Simulator name or UDID to boot" };
		var noOpenOption = new Option<bool>("--no-open") { Description = "Do not open the Simulator UI window after booting" };
		var startCommand = new Command("start", "Boot a simulator and open the Simulator UI") { startNameArg, noOpenOption };
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
			var noOpen = parseResult.GetValue(noOpenOption);

			var success = appleProvider.BootSimulator(target!);
			if (success)
			{
				if (!noOpen)
					appleProvider.OpenSimulatorApp();
				formatter.WriteSuccess($"Simulator '{target}' booted.");
			}
			else
			{
				formatter.WriteWarning($"Failed to boot simulator '{target}'.");
			}

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
		simCommand.Add(CreateSimulatorCreateCommand());
		simCommand.Add(CreateSimulatorEraseCommand());
		return simCommand;
	}

	static Command CreateSimulatorCreateCommand()
	{
		var deviceTypeArg = new Argument<string>("device-type") { Description = "Device type identifier (e.g. com.apple.CoreSimulator.SimDeviceType.iPhone-15)" };
		var nameOption = new Option<string?>("--name") { Description = "Custom name for the new simulator (defaults to a name derived from device-type)" };
		var runtimeOption = new Option<string?>("--runtime") { Description = "Runtime identifier (e.g. com.apple.CoreSimulator.SimRuntime.iOS-17-2)" };

		var ifNotExistsOption = new Option<bool>("--if-not-exists") { Description = "Treat name collision as success: if a simulator with this name already exists, return its UDID instead of failing." };

		var createCommand = new Command("create", "Create a new simulator device") { deviceTypeArg, nameOption, runtimeOption, ifNotExistsOption };
		createCommand.SetAction((ParseResult parseResult) =>
		{
			var formatter = Program.GetFormatter(parseResult);

			if (!PlatformDetector.IsMacOS)
			{
				formatter.WriteError(new MauiToolException(ErrorCodes.PlatformNotSupported, "Simulators are only available on macOS."));
				return 1;
			}

			var appleProvider = Program.AppleProvider;
			var deviceType = parseResult.GetValue(deviceTypeArg)!;
			var runtime = parseResult.GetValue(runtimeOption);

			// Derive a human-readable default name from the device-type identifier
			var customName = parseResult.GetValue(nameOption);
			var parts = deviceType.Split('.');
			var shortType = parts.Length > 1 ? parts[parts.Length - 1].Replace('-', ' ') : deviceType;
			var name = !string.IsNullOrWhiteSpace(customName) ? customName : shortType;
			if (string.IsNullOrWhiteSpace(customName) && runtime is not null)
			{
				var rParts = runtime.Split('.');
				var rLast = rParts.Length > 1 ? rParts[rParts.Length - 1] : runtime;
				var dashIdx = rLast.IndexOf('-');
				var rShort = dashIdx >= 0
					? rLast[..dashIdx] + ' ' + rLast[(dashIdx + 1)..].Replace('-', '.')
					: rLast;
				name = $"{shortType} ({rShort})";
			}

			// Idempotency probe: simctl create does not dedupe by name. Without this check
			// repeated invocations create multiple devices with the same name, which then
			// makes name-keyed commands (boot/erase/delete) ambiguous.
			var ifNotExists = parseResult.GetValue(ifNotExistsOption);
			var existing = appleProvider.GetSimulators().FirstOrDefault(s =>
				string.Equals(s.Name, name, StringComparison.Ordinal));
			if (existing is not null)
			{
				if (ifNotExists)
				{
					var useJson2 = parseResult.GetValue(GlobalOptions.JsonOption);
					if (useJson2)
						formatter.Write(new SimulatorCreateResult { Udid = existing.Udid, Name = name, DeviceType = existing.DeviceTypeIdentifier ?? deviceType, Runtime = existing.RuntimeIdentifier ?? runtime });
					else
						formatter.WriteSuccess($"Simulator '{name}' already exists with UDID: {existing.Udid}");
					return 0;
				}

				var dupEx = new MauiToolException(
					ErrorCodes.AppleSimulatorCreateFailed,
					$"A simulator named '{name}' already exists (UDID: {existing.Udid}). Use --name to choose a different name, --if-not-exists to reuse the existing one, or 'maui apple simulator delete {existing.Udid}' first.");
				formatter.WriteError(dupEx);
				return 1;
			}

			var udid = appleProvider.CreateSimulator(name, deviceType, runtime);
			if (udid is null)
			{
				var ex = new MauiToolException(ErrorCodes.AppleSimulatorCreateFailed, $"Failed to create simulator for device type '{deviceType}'.");
				formatter.WriteError(ex);
				return 1;
			}

			var useJson = parseResult.GetValue(GlobalOptions.JsonOption);
			if (useJson)
				formatter.Write(new SimulatorCreateResult { Udid = udid, Name = name, DeviceType = deviceType, Runtime = runtime });
			else
				formatter.WriteSuccess($"Simulator '{name}' created with UDID: {udid}");
			return 0;
		});

		return createCommand;
	}

	static Command CreateSimulatorEraseCommand()
	{
		var nameOrUdidArg = new Argument<string>("name-or-udid") { Description = "Simulator name or UDID to erase" };
		var eraseCommand = new Command("erase", "Erase (reset) a simulator device to factory state") { nameOrUdidArg };
		eraseCommand.SetAction((ParseResult parseResult) =>
		{
			var formatter = Program.GetFormatter(parseResult);

			if (!PlatformDetector.IsMacOS)
			{
				formatter.WriteError(new MauiToolException(ErrorCodes.PlatformNotSupported, "Simulators are only available on macOS."));
				return 1;
			}

			var appleProvider = Program.AppleProvider;
			var target = parseResult.GetValue(nameOrUdidArg)!;

			// Probe state first so we can distinguish "not found" from "wrong state",
			// which simctl's bool return value otherwise conflates.
			var sims = appleProvider.GetSimulators();
			var match = sims.FirstOrDefault(s =>
				string.Equals(s.Udid, target, StringComparison.OrdinalIgnoreCase) ||
				string.Equals(s.Name, target, StringComparison.Ordinal));
			if (match is null)
			{
				var notFoundEx = new MauiToolException(
					ErrorCodes.AppleSimulatorNotFound,
					$"No simulator found matching '{target}'. List simulators with 'maui apple simulator list'.");
				formatter.WriteError(notFoundEx);
				return 1;
			}
			if (match.IsBooted)
			{
				var bootedEx = new MauiToolException(
					ErrorCodes.AppleSimulatorEraseFailed,
					$"Simulator '{match.Name}' (UDID: {match.Udid}) is booted; shut it down first with 'maui apple simulator stop {match.Udid}'.");
				formatter.WriteError(bootedEx);
				return 1;
			}

			var erased = appleProvider.EraseSimulator(target);

			if (!erased)
			{
				var ex = new MauiToolException(ErrorCodes.AppleSimulatorEraseFailed, $"Failed to erase simulator '{target}' (UDID: {match.Udid}). Check 'xcrun simctl' is available and the simulator state is 'Shutdown'.");
				formatter.WriteError(ex);
				return 1;
			}

			var useJson = parseResult.GetValue(GlobalOptions.JsonOption);
			if (useJson)
				formatter.Write(new SimulatorEraseResult { Target = target, Erased = true });
			else
				formatter.WriteSuccess($"Simulator '{target}' erased.");
			return 0;
		});

		return eraseCommand;
	}
}
