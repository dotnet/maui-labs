// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Maui.Cli.Errors;
using Microsoft.Maui.Cli.Models;
using Microsoft.Maui.Cli.Output;
using Microsoft.Maui.Cli.Services;
using Microsoft.Maui.Cli.Utils;
using Spectre.Console;

namespace Microsoft.Maui.Cli.Commands;

/// <summary>
/// Implementation of 'maui profile' command.
/// </summary>
public static class ProfileCommand
{
	static readonly TimeSpan s_buildLaunchTimeout = TimeSpan.FromMinutes(15);
	static readonly TimeSpan s_dsrouterStartupTimeout = TimeSpan.FromSeconds(30);
	static readonly TimeSpan s_traceStartupRetryTimeout = TimeSpan.FromSeconds(15);
	static readonly TimeSpan s_traceStartupRetryDelay = TimeSpan.FromMilliseconds(500);
	static readonly TimeSpan s_adbPortForwardTimeout = TimeSpan.FromSeconds(15);
	static readonly TimeSpan s_exitControlConnectTimeout = TimeSpan.FromSeconds(5);
	static readonly TimeSpan s_exitControlCommandTimeout = TimeSpan.FromSeconds(10);
	static readonly TimeSpan s_traceStopInterruptDelay = TimeSpan.FromSeconds(5);
	static readonly TimeSpan s_traceStopTimeout = TimeSpan.FromSeconds(15);
	const int DefaultDiagnosticPort = 9000;
	const int ExitControlPortOffset = 1;
	const string StartupProfilingPackageId = "Microsoft.Maui.StartupProfiling";
	const string StartupProfilingProviderName = "Microsoft.Maui.StartupProfiling";
	const string StartupProfilingEventName = "StartupComplete";
	const string StartupProfilingAssemblyFileName = "Microsoft.Maui.StartupProfiling.dll";
	const string StartupProfilingInjectionTargetsFileName = "MauiStartupProfilingInjection.targets";
	const string StartupProfilingInjectionSourceFileName = "MauiStartupProfiling.AutoInitialize.cs";
	const string SpeedscopeExtension = ".speedscope.json";

	// MSBuild SDK path env vars set by a parent `dotnet run` process that would otherwise
	// pin the child build to the wrong SDK version (e.g. the CLI's own SDK instead of the
	// user's project SDK). Removing them lets the child process discover the correct SDK
	// from the project directory's global.json or the latest installed SDK.
	static readonly string[] s_msbuildSdkEnvVars =
	[
		"MSBuildSDKsPath",
		"MSBUILD_EXE_PATH",
		"MSBuildExtensionsPath",
		"MSBuildStartupDirectory",
	];

	public static Command Create()
	{
		var projectOption = new Option<string?>("--project")
		{
			Description = "Path to the target .csproj or a directory containing it (default: current directory)"
		};
		var frameworkOption = new Option<string?>("--framework", "-f")
		{
			Description = "Target framework to profile (for example net10.0-android)"
		};
		var deviceOption = new Option<string?>("--device", "-d")
		{
			Description = "Device or simulator identifier to target (defaults to the only running compatible device)"
		};
		var outputOption = new Option<string?>("--output", "-o")
		{
			Description = "Output trace path (default: <project>_<timestamp>.nettrace in the current directory). Speedscope also emits a sibling .speedscope.json file."
		};
		var formatOption = new Option<string>("--format")
		{
			Description = "Output format to generate: nettrace (default) or speedscope.",
			DefaultValueFactory = _ => "nettrace"
		};
		var configurationOption = new Option<string>("--configuration", "-c")
		{
			Description = "Build configuration to use. Defaults to Release.",
			DefaultValueFactory = _ => "Release"
		};
		var platformOption = new Option<string>("--platform")
		{
			Description = "Target platform to profile. When omitted, the platform is inferred from the selected target framework.",
			DefaultValueFactory = _ => Platforms.All
		};
		var durationOption = new Option<TimeSpan?>("--duration")
		{
			Description = "Optional trace duration in hh:mm:ss format. If omitted, press Enter to stop the trace."
		};
		var traceProfileOption = new Option<string?>("--trace-profile")
		{
			Description = "Optional dotnet-trace profile(s), for example dotnet-sampled-thread-time or gc-verbose"
		};
		var noBuildOption = new Option<bool>("--no-build")
		{
			Description = "Skip the build step and just deploy/run with the existing outputs"
		};
		var diagnosticPortOption = new Option<int>("--diagnostic-port")
		{
			Description = "Preferred TCP port for the diagnostic connection. If it's busy, the next free port is used.",
			DefaultValueFactory = _ => DefaultDiagnosticPort
		};
		var stoppingEventProviderOption = new Option<string?>("--stopping-event-provider-name")
		{
			Description = "Optional event provider name for an event-based stop condition. " +
				"When omitted, maui profile waits for --duration or a manual Enter stop."
		};
		var stoppingEventNameOption = new Option<string?>("--stopping-event-event-name")
		{
			Description = "Optional event name to combine with --stopping-event-provider-name."
		};
		var stoppingEventPayloadFilterOption = new Option<string?>("--stopping-event-payload-filter")
		{
			Description = "Optional payload filter (key:value,key:value) to combine with the stopping event options"
		};

		var command = new Command("profile", "Collect a startup trace for a .NET MAUI app")
		{
			projectOption,
			frameworkOption,
			deviceOption,
			outputOption,
			formatOption,
			configurationOption,
			platformOption,
			durationOption,
			traceProfileOption,
			noBuildOption,
			diagnosticPortOption,
			stoppingEventProviderOption,
			stoppingEventNameOption,
			stoppingEventPayloadFilterOption
		};

		command.SetAction(async (ParseResult parseResult, CancellationToken cancellationToken) =>
		{
			var formatter = Program.GetFormatter(parseResult);
			var useJson = parseResult.GetValue(GlobalOptions.JsonOption);
			var isCi = Program.IsCiMode(parseResult);
			var verbose = Program.IsVerbose(parseResult);

			try
			{
				var requestedPlatform = Platforms.Normalize(parseResult.GetValue(platformOption));
				var project = MauiProjectResolver.Resolve(parseResult.GetValue(projectOption));
				var framework = ResolveTargetFramework(
					project,
					parseResult.GetValue(frameworkOption),
					requestedPlatform,
					isCi || useJson,
					formatter as SpectreOutputFormatter);
				var platform = ResolveProfilePlatform(requestedPlatform, framework);

				if (!string.Equals(platform, Platforms.Android, StringComparison.OrdinalIgnoreCase)
					&& !string.Equals(platform, Platforms.iOS, StringComparison.OrdinalIgnoreCase))
				{
					throw MauiToolException.UserActionRequired(
						ErrorCodes.PlatformNotSupported,
						$"Startup profiling for target framework '{framework}' is not implemented yet because it targets platform '{platform}'.",
						[
							"Choose an Android or iOS simulator target framework such as --framework net10.0-ios.",
							"Or pass --platform android/ios to filter the available target frameworks.",
							"Mac Catalyst support can be added in a future iteration."
						]);
				}

				ValidateStoppingEventOptions(
					parseResult.GetValue(stoppingEventProviderOption),
					parseResult.GetValue(stoppingEventNameOption),
					parseResult.GetValue(stoppingEventPayloadFilterOption));

				var duration = parseResult.GetValue(durationOption);
				var stoppingEvent = ResolveStoppingEventConfiguration(
					duration,
					parseResult.GetValue(stoppingEventProviderOption),
					parseResult.GetValue(stoppingEventNameOption),
					parseResult.GetValue(stoppingEventPayloadFilterOption));

				if ((isCi || useJson)
					&& duration is null
					&& string.IsNullOrWhiteSpace(stoppingEvent.ProviderName))
				{
					throw MauiToolException.UserActionRequired(
						ErrorCodes.InvalidArgument,
						"Non-interactive profile runs require an explicit stop condition because the default behavior waits for a manual Enter stop.",
						[
							"Add --duration 00:00:15 for a fixed-length startup trace.",
							"Or pass --stopping-event-provider-name/--stopping-event-event-name to stop on a custom EventSource marker."
						]);
				}

				ValidateDnxAvailable();

				var device = await ResolveProfileDeviceAsync(
					platform,
					parseResult.GetValue(deviceOption),
					Program.DeviceManager,
					isCi || useJson,
					formatter as SpectreOutputFormatter,
					cancellationToken);

				var outputFormat = ResolveTraceOutputFormat(
					parseResult.GetValue(formatOption),
					WasOptionExplicitlySpecified(parseResult, formatOption),
					isCi || useJson,
					formatter as SpectreOutputFormatter);
				var configuration = ResolveProfileConfiguration(
					parseResult.GetValue(configurationOption),
					WasOptionExplicitlySpecified(parseResult, configurationOption),
					platform);
				var outputPath = ResolveOutputPath(project.ProjectName, parseResult.GetValue(outputOption), outputFormat);

				var result = await RunProfileAsync(
					project,
					framework,
					device,
					outputPath,
					outputFormat,
					configuration,
					parseResult.GetValue(traceProfileOption),
					parseResult.GetValue(noBuildOption),
					parseResult.GetValue(diagnosticPortOption),
					duration,
					stoppingEvent.ProviderName,
					stoppingEvent.EventName,
					stoppingEvent.PayloadFilter,
					stoppingEvent.AutoSelected,
					formatter,
					useJson,
					verbose,
					cancellationToken);

				if (useJson)
				{
					formatter.Write(result);
				}
				else
				{
					var successMessage = string.IsNullOrWhiteSpace(result.RawTracePath)
						? $"Startup trace saved to {result.OutputPath}"
						: $"Startup trace saved to {result.OutputPath} (raw .nettrace companion: {result.RawTracePath})";
					formatter.WriteSuccess(successMessage);
				}

				return 0;
			}
			catch (Exception ex)
			{
				return Program.HandleCommandException(formatter, ex);
			}
		});

		return command;
	}

	internal static string ResolveTargetFramework(
		ResolvedMauiProject project,
		string? requestedFramework,
		string platform,
		bool nonInteractive,
		SpectreOutputFormatter? spectre)
	{
		if (!string.IsNullOrWhiteSpace(requestedFramework))
		{
			var match = project.TargetFrameworks.FirstOrDefault(tfm =>
				string.Equals(tfm, requestedFramework, StringComparison.OrdinalIgnoreCase));

			if (match == null)
			{
				throw new MauiToolException(
					ErrorCodes.InvalidArgument,
					$"Target framework '{requestedFramework}' was not found in {Path.GetFileName(project.ProjectPath)}.");
			}

			if (!IsTargetFrameworkCompatible(match, platform))
			{
				throw new MauiToolException(
					ErrorCodes.PlatformNotSupported,
					$"Target framework '{requestedFramework}' does not target platform '{platform}'.");
			}

			return match;
		}

		var candidates = project.TargetFrameworks
			.Where(tfm => IsTargetFrameworkCompatible(tfm, platform))
			.OrderByDescending(GetFrameworkSortKey)
			.ThenBy(GetFrameworkPlatformPriority)
			.ToList();

		if (candidates.Count == 0)
		{
			var platformDescription = string.Equals(platform, Platforms.All, StringComparison.OrdinalIgnoreCase)
				? "No target frameworks were found"
				: $"No target framework in {Path.GetFileName(project.ProjectPath)} matches platform '{platform}'";
			throw new MauiToolException(
				ErrorCodes.PlatformNotSupported,
				platformDescription + ".");
		}

		if (candidates.Count == 1 || nonInteractive || spectre == null)
			return candidates[0];

		var title = string.Equals(platform, Platforms.All, StringComparison.OrdinalIgnoreCase)
			? "[bold]Select the target framework to profile[/]"
			: $"[bold]Select the {Markup.Escape(platform)} target framework to profile[/]";

		return spectre.Prompt(
			new SelectionPrompt<string>()
				.Title(title)
				.HighlightStyle(new Style(Color.DodgerBlue1))
				.UseConverter(FormatFrameworkPromptChoice)
				.AddChoices(candidates));
	}

	internal static bool WasOptionExplicitlySpecified<T>(ParseResult parseResult, Option<T> option)
		=> parseResult.GetResult(option)?.Tokens.Count > 0;

	internal static string ResolveProfileConfiguration(string? requestedConfiguration, bool explicitlySpecified, string platform)
	{
		return string.IsNullOrWhiteSpace(requestedConfiguration)
			? "Release"
			: requestedConfiguration.Trim();
	}

	static Task<Device> ResolveProfileDeviceAsync(
		string platform,
		string? requestedDevice,
		IDeviceManager deviceManager,
		bool nonInteractive,
		SpectreOutputFormatter? spectre,
		CancellationToken cancellationToken)
	{
		var normalizedPlatform = Platforms.Normalize(platform);
		return normalizedPlatform switch
		{
			Platforms.Android => ResolveAndroidDeviceAsync(requestedDevice, deviceManager, nonInteractive, spectre, cancellationToken),
			Platforms.iOS => ResolveIosSimulatorAsync(requestedDevice, deviceManager, nonInteractive, spectre, cancellationToken),
			_ => Task.FromException<Device>(new MauiToolException(
				ErrorCodes.PlatformNotSupported,
				$"Startup profiling is not implemented yet for platform '{platform}'.")),
		};
	}

	static async Task<Device> ResolveAndroidDeviceAsync(
		string? requestedDevice,
		IDeviceManager deviceManager,
		bool nonInteractive,
		SpectreOutputFormatter? spectre,
		CancellationToken cancellationToken)
	{
		var runningDevices = (await deviceManager.GetDevicesByPlatformAsync(Platforms.Android, cancellationToken))
			.Where(d => d.IsRunning)
			.ToList();

		if (!runningDevices.Any())
		{
			throw MauiToolException.UserActionRequired(
				ErrorCodes.AndroidDeviceNotFound,
				"No running Android device or emulator was found.",
				[
					"Start an emulator with `maui android emulator start --name <name>`.",
					"Or connect a physical device over USB and verify it appears in `maui device list --platform android`."
				]);
		}

		if (!string.IsNullOrWhiteSpace(requestedDevice))
		{
			var match = runningDevices.FirstOrDefault(device =>
				string.Equals(device.Id, requestedDevice, StringComparison.OrdinalIgnoreCase) ||
				string.Equals(device.EmulatorId, requestedDevice, StringComparison.OrdinalIgnoreCase) ||
				string.Equals(device.Name, requestedDevice, StringComparison.OrdinalIgnoreCase));

			if (match == null)
			{
				throw new MauiToolException(
					ErrorCodes.AndroidDeviceNotFound,
					$"Android device '{requestedDevice}' was not found among the running devices.");
			}

			return match;
		}

		if (runningDevices.Count == 1 || nonInteractive || spectre == null)
			return runningDevices[0];

		return spectre.Prompt(
			new SelectionPrompt<Device>()
				.Title("[bold]Select the Android device to profile[/]")
				.HighlightStyle(new Style(Color.DodgerBlue1))
				.UseConverter(device =>
				{
					var type = device.IsEmulator ? "emulator" : "device";
					var version = string.IsNullOrWhiteSpace(device.VersionName) ? device.Version : device.VersionName;
					return $"[bold]{Markup.Escape(device.Name)}[/]  [grey]{Markup.Escape(device.Id)}[/]  [dim]{type} {Markup.Escape(version ?? string.Empty)}[/]";
				})
				.AddChoices(runningDevices));
	}

	static async Task<Device> ResolveIosSimulatorAsync(
		string? requestedDevice,
		IDeviceManager deviceManager,
		bool nonInteractive,
		SpectreOutputFormatter? spectre,
		CancellationToken cancellationToken)
	{
		var runningDevices = (await deviceManager.GetDevicesByPlatformAsync(Platforms.iOS, cancellationToken))
			.Where(d => d.IsRunning)
			.ToList();

		if (!runningDevices.Any())
		{
			throw MauiToolException.UserActionRequired(
				ErrorCodes.DeviceNotFound,
				"No booted iOS simulator was found.",
				[
					"Boot a simulator with `xcrun simctl boot <UDID>` or open Simulator.app and start one there.",
					"Then verify it appears in `maui device list --platform ios`."
				]);
		}

		if (!string.IsNullOrWhiteSpace(requestedDevice))
		{
			var match = runningDevices.FirstOrDefault(device =>
				string.Equals(device.Id, requestedDevice, StringComparison.OrdinalIgnoreCase) ||
				string.Equals(device.EmulatorId, requestedDevice, StringComparison.OrdinalIgnoreCase) ||
				string.Equals(device.Name, requestedDevice, StringComparison.OrdinalIgnoreCase));

			if (match == null)
			{
				throw new MauiToolException(
					ErrorCodes.DeviceNotFound,
					$"iOS simulator '{requestedDevice}' was not found among the booted simulators.");
			}

			return match;
		}

		if (runningDevices.Count == 1 || nonInteractive || spectre == null)
			return runningDevices[0];

		return spectre.Prompt(
			new SelectionPrompt<Device>()
				.Title("[bold]Select the iOS simulator to profile[/]")
				.HighlightStyle(new Style(Color.DodgerBlue1))
				.UseConverter(device =>
				{
					var version = string.IsNullOrWhiteSpace(device.VersionName) ? device.Version : device.VersionName;
					return $"[bold]{Markup.Escape(device.Name)}[/]  [grey]{Markup.Escape(device.Id)}[/]  [dim]simulator {Markup.Escape(version ?? string.Empty)}[/]";
				})
				.AddChoices(runningDevices));
	}

	static void ValidateDnxAvailable()
	{
		if (ProcessRunner.GetCommandPath("dnx") is not null)
			return;

		if (FindInstalledDotnetToolCommand("dotnet-trace") is not null &&
			FindInstalledDotnetToolCommand("dotnet-dsrouter") is not null)
		{
			return;
		}

		if (FindCachedDotnetToolDll("dotnet-trace") is not null &&
			FindCachedDotnetToolDll("dotnet-dsrouter") is not null)
		{
			return;
		}

		throw MauiToolException.UserActionRequired(
			ErrorCodes.DiagnosticsToolNotFound,
			"Neither 'dnx' nor the required dotnet diagnostics tools were found.",
			[
				"Install the global tools: `dotnet tool install -g dotnet-trace` and `dotnet tool install -g dotnet-dsrouter`.",
				"Or use a .NET 10 SDK with `dnx` available on PATH: https://dot.net/download"
			]);
	}

	static void ConfigureDotnetToolStartInfo(ProcessStartInfo startInfo, string packageId, IReadOnlyList<string> toolArgs, out string commandLine)
	{
		var installedToolPath = FindInstalledDotnetToolCommand(packageId);
		if (installedToolPath is not null)
		{
			startInfo.FileName = installedToolPath;
			foreach (var arg in toolArgs)
				startInfo.ArgumentList.Add(arg);

			commandLine = FormatCommandLine(installedToolPath, [.. toolArgs]);
			return;
		}

		var cachedToolDll = FindCachedDotnetToolDll(packageId);
		if (cachedToolDll is not null)
		{
			startInfo.FileName = "dotnet";
			startInfo.ArgumentList.Add(cachedToolDll);
			foreach (var arg in toolArgs)
				startInfo.ArgumentList.Add(arg);

			commandLine = FormatCommandLine("dotnet", [cachedToolDll, .. toolArgs]);
			return;
		}

		startInfo.FileName = "dnx";
		startInfo.ArgumentList.Add("-y");
		startInfo.ArgumentList.Add(packageId);
		startInfo.ArgumentList.Add("--");
		foreach (var arg in toolArgs)
			startInfo.ArgumentList.Add(arg);

		commandLine = FormatCommandLine("dnx", ["-y", packageId, "--", .. toolArgs]);
	}

	static string? FindInstalledDotnetToolCommand(string packageId)
	{
		var commandPath = ProcessRunner.GetCommandPath(packageId);
		if (!string.IsNullOrWhiteSpace(commandPath))
			return commandPath;

		var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		if (string.IsNullOrWhiteSpace(userProfile))
			return null;

		var extension = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
		var candidate = Path.Combine(userProfile, ".dotnet", "tools", packageId + extension);
		return File.Exists(candidate) ? candidate : null;
	}

	/// <summary>
	/// Starts dotnet-dsrouter via <c>dnx</c> and waits for it to print its PID to stdout.
	/// dotnet-dsrouter always prints a line containing <c>pid=&lt;N&gt;</c> shortly after startup.
	/// </summary>
	static async Task<(MonitoredProcess DnxProcess, int DsrouterPid)> StartDsrouterAsync(
		ProfileTransportConfiguration transport,
		int diagnosticPort,
		IOutputFormatter formatter,
		bool useJson,
		bool verbose,
		CancellationToken cancellationToken)
	{
		var startInfo = new ProcessStartInfo
		{
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			RedirectStandardInput = true,
			CreateNoWindow = true
		};

		var dsrouterArgs = BuildDsrouterArguments(transport, diagnosticPort);
		ConfigureDotnetToolStartInfo(startInfo, "dotnet-dsrouter", dsrouterArgs, out var commandLine);
		WriteVerbose(formatter, useJson, verbose, $"dsrouter command: {commandLine}");

		var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
		if (!process.Start())
			throw new MauiToolException(ErrorCodes.InternalError, "Failed to start dotnet-dsrouter via dnx.");

		// dotnet-dsrouter prints "pid=<N>" to stdout shortly after startup.
		// Wait for that line to discover the PID we pass to dotnet-trace.
		var pidRegex = new Regex(@"\bpid=(\d+)\b");
		var pidTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

		var monitoredProcess = MonitoredProcess.Attach(
			process,
			formatter,
			useJson,
			verbose,
			"dsrouter",
			cancellationToken,
			onStdoutLine: line =>
			{
				var m = pidRegex.Match(line);
				if (m.Success && int.TryParse(m.Groups[1].Value, out var pid))
				{
					pidTcs.TrySetResult(pid);
				}
			});

		using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeoutCts.CancelAfter(s_dsrouterStartupTimeout);

		int dsrouterPid;
		try
		{
			dsrouterPid = await pidTcs.Task.WaitAsync(timeoutCts.Token);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			if (!process.HasExited)
				process.Kill(entireProcessTree: true);
			throw new MauiToolException(
				ErrorCodes.InternalError,
				$"dotnet-dsrouter did not report its PID within {s_dsrouterStartupTimeout.TotalSeconds}s.",
				nativeError: monitoredProcess.GetCombinedOutput());
		}

		return (monitoredProcess, dsrouterPid);
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
				.AddChoices([TraceOutputFormat.NetTrace, TraceOutputFormat.Speedscope]));
	}

	internal static TraceOutputFormat ResolveTraceOutputFormat(string? requestedFormat) => requestedFormat?.Trim().ToLowerInvariant() switch
	{
		null or "" or "nettrace" => TraceOutputFormat.NetTrace,
		"speedscope" => TraceOutputFormat.Speedscope,
		_ => throw new MauiToolException(
			ErrorCodes.InvalidArgument,
			$"Unsupported output format '{requestedFormat}'. Supported values are: nettrace, speedscope.")
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
			fullPath.EndsWith(SpeedscopeExtension, StringComparison.OrdinalIgnoreCase))
		{
			fullPath = fullPath[..^SpeedscopeExtension.Length];
		}

		if (string.IsNullOrWhiteSpace(Path.GetExtension(fullPath)))
			fullPath += ".nettrace";
		return fullPath;
	}

	internal static string GetPrimaryOutputPath(string collectorOutputPath, TraceOutputFormat outputFormat) => outputFormat switch
	{
		TraceOutputFormat.Speedscope => collectorOutputPath + SpeedscopeExtension,
		_ => collectorOutputPath
	};

	internal static string FormatOutputFormat(TraceOutputFormat outputFormat) => outputFormat switch
	{
		TraceOutputFormat.NetTrace => "nettrace",
		TraceOutputFormat.Speedscope => "speedscope",
		_ => outputFormat.ToString().ToLowerInvariant()
	};

	static string FormatTraceOutputPromptChoice(TraceOutputFormat outputFormat) => outputFormat switch
	{
		TraceOutputFormat.NetTrace => "[bold]nettrace[/] [dim](raw EventPipe trace for PerfView / Visual Studio)[/]",
		TraceOutputFormat.Speedscope => "[bold]speedscope[/] [dim](browser-friendly flame chart; also keeps the raw .nettrace)[/]",
		_ => $"[bold]{Markup.Escape(FormatOutputFormat(outputFormat))}[/]"
	};

	static void ValidateStoppingEventOptions(string? providerName, string? eventName, string? payloadFilter)
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
	{
		return new StoppingEventConfiguration(providerName, eventName, payloadFilter, AutoSelected: false);
	}

	static async Task<MauiProfileResult> RunProfileAsync(
		ResolvedMauiProject project,
		string framework,
		Device device,
		string outputPath,
		TraceOutputFormat outputFormat,
		string configuration,
		string? traceProfile,
		bool noBuild,
		int diagnosticPort,
		TimeSpan? duration,
		string? stoppingEventProvider,
		string? stoppingEventName,
		string? stoppingEventPayloadFilter,
		bool autoSelectedStoppingEvent,
		IOutputFormatter formatter,
		bool useJson,
		bool verbose,
		CancellationToken cancellationToken)
	{
		var primaryOutputPath = GetPrimaryOutputPath(outputPath, outputFormat);
		var outputDirectory = Path.GetDirectoryName(outputPath);
		if (string.IsNullOrWhiteSpace(outputDirectory))
		{
			throw new MauiToolException(
				ErrorCodes.InvalidArgument,
				$"Could not determine the output directory for '{outputPath}'.");
		}

		Directory.CreateDirectory(outputDirectory);

		var profilePlatform = InferPlatformFromTargetFramework(framework) ?? device.Platform;
		var transport = ResolveProfileTransport(profilePlatform, device);
		var dsrouterKind = transport.DsrouterKind;
		var hasStartupProfilingHelper = MauiProjectResolver.HasPackageReference(project.ProjectPath, StartupProfilingPackageId);
		var requestedDiagnosticPort = diagnosticPort;
		var diagnosticAddress = transport.DiagnosticAddress;
		var startedAtUtc = DateTimeOffset.UtcNow;
		var effectiveDuration = duration;

		if (!useJson)
		{
			formatter.WriteInfo($"Project: {project.ProjectPath}");
			formatter.WriteInfo($"Framework: {framework}");
			formatter.WriteInfo($"Configuration: {configuration}");
			formatter.WriteInfo($"Device: {device.Name} ({device.Id})");
			formatter.WriteInfo($"Format: {FormatOutputFormat(outputFormat)}");
			formatter.WriteInfo($"Output: {primaryOutputPath}");
			if (!string.Equals(primaryOutputPath, outputPath, StringComparison.OrdinalIgnoreCase))
				formatter.WriteInfo($"Raw trace companion: {outputPath}");
			if (autoSelectedStoppingEvent)
			{
				formatter.WriteInfo(
					$"Stopping event: {StartupProfilingProviderName}/{StartupProfilingEventName} " +
					"(auto-detected from the app's startup profiling helper).");
			}
		}

		WriteVerbose(
			formatter,
			useJson,
			verbose,
			$"Profile settings: configuration={configuration}, noBuild={noBuild}, dsrouterKind={dsrouterKind}, " +
			$"diagnosticAddress={diagnosticAddress}, diagnosticListenMode={transport.DiagnosticListenMode}, diagnosticPort={diagnosticPort}, " +
			$"traceProfile={traceProfile ?? "(default)"}, outputFormat={FormatOutputFormat(outputFormat)}, duration={duration?.ToString() ?? "(manual stop)"}, " +
			$"stoppingEventProvider={stoppingEventProvider ?? "(none)"}, stoppingEventName={stoppingEventName ?? "(none)"}, " +
			$"stoppingEventPayloadFilter={stoppingEventPayloadFilter ?? "(none)"}");

		// Reserve the diagnostics port and the app-exit control port up front so the build can
		// inject matching environment variables into the packaged app. dotnet-dsrouter now owns
		// the diagnostics port forwarding; we only keep manual routing for the extra exit channel.
		MonitoredProcess? dsrouterProcess = null;
		MonitoredProcess? traceProcess = null;
		ReservedProfilePorts? reservedPorts = null;
		ExitControlServer? exitControlServer = null;
		ProfilingBuildInjection? buildInjection = null;
		try
		{
			reservedPorts = await ReserveProfilePortsAndConfigureRoutingAsync(
				device,
				transport,
				diagnosticPort,
				formatter,
				useJson,
				verbose,
				cancellationToken);

			diagnosticPort = reservedPorts.DiagnosticPort;
			buildInjection = string.Equals(profilePlatform, Platforms.iOS, StringComparison.OrdinalIgnoreCase)
				? null
				: TryCreateBuildInjection(
					diagnosticAddress,
					reservedPorts.ExitControlPort,
					injectBootstrap: !hasStartupProfilingHelper);

			if (!useJson)
			{
				formatter.WriteInfo($"Diagnostic port: {diagnosticPort}");
				if (diagnosticPort != requestedDiagnosticPort)
					formatter.WriteInfo($"Port {requestedDiagnosticPort} was busy, so the profiler selected {diagnosticPort}.");

				if (buildInjection is null)
				{
					formatter.WriteWarning(
						"The CLI's startup profiling injection assets were not found next to the tool binaries, so automatic startup-complete and graceful app-exit injection are unavailable for this run.");
				}
			}

			// Phase 1: Build (compile + package) without running, so the deploy+launch step
			// later is fast (seconds) and won't race with dotnet-trace's connection timeout.
			if (!noBuild)
			{
				if (!useJson && formatter is not SpectreOutputFormatter)
					formatter.WriteInfo("Building the app...");

				var buildArgs = BuildCompileArguments(project.ProjectPath, framework, configuration, transport, diagnosticPort, buildInjection);
				WriteVerbose(formatter, useJson, verbose, $"Build command: {FormatCommandLine("dotnet", buildArgs)}");
				var buildResult = formatter is SpectreOutputFormatter spectreForBuild && !useJson
					? await spectreForBuild.StatusAsync(
						"Building the app...",
						() => ProcessRunner.RunAsync("dotnet", buildArgs, project.ProjectDirectory, timeout: s_buildLaunchTimeout, environmentVariablesToRemove: s_msbuildSdkEnvVars, cancellationToken: cancellationToken))
					: await ProcessRunner.RunAsync("dotnet", buildArgs, project.ProjectDirectory, timeout: s_buildLaunchTimeout, environmentVariablesToRemove: s_msbuildSdkEnvVars, cancellationToken: cancellationToken);

				if (!buildResult.Success)
					throw CreateProcessFailureException("dotnet build", buildResult);
			}
			else
			{
				WriteVerbose(formatter, useJson, verbose, "Skipping build because --no-build was specified.");
			}

			await TryForceStopRunningAndroidAppAsync(project, framework, configuration, device, formatter, useJson, verbose, cancellationToken);

			// Phase 2: Start dsrouter, then dotnet-trace AFTER build artifacts exist, then
			// immediately deploy+launch. Deploy only takes seconds, well within dotnet-trace's
			// connection timeout.
			exitControlServer = ExitControlServer.Attach(reservedPorts.ExitControlReservation, formatter, useJson, verbose);
			reservedPorts.DiagnosticReservation.Dispose();

			WriteVerbose(formatter, useJson, verbose, $"Starting dotnet-dsrouter in '{dsrouterKind}' mode on port {diagnosticPort}.");
			var dsrouterStart = await StartDsrouterAsync(transport, diagnosticPort, formatter, useJson, verbose, cancellationToken);
			dsrouterProcess = dsrouterStart.DnxProcess;
			var dsrouterPid = dsrouterStart.DsrouterPid;
			WriteVerbose(formatter, useJson, verbose, $"dotnet-dsrouter reported PID {dsrouterPid}.");
			await EnsureDsrouterStartedAsync(dsrouterProcess, diagnosticPort, cancellationToken);
			var startTraceAfterLaunch = string.Equals(transport.Platform, Platforms.iOS, StringComparison.OrdinalIgnoreCase);
			if (!startTraceAfterLaunch)
			{
				traceProcess = StartTraceCollector(
					project.ProjectDirectory,
					outputPath,
					outputFormat,
					dsrouterPid,
					device.Id,
					traceProfile,
					effectiveDuration,
					stoppingEventProvider,
					stoppingEventName,
					stoppingEventPayloadFilter,
					formatter,
					useJson,
					verbose,
					cancellationToken);

				WriteVerbose(formatter, useJson, verbose, $"Waiting briefly for dotnet-trace (PID {traceProcess.Process.Id}) to connect.");
				await EnsureTraceCollectorStartedAsync(traceProcess, cancellationToken);
			}

			var launchArgs = BuildLaunchArguments(
				project.ProjectPath,
				framework,
				configuration,
				device,
				transport,
				diagnosticPort,
				buildInjection);
			WriteVerbose(formatter, useJson, verbose, $"Launch command: {FormatCommandLine("dotnet", launchArgs)}");

			if (!useJson && formatter is not SpectreOutputFormatter)
				formatter.WriteInfo("Deploying and launching the app with startup diagnostics enabled...");

			var launchResult = formatter is SpectreOutputFormatter spectre && !useJson
				? await spectre.StatusAsync(
					"Deploying and launching the app...",
					() => ProcessRunner.RunAsync("dotnet", launchArgs, project.ProjectDirectory, timeout: s_buildLaunchTimeout, environmentVariablesToRemove: s_msbuildSdkEnvVars, cancellationToken: cancellationToken))
				: await ProcessRunner.RunAsync("dotnet", launchArgs, project.ProjectDirectory, timeout: s_buildLaunchTimeout, environmentVariablesToRemove: s_msbuildSdkEnvVars, cancellationToken: cancellationToken);

			if (!launchResult.Success)
			{
				if (traceProcess is not null)
				{
					await RequestTraceStopAsync(traceProcess.Process, formatter, useJson, verbose);
					await traceProcess.WaitForExitAsync();
				}

				throw CreateProcessFailureException("dotnet build -t:Run", launchResult);
			}

			if (startTraceAfterLaunch)
			{
				traceProcess = await StartTraceCollectorWithRetryAsync(
					project.ProjectDirectory,
					outputPath,
					outputFormat,
					dsrouterPid,
					device.Id,
					traceProfile,
					effectiveDuration,
					stoppingEventProvider,
					stoppingEventName,
					stoppingEventPayloadFilter,
					formatter,
					useJson,
					verbose,
					cancellationToken);
			}

			if (!useJson)
			{
				if (traceProcess is not null && traceProcess.Process.HasExited)
				{
					formatter.WriteWarning(
						"Trace collection completed during app launch before a manual stop request. " +
						"This usually means the target process disconnected and the trace finalized early.");
				}
				else
				{
					var traceStatusMessage = !string.IsNullOrWhiteSpace(stoppingEventProvider)
						? "Waiting for the configured stopping event. Press Enter to stop early."
						: effectiveDuration is { } explicitDuration
							? $"Startup trace is running. It will stop automatically after {FormatDuration(explicitDuration)} unless you press Enter sooner."
							: "Startup trace is running. Press Enter to stop and finalize the trace output.";
					formatter.WriteInfo(traceStatusMessage);
				}
			}

			if (traceProcess is not null)
			{
				await WaitForTraceCompletionAsync(
					traceProcess,
					allowManualStop: !useJson,
					formatter,
					useJson,
					verbose,
					cancellationToken);
			}

			if (exitControlServer is not null)
			{
				var appExitRequested = await exitControlServer.TryRequestExitAsync(s_exitControlConnectTimeout, s_exitControlCommandTimeout, cancellationToken);
				if (!appExitRequested && !useJson)
				{
					formatter.WriteWarning(
						"The app did not connect to the startup profiling exit channel, so it may remain running and not flush PGO data. " +
						"Ensure it references Microsoft.Maui.StartupProfiling and loads that assembly during startup.");
				}
			}
		}
		finally
		{
			reservedPorts?.Dispose();
			exitControlServer?.Dispose();

			if (traceProcess is not null)
			{
				await StopBackgroundProcessAsync(traceProcess.Process, "dotnet-trace", formatter, useJson, verbose);
				traceProcess.Dispose();
			}

			if (dsrouterProcess is not null)
			{
				await StopBackgroundProcessAsync(dsrouterProcess.Process, "dotnet-dsrouter", formatter, useJson, verbose);
				dsrouterProcess.Dispose();
			}

			if (transport.RequiresManualExitControlPortRouting)
			{
				if (reservedPorts is not null)
					await RemoveAdbPortRoutingAsync(device, formatter, useJson, verbose, reservedPorts.ExitControlPort);
				else
					await RemoveAdbPortRoutingAsync(device, formatter, useJson, verbose, GetExitControlPort(diagnosticPort));
			}
		}

		if (!File.Exists(primaryOutputPath))
		{
			throw new MauiToolException(
				ErrorCodes.InternalError,
				$"Trace collection completed, but '{primaryOutputPath}' was not created.");
		}

		ValidateTraceOutput(primaryOutputPath, outputPath, outputFormat, transport.Platform);

		return new MauiProfileResult
		{
			ProjectPath = project.ProjectPath,
			ProjectName = project.ProjectName,
			Framework = framework,
			Platform = transport.Platform,
			DeviceId = device.Id,
			DeviceName = device.Name,
			Configuration = configuration,
			Format = FormatOutputFormat(outputFormat),
			OutputPath = primaryOutputPath,
			RawTracePath = outputFormat == TraceOutputFormat.Speedscope ? outputPath : null,
			DsrouterKind = dsrouterKind,
			DiagnosticAddress = diagnosticAddress,
			DiagnosticPort = diagnosticPort,
			UsedStoppingEvent = !string.IsNullOrWhiteSpace(stoppingEventProvider),
			StartedAtUtc = startedAtUtc,
			CompletedAtUtc = DateTimeOffset.UtcNow
		};
	}

	internal static string[] BuildCompileArguments(
		string projectPath,
		string framework,
		string configuration,
		ProfileTransportConfiguration transport,
		int diagnosticPort,
		ProfilingBuildInjection? buildInjection)
	{
		var args = new List<string>
		{
			"build",
			projectPath,
			"-c", configuration,
			"-f", framework,
			"--nologo"
		};

		AppendDiagnosticArguments(args, transport, diagnosticPort);
		AppendBuildInjectionArguments(args, buildInjection);
		return [.. args];
	}

	internal static string[] BuildLaunchArguments(
		string projectPath,
		string framework,
		string configuration,
		Device device,
		ProfileTransportConfiguration transport,
		int diagnosticPort,
		ProfilingBuildInjection? buildInjection)
	{
		var args = new List<string>
		{
			"build",
			projectPath,
			"-t:Run",
			"-c", configuration,
			"-f", framework,
			"-p:WaitForExit=false",
			// Phase 1 already compiled+packaged; incremental check here is near-instant.
			// Do NOT pass NoBuild=true — it triggers NETSDK1085 when Build is invoked via -t:Run.
		};

		AppendDiagnosticArguments(args, transport, diagnosticPort);

		if (string.Equals(transport.Platform, Platforms.Android, StringComparison.OrdinalIgnoreCase))
		{
			args.Add($"-p:AdbTarget=-s {device.Id}");
			args.Add("-p:AndroidEnableProfiler=true");
		}
		else if (string.Equals(transport.Platform, Platforms.iOS, StringComparison.OrdinalIgnoreCase))
		{
			args.Add($"-p:_DeviceName=:v2:udid={device.Id}");
			args.Add("-p:_MlaunchWaitForExit=false");
		}

		AppendBuildInjectionArguments(args, buildInjection);
		return [.. args];
	}

	static void AppendDiagnosticArguments(List<string> args, ProfileTransportConfiguration transport, int diagnosticPort)
	{
		args.Add($"-p:DiagnosticAddress={transport.DiagnosticAddress}");
		args.Add($"-p:DiagnosticPort={diagnosticPort}");
		args.Add("-p:DiagnosticSuspend=true");
		args.Add($"-p:DiagnosticListenMode={transport.DiagnosticListenMode}");
		args.Add("-p:EnableDiagnostics=true");
	}

	static void AppendBuildInjectionArguments(List<string> args, ProfilingBuildInjection? buildInjection)
	{
		if (buildInjection is null)
			return;

		args.Add($"-p:CustomAfterMicrosoftCommonTargets={buildInjection.TargetsPath}");
		args.Add("-p:MauiStartupProfilingInject=true");
		args.Add($"-p:MauiStartupProfilingExitHost={buildInjection.ExitControlHost}");
		args.Add($"-p:MauiStartupProfilingExitPort={buildInjection.ExitControlPort}");
		args.Add($"-p:MauiStartupProfilingInjectBootstrap={(buildInjection.InjectBootstrap ? "true" : "false")}");

		if (!string.IsNullOrWhiteSpace(buildInjection.AssemblyPath))
			args.Add($"-p:MauiStartupProfilingAssemblyPath={buildInjection.AssemblyPath}");
	}

	internal static string[] BuildDsrouterArguments(ProfileTransportConfiguration transport, int diagnosticPort)
	{
		var args = new List<string>
		{
			transport.DsrouterKind,
			transport.DsrouterRuntimeEndpointOption,
			$"{IPAddress.Loopback}:{diagnosticPort}"
		};

		if (!string.IsNullOrWhiteSpace(transport.DsrouterForwardPort))
		{
			args.Add("--forward-port");
			args.Add(transport.DsrouterForwardPort);
		}

		return [.. args];
	}

	static MonitoredProcess StartTraceCollector(
		string workingDirectory,
		string outputPath,
		TraceOutputFormat outputFormat,
		int dsrouterPid,
		string androidSerial,
		string? traceProfile,
		TimeSpan? duration,
		string? stoppingEventProvider,
		string? stoppingEventName,
		string? stoppingEventPayloadFilter,
		IOutputFormatter formatter,
		bool useJson,
		bool verbose,
		CancellationToken cancellationToken)
	{
		var startInfo = new ProcessStartInfo
		{
			WorkingDirectory = workingDirectory,
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			RedirectStandardInput = true,
			CreateNoWindow = true
		};

		var traceArgs = BuildTraceArguments(outputPath, outputFormat, dsrouterPid, traceProfile, duration, stoppingEventProvider, stoppingEventName, stoppingEventPayloadFilter).ToArray();
		ConfigureDotnetToolStartInfo(startInfo, "dotnet-trace", traceArgs, out var commandLine);
		WriteVerbose(formatter, useJson, verbose, $"Trace command: {commandLine}");

		startInfo.EnvironmentVariables["ANDROID_SERIAL"] = androidSerial;

		var process = new Process
		{
			StartInfo = startInfo,
			EnableRaisingEvents = true
		};

		if (!process.Start())
		{
			throw new MauiToolException(
				ErrorCodes.InternalError,
				"Failed to start dotnet-trace.");
		}

		return MonitoredProcess.Attach(process, formatter, useJson, verbose, "trace", cancellationToken);
	}

	static string? FindCachedDotnetToolDll(string packageId)
	{
		var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		if (string.IsNullOrWhiteSpace(userProfile))
			return null;

		var packageRoot = Path.Combine(userProfile, ".nuget", "packages", packageId.ToLowerInvariant());
		if (!Directory.Exists(packageRoot))
			return null;

		var versionDirectories = Directory
			.GetDirectories(packageRoot)
			.OrderByDescending(path => TryParsePackageVersion(Path.GetFileName(path), out var version) ? version : new Version(0, 0))
			.ThenByDescending(Path.GetFileName, StringComparer.OrdinalIgnoreCase);

		foreach (var versionDirectory in versionDirectories)
		{
			var directPath = Path.Combine(versionDirectory, "tools", "net8.0", "any", $"{packageId}.dll");
			if (File.Exists(directPath))
				return directPath;

			var candidate = Directory
				.EnumerateFiles(versionDirectory, $"{packageId}.dll", SearchOption.AllDirectories)
				.FirstOrDefault(path =>
					path.Contains($"{Path.DirectorySeparatorChar}tools{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
					path.Contains($"{Path.AltDirectorySeparatorChar}tools{Path.AltDirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
			if (candidate is not null)
				return candidate;
		}

		return null;
	}

	static bool TryParsePackageVersion(string? value, out Version version)
	{
		var success = Version.TryParse(value, out var parsedVersion);
		version = parsedVersion ?? new Version(0, 0);
		return success;
	}

	internal static IEnumerable<string> BuildTraceArguments(
		string outputPath,
		TraceOutputFormat outputFormat,
		int dsrouterPid,
		string? traceProfile,
		TimeSpan? duration,
		string? stoppingEventProvider,
		string? stoppingEventName,
		string? stoppingEventPayloadFilter)
	{
		var args = new List<string>
		{
			"collect",
			// Connect to the already-running dsrouter by its PID (IPC socket is named
			// after the dsrouter process: /tmp/dotnet-diagnostic-<pid>-*).
			"--process-id",
			dsrouterPid.ToString(),
			"--format",
			outputFormat switch
			{
				TraceOutputFormat.Speedscope => "Speedscope",
				_ => "NetTrace"
			},
			"--output",
			outputPath,
			// Required when DiagnosticSuspend=true: tells dotnet-trace to send a
			// ResumeRuntime IPC command after connecting, so the app starts executing.
			"--resume-runtime"
		};

		if (!string.IsNullOrWhiteSpace(traceProfile))
		{
			args.Add("--profile");
			args.Add(traceProfile);
		}
		else if (!string.IsNullOrWhiteSpace(stoppingEventProvider))
		{
			// When a stopping event provider is specified but no explicit --profile, we must
			// explicitly include the default collection profiles.
			args.Add("--profile");
			args.Add("dotnet-common,dotnet-sampled-thread-time");
		}

		if (duration is { } durationValue)
		{
			args.Add("--duration");
			args.Add(FormatDuration(durationValue));
		}

		if (!string.IsNullOrWhiteSpace(stoppingEventProvider))
		{
			// Explicitly enable the stopping event provider in the EventPipe session.
			// --stopping-event-provider-name alone only configures the stopping condition check;
			// the provider must also appear in --providers so EventPipe actually delivers its
			// events to dotnet-trace (otherwise the stop never triggers).
			// Per dotnet-trace docs, --providers is additive on top of --profile.
			args.Add("--providers");
			args.Add($"{stoppingEventProvider}:ffffffffffffffff:5");
		}

		if (!string.IsNullOrWhiteSpace(stoppingEventProvider))
		{
			args.Add("--stopping-event-provider-name");
			args.Add(stoppingEventProvider);
		}

		if (!string.IsNullOrWhiteSpace(stoppingEventName))
		{
			args.Add("--stopping-event-event-name");
			args.Add(stoppingEventName);
		}

		if (!string.IsNullOrWhiteSpace(stoppingEventPayloadFilter))
		{
			args.Add("--stopping-event-payload-filter");
			args.Add(stoppingEventPayloadFilter);
		}

		return args;
	}

	static string FormatDuration(TimeSpan duration)
	{
		var positiveDuration = duration < TimeSpan.Zero ? duration.Negate() : duration;
		return $"{(int)positiveDuration.TotalDays:00}:{positiveDuration.Hours:00}:{positiveDuration.Minutes:00}:{positiveDuration.Seconds:00}";
	}

	static async Task EnsureTraceCollectorStartedAsync(MonitoredProcess traceProcess, CancellationToken cancellationToken)
	{
		await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
		if (!traceProcess.Process.HasExited)
			return;

		await traceProcess.WaitForExitAsync();
		var details = traceProcess.GetCombinedOutput();
		throw new MauiToolException(
			ErrorCodes.InternalError,
			"dotnet-trace exited before the app launch started.",
			nativeError: details);
	}

	static async Task<MonitoredProcess> StartTraceCollectorWithRetryAsync(
		string workingDirectory,
		string outputPath,
		TraceOutputFormat outputFormat,
		int dsrouterPid,
		string androidSerial,
		string? traceProfile,
		TimeSpan? duration,
		string? stoppingEventProvider,
		string? stoppingEventName,
		string? stoppingEventPayloadFilter,
		IOutputFormatter formatter,
		bool useJson,
		bool verbose,
		CancellationToken cancellationToken)
	{
		var startedAt = Stopwatch.GetTimestamp();
		MauiToolException? lastFailure = null;

		while (Stopwatch.GetElapsedTime(startedAt) < s_traceStartupRetryTimeout)
		{
			var traceProcess = StartTraceCollector(
				workingDirectory,
				outputPath,
				outputFormat,
				dsrouterPid,
				androidSerial,
				traceProfile,
				duration,
				stoppingEventProvider,
				stoppingEventName,
				stoppingEventPayloadFilter,
				formatter,
				useJson,
				verbose,
				cancellationToken);

			try
			{
				WriteVerbose(formatter, useJson, verbose, $"Waiting briefly for dotnet-trace (PID {traceProcess.Process.Id}) to connect after launching the suspended iOS app.");
				await EnsureTraceCollectorStartedAsync(traceProcess, cancellationToken);
				return traceProcess;
			}
			catch (MauiToolException ex) when (IsRetryableTraceStartupFailure(ex.NativeError))
			{
				lastFailure = ex;
				traceProcess.Dispose();
				WriteVerbose(
					formatter,
					useJson,
					verbose,
					$"dotnet-trace could not connect yet; retrying in {s_traceStartupRetryDelay.TotalSeconds:0.#}s while the iOS runtime finishes opening its diagnostics channel.");
				await Task.Delay(s_traceStartupRetryDelay, cancellationToken);
			}
		}

		throw lastFailure ?? new MauiToolException(
			ErrorCodes.InternalError,
			$"dotnet-trace could not connect to the iOS app within {s_traceStartupRetryTimeout.TotalSeconds:0}s.");
	}

	internal static bool IsRetryableTraceStartupFailure(string? details)
	{
		if (string.IsNullOrWhiteSpace(details))
			return false;

		return details.Contains("EndOfStreamException", StringComparison.OrdinalIgnoreCase) ||
			details.Contains("ServerNotAvailableException", StringComparison.OrdinalIgnoreCase) ||
			details.Contains("Unable to connect to the server", StringComparison.OrdinalIgnoreCase) ||
			details.Contains("Connection refused", StringComparison.OrdinalIgnoreCase) ||
			details.Contains("Can't assign requested address", StringComparison.OrdinalIgnoreCase);
	}

	static async Task EnsureDsrouterStartedAsync(MonitoredProcess dsrouterProcess, int diagnosticPort, CancellationToken cancellationToken)
	{
		await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
		if (!dsrouterProcess.Process.HasExited)
			return;

		await dsrouterProcess.WaitForExitAsync();
		var details = dsrouterProcess.GetCombinedOutput();
		var message = $"dotnet-dsrouter exited before the app launch started.";
		var suggestions = details.Contains("Address already in use", StringComparison.OrdinalIgnoreCase)
			? new[]
			{
				$"Port {diagnosticPort} is already in use. Wait for the previous profile run to finish, or stop the stale dotnet-dsrouter process.",
				$"If needed, retry with a different --diagnostic-port value."
			}
			: null;

		throw suggestions is null
			? new MauiToolException(ErrorCodes.InternalError, message, nativeError: details)
			: MauiToolException.UserActionRequired(ErrorCodes.InternalError, message, suggestions, nativeError: details);
	}

	static async Task TryForceStopRunningAndroidAppAsync(
		ResolvedMauiProject project,
		string framework,
		string configuration,
		Device device,
		IOutputFormatter formatter,
		bool useJson,
		bool verbose,
		CancellationToken cancellationToken)
	{
		var applicationId = MauiProjectResolver.GetAndroidApplicationId(project.ProjectPath, framework, configuration);
		if (string.IsNullOrWhiteSpace(applicationId))
		{
			WriteVerbose(formatter, useJson, verbose, $"Could not resolve the Android application ID for '{project.ProjectName}'. Skipping pre-launch force-stop.");
			return;
		}

		var adbPath = ResolveAdbPath();
		if (adbPath is null)
			return;

		WriteVerbose(formatter, useJson, verbose, $"Force-stopping any existing '{applicationId}' process on {device.Id} before starting trace collection.");
		var stopResult = await ProcessRunner.RunAsync(
			adbPath,
			["-s", device.Id, "shell", "am", "force-stop", applicationId],
			timeout: s_adbPortForwardTimeout,
			cancellationToken: cancellationToken);

		if (!stopResult.Success)
		{
			WriteVerbose(
				formatter,
				useJson,
				verbose,
				$"adb force-stop for '{applicationId}' returned exit code {stopResult.ExitCode}: {GetProcessFailureDetails(stopResult)}");
		}
	}

	internal static int FindAvailableTcpPort(int startingPort, int maxPort = IPEndPoint.MaxPort)
	{
		using var reservation = ReserveAvailableTcpPort(startingPort, maxPort);
		return reservation.Port;
	}

	static async Task<ReservedProfilePorts> ReserveProfilePortsAndConfigureRoutingAsync(
		Device device,
		ProfileTransportConfiguration transport,
		int startingPort,
		IOutputFormatter formatter,
		bool useJson,
		bool verbose,
		CancellationToken cancellationToken)
	{
		if (startingPort < 1 || startingPort > IPEndPoint.MaxPort)
		{
			throw new MauiToolException(
				ErrorCodes.InvalidArgument,
				$"--diagnostic-port must be between 1 and {IPEndPoint.MaxPort}.");
		}

		for (var port = startingPort; port < IPEndPoint.MaxPort; port++)
		{
			ReservedTcpPort? diagnosticReservation = null;
			ReservedTcpPort? exitControlReservation = null;
			var exitControlPort = GetExitControlPort(port);

			try
			{
				diagnosticReservation = TryReserveTcpPort(port);
				if (diagnosticReservation is null)
					continue;

				exitControlReservation = TryReserveTcpPort(exitControlPort);
				if (exitControlReservation is null)
				{
					diagnosticReservation.Dispose();
					continue;
				}

				WriteVerbose(
					formatter,
					useJson,
					verbose,
					$"Reserved diagnostic port {port} and exit control port {exitControlPort}.");
				if (transport.RequiresManualExitControlPortRouting)
				{
					WriteVerbose(
						formatter,
						useJson,
						verbose,
						$"dotnet-dsrouter will handle the diagnostics port; configuring adb reverse for the auxiliary exit-control port on {device.Id}.");
					await EnsureAdbPortRoutingAsync(device, formatter, useJson, verbose, cancellationToken, exitControlPort);
				}
				return new ReservedProfilePorts(port, exitControlPort, diagnosticReservation, exitControlReservation);
			}
			catch (DiagnosticPortRoutingConflictException ex)
			{
				diagnosticReservation?.Dispose();
				exitControlReservation?.Dispose();
				await RemoveAdbPortRoutingAsync(device, formatter, useJson, verbose, port, exitControlPort);
				WriteVerbose(formatter, useJson, verbose, $"Port {ex.Port} was unavailable for adb routing ({ex.Direction}): {ex.Details}");
			}
			catch
			{
				diagnosticReservation?.Dispose();
				exitControlReservation?.Dispose();
				throw;
			}
		}

		throw new MauiToolException(
			ErrorCodes.InternalError,
			$"Could not find free diagnostic/control TCP ports starting at {startingPort}.");
	}

	static ReservedTcpPort ReserveAvailableTcpPort(int startingPort, int maxPort = IPEndPoint.MaxPort)
	{
		if (startingPort < 1 || startingPort > IPEndPoint.MaxPort)
		{
			throw new MauiToolException(
				ErrorCodes.InvalidArgument,
				$"--diagnostic-port must be between 1 and {IPEndPoint.MaxPort}.");
		}

		var finalPort = Math.Min(maxPort, IPEndPoint.MaxPort);
		for (var port = startingPort; port <= finalPort; port++)
		{
			var reservation = TryReserveTcpPort(port);
			if (reservation is not null)
				return reservation;
		}

		throw new MauiToolException(
			ErrorCodes.InternalError,
			$"Could not find a free diagnostic TCP port starting at {startingPort}.");
	}

	static async Task EnsureAdbPortRoutingAsync(
		Device device,
		IOutputFormatter formatter,
		bool useJson,
		bool verbose,
		CancellationToken cancellationToken,
		params int[] ports)
	{
		var adbPath = ResolveAdbPath();
		if (adbPath is null)
		{
			throw MauiToolException.UserActionRequired(
				ErrorCodes.AndroidAdbNotFound,
				"ADB was not found, so the app exit-control port could not be opened on the Android device.",
				[
					"Install the Android SDK platform-tools so adb is available.",
					"Or add adb to PATH and rerun `maui profile`."
				]);
		}

		foreach (var port in ports.Distinct().Where(port => port > 0))
		{
			var portSpec = $"tcp:{port}";
			WriteVerbose(formatter, useJson, verbose, $"Ensuring adb reverse for {device.Id} on {portSpec}.");

			// dotnet-dsrouter handles the diagnostic connection itself. The only remaining
			// manual adb reverse is for the separate app-exit control channel we host in the CLI.
			await ResetAdbPortMappingAsync(adbPath, device.Id, "reverse", portSpec, cancellationToken);
			var reverseResult = await ProcessRunner.RunAsync(
				adbPath,
				["-s", device.Id, "reverse", portSpec, portSpec],
				timeout: s_adbPortForwardTimeout,
				cancellationToken: cancellationToken);

			if (!reverseResult.Success)
			{
				var details = GetProcessFailureDetails(reverseResult);
				if (IsPortBindingConflict(details))
					throw new DiagnosticPortRoutingConflictException(port, "reverse", details);

				throw MauiToolException.UserActionRequired(
					ErrorCodes.InternalError,
					$"Failed to open Android reverse port forwarding for {portSpec} on '{device.Id}'.",
					[
						$"Reconnect the device or emulator and verify `adb -s {device.Id} reverse {portSpec} {portSpec}` succeeds.",
						"Then rerun `maui profile`."
					],
					nativeError: details);
			}
		}
	}

	static async Task RemoveAdbPortRoutingAsync(
		Device device,
		IOutputFormatter formatter,
		bool useJson,
		bool verbose,
		params int[] ports)
	{
		if (ports.Length == 0 || ports.All(port => port < 1))
			return;

		var adbPath = ResolveAdbPath();
		if (adbPath is null)
			return;

		try
		{
			foreach (var port in ports.Distinct().Where(port => port > 0))
			{
				var portSpec = $"tcp:{port}";
				WriteVerbose(formatter, useJson, verbose, $"Removing adb reverse/forward mappings for {device.Id} on {portSpec}.");
				await ResetAdbPortMappingAsync(adbPath, device.Id, "reverse", portSpec, CancellationToken.None);
				await ResetAdbPortMappingAsync(adbPath, device.Id, "forward", portSpec, CancellationToken.None);
			}
		}
		catch
		{
			// Best-effort cleanup only.
		}
	}

	static async Task ResetAdbPortMappingAsync(string adbPath, string deviceId, string direction, string portSpec, CancellationToken cancellationToken)
	{
		var removeArgs = direction switch
		{
			"reverse" => new[] { "-s", deviceId, "reverse", "--remove", portSpec },
			"forward" => new[] { "-s", deviceId, "forward", "--remove", portSpec },
			_ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "Expected 'reverse' or 'forward'.")
		};

		_ = await ProcessRunner.RunAsync(
			adbPath,
			removeArgs,
			timeout: s_adbPortForwardTimeout,
			cancellationToken: cancellationToken);
	}

	static string? ResolveAdbPath()
	{
		var adbPath = ProcessRunner.GetCommandPath("adb");
		if (!string.IsNullOrWhiteSpace(adbPath))
			return adbPath;

		var sdkPath = PlatformDetector.Paths.GetAndroidSdkPath();
		if (string.IsNullOrWhiteSpace(sdkPath))
			return null;

		var extension = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
		var candidate = Path.Combine(sdkPath, "platform-tools", "adb" + extension);
		return File.Exists(candidate) ? candidate : null;
	}

	static ProfilingBuildInjection? TryCreateBuildInjection(string exitControlHost, int exitControlPort, bool injectBootstrap)
	{
		var targetsPath = TryResolveBuildAssetPath(StartupProfilingInjectionTargetsFileName);
		var assemblyPath = TryResolveBuildAssetPath(StartupProfilingAssemblyFileName);
		var sourcePath = TryResolveBuildAssetPath(StartupProfilingInjectionSourceFileName);

		if (targetsPath is null || assemblyPath is null || sourcePath is null)
			return null;

		return new ProfilingBuildInjection(targetsPath, assemblyPath, exitControlHost, exitControlPort, injectBootstrap);
	}

	static string? TryResolveBuildAssetPath(string fileName)
	{
		var baseDirectory = AppContext.BaseDirectory;
		var candidates = new[]
		{
			Path.Combine(baseDirectory, fileName),
			Path.Combine(baseDirectory, "Build", fileName)
		};

		return candidates.FirstOrDefault(File.Exists);
	}

	static string GetProcessFailureDetails(ProcessResult result) =>
		string.IsNullOrWhiteSpace(result.StandardError)
			? result.StandardOutput.Trim()
			: result.StandardError.Trim();

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

	static bool IsPortBindingConflict(string details) =>
		details.Contains("Address already in use", StringComparison.OrdinalIgnoreCase)
		|| details.Contains("cannot bind listener", StringComparison.OrdinalIgnoreCase)
		|| details.Contains("cannot bind socket", StringComparison.OrdinalIgnoreCase);

	static int GetExitControlPort(int diagnosticPort)
	{
		if (diagnosticPort >= IPEndPoint.MaxPort)
		{
			throw new MauiToolException(
				ErrorCodes.InvalidArgument,
				$"Cannot reserve an exit control port after diagnostic port {diagnosticPort}.");
		}

		return checked(diagnosticPort + ExitControlPortOffset);
	}

	static ReservedTcpPort? TryReserveTcpPort(int port)
	{
		try
		{
			var listener = new TcpListener(IPAddress.Loopback, port);
			listener.Start();
			return new ReservedTcpPort(port, listener);
		}
		catch (SocketException)
		{
			return null;
		}
	}

	static async Task WaitForTraceCompletionAsync(
		MonitoredProcess traceProcess,
		bool allowManualStop,
		IOutputFormatter formatter,
		bool useJson,
		bool verbose,
		CancellationToken cancellationToken)
	{
		WriteVerbose(
			formatter,
			useJson,
			verbose,
			allowManualStop
				? $"Waiting for dotnet-trace (PID {traceProcess.Process.Id}) to exit or for a manual stop request."
				: $"Waiting for dotnet-trace (PID {traceProcess.Process.Id}) to complete in non-interactive mode.");

		var processWaitTask = traceProcess.WaitForExitAsync();
		var stopRequested = false;
		if (!allowManualStop)
		{
			await processWaitTask;
		}
		else
		{
			var manualStopSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			ConsoleCancelEventHandler? cancelHandler = null;
			cancelHandler = (_, e) =>
			{
				e.Cancel = true;
				manualStopSignal.TrySetResult(true);
			};

			Console.CancelKeyPress += cancelHandler;
			var readLineTask = Task.Run(() =>
			{
				try
				{
					Console.ReadLine();
				}
				finally
				{
					manualStopSignal.TrySetResult(true);
				}
			}, CancellationToken.None);

			try
			{
				while (true)
				{
					var completedTask = await Task.WhenAny(processWaitTask, manualStopSignal.Task);
					if (completedTask == processWaitTask)
					{
						WriteVerbose(formatter, useJson, verbose, "dotnet-trace exited before any manual stop request was needed.");
						break;
					}

					if (completedTask == manualStopSignal.Task && !traceProcess.Process.HasExited)
					{
						formatter.WriteInfo("Stopping trace and finalizing output...");
						WriteVerbose(formatter, useJson, verbose, "Manual stop requested from the console.");
						stopRequested = true;
						await RequestTraceStopAsync(traceProcess.Process, formatter, useJson, verbose);
						break;
					}
				}
			}
			finally
			{
				Console.CancelKeyPress -= cancelHandler;
				manualStopSignal.TrySetResult(true);
				try
				{
					await readLineTask.WaitAsync(TimeSpan.FromMilliseconds(100));
				}
				catch
				{
					// Ignore any late console-read completions.
				}
			}
		}

		if (stopRequested)
		{
			try
			{
				await processWaitTask.WaitAsync(s_traceStopTimeout);
			}
			catch (TimeoutException)
			{
				throw new MauiToolException(
					ErrorCodes.InternalError,
					$"dotnet-trace did not exit within {s_traceStopTimeout.TotalSeconds:0}s after the stop request.",
					nativeError: traceProcess.GetCombinedOutput());
			}
		}

		WriteVerbose(formatter, useJson, verbose, $"dotnet-trace exited with code {traceProcess.Process.ExitCode}.");

		if (stopRequested && traceProcess.Process.ExitCode == 130)
		{
			WriteVerbose(
				formatter,
				useJson,
				verbose,
				"dotnet-trace exited with SIGINT after the stop request; treating the canceled collector exit as a successful finalized trace.");
			return;
		}

		if (traceProcess.Process.ExitCode != 0)
		{
			throw new MauiToolException(
				ErrorCodes.InternalError,
				$"dotnet-trace exited with code {traceProcess.Process.ExitCode}.",
				nativeError: traceProcess.GetCombinedOutput());
		}
	}

	static async Task RequestTraceStopAsync(Process traceProcess, IOutputFormatter formatter, bool useJson, bool verbose)
	{
		if (traceProcess.HasExited)
		{
			WriteVerbose(formatter, useJson, verbose, "dotnet-trace had already exited before the stop request was sent.");
			return;
		}

		WriteVerbose(formatter, useJson, verbose, $"Sending a stop newline to dotnet-trace stdin (PID {traceProcess.Id}).");
		try
		{
			await traceProcess.StandardInput.WriteLineAsync();
			await traceProcess.StandardInput.FlushAsync();
		}
		catch (ObjectDisposedException)
		{
			WriteVerbose(formatter, useJson, verbose, "dotnet-trace stdin was already closed before the stop request.");
		}

		try
		{
			traceProcess.StandardInput.Close();
		}
		catch (ObjectDisposedException)
		{
			// Already closed.
		}

		WriteVerbose(formatter, useJson, verbose, "Closed dotnet-trace stdin after the stop request.");

		await Task.Delay(s_traceStopInterruptDelay);
		if (!traceProcess.HasExited)
		{
			WriteVerbose(
				formatter,
				useJson,
				verbose,
				$"dotnet-trace was still running {s_traceStopInterruptDelay.TotalSeconds:0.#}s after the stdin stop request; sending SIGINT to the process tree.");
			await SendInterruptToProcessTreeAsync(traceProcess, formatter, useJson, verbose);
		}
	}

	static async Task SendInterruptToProcessTreeAsync(Process rootProcess, IOutputFormatter formatter, bool useJson, bool verbose)
	{
		if (rootProcess.HasExited)
			return;

		if (OperatingSystem.IsWindows())
		{
			WriteVerbose(formatter, useJson, verbose, "Skipping SIGINT fallback on Windows.");
			return;
		}

		var pids = await GetDescendantProcessIdsAsync(rootProcess.Id);
		pids.Add(rootProcess.Id);

		foreach (var pid in pids.Distinct().OrderByDescending(pid => pid))
		{
			WriteVerbose(formatter, useJson, verbose, $"Sending SIGINT to PID {pid}.");
			_ = await ProcessRunner.RunAsync(
				"kill",
				["-INT", pid.ToString()],
				timeout: TimeSpan.FromSeconds(5),
				cancellationToken: CancellationToken.None);
		}
	}

	static async Task<List<int>> GetDescendantProcessIdsAsync(int rootPid)
	{
		if (OperatingSystem.IsWindows())
			return [];

		var result = await ProcessRunner.RunAsync(
			"ps",
			["-eo", "pid=,ppid="],
			timeout: TimeSpan.FromSeconds(5),
			cancellationToken: CancellationToken.None);

		if (!result.Success)
			return [];

		var childrenByParent = new Dictionary<int, List<int>>();
		var lines = result.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		foreach (var line in lines)
		{
			var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
			if (parts.Length != 2
				|| !int.TryParse(parts[0], out var pid)
				|| !int.TryParse(parts[1], out var parentPid))
			{
				continue;
			}

			if (!childrenByParent.TryGetValue(parentPid, out var children))
			{
				children = [];
				childrenByParent[parentPid] = children;
			}

			children.Add(pid);
		}

		var descendants = new List<int>();
		var queue = new Queue<int>();
		queue.Enqueue(rootPid);

		while (queue.Count > 0)
		{
			var parent = queue.Dequeue();
			if (!childrenByParent.TryGetValue(parent, out var children))
				continue;

			foreach (var child in children)
			{
				descendants.Add(child);
				queue.Enqueue(child);
			}
		}

		return descendants;
	}

	static async Task StopBackgroundProcessAsync(Process? process, string processName, IOutputFormatter formatter, bool useJson, bool verbose)
	{
		if (process is null || process.HasExited)
			return;

		WriteVerbose(formatter, useJson, verbose, $"Stopping {processName} (PID {process.Id}).");
		try
		{
			process.Kill(entireProcessTree: true);
			await process.WaitForExitAsync();
			WriteVerbose(formatter, useJson, verbose, $"{processName} exited with code {process.ExitCode} during cleanup.");
		}
		catch (InvalidOperationException)
		{
			// The process already exited between the check and the kill call.
		}
	}

	static void WriteVerbose(IOutputFormatter formatter, bool useJson, bool verbose, string message)
	{
		if (verbose && !useJson)
			formatter.WriteProgress($"[debug] {message}");
	}

	static string FormatCommandLine(string fileName, IEnumerable<string> arguments) =>
		string.Join(" ", [QuoteForDisplay(fileName), .. arguments.Select(QuoteForDisplay)]);

	static string QuoteForDisplay(string value) =>
		value.Any(ch => char.IsWhiteSpace(ch) || ch is '"' or '\'')
			? $"\"{value.Replace("\"", "\\\"")}\""
			: value;

	static Exception CreateProcessFailureException(string commandName, ProcessResult result)
	{
		var details = string.IsNullOrWhiteSpace(result.StandardError)
			? result.StandardOutput.Trim()
			: result.StandardError.Trim();

		return new MauiToolException(
			ErrorCodes.InternalError,
			$"{commandName} failed with exit code {result.ExitCode}.",
			nativeError: details);
	}

	internal static bool IsTargetFrameworkCompatible(string tfm, string platform) => Platforms.Normalize(platform) switch
	{
		Platforms.All => true,
		Platforms.Android => tfm.Contains("-android", StringComparison.OrdinalIgnoreCase),
		Platforms.iOS => tfm.Contains("-ios", StringComparison.OrdinalIgnoreCase),
		Platforms.MacCatalyst => tfm.Contains("-maccatalyst", StringComparison.OrdinalIgnoreCase),
		Platforms.Windows => tfm.Contains("-windows", StringComparison.OrdinalIgnoreCase),
		_ => false
	};

	internal static string ResolveProfilePlatform(string requestedPlatform, string framework)
	{
		var normalizedPlatform = Platforms.Normalize(requestedPlatform);
		if (!string.Equals(normalizedPlatform, Platforms.All, StringComparison.OrdinalIgnoreCase))
			return normalizedPlatform;

		return InferPlatformFromTargetFramework(framework) ?? Platforms.All;
	}

	internal static ProfileTransportConfiguration ResolveProfileTransport(string platform, Device device)
	{
		var normalizedPlatform = Platforms.Normalize(platform);
		return normalizedPlatform switch
		{
			Platforms.Android => new ProfileTransportConfiguration(
				Platform: Platforms.Android,
				DiagnosticAddress: device.IsEmulator ? "10.0.2.2" : IPAddress.Loopback.ToString(),
				DiagnosticListenMode: "connect",
				DsrouterKind: "server-server",
				DsrouterRuntimeEndpointOption: "-tcps",
				DsrouterForwardPort: "Android",
				RequiresManualExitControlPortRouting: !device.IsEmulator),
			Platforms.iOS => new ProfileTransportConfiguration(
				Platform: Platforms.iOS,
				DiagnosticAddress: IPAddress.Loopback.ToString(),
				DiagnosticListenMode: "listen",
				DsrouterKind: "server-client",
				DsrouterRuntimeEndpointOption: "-tcpc",
				DsrouterForwardPort: device.IsEmulator ? null : "iOS",
				RequiresManualExitControlPortRouting: false),
			_ => throw new MauiToolException(
				ErrorCodes.PlatformNotSupported,
				$"Startup profiling is not implemented yet for platform '{platform}'.")
		};
	}

	internal static string? InferPlatformFromTargetFramework(string tfm)
	{
		if (string.IsNullOrWhiteSpace(tfm))
			return null;

		if (tfm.Contains("-android", StringComparison.OrdinalIgnoreCase))
			return Platforms.Android;
		if (tfm.Contains("-ios", StringComparison.OrdinalIgnoreCase))
			return Platforms.iOS;
		if (tfm.Contains("-maccatalyst", StringComparison.OrdinalIgnoreCase))
			return Platforms.MacCatalyst;
		if (tfm.Contains("-windows", StringComparison.OrdinalIgnoreCase))
			return Platforms.Windows;

		return null;
	}

	static int GetFrameworkPlatformPriority(string tfm) => InferPlatformFromTargetFramework(tfm) switch
	{
		Platforms.Android => 0,
		Platforms.iOS => 1,
		Platforms.MacCatalyst => 2,
		Platforms.Windows => 3,
		_ => 4
	};

	static string FormatFrameworkPromptChoice(string tfm)
	{
		var platform = InferPlatformFromTargetFramework(tfm);
		return string.IsNullOrWhiteSpace(platform)
			? $"[bold]{Markup.Escape(tfm)}[/]"
			: $"[bold]{Markup.Escape(tfm)}[/] [dim]({Markup.Escape(platform)})[/]";
	}

	internal static Version GetFrameworkSortKey(string tfm)
	{
		var match = Regex.Match(tfm, @"net(?<version>\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
		if (!match.Success)
			return new Version(0, 0);

		return Version.TryParse(match.Groups["version"].Value, out var parsed)
			? parsed
			: new Version(0, 0);
	}
}

internal sealed record ResolvedMauiProject
{
	public required string ProjectPath { get; init; }
	public required string ProjectDirectory { get; init; }
	public required string ProjectName { get; init; }
	public required IReadOnlyList<string> TargetFrameworks { get; init; }
}

internal sealed record StoppingEventConfiguration(
	string? ProviderName,
	string? EventName,
	string? PayloadFilter,
	bool AutoSelected);

internal enum TraceOutputFormat
{
	NetTrace,
	Speedscope
}

internal sealed record ProfilingBuildInjection(
	string TargetsPath,
	string AssemblyPath,
	string ExitControlHost,
	int ExitControlPort,
	bool InjectBootstrap);

internal sealed record ProfileTransportConfiguration(
	string Platform,
	string DiagnosticAddress,
	string DiagnosticListenMode,
	string DsrouterKind,
	string DsrouterRuntimeEndpointOption,
	string? DsrouterForwardPort,
	bool RequiresManualExitControlPortRouting);

internal static class MauiProjectResolver
{
	public static ResolvedMauiProject Resolve(string? projectOrDirectory)
	{
		var projectPath = ResolveProjectPath(projectOrDirectory);
		var realProjectPath = ResolvePath(projectPath);
		return new ResolvedMauiProject
		{
			ProjectPath = realProjectPath,
			ProjectDirectory = Path.GetDirectoryName(realProjectPath) ?? Environment.CurrentDirectory,
			ProjectName = Path.GetFileNameWithoutExtension(realProjectPath),
			TargetFrameworks = GetTargetFrameworks(realProjectPath)
		};
	}

	// Resolve any symlinks in ALL components of the path (e.g. /tmp → /private/tmp on macOS).
	// File.ResolveLinkTarget only resolves if the final file is a symlink; this version
	// handles symlinks anywhere in the path. MSBuild's XAML source generators fail to
	// resolve relative resource paths when intermediate path components are symlinks.
	static string ResolvePath(string path)
	{
		if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
			return path;

		try
		{
			path = Path.GetFullPath(path);
			var root = Path.GetPathRoot(path) ?? "/";
			var parts = path[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
			var current = root;
			foreach (var part in parts)
			{
				current = Path.Combine(current, part);
				// Resolve if this component is itself a symlink
				var resolved = Directory.Exists(current)
					? Directory.ResolveLinkTarget(current, returnFinalTarget: false)?.FullName
					: File.Exists(current)
						? File.ResolveLinkTarget(current, returnFinalTarget: false)?.FullName
						: null;
				if (resolved != null)
					current = resolved;
			}
			return current;
		}
		catch
		{
			return path;
		}
	}

	static string ResolveProjectPath(string? projectOrDirectory)
	{
		var candidate = string.IsNullOrWhiteSpace(projectOrDirectory)
			? Environment.CurrentDirectory
			: Path.GetFullPath(projectOrDirectory);

		if (File.Exists(candidate))
		{
			if (!string.Equals(Path.GetExtension(candidate), ".csproj", StringComparison.OrdinalIgnoreCase))
			{
				throw new MauiToolException(
					ErrorCodes.InvalidArgument,
					$"'{candidate}' is not a .csproj file.");
			}

			return candidate;
		}

		if (!Directory.Exists(candidate))
		{
			throw new MauiToolException(
				ErrorCodes.InvalidArgument,
				$"Could not find project path '{candidate}'.");
		}

		var projects = Directory.GetFiles(candidate, "*.csproj", SearchOption.TopDirectoryOnly);
		if (projects.Length == 0)
		{
			throw MauiToolException.UserActionRequired(
				ErrorCodes.InvalidArgument,
				$"No .csproj file was found in '{candidate}'.",
				[
					"Run the command from your app directory.",
					"Or pass --project <path-to-your-app.csproj>."
				]);
		}

		if (projects.Length > 1)
		{
			throw new MauiToolException(
				ErrorCodes.InvalidArgument,
				$"Multiple .csproj files were found in '{candidate}'. Please specify one explicitly with --project.");
		}

		return Path.GetFullPath(projects[0]);
	}

	internal static IReadOnlyList<string> GetTargetFrameworks(string projectPath)
	{
		var frameworks = GetTargetFrameworksFromEvaluatedMsbuild(projectPath);
		if (frameworks.Count > 0)
			return frameworks;

		frameworks = GetTargetFrameworksFromProjectFile(projectPath);
		if (frameworks.Count > 0)
			return frameworks;

		throw new MauiToolException(
			ErrorCodes.PlatformNotSupported,
			$"Could not determine any target frameworks for '{projectPath}'.");
	}

	internal static string? GetAndroidApplicationId(string projectPath, string framework, string configuration)
	{
		var manifestPath = Path.Combine(
			Path.GetDirectoryName(projectPath) ?? Environment.CurrentDirectory,
			"obj",
			configuration,
			framework,
			"AndroidManifest.xml");

		if (File.Exists(manifestPath))
		{
			try
			{
				var manifest = XDocument.Load(manifestPath);
				var packageName = manifest.Root?.Attribute("package")?.Value;
				if (!string.IsNullOrWhiteSpace(packageName))
					return packageName.Trim();
			}
			catch
			{
				// Fall back to project-file parsing below.
			}
		}

		try
		{
			var document = XDocument.Load(projectPath);
			var applicationId = document
				.Descendants()
				.FirstOrDefault(element => element.Name.LocalName.Equals("ApplicationId", StringComparison.OrdinalIgnoreCase))
				?.Value;

			return string.IsNullOrWhiteSpace(applicationId)
				? null
				: applicationId.Trim();
		}
		catch
		{
			return null;
		}
	}

	internal static bool HasPackageReference(string projectPath, string packageId)
	{
		try
		{
			var document = XDocument.Load(projectPath);
			return document
				.Descendants()
				.Where(element => element.Name.LocalName.Equals("PackageReference", StringComparison.OrdinalIgnoreCase))
				.Any(element =>
				{
					var include = element.Attribute("Include")?.Value ?? element.Attribute("Update")?.Value;
					return string.Equals(include, packageId, StringComparison.OrdinalIgnoreCase);
				});
		}
		catch
		{
			return false;
		}
	}

	static IReadOnlyList<string> GetTargetFrameworksFromEvaluatedMsbuild(string projectPath)
	{
		var result = ProcessRunner.RunSync(
			"dotnet",
			[
				"msbuild",
				projectPath,
				"-nologo",
				"-getProperty:TargetFramework",
				"-getProperty:TargetFrameworks"
			],
			timeout: TimeSpan.FromSeconds(30));

		if (!result.Success || string.IsNullOrWhiteSpace(result.StandardOutput))
			return [];

		try
		{
			using var document = JsonDocument.Parse(result.StandardOutput);
			if (!document.RootElement.TryGetProperty("Properties", out var properties))
				return [];

			var values = new List<string>();
			if (properties.TryGetProperty("TargetFramework", out var targetFramework))
				values.AddRange(SplitTargetFrameworks(targetFramework.GetString()));
			if (properties.TryGetProperty("TargetFrameworks", out var targetFrameworks))
				values.AddRange(SplitTargetFrameworks(targetFrameworks.GetString()));

			return values
				.Where(value => !string.IsNullOrWhiteSpace(value))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();
		}
		catch (JsonException)
		{
			return [];
		}
	}

	static IReadOnlyList<string> GetTargetFrameworksFromProjectFile(string projectPath)
	{
		var document = XDocument.Load(projectPath);
		return document
			.Descendants()
			.Where(element => element.Name.LocalName is "TargetFramework" or "TargetFrameworks")
			.SelectMany(element => SplitTargetFrameworks(element.Value))
			.Where(value => !string.IsNullOrWhiteSpace(value) && !value.Contains("$(", StringComparison.Ordinal))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	static IEnumerable<string> SplitTargetFrameworks(string? rawValue) =>
		(rawValue ?? string.Empty)
			.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

internal sealed class ReservedTcpPort(int port, TcpListener listener) : IDisposable
{
	bool _disposed;
	TcpListener? _listener = listener;

	public int Port { get; } = port;

	public TcpListener DetachListener()
	{
		if (_disposed || _listener is null)
			throw new ObjectDisposedException(nameof(ReservedTcpPort));

		var detached = _listener;
		_listener = null;
		_disposed = true;
		return detached;
	}

	public void Dispose()
	{
		if (_disposed)
			return;

		_listener?.Stop();
		_listener = null;
		_disposed = true;
	}
}

internal sealed class ReservedProfilePorts(
	int diagnosticPort,
	int exitControlPort,
	ReservedTcpPort diagnosticReservation,
	ReservedTcpPort exitControlReservation) : IDisposable
{
	public int DiagnosticPort { get; } = diagnosticPort;
	public int ExitControlPort { get; } = exitControlPort;
	public ReservedTcpPort DiagnosticReservation { get; } = diagnosticReservation;
	public ReservedTcpPort ExitControlReservation { get; } = exitControlReservation;

	public void Dispose()
	{
		DiagnosticReservation.Dispose();
		ExitControlReservation.Dispose();
	}
}

internal sealed class DiagnosticPortRoutingConflictException(int port, string direction, string details)
	: Exception($"Diagnostic port {port} was unavailable during adb {direction} routing.")
{
	public int Port { get; } = port;
	public string Direction { get; } = direction;
	public string Details { get; } = details;
}

internal sealed class ExitControlServer : IDisposable
{
	readonly TcpListener _listener;
	readonly Task<TcpClient?> _acceptTask;
	readonly TaskCompletionSource<bool> _clientClosedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
	readonly IOutputFormatter _formatter;
	readonly bool _useJson;
	readonly bool _verbose;
	bool _disposed;
	TcpClient? _client;

	ExitControlServer(TcpListener listener, IOutputFormatter formatter, bool useJson, bool verbose)
	{
		_listener = listener;
		_formatter = formatter;
		_useJson = useJson;
		_verbose = verbose;
		_acceptTask = AcceptClientAsync();
	}

	public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

	public static ExitControlServer Attach(ReservedTcpPort reservation, IOutputFormatter formatter, bool useJson, bool verbose) =>
		new(reservation.DetachListener(), formatter, useJson, verbose);

	public async Task<bool> TryRequestExitAsync(TimeSpan connectTimeout, TimeSpan commandTimeout, CancellationToken cancellationToken)
	{
		var client = await WaitForClientAsync(connectTimeout, cancellationToken);
		if (client is null)
			return false;

		try
		{
			LogVerbose($"Sending graceful exit command over the startup profiling control channel on port {Port}.");
			using var writer = new StreamWriter(client.GetStream(), Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
			await writer.WriteLineAsync("exit");
			await writer.FlushAsync();

			await _clientClosedTcs.Task.WaitAsync(commandTimeout, cancellationToken);
			LogVerbose("The profiled app acknowledged the exit command and closed the control channel.");
			return true;
		}
		catch (TimeoutException)
		{
			LogVerbose("Timed out waiting for the profiled app to close after the exit command.");
			return true;
		}
		catch (OperationCanceledException)
		{
			return false;
		}
		catch (ObjectDisposedException)
		{
			return false;
		}
	}

	async Task<TcpClient?> WaitForClientAsync(TimeSpan timeout, CancellationToken cancellationToken)
	{
		try
		{
			var completed = await Task.WhenAny(_acceptTask, Task.Delay(timeout, cancellationToken));
			if (completed != _acceptTask)
				return null;

			_client ??= await _acceptTask;
			return _client;
		}
		catch (OperationCanceledException)
		{
			return null;
		}
	}

	async Task<TcpClient?> AcceptClientAsync()
	{
		try
		{
			var client = await _listener.AcceptTcpClientAsync();
			LogVerbose($"Startup profiling exit control client connected from {client.Client.RemoteEndPoint}.");
			_ = MonitorClientAsync(client);
			return client;
		}
		catch (ObjectDisposedException)
		{
			return null;
		}
		catch (SocketException ex)
		{
			LogVerbose($"Exit control server stopped accepting clients: {ex.Message}");
			return null;
		}
	}

	async Task MonitorClientAsync(TcpClient client)
	{
		try
		{
			using var reader = new StreamReader(client.GetStream(), Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
			while (true)
			{
				var message = await reader.ReadLineAsync();
				if (message is null)
					break;

				LogVerbose($"Exit control channel message received: '{message.Trim()}'.");
			}
		}
		catch (IOException ex)
		{
			LogVerbose($"Exit control channel read loop ended: {ex.Message}");
		}
		catch (ObjectDisposedException)
		{
		}
		finally
		{
			_clientClosedTcs.TrySetResult(true);
		}
	}

	void LogVerbose(string message)
	{
		if (_verbose && !_useJson)
			_formatter.WriteProgress($"[debug] {message}");
	}

	public void Dispose()
	{
		if (_disposed)
			return;

		try
		{
			_client?.Dispose();
			_listener.Stop();
		}
		catch
		{
			// Best-effort cleanup only.
		}

		_disposed = true;
	}
}

internal sealed class MonitoredProcess : IDisposable
{
	readonly Task _stdoutPump;
	readonly Task _stderrPump;

	MonitoredProcess(
		Process process,
		StringBuilder standardOutput,
		StringBuilder standardError,
		Task stdoutPump,
		Task stderrPump)
	{
		Process = process;
		StandardOutput = standardOutput;
		StandardError = standardError;
		_stdoutPump = stdoutPump;
		_stderrPump = stderrPump;
	}

	public Process Process { get; }
	public StringBuilder StandardOutput { get; }
	public StringBuilder StandardError { get; }

	public static MonitoredProcess Attach(
		Process process,
		IOutputFormatter formatter,
		bool useJson,
		bool verbose,
		string prefix,
		CancellationToken cancellationToken,
		Action<string>? onStdoutLine = null,
		Action<string>? onStderrLine = null)
	{
		var stdout = new StringBuilder();
		var stderr = new StringBuilder();

		var stdoutPump = PumpStreamAsync(
			process.StandardOutput,
			stdout,
			line =>
			{
				onStdoutLine?.Invoke(line);
				if (verbose && !useJson)
					formatter.WriteProgress($"[{prefix}] {line}");
			},
			cancellationToken);

		var stderrPump = PumpStreamAsync(
			process.StandardError,
			stderr,
			line =>
			{
				onStderrLine?.Invoke(line);
				if (verbose && !useJson)
					formatter.WriteProgress($"[{prefix}:stderr] {line}");
			},
			cancellationToken);

		return new MonitoredProcess(process, stdout, stderr, stdoutPump, stderrPump);
	}

	public async Task WaitForExitAsync()
	{
		await Process.WaitForExitAsync();
		await Task.WhenAll(_stdoutPump, _stderrPump);
	}

	public string GetCombinedOutput()
	{
		var builder = new StringBuilder();
		if (StandardOutput.Length > 0)
			builder.AppendLine(StandardOutput.ToString().Trim());
		if (StandardError.Length > 0)
			builder.AppendLine(StandardError.ToString().Trim());
		return builder.ToString().Trim();
	}

	public void Dispose() => Process.Dispose();

	static async Task PumpStreamAsync(
		StreamReader reader,
		StringBuilder buffer,
		Action<string>? onLine,
		CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			var line = await reader.ReadLineAsync(cancellationToken);
			if (line == null)
				break;

			buffer.AppendLine(line);
			onLine?.Invoke(line);
		}
	}
}

internal sealed record MauiProfileResult
{
	public required string ProjectPath { get; init; }
	public required string ProjectName { get; init; }
	public required string Framework { get; init; }
	public required string Platform { get; init; }
	public required string DeviceId { get; init; }
	public required string DeviceName { get; init; }
	public required string Configuration { get; init; }
	public required string Format { get; init; }
	public required string OutputPath { get; init; }
	public string? RawTracePath { get; init; }
	public required string DsrouterKind { get; init; }
	public required string DiagnosticAddress { get; init; }
	public required int DiagnosticPort { get; init; }
	public required bool UsedStoppingEvent { get; init; }
	public required DateTimeOffset StartedAtUtc { get; init; }
	public required DateTimeOffset CompletedAtUtc { get; init; }
}
