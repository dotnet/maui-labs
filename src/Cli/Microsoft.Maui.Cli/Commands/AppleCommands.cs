// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using Microsoft.Maui.Cli.Errors;
using System.CommandLine.Parsing;
using Microsoft.Maui.Cli.Output;
using Microsoft.Maui.Cli.Providers.Apple;
using Microsoft.Maui.Cli.Utils;
using Xamarin.MacDev;

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
		simCommand.Add(CreateSimulatorInstallCommand());
		simCommand.Add(CreateSimulatorUninstallCommand());
		simCommand.Add(CreateSimulatorLaunchCommand());
		simCommand.Add(CreateSimulatorTerminateCommand());
		simCommand.Add(CreateSimulatorGetAppContainerCommand());
		simCommand.Add(CreateSimulatorPrivacyCommand());
		simCommand.Add(CreateSimulatorAppearanceCommand());
		simCommand.Add(CreateSimulatorStatusBarCommand());
		simCommand.Add(CreateSimulatorOpenUrlCommand());
		simCommand.Add(CreateSimulatorPushCommand());
		simCommand.Add(CreateSimulatorLocationCommand());
		simCommand.Add(CreateSimulatorAddMediaCommand());
		simCommand.Add(CreateSimulatorScreenshotCommand());
		simCommand.Add(CreateSimulatorRecordVideoCommand());
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

	static Command CreateSimulatorInstallCommand()
	{
		var udidArg = new Argument<string>("udid") { Description = "Simulator UDID" };
		var appPathArg = new Argument<string>("app-bundle-path") { Description = "Path to the .app bundle to install" };

		var installCommand = new Command("install", "Install an app bundle on a simulator") { udidArg, appPathArg };
		installCommand.SetAction((ParseResult parseResult) =>
		{
			var formatter = Program.GetFormatter(parseResult);

			if (!PlatformDetector.IsMacOS)
			{
				formatter.WriteError(new MauiToolException(ErrorCodes.PlatformNotSupported, "Simulators are only available on macOS."));
				return 1;
			}

			var appleProvider = Program.AppleProvider;
			var udid = parseResult.GetValue(udidArg)!;
			var appPath = parseResult.GetValue(appPathArg)!;

			if (!appPath.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
			{
				formatter.WriteError(new MauiToolException(ErrorCodes.InvalidArgument, $"Path must point to a .app bundle directory, got: '{appPath}'."));
				return 1;
			}

			if (!Directory.Exists(appPath))
			{
				formatter.WriteError(new MauiToolException(ErrorCodes.InvalidArgument, $"App bundle not found at '{appPath}'."));
				return 1;
			}

			if (!ValidateSimulator(appleProvider, udid, formatter))
				return 1;

			var success = appleProvider.InstallApp(udid, appPath);

			if (!success)
			{
				formatter.WriteError(new MauiToolException(ErrorCodes.AppleSimulatorInstallFailed, $"Failed to install app on simulator '{udid}'. Ensure the simulator is booted."));
				return 1;
			}

			var useJson = parseResult.GetValue(GlobalOptions.JsonOption);
			if (useJson)
				formatter.Write(new SimulatorAppResult { Udid = udid, AppPath = appPath, Action = "installed", Success = true });
			else
				formatter.WriteSuccess($"App installed on simulator '{udid}'.");
			return 0;
		});

		return installCommand;
	}

	static Command CreateSimulatorUninstallCommand()
	{
		var udidArg = new Argument<string>("udid") { Description = "Simulator UDID" };
		var bundleIdArg = new Argument<string>("bundle-identifier") { Description = "App bundle identifier (e.g. com.example.MyApp)" };

		var uninstallCommand = new Command("uninstall", "Uninstall an app from a simulator") { udidArg, bundleIdArg };
		uninstallCommand.SetAction((ParseResult parseResult) =>
		{
			var formatter = Program.GetFormatter(parseResult);

			if (!PlatformDetector.IsMacOS)
			{
				formatter.WriteError(new MauiToolException(ErrorCodes.PlatformNotSupported, "Simulators are only available on macOS."));
				return 1;
			}

			var appleProvider = Program.AppleProvider;
			var udid = parseResult.GetValue(udidArg)!;
			var bundleId = parseResult.GetValue(bundleIdArg)!;

			if (!ValidateSimulator(appleProvider, udid, formatter))
				return 1;

			var success = appleProvider.UninstallApp(udid, bundleId);

			if (!success)
			{
				formatter.WriteError(new MauiToolException(ErrorCodes.AppleSimulatorUninstallFailed, $"Failed to uninstall '{bundleId}' from simulator '{udid}'."));
				return 1;
			}

			var useJson = parseResult.GetValue(GlobalOptions.JsonOption);
			if (useJson)
				formatter.Write(new SimulatorAppResult { Udid = udid, BundleIdentifier = bundleId, Action = "uninstalled", Success = true });
			else
				formatter.WriteSuccess($"App '{bundleId}' uninstalled from simulator '{udid}'.");
			return 0;
		});

		return uninstallCommand;
	}

	static Command CreateSimulatorLaunchCommand()
	{
		var udidArg = new Argument<string>("udid") { Description = "Simulator UDID" };
		var bundleIdArg = new Argument<string>("bundle-identifier") { Description = "App bundle identifier (e.g. com.example.MyApp)" };
		var extraArgsOption = new Option<string[]>("--args") { Description = "Extra arguments to pass to the app", AllowMultipleArgumentsPerToken = true };

		var launchCommand = new Command("launch", "Launch an app on a simulator") { udidArg, bundleIdArg, extraArgsOption };
		launchCommand.SetAction((ParseResult parseResult) =>
		{
			var formatter = Program.GetFormatter(parseResult);

			if (!PlatformDetector.IsMacOS)
			{
				formatter.WriteError(new MauiToolException(ErrorCodes.PlatformNotSupported, "Simulators are only available on macOS."));
				return 1;
			}

			var appleProvider = Program.AppleProvider;
			var udid = parseResult.GetValue(udidArg)!;
			var bundleId = parseResult.GetValue(bundleIdArg)!;
			var extraArgs = parseResult.GetValue(extraArgsOption) ?? Array.Empty<string>();

			if (!ValidateSimulator(appleProvider, udid, formatter))
				return 1;

			var success = appleProvider.LaunchApp(udid, bundleId, extraArgs);

			if (!success)
			{
				formatter.WriteError(new MauiToolException(ErrorCodes.AppleSimulatorLaunchFailed, $"Failed to launch '{bundleId}' on simulator '{udid}'. Ensure the app is installed and the simulator is booted."));
				return 1;
			}

			var useJson = parseResult.GetValue(GlobalOptions.JsonOption);
			if (useJson)
				formatter.Write(new SimulatorAppResult { Udid = udid, BundleIdentifier = bundleId, Action = "launched", Success = true });
			else
				formatter.WriteSuccess($"App '{bundleId}' launched on simulator '{udid}'.");
			return 0;
		});

		return launchCommand;
	}

	static Command CreateSimulatorTerminateCommand()
	{
		var udidArg = new Argument<string>("udid") { Description = "Simulator UDID" };
		var bundleIdArg = new Argument<string>("bundle-identifier") { Description = "App bundle identifier (e.g. com.example.MyApp)" };

		var terminateCommand = new Command("terminate", "Terminate a running app on a simulator") { udidArg, bundleIdArg };
		terminateCommand.SetAction((ParseResult parseResult) =>
		{
			var formatter = Program.GetFormatter(parseResult);

			if (!PlatformDetector.IsMacOS)
			{
				formatter.WriteError(new MauiToolException(ErrorCodes.PlatformNotSupported, "Simulators are only available on macOS."));
				return 1;
			}

			var appleProvider = Program.AppleProvider;
			var udid = parseResult.GetValue(udidArg)!;
			var bundleId = parseResult.GetValue(bundleIdArg)!;

			if (!ValidateSimulator(appleProvider, udid, formatter))
				return 1;

			var success = appleProvider.TerminateApp(udid, bundleId);

			if (!success)
			{
				formatter.WriteError(new MauiToolException(ErrorCodes.AppleSimulatorTerminateFailed, $"Failed to terminate '{bundleId}' on simulator '{udid}'."));
				return 1;
			}

			var useJson = parseResult.GetValue(GlobalOptions.JsonOption);
			if (useJson)
				formatter.Write(new SimulatorAppResult { Udid = udid, BundleIdentifier = bundleId, Action = "terminated", Success = true });
			else
				formatter.WriteSuccess($"App '{bundleId}' terminated on simulator '{udid}'.");
			return 0;
		});

		return terminateCommand;
	}

	static Command CreateSimulatorGetAppContainerCommand()
	{
		var udidArg = new Argument<string>("udid") { Description = "Simulator UDID" };
		var bundleIdArg = new Argument<string>("bundle-identifier") { Description = "App bundle identifier (e.g. com.example.MyApp)" };
		var containerTypeOption = new Option<string?>("--type") { Description = "Container type: 'app' (default), 'data', 'groups', or a specific group identifier" };

		var containerCommand = new Command("get-app-container", "Get the container path for an installed app") { udidArg, bundleIdArg, containerTypeOption };
		containerCommand.SetAction((ParseResult parseResult) =>
		{
			var formatter = Program.GetFormatter(parseResult);

			if (!PlatformDetector.IsMacOS)
			{
				formatter.WriteError(new MauiToolException(ErrorCodes.PlatformNotSupported, "Simulators are only available on macOS."));
				return 1;
			}

			var appleProvider = Program.AppleProvider;
			var udid = parseResult.GetValue(udidArg)!;
			var bundleId = parseResult.GetValue(bundleIdArg)!;
			var containerType = parseResult.GetValue(containerTypeOption);

			if (!ValidateSimulator(appleProvider, udid, formatter))
				return 1;

			var path = appleProvider.GetAppContainer(udid, bundleId, containerType);

			if (path is null)
			{
				formatter.WriteError(new MauiToolException(ErrorCodes.AppleSimulatorGetContainerFailed, $"Failed to get container for '{bundleId}' on simulator '{udid}'. Ensure the app is installed."));
				return 1;
			}

			var useJson = parseResult.GetValue(GlobalOptions.JsonOption);
			if (useJson)
				formatter.Write(new SimulatorAppContainerResult { Udid = udid, BundleIdentifier = bundleId, ContainerType = containerType, Path = path });
			else
				formatter.WriteSuccess(path);
			return 0;
		});

		return containerCommand;
	}

	static Command CreateSimulatorPrivacyCommand()
	{
		var privacyCommand = new Command("privacy", "Grant, revoke, or reset simulator privacy permissions");

		foreach (var action in new[] { "grant", "revoke", "reset" })
		{
			var udidArg = new Argument<string>("udid") { Description = "Simulator UDID" };
			var permissionArg = new Argument<string>("permission") { Description = $"Privacy service ({SimulatorEnumParsing.PrivacyPermissionNames})" };
			var bundleIdOption = new Option<string?>("--bundle-id") { Description = "App bundle identifier to scope the change (applies to all apps if omitted)" };

			var description = action switch
			{
				"grant" => "Grant a privacy permission (no dialog will appear)",
				"revoke" => "Revoke a privacy permission",
				_ => "Reset a privacy permission (the app will be prompted again)",
			};

			var actionCommand = new Command(action, description) { udidArg, permissionArg, bundleIdOption };
			actionCommand.SetAction((ParseResult parseResult) =>
			{
				var formatter = Program.GetFormatter(parseResult);

				if (!PlatformDetector.IsMacOS)
				{
					formatter.WriteError(new MauiToolException(ErrorCodes.PlatformNotSupported, "Simulators are only available on macOS."));
					return 1;
				}

				var appleProvider = Program.AppleProvider;
				var udid = parseResult.GetValue(udidArg)!;
				var permissionText = parseResult.GetValue(permissionArg)!;
				var bundleId = parseResult.GetValue(bundleIdOption);

				if (!SimulatorEnumParsing.TryParsePrivacyPermission(permissionText, out var permission))
				{
					formatter.WriteError(new MauiToolException(ErrorCodes.InvalidArgument, $"Unknown privacy service '{permissionText}'. Valid services: {SimulatorEnumParsing.PrivacyPermissionNames}."));
					return 1;
				}

				if (!ValidateSimulator(appleProvider, udid, formatter))
					return 1;

				var success = appleProvider.SetPrivacy(action, udid, permission, bundleId);
				if (!success)
				{
					formatter.WriteError(new MauiToolException(ErrorCodes.AppleSimulatorPrivacyFailed, $"Failed to {action} '{permissionText}' on simulator '{udid}'. Ensure the simulator is booted."));
					return 1;
				}

				var useJson = parseResult.GetValue(GlobalOptions.JsonOption);
				if (useJson)
					formatter.Write(new SimulatorPrivacyResult { Udid = udid, Action = action, Service = SimulatorPrivacy.ToSimctlServiceName(permission), BundleIdentifier = bundleId, Success = true });
				else
					formatter.WriteSuccess($"Permission '{permissionText}' {action}ed on simulator '{udid}'" + (bundleId != null ? $" for {bundleId}." : "."));
				return 0;
			});

			privacyCommand.Add(actionCommand);
		}

		return privacyCommand;
	}

	static Command CreateSimulatorAppearanceCommand()
	{
		var appearanceCommand = new Command("appearance", "Get or set the simulator UI appearance (light/dark)");

		// get <udid>
		var getUdidArg = new Argument<string>("udid") { Description = "Simulator UDID" };
		var getCommand = new Command("get", "Read the current appearance") { getUdidArg };
		getCommand.SetAction((ParseResult parseResult) =>
		{
			var formatter = Program.GetFormatter(parseResult);

			if (!PlatformDetector.IsMacOS)
			{
				formatter.WriteError(new MauiToolException(ErrorCodes.PlatformNotSupported, "Simulators are only available on macOS."));
				return 1;
			}

			var appleProvider = Program.AppleProvider;
			var udid = parseResult.GetValue(getUdidArg)!;
			var useJson = parseResult.GetValue(GlobalOptions.JsonOption);

			if (!ValidateSimulator(appleProvider, udid, formatter))
				return 1;

			var current = appleProvider.GetAppearance(udid);
			if (current is null)
			{
				formatter.WriteError(new MauiToolException(ErrorCodes.AppleSimulatorAppearanceFailed, $"Failed to read appearance for simulator '{udid}'. Ensure the simulator is booted."));
				return 1;
			}

			var value = current.Value == SimulatorAppearance.Dark ? "dark" : "light";
			if (useJson)
				formatter.Write(new SimulatorAppearanceResult { Udid = udid, Appearance = value, Action = "get" });
			else
				formatter.WriteSuccess(value);
			return 0;
		});
		appearanceCommand.Add(getCommand);

		// light <udid> / dark <udid>
		foreach (var mode in new[] { "light", "dark" })
		{
			var setUdidArg = new Argument<string>("udid") { Description = "Simulator UDID" };
			var setCommand = new Command(mode, $"Set appearance to {mode}") { setUdidArg };
			setCommand.SetAction((ParseResult parseResult) =>
			{
				var formatter = Program.GetFormatter(parseResult);

				if (!PlatformDetector.IsMacOS)
				{
					formatter.WriteError(new MauiToolException(ErrorCodes.PlatformNotSupported, "Simulators are only available on macOS."));
					return 1;
				}

				var appleProvider = Program.AppleProvider;
				var udid = parseResult.GetValue(setUdidArg)!;
				var useJson = parseResult.GetValue(GlobalOptions.JsonOption);

				if (!ValidateSimulator(appleProvider, udid, formatter))
					return 1;

				var appearance = mode == "dark" ? SimulatorAppearance.Dark : SimulatorAppearance.Light;
				var success = appleProvider.SetAppearance(udid, appearance);
				if (!success)
				{
					formatter.WriteError(new MauiToolException(ErrorCodes.AppleSimulatorAppearanceFailed, $"Failed to set appearance '{mode}' on simulator '{udid}'. Ensure the simulator is booted."));
					return 1;
				}

				if (useJson)
					formatter.Write(new SimulatorAppearanceResult { Udid = udid, Appearance = mode, Action = "set" });
				else
					formatter.WriteSuccess($"Appearance set to '{mode}' on simulator '{udid}'.");
				return 0;
			});
			appearanceCommand.Add(setCommand);
		}

		return appearanceCommand;
	}

	static Command CreateSimulatorStatusBarCommand()
	{
		var statusBarCommand = new Command("status-bar", "Override or clear simulator status-bar values");

		// override
		var ovUdidArg = new Argument<string>("udid") { Description = "Simulator UDID" };
		var timeOption = new Option<string?>("--time") { Description = "Time string to display (e.g. '9:41')" };
		var batteryLevelOption = new Option<int?>("--battery-level") { Description = "Battery level percentage (0-100)" };
		var batteryStateOption = new Option<string?>("--battery-state") { Description = "Battery state: charging, charged, discharging" };
		var dataNetworkOption = new Option<string?>("--data-network") { Description = "Data network: wifi, 3g, 4g, lte, lte-a, lte+, 5g, 5g+, 5g-uc, 5g-a" };
		var cellularBarsOption = new Option<int?>("--cellular-bars") { Description = "Number of cellular signal bars (0-4)" };
		var wifiBarsOption = new Option<int?>("--wifi-bars") { Description = "Number of Wi-Fi signal bars (0-3)" };
		var operatorNameOption = new Option<string?>("--operator-name") { Description = "Carrier/operator name to display" };

		var overrideCommand = new Command("override", "Override status-bar values")
		{
			ovUdidArg, timeOption, batteryLevelOption, batteryStateOption, dataNetworkOption, cellularBarsOption, wifiBarsOption, operatorNameOption
		};
		overrideCommand.SetAction((ParseResult parseResult) =>
		{
			var formatter = Program.GetFormatter(parseResult);

			if (!PlatformDetector.IsMacOS)
			{
				formatter.WriteError(new MauiToolException(ErrorCodes.PlatformNotSupported, "Simulators are only available on macOS."));
				return 1;
			}

			var appleProvider = Program.AppleProvider;
			var udid = parseResult.GetValue(ovUdidArg)!;

			var time = parseResult.GetValue(timeOption);
			var batteryLevel = parseResult.GetValue(batteryLevelOption);
			var cellularBars = parseResult.GetValue(cellularBarsOption);
			var wifiBars = parseResult.GetValue(wifiBarsOption);
			var operatorName = parseResult.GetValue(operatorNameOption);

			SimulatorBatteryState? batteryState = null;
			var batteryStateText = parseResult.GetValue(batteryStateOption);
			if (batteryStateText is not null)
			{
				if (!SimulatorEnumParsing.TryParseBatteryState(batteryStateText, out var parsedState))
				{
					formatter.WriteError(new MauiToolException(ErrorCodes.InvalidArgument, $"Unknown battery state '{batteryStateText}'. Use charging, charged, or discharging."));
					return 1;
				}
				batteryState = parsedState;
			}

			SimulatorDataNetwork? dataNetwork = null;
			var dataNetworkText = parseResult.GetValue(dataNetworkOption);
			if (dataNetworkText is not null)
			{
				if (!SimulatorEnumParsing.TryParseDataNetwork(dataNetworkText, out var parsedNetwork))
				{
					formatter.WriteError(new MauiToolException(ErrorCodes.InvalidArgument, $"Unknown data network '{dataNetworkText}'. Use wifi, 3g, 4g, lte, lte-a, lte+, 5g, 5g+, 5g-uc, or 5g-a."));
					return 1;
				}
				dataNetwork = parsedNetwork;
			}

			if (time is null && !batteryLevel.HasValue && !batteryState.HasValue && !dataNetwork.HasValue &&
				!cellularBars.HasValue && !wifiBars.HasValue && operatorName is null)
			{
				formatter.WriteError(new MauiToolException(ErrorCodes.InvalidArgument, "At least one status-bar override option must be provided (e.g. --time, --battery-level, --data-network)."));
				return 1;
			}

			if (!ValidateSimulator(appleProvider, udid, formatter))
				return 1;

			var overrides = new StatusBarOverrides(time, batteryLevel, batteryState, dataNetwork, cellularBars, wifiBars, operatorName);
			var success = appleProvider.OverrideStatusBar(udid, overrides);
			if (!success)
			{
				formatter.WriteError(new MauiToolException(ErrorCodes.AppleSimulatorStatusBarFailed, $"Failed to override status bar on simulator '{udid}'. Ensure the simulator is booted."));
				return 1;
			}

			var useJson = parseResult.GetValue(GlobalOptions.JsonOption);
			if (useJson)
				formatter.Write(new SimulatorStatusBarResult { Udid = udid, Action = "override", Success = true });
			else
				formatter.WriteSuccess($"Status bar overridden on simulator '{udid}'.");
			return 0;
		});
		statusBarCommand.Add(overrideCommand);

		// clear
		var clearUdidArg = new Argument<string>("udid") { Description = "Simulator UDID" };
		var clearCommand = new Command("clear", "Clear all status-bar overrides") { clearUdidArg };
		clearCommand.SetAction((ParseResult parseResult) =>
		{
			var formatter = Program.GetFormatter(parseResult);

			if (!PlatformDetector.IsMacOS)
			{
				formatter.WriteError(new MauiToolException(ErrorCodes.PlatformNotSupported, "Simulators are only available on macOS."));
				return 1;
			}

			var appleProvider = Program.AppleProvider;
			var udid = parseResult.GetValue(clearUdidArg)!;

			if (!ValidateSimulator(appleProvider, udid, formatter))
				return 1;

			var success = appleProvider.ClearStatusBar(udid);
			if (!success)
			{
				formatter.WriteError(new MauiToolException(ErrorCodes.AppleSimulatorStatusBarFailed, $"Failed to clear status bar on simulator '{udid}'. Ensure the simulator is booted."));
				return 1;
			}

			var useJson = parseResult.GetValue(GlobalOptions.JsonOption);
			if (useJson)
				formatter.Write(new SimulatorStatusBarResult { Udid = udid, Action = "clear", Success = true });
			else
				formatter.WriteSuccess($"Status bar cleared on simulator '{udid}'.");
			return 0;
		});
		statusBarCommand.Add(clearCommand);

		return statusBarCommand;
	}

	static Command CreateSimulatorOpenUrlCommand()
	{
		var udidArg = new Argument<string>("udid") { Description = "Simulator UDID" };
		var urlArg = new Argument<string>("url") { Description = "URL to open (deep link or web URL)" };

		var openUrlCommand = new Command("openurl", "Open a URL on a simulator") { udidArg, urlArg };
		openUrlCommand.SetAction((ParseResult parseResult) =>
		{
			var formatter = Program.GetFormatter(parseResult);

			if (!PlatformDetector.IsMacOS)
			{
				formatter.WriteError(new MauiToolException(ErrorCodes.PlatformNotSupported, "Simulators are only available on macOS."));
				return 1;
			}

			var appleProvider = Program.AppleProvider;
			var udid = parseResult.GetValue(udidArg)!;
			var url = parseResult.GetValue(urlArg)!;

			if (!ValidateSimulator(appleProvider, udid, formatter))
				return 1;

			var success = appleProvider.OpenUrl(udid, url);
			if (!success)
			{
				formatter.WriteError(new MauiToolException(ErrorCodes.AppleSimulatorOpenUrlFailed, $"Failed to open URL on simulator '{udid}'. Ensure the simulator is booted."));
				return 1;
			}

			var useJson = parseResult.GetValue(GlobalOptions.JsonOption);
			if (useJson)
				formatter.Write(new SimulatorOpenUrlResult { Udid = udid, Url = url, Success = true });
			else
				formatter.WriteSuccess($"Opened URL on simulator '{udid}'.");
			return 0;
		});

		return openUrlCommand;
	}

	static Command CreateSimulatorPushCommand()
	{
		var udidArg = new Argument<string>("udid") { Description = "Simulator UDID" };
		var bundleIdArg = new Argument<string>("bundle-identifier") { Description = "Target app bundle identifier (e.g. com.example.MyApp)" };
		var payloadArg = new Argument<string>("payload") { Description = "APNS payload as inline JSON (starting with '{') or a path to a .apns/.json file" };

		var pushCommand = new Command("push", "Send a push notification to a simulator") { udidArg, bundleIdArg, payloadArg };
		pushCommand.SetAction((ParseResult parseResult) =>
		{
			var formatter = Program.GetFormatter(parseResult);

			if (!PlatformDetector.IsMacOS)
			{
				formatter.WriteError(new MauiToolException(ErrorCodes.PlatformNotSupported, "Simulators are only available on macOS."));
				return 1;
			}

			var appleProvider = Program.AppleProvider;
			var udid = parseResult.GetValue(udidArg)!;
			var bundleId = parseResult.GetValue(bundleIdArg)!;
			var payload = parseResult.GetValue(payloadArg)!;

			// If it isn't inline JSON it must be an existing file.
			var isInlineJson = payload.TrimStart().StartsWith("{", StringComparison.Ordinal);
			if (!isInlineJson && !File.Exists(payload))
			{
				formatter.WriteError(new MauiToolException(ErrorCodes.InvalidArgument, $"Payload '{payload}' is not inline JSON (must start with '{{') and no file exists at that path."));
				return 1;
			}

			if (!ValidateSimulator(appleProvider, udid, formatter))
				return 1;

			var success = appleProvider.PushNotification(udid, bundleId, payload);
			if (!success)
			{
				formatter.WriteError(new MauiToolException(ErrorCodes.AppleSimulatorPushFailed, $"Failed to send push notification to '{bundleId}' on simulator '{udid}'. Ensure the simulator is booted and the payload is valid."));
				return 1;
			}

			var useJson = parseResult.GetValue(GlobalOptions.JsonOption);
			if (useJson)
				formatter.Write(new SimulatorPushResult { Udid = udid, BundleIdentifier = bundleId, Success = true });
			else
				formatter.WriteSuccess($"Push notification sent to '{bundleId}' on simulator '{udid}'.");
			return 0;
		});

		return pushCommand;
	}

	static Command CreateSimulatorLocationCommand()
	{
		var locationCommand = new Command("location", "Set, clear, or replay the simulated GPS location");

		// set
		var setUdidArg = new Argument<string>("udid") { Description = "Simulator UDID" };
		var latArg = new Argument<string>("latitude") { Description = "Latitude in decimal degrees" };
		var lngArg = new Argument<string>("longitude") { Description = "Longitude in decimal degrees" };
		var setCommand = new Command("set", "Set the simulated GPS location") { setUdidArg, latArg, lngArg };
		setCommand.SetAction((ParseResult parseResult) =>
		{
			var formatter = Program.GetFormatter(parseResult);

			if (!PlatformDetector.IsMacOS)
			{
				formatter.WriteError(new MauiToolException(ErrorCodes.PlatformNotSupported, "Simulators are only available on macOS."));
				return 1;
			}

			var appleProvider = Program.AppleProvider;
			var udid = parseResult.GetValue(setUdidArg)!;
			var latText = parseResult.GetValue(latArg)!;
			var lngText = parseResult.GetValue(lngArg)!;

			if (!double.TryParse(latText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lat))
			{
				formatter.WriteError(new MauiToolException(ErrorCodes.InvalidArgument, $"Invalid latitude value '{latText}'. Provide a decimal number (e.g. 37.3349)."));
				return 1;
			}
			if (!double.TryParse(lngText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lng))
			{
				formatter.WriteError(new MauiToolException(ErrorCodes.InvalidArgument, $"Invalid longitude value '{lngText}'. Provide a decimal number (e.g. -122.009)."));
				return 1;
			}

			if (!ValidateSimulator(appleProvider, udid, formatter))
				return 1;

			var success = appleProvider.SetLocation(udid, lat, lng);
			if (!success)
			{
				formatter.WriteError(new MauiToolException(ErrorCodes.AppleSimulatorLocationFailed, $"Failed to set location on simulator '{udid}'. Ensure the simulator is booted."));
				return 1;
			}

			var useJson = parseResult.GetValue(GlobalOptions.JsonOption);
			if (useJson)
				formatter.Write(new SimulatorLocationResult { Udid = udid, Action = "set", Latitude = lat, Longitude = lng, Success = true });
			else
				formatter.WriteSuccess($"Location set to {lat},{lng} on simulator '{udid}'.");
			return 0;
		});
		locationCommand.Add(setCommand);

		// clear
		var clearUdidArg = new Argument<string>("udid") { Description = "Simulator UDID" };
		var clearCommand = new Command("clear", "Clear the simulated GPS location") { clearUdidArg };
		clearCommand.SetAction((ParseResult parseResult) =>
		{
			var formatter = Program.GetFormatter(parseResult);

			if (!PlatformDetector.IsMacOS)
			{
				formatter.WriteError(new MauiToolException(ErrorCodes.PlatformNotSupported, "Simulators are only available on macOS."));
				return 1;
			}

			var appleProvider = Program.AppleProvider;
			var udid = parseResult.GetValue(clearUdidArg)!;

			if (!ValidateSimulator(appleProvider, udid, formatter))
				return 1;

			var success = appleProvider.ClearLocation(udid);
			if (!success)
			{
				formatter.WriteError(new MauiToolException(ErrorCodes.AppleSimulatorLocationFailed, $"Failed to clear location on simulator '{udid}'. Ensure the simulator is booted."));
				return 1;
			}

			var useJson = parseResult.GetValue(GlobalOptions.JsonOption);
			if (useJson)
				formatter.Write(new SimulatorLocationResult { Udid = udid, Action = "clear", Success = true });
			else
				formatter.WriteSuccess($"Location cleared on simulator '{udid}'.");
			return 0;
		});
		locationCommand.Add(clearCommand);

		// run
		var runUdidArg = new Argument<string>("udid") { Description = "Simulator UDID" };
		var gpxArg = new Argument<string>("gpx-path") { Description = "Path to a GPX file describing the route to replay" };
		var runCommand = new Command("run", "Replay a GPX route on the simulator") { runUdidArg, gpxArg };
		runCommand.SetAction((ParseResult parseResult) =>
		{
			var formatter = Program.GetFormatter(parseResult);

			if (!PlatformDetector.IsMacOS)
			{
				formatter.WriteError(new MauiToolException(ErrorCodes.PlatformNotSupported, "Simulators are only available on macOS."));
				return 1;
			}

			var appleProvider = Program.AppleProvider;
			var udid = parseResult.GetValue(runUdidArg)!;
			var gpxPath = parseResult.GetValue(gpxArg)!;

			if (!File.Exists(gpxPath))
			{
				formatter.WriteError(new MauiToolException(ErrorCodes.InvalidArgument, $"GPX file not found at '{gpxPath}'."));
				return 1;
			}

			if (!ValidateSimulator(appleProvider, udid, formatter))
				return 1;

			var success = appleProvider.RunLocation(udid, gpxPath);
			if (!success)
			{
				formatter.WriteError(new MauiToolException(ErrorCodes.AppleSimulatorLocationFailed, $"Failed to run GPX route on simulator '{udid}'. Ensure the simulator is booted."));
				return 1;
			}

			var useJson = parseResult.GetValue(GlobalOptions.JsonOption);
			if (useJson)
				formatter.Write(new SimulatorLocationResult { Udid = udid, Action = "run", GpxPath = gpxPath, Success = true });
			else
				formatter.WriteSuccess($"GPX route '{gpxPath}' running on simulator '{udid}'.");
			return 0;
		});
		locationCommand.Add(runCommand);

		return locationCommand;
	}

	static Command CreateSimulatorAddMediaCommand()
	{
		var udidArg = new Argument<string>("udid") { Description = "Simulator UDID" };
		var pathsArg = new Argument<string[]>("paths") { Description = "One or more media file paths (photos or videos)", Arity = ArgumentArity.OneOrMore };

		var addMediaCommand = new Command("add-media", "Add photos or videos to a simulator's media library") { udidArg, pathsArg };
		addMediaCommand.SetAction((ParseResult parseResult) =>
		{
			var formatter = Program.GetFormatter(parseResult);

			if (!PlatformDetector.IsMacOS)
			{
				formatter.WriteError(new MauiToolException(ErrorCodes.PlatformNotSupported, "Simulators are only available on macOS."));
				return 1;
			}

			var appleProvider = Program.AppleProvider;
			var udid = parseResult.GetValue(udidArg)!;
			var paths = parseResult.GetValue(pathsArg) ?? Array.Empty<string>();

			if (paths.Length == 0)
			{
				formatter.WriteError(new MauiToolException(ErrorCodes.InvalidArgument, "At least one media file path must be provided."));
				return 1;
			}

			var missing = paths.Where(p => !File.Exists(p)).ToArray();
			if (missing.Length > 0)
			{
				formatter.WriteError(new MauiToolException(ErrorCodes.InvalidArgument, $"Media file(s) not found: {string.Join(", ", missing)}."));
				return 1;
			}

			if (!ValidateSimulator(appleProvider, udid, formatter))
				return 1;

			var success = appleProvider.AddMedia(udid, paths);
			if (!success)
			{
				formatter.WriteError(new MauiToolException(ErrorCodes.AppleSimulatorAddMediaFailed, $"Failed to add media to simulator '{udid}'. Ensure the simulator is booted."));
				return 1;
			}

			var useJson = parseResult.GetValue(GlobalOptions.JsonOption);
			if (useJson)
				formatter.Write(new SimulatorAddMediaResult { Udid = udid, Paths = paths, Count = paths.Length, Success = true });
			else
				formatter.WriteSuccess($"Added {paths.Length} media file(s) to simulator '{udid}'.");
			return 0;
		});

		return addMediaCommand;
	}

	static Command CreateSimulatorScreenshotCommand()
	{
		var udidArg = new Argument<string>("udid") { Description = "Simulator UDID" };
		var outputArg = new Argument<string>("output-path") { Description = "Path to write the screenshot to" };
		var formatOption = new Option<string>("--format") { Description = "Image format: png (default), jpeg, tiff, bmp", DefaultValueFactory = _ => "png" };

		var screenshotCommand = new Command("screenshot", "Capture a screenshot from a simulator") { udidArg, outputArg, formatOption };
		screenshotCommand.SetAction((ParseResult parseResult) =>
		{
			var formatter = Program.GetFormatter(parseResult);

			if (!PlatformDetector.IsMacOS)
			{
				formatter.WriteError(new MauiToolException(ErrorCodes.PlatformNotSupported, "Simulators are only available on macOS."));
				return 1;
			}

			var appleProvider = Program.AppleProvider;
			var udid = parseResult.GetValue(udidArg)!;
			var outputPath = parseResult.GetValue(outputArg)!;
			var formatText = parseResult.GetValue(formatOption)!;

			if (!SimulatorEnumParsing.TryParseScreenshotFormat(formatText, out var format))
			{
				formatter.WriteError(new MauiToolException(ErrorCodes.InvalidArgument, $"Unknown screenshot format '{formatText}'. Use png, jpeg, tiff, or bmp."));
				return 1;
			}

			if (!ValidateSimulator(appleProvider, udid, formatter))
				return 1;

			var success = appleProvider.Screenshot(udid, outputPath, format);
			if (!success)
			{
				formatter.WriteError(new MauiToolException(ErrorCodes.AppleSimulatorScreenshotFailed, $"Failed to capture screenshot from simulator '{udid}'. Ensure the simulator is booted."));
				return 1;
			}

			var useJson = parseResult.GetValue(GlobalOptions.JsonOption);
			if (useJson)
				formatter.Write(new SimulatorScreenshotResult { Udid = udid, OutputPath = outputPath, Format = SimulatorScreenCapture.ToSimctlFormatName(format), Success = true });
			else
				formatter.WriteSuccess($"Screenshot saved to '{outputPath}'.");
			return 0;
		});

		return screenshotCommand;
	}

	static Command CreateSimulatorRecordVideoCommand()
	{
		var udidArg = new Argument<string>("udid") { Description = "Simulator UDID" };
		var outputArg = new Argument<string>("output-path") { Description = "Path to write the recorded video to" };
		var formatOption = new Option<string?>("--format") { Description = "Video format: mp4 (default), h264, fmp4, gif" };
		var forceOption = new Option<bool>("--force") { Description = "Overwrite the output file if it already exists" };

		// Recording streams until interrupted (Ctrl-C), mirroring the upstream
		// "dispose to stop" model. This keeps the CLI process alive for the
		// duration of the recording without a separate session-tracking store.
		var recordCommand = new Command("record-video", "Record a video from a simulator until interrupted (Ctrl-C)") { udidArg, outputArg, formatOption, forceOption };
		recordCommand.SetAction((ParseResult parseResult) =>
		{
			var formatter = Program.GetFormatter(parseResult);

			if (!PlatformDetector.IsMacOS)
			{
				formatter.WriteError(new MauiToolException(ErrorCodes.PlatformNotSupported, "Simulators are only available on macOS."));
				return 1;
			}

			var appleProvider = Program.AppleProvider;
			var udid = parseResult.GetValue(udidArg)!;
			var outputPath = parseResult.GetValue(outputArg)!;
			var formatText = parseResult.GetValue(formatOption);
			var force = parseResult.GetValue(forceOption);

			VideoRecordingFormat? format = null;
			if (formatText is not null)
			{
				if (!SimulatorEnumParsing.TryParseVideoFormat(formatText, out var parsedFormat))
				{
					formatter.WriteError(new MauiToolException(ErrorCodes.InvalidArgument, $"Unknown video format '{formatText}'. Use mp4, h264, fmp4, or gif."));
					return 1;
				}
				format = parsedFormat;
			}

			if (!ValidateSimulator(appleProvider, udid, formatter))
				return 1;

			var options = new RecordingOptions { Format = format, Force = force };
			var session = appleProvider.StartRecording(udid, outputPath, options);
			if (session is null)
			{
				formatter.WriteError(new MauiToolException(ErrorCodes.AppleSimulatorRecordVideoFailed, $"Failed to start recording from simulator '{udid}'. Ensure the simulator is booted and xcrun is available."));
				return 1;
			}

			var useJson = parseResult.GetValue(GlobalOptions.JsonOption);

			// Emit a "recording started" signal so JSON consumers know we're active.
			if (useJson)
				formatter.WriteInfo("{\"status\":\"recording\",\"udid\":\"" + udid + "\",\"output_path\":\"" + outputPath.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"}");
			else
				formatter.WriteInfo($"Recording simulator '{udid}' to '{outputPath}'. Press Ctrl-C to stop.");

			// Block until the user presses Ctrl-C, then dispose to stop and flush the file.
			using var stopSignal = new ManualResetEventSlim(false);
			ConsoleCancelEventHandler handler = (_, e) =>
			{
				e.Cancel = true; // prevent abrupt termination so we can dispose cleanly
				stopSignal.Set();
			};
			Console.CancelKeyPress += handler;
			try
			{
				stopSignal.Wait();
			}
			finally
			{
				Console.CancelKeyPress -= handler;
				session.Dispose();
			}

			if (useJson)
				formatter.Write(new SimulatorRecordingResult { Udid = udid, OutputPath = outputPath, Format = format is { } f ? SimulatorScreenCapture.ToSimctlVideoFormatName(f) : null, Success = true });
			else
				formatter.WriteSuccess($"Recording saved to '{outputPath}'.");
			return 0;
		});

		return recordCommand;
	}

	/// <summary>
	/// Resolves a simulator by UDID, returning the match or null. Fetches the list once per call
	/// (intentional: the subsequent simctl operation is a different command, so caching across
	/// the validation + operation boundary isn't possible).
	/// </summary>
	static SimulatorInfo? FindSimulator(IAppleProvider appleProvider, string udid)
	{
		var sims = appleProvider.GetSimulators();
		return sims.FirstOrDefault(s => string.Equals(s.Udid, udid, StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>
	/// Validates that the UDID refers to an existing, available simulator. Writes the appropriate
	/// error via the formatter and returns false if validation fails.
	/// </summary>
	static bool ValidateSimulator(IAppleProvider appleProvider, string udid, IOutputFormatter formatter)
	{
		var sim = FindSimulator(appleProvider, udid);
		if (sim is null)
		{
			formatter.WriteError(new MauiToolException(ErrorCodes.AppleSimulatorNotFound, $"No simulator found with UDID '{udid}'. List simulators with 'maui apple simulator list'."));
			return false;
		}
		if (!sim.IsAvailable)
		{
			formatter.WriteError(new MauiToolException(ErrorCodes.AppleSimulatorUnavailable, $"Simulator '{udid}' exists but is unavailable (its runtime may have been deleted). Use 'maui apple simulator list' to find an available device."));
			return false;
		}
		return true;
	}
}
