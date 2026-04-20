// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using System.CommandLine.Parsing;
using Microsoft.Maui.Cli.Models;
using Microsoft.Maui.Cli.Output;
using Microsoft.Maui.Cli.Providers.Android;
using Microsoft.Maui.Cli.Utils;
using Spectre.Console;

namespace Microsoft.Maui.Cli.Commands;

public static partial class AndroidCommands
{
	static Command CreateInstallCommand()
	{
		var packagesOption = new Option<string[]>("--packages")
		{
			Description = "SDK packages to install (replaces defaults; comma-separated or multiple --packages flags)",
			AllowMultipleArgumentsPerToken = true
		};

		var command = new Command("install", "Set up Android development environment")
		{
			new Option<string>("--sdk-path") { Description = "Custom SDK installation path" },
			new Option<string>("--jdk-path") { Description = "Custom JDK installation path" },
			new Option<int>("--jdk-version") { Description = "JDK version to install (17 or 21)", DefaultValueFactory = _ => 17 },
			new Option<bool>("--accept-licenses") { Description = "Non-interactively accept all SDK licenses" },
			packagesOption
		};

		command.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
		{
			var androidProvider = Program.AndroidProvider;

			var useJson = parseResult.GetValue(GlobalOptions.JsonOption);
			var dryRun = parseResult.GetValue(GlobalOptions.DryRunOption);
			var sdkPath = parseResult.GetOption<string>("sdk-path");
			var jdkPath = parseResult.GetOption<string>("jdk-path");
			var jdkVersion = parseResult.GetOption<int>("jdk-version");
			var rawPackages = parseResult.GetOption<string[]>("packages");
			var acceptLicenses = parseResult.GetOption<bool>("accept-licenses");

			// Support comma-separated packages: "pkg1,pkg2,pkg3" becomes ["pkg1", "pkg2", "pkg3"]
			var packages = rawPackages?
				.SelectMany(p => p.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
				.ToArray();
			var formatter = Program.GetFormatter(parseResult);

			try
			{
				if (dryRun)
				{
					formatter.WriteInfo("[dry-run] Would install Android environment:");
					formatter.WriteProgress($"JDK version: {jdkVersion}");
					formatter.WriteProgress($"JDK path: {jdkPath ?? "(default)"}");
					formatter.WriteProgress($"SDK path: {sdkPath ?? "(default)"}");
					formatter.WriteProgress($"Accept licenses: {acceptLicenses}");
					if (packages?.Any() == true)
						formatter.WriteProgress($"Extra packages: {string.Join(", ", packages)}");
					return 0;
				}

				if (!useJson && formatter is SpectreOutputFormatter spectre)
				{
					// Proactively detect if SDK is in a protected location and request elevation
					if (TryRequestElevation(androidProvider, formatter, useJson))
					{
						formatter.WriteSuccess("Android environment installed successfully (elevated)");
						return 0;
					}

					// Resolve package list: explicit --packages or interactive selection
					var isCi = Program.IsCiMode(parseResult);
					var pkgList = await ResolveInstallPackagesAsync(packages, spectre, androidProvider, isCi, cancellationToken);

					await spectre.LiveProgressAsync(async (ctx) =>
					{
						// Step 1: JDK
						var jdkTask = ctx.AddTask("Installing JDK");
						if (!androidProvider.IsJdkInstalled)
						{
							var jdkManager = Program.JdkManager;
							await jdkManager.InstallAsync(jdkVersion, jdkPath,
								onProgress: (pct, msg) => jdkTask.Update(pct, $"JDK: {msg}"),
								cancellationToken);
							jdkTask.Complete($"OpenJDK {jdkVersion} installed");
						}
						else
						{
							jdkTask.Complete("JDK already installed");
						}

						// Step 2: SDK command-line tools
						var sdkTask = ctx.AddTask("Installing SDK Tools");
						if (!HasSdkManager(androidProvider))
						{
							var targetSdkPath = sdkPath ?? PlatformDetector.Paths.DefaultAndroidSdkPath;
							await androidProvider.InstallSdkToolsAsync(targetSdkPath,
								onProgress: (phase, pct, msg) =>
								{
									var label = phase switch
									{
										"ReadingManifest" => "Reading manifest...",
										"Downloading" => $"Downloading: {msg}",
										"Verifying" => "Verifying checksum...",
										"Extracting" => "Extracting...",
										"Complete" => "SDK Tools installed",
										_ => msg
									};
									sdkTask.Update(Math.Max(0, pct), label);
								},
								cancellationToken);
							sdkTask.Complete("SDK Tools installed");
						}
						else
						{
							sdkTask.Complete("SDK Tools already installed");
						}
					});

					// Step 3: Ensure licenses are accepted.
					// - If --accept-licenses was passed, bulk-accept non-interactively.
					// - Otherwise, if running interactively, hand stdin/stdout to `sdkmanager --licenses`
					//   so the user can review and accept each license. The Spectre live renderer
					//   has exited above, so child-process prompts are visible.
					// - In CI/non-interactive mode without --accept-licenses, fail fast rather than hang.
					var licensesAccepted = await androidProvider.AreLicensesAcceptedAsync(cancellationToken);
					if (!licensesAccepted)
					{
						if (acceptLicenses)
						{
							await spectre.LiveProgressAsync(async (ctx) =>
							{
								var licenseTask = ctx.AddTask("Accepting licenses");
								licenseTask.SetIndeterminate("Checking licenses...");
								await androidProvider.AcceptLicensesAsync(
									onProgress: msg => licenseTask.SetIndeterminate(msg),
									cancellationToken);
								licenseTask.Complete("Licenses accepted");
							});
						}
						else if (isCi || Console.IsInputRedirected || Console.IsOutputRedirected)
						{
							formatter.WriteError(new Exception(
								"Android SDK licenses have not been accepted. " +
								"Re-run with --accept-licenses, or run 'maui android sdk accept-licenses' interactively."));
							return 1;
						}
						else
						{
							formatter.WriteInfo("Android SDK licenses must be accepted to continue.");
							formatter.WriteInfo("Review each license and type 'y' to accept.\n");
							var exitCode = await RunInteractiveLicenseAcceptanceAsync(androidProvider, cancellationToken);
							if (exitCode != 0)
							{
								formatter.WriteError(new Exception(
									$"License acceptance exited with code {exitCode}. Aborting install."));
								return 1;
							}
							formatter.WriteSuccess("Licenses accepted");
						}
					}

					// By this point licenses are accepted (either already, bulk-accepted, or
					// interactively accepted). Force acceptLicenses=true into Step 4 so that
					// if a newly-requested package introduces an as-yet-unaccepted license,
					// sdkmanager bulk-accepts it rather than blocking on stdin behind the
					// live progress renderer.
					acceptLicenses = true;

					// Step 4: Install packages
					await spectre.LiveProgressAsync(async (ctx) =>
					{
						var pkgTask = ctx.AddTask($"Installing packages (0/{pkgList.Count})");
						pkgTask.Update(0, $"Installing packages (0/{pkgList.Count})...");
						await androidProvider.InstallPackagesAsync(pkgList, acceptLicenses,
							onProgress: (pkg, idx, total) =>
							{
								var pct = (double)idx / total * 100;
								pkgTask.Update(pct, $"Installing {pkg} ({idx}/{total})");
							},
							cancellationToken);
						pkgTask.Complete($"{pkgList.Count} packages installed");
					});
				}
				else
				{
					// JSON / non-Spectre path: non-interactive by nature. If the SDK is already
					// installed and licenses aren't yet accepted, fail fast rather than letting
					// sdkmanager block on stdin. If the SDK isn't installed yet, there's nothing
					// to hang on — InstallAsync will bootstrap tools and (when acceptLicenses
					// is true) accept licenses non-interactively.
					if (!acceptLicenses
						&& androidProvider.IsSdkInstalled
						&& !await androidProvider.AreLicensesAcceptedAsync(cancellationToken))
					{
						formatter.WriteError(new Exception(
							"Android SDK licenses have not been accepted. " +
							"Re-run this install command with --accept-licenses to bootstrap SDK tools and accept licenses non-interactively."));
						return 1;
					}

					var progress = new Progress<string>(message =>
					{
						formatter.WriteProgress(message);
					});

					await androidProvider.InstallAsync(
						sdkPath: sdkPath,
						jdkPath: jdkPath,
						jdkVersion: jdkVersion,
						additionalPackages: packages is { Length: > 0 } ? packages : null,
						acceptLicenses: acceptLicenses,
						progress: progress,
						cancellationToken: cancellationToken);
				}

				formatter.WriteSuccess("Android environment installed successfully");
				return 0;
			}
			catch (UnauthorizedAccessException uaEx) when (PlatformDetector.IsWindows)
			{
				if (!useJson)
					formatter.WriteWarning("Administrator access required. Requesting elevation...");

				if (ProcessRunner.RelaunchElevated())
				{
					formatter.WriteSuccess("Android environment installed successfully (elevated)");
					return 0;
				}

				formatter.WriteError(uaEx);
				return 1;
			}
			catch (Exception ex)
			{
				return Program.HandleCommandException(formatter, ex);
			}
		});

		return command;
	}

	/// <summary>
	/// Spawns <c>sdkmanager --licenses</c> with inherited stdio so the user can review and
	/// accept each SDK license interactively. Returns the child process exit code.
	/// </summary>
	static async Task<int> RunInteractiveLicenseAcceptanceAsync(
		IAndroidProvider androidProvider,
		CancellationToken cancellationToken)
	{
		var licenseCommand = androidProvider.GetLicenseAcceptanceCommand()
			?? throw new InvalidOperationException(
				"Android SDK is not installed (sdkmanager not found). " +
				"Install the command-line tools first.");

		var psi = new System.Diagnostics.ProcessStartInfo
		{
			FileName = licenseCommand.Command,
			Arguments = licenseCommand.Arguments,
			UseShellExecute = false,
			RedirectStandardInput = false,
			RedirectStandardOutput = false,
			RedirectStandardError = false
		};

		foreach (var kvp in AndroidEnvironment.BuildEnvironmentVariables(androidProvider.SdkPath, androidProvider.JdkPath))
			psi.Environment[kvp.Key] = kvp.Value;

		using var process = System.Diagnostics.Process.Start(psi)
			?? throw new InvalidOperationException("Failed to start sdkmanager --licenses");

		try
		{
			await process.WaitForExitAsync(cancellationToken);
			return process.ExitCode;
		}
		catch (OperationCanceledException)
		{
			// Kill the child process tree so Ctrl+C doesn't leave an orphaned sdkmanager
			// blocked on stdin.
			if (!process.HasExited)
			{
				try { process.Kill(entireProcessTree: true); }
				catch { /* Best-effort: process may have already exited. */ }

				try { await process.WaitForExitAsync(CancellationToken.None); }
				catch { /* Ignore cleanup failures; preserve original cancellation. */ }
			}

			throw;
		}
	}
}
