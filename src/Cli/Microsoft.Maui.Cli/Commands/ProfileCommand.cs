// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Diagnostics;
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
	const int DefaultDiagnosticPort = 9000;

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
			Description = "Android device or emulator serial to target (defaults to the only running device)"
		};
		var outputOption = new Option<string?>("--output", "-o")
		{
			Description = "Output .nettrace path (default: <project>_<timestamp>.nettrace in the current directory)"
		};
		var configurationOption = new Option<string>("--configuration", "-c")
		{
			Description = "Build configuration to use",
			DefaultValueFactory = _ => "Release"
		};
		var platformOption = new Option<string>("--platform")
		{
			Description = "Target platform to profile (currently android only)",
			DefaultValueFactory = _ => Platforms.Android
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
			Description = "TCP port used for the diagnostic connection between the app and dotnet-dsrouter",
			DefaultValueFactory = _ => DefaultDiagnosticPort
		};
		var stoppingEventProviderOption = new Option<string?>("--stopping-event-provider-name")
		{
			Description = "Stop tracing when the first matching event provider emits an event. " +
				"Use 'Microsoft.Maui.StartupProfiling' with the Microsoft.Maui.StartupProfiling NuGet package for automatic stop."
		};
		var stoppingEventNameOption = new Option<string?>("--stopping-event-event-name")
		{
			Description = "Optional event name to combine with --stopping-event-provider-name. " +
				"Use 'StartupComplete' with the Microsoft.Maui.StartupProfiling NuGet package."
		};
		var stoppingEventPayloadFilterOption = new Option<string?>("--stopping-event-payload-filter")
		{
			Description = "Optional payload filter (key:value,key:value) to combine with the stopping event options"
		};

		var command = new Command("profile", "Collect a startup .nettrace for a .NET MAUI app")
		{
			projectOption,
			frameworkOption,
			deviceOption,
			outputOption,
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
				var platform = Platforms.Normalize(parseResult.GetValue(platformOption));
				if (!string.Equals(platform, Platforms.Android, StringComparison.OrdinalIgnoreCase))
				{
					throw MauiToolException.UserActionRequired(
						ErrorCodes.PlatformNotSupported,
						$"Startup profiling is currently implemented only for '{Platforms.Android}'.",
						[
							"Use --platform android for the current implementation.",
							"Support for iOS and Mac Catalyst can be added in a future iteration."
						]);
				}

				var project = MauiProjectResolver.Resolve(parseResult.GetValue(projectOption));
				var framework = ResolveTargetFramework(
					project,
					parseResult.GetValue(frameworkOption),
					platform,
					isCi || useJson,
					formatter as SpectreOutputFormatter);

				ValidateStoppingEventOptions(
					parseResult.GetValue(stoppingEventProviderOption),
					parseResult.GetValue(stoppingEventNameOption),
					parseResult.GetValue(stoppingEventPayloadFilterOption));

				var duration = parseResult.GetValue(durationOption);
				var stoppingEventProvider = parseResult.GetValue(stoppingEventProviderOption);
				if ((isCi || useJson) && duration is null && string.IsNullOrWhiteSpace(stoppingEventProvider))
				{
					throw MauiToolException.UserActionRequired(
						ErrorCodes.InvalidArgument,
						"Non-interactive profile runs require either --duration or --stopping-event-provider-name so the trace can stop deterministically.",
						[
							"Add --duration 00:00:15 for a fixed-length startup trace.",
							"Or pass --stopping-event-provider-name/--stopping-event-event-name to stop on an EventSource marker."
						]);
				}

				ValidateDnxAvailable();

				var device = await ResolveAndroidDeviceAsync(
					parseResult.GetValue(deviceOption),
					Program.DeviceManager,
					isCi || useJson,
					formatter as SpectreOutputFormatter,
					cancellationToken);

				var outputPath = ResolveOutputPath(project.ProjectName, parseResult.GetValue(outputOption));
				var result = await RunProfileAsync(
					project,
					framework,
					device,
					outputPath,
					parseResult.GetValue(configurationOption) ?? "Release",
					parseResult.GetValue(traceProfileOption),
					parseResult.GetValue(noBuildOption),
					parseResult.GetValue(diagnosticPortOption),
					duration,
					stoppingEventProvider,
					parseResult.GetValue(stoppingEventNameOption),
					parseResult.GetValue(stoppingEventPayloadFilterOption),
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
					formatter.WriteSuccess($"Startup trace saved to {result.OutputPath}");
				}

				return 0;
			}
			catch (Exception ex)
			{
				formatter.WriteError(ex);
				return 1;
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
			.ToList();

		if (candidates.Count == 0)
		{
			throw new MauiToolException(
				ErrorCodes.PlatformNotSupported,
				$"No target framework in {Path.GetFileName(project.ProjectPath)} matches platform '{platform}'.");
		}

		if (candidates.Count == 1 || nonInteractive || spectre == null)
			return candidates[0];

		return spectre.Prompt(
			new SelectionPrompt<string>()
				.Title("[bold]Select the target framework to profile[/]")
				.HighlightStyle(new Style(Color.DodgerBlue1))
				.AddChoices(candidates));
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

	static void ValidateDnxAvailable()
	{
		if (ProcessRunner.GetCommandPath("dnx") == null)
		{
			throw MauiToolException.UserActionRequired(
				ErrorCodes.DiagnosticsToolNotFound,
				"'dnx' was not found in PATH.",
				[
					"'dnx' ships with .NET 10 SDK and later.",
					"Update your .NET SDK: https://dot.net/download"
				]);
		}
	}

	/// <summary>
	/// Starts dotnet-dsrouter via <c>dnx</c> and waits for it to print its PID to stdout.
	/// dotnet-dsrouter always prints a line containing <c>pid=&lt;N&gt;</c> shortly after startup.
	/// </summary>
	static async Task<(Process DnxProcess, int DsrouterPid)> StartDsrouterAsync(
		string dsrouterKind,
		CancellationToken cancellationToken)
	{
		var startInfo = new ProcessStartInfo
		{
			FileName = "dnx",
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			RedirectStandardInput = true,
			CreateNoWindow = true
		};

		// "dnx -y dotnet-dsrouter -- android-emu"
		// The "--" separator prevents dnx from interpreting dsrouter's own flags.
		startInfo.ArgumentList.Add("-y");
		startInfo.ArgumentList.Add("dotnet-dsrouter");
		startInfo.ArgumentList.Add("--");
		startInfo.ArgumentList.Add(dsrouterKind);

		var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
		if (!process.Start())
			throw new MauiToolException(ErrorCodes.InternalError, "Failed to start dotnet-dsrouter via dnx.");

		// dotnet-dsrouter prints "pid=<N>" to stdout shortly after startup.
		// Wait for that line to discover the PID we pass to dotnet-trace.
		var pidRegex = new Regex(@"\bpid=(\d+)\b");
		var pidTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

		_ = Task.Run(async () =>
		{
			try
			{
				string? line;
				while ((line = await process.StandardOutput.ReadLineAsync(cancellationToken)) != null)
				{
					var m = pidRegex.Match(line);
					if (m.Success && int.TryParse(m.Groups[1].Value, out var pid))
					{
						pidTcs.TrySetResult(pid);
						// Keep draining stdout so the pipe doesn't block.
					}
				}
			}
			catch (OperationCanceledException)
			{
				pidTcs.TrySetCanceled(cancellationToken);
			}
			catch (Exception ex)
			{
				pidTcs.TrySetException(ex);
			}
		}, cancellationToken);

		// Drain stderr in background (dsrouter writes info logs there).
		_ = Task.Run(async () =>
		{
			try
			{
				while (await process.StandardError.ReadLineAsync(cancellationToken) != null) { }
			}
			catch { }
		}, cancellationToken);

		using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeoutCts.CancelAfter(s_dsrouterStartupTimeout);

		int dsrouterPid;
		try
		{
			dsrouterPid = await pidTcs.Task.WaitAsync(timeoutCts.Token);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			process.Kill(entireProcessTree: true);
			throw new MauiToolException(
				ErrorCodes.InternalError,
				$"dotnet-dsrouter did not report its PID within {s_dsrouterStartupTimeout.TotalSeconds}s.");
		}

		return (process, dsrouterPid);
	}

	internal static string ResolveOutputPath(string projectName, string? requestedOutput)
	{
		if (string.IsNullOrWhiteSpace(requestedOutput))
		{
			var safeProjectName = string.IsNullOrWhiteSpace(projectName) ? "maui-startup-profile" : projectName;
			var defaultName = $"{safeProjectName}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.nettrace";
			return Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, defaultName));
		}

		var fullPath = Path.GetFullPath(requestedOutput);
		if (string.IsNullOrWhiteSpace(Path.GetExtension(fullPath)))
			fullPath += ".nettrace";
		return fullPath;
	}

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

	static async Task<MauiProfileResult> RunProfileAsync(
		ResolvedMauiProject project,
		string framework,
		Device device,
		string outputPath,
		string configuration,
		string? traceProfile,
		bool noBuild,
		int diagnosticPort,
		TimeSpan? duration,
		string? stoppingEventProvider,
		string? stoppingEventName,
		string? stoppingEventPayloadFilter,
		IOutputFormatter formatter,
		bool useJson,
		bool verbose,
		CancellationToken cancellationToken)
	{
		var outputDirectory = Path.GetDirectoryName(outputPath);
		if (string.IsNullOrWhiteSpace(outputDirectory))
		{
			throw new MauiToolException(
				ErrorCodes.InvalidArgument,
				$"Could not determine the output directory for '{outputPath}'.");
		}

		Directory.CreateDirectory(outputDirectory);

		var dsrouterKind = device.IsEmulator ? "android-emu" : "android";
		var diagnosticAddress = device.IsEmulator ? "10.0.2.2" : "127.0.0.1";
		var startedAtUtc = DateTimeOffset.UtcNow;

		if (!useJson)
		{
			formatter.WriteInfo($"Project: {project.ProjectPath}");
			formatter.WriteInfo($"Framework: {framework}");
			formatter.WriteInfo($"Device: {device.Name} ({device.Id})");
			formatter.WriteInfo($"Output: {outputPath}");
		}

		// Phase 1: Build (compile + package) without running, so the deploy+launch step
		// later is fast (seconds) and won't race with dotnet-trace's connection timeout.
		if (!noBuild)
		{
			if (!useJson)
				formatter.WriteInfo("Building the app...");

			var buildArgs = BuildCompileArguments(project.ProjectPath, framework, configuration);
			var buildResult = formatter is SpectreOutputFormatter spectreForBuild && !useJson
				? await spectreForBuild.StatusAsync(
					"Building the app...",
					() => ProcessRunner.RunAsync("dotnet", buildArgs, project.ProjectDirectory, timeout: s_buildLaunchTimeout, environmentVariablesToRemove: s_msbuildSdkEnvVars, cancellationToken: cancellationToken))
				: await ProcessRunner.RunAsync("dotnet", buildArgs, project.ProjectDirectory, timeout: s_buildLaunchTimeout, environmentVariablesToRemove: s_msbuildSdkEnvVars, cancellationToken: cancellationToken);

			if (!buildResult.Success)
				throw CreateProcessFailureException("dotnet build", buildResult);
		}

		// Phase 2: Start dsrouter, then dotnet-trace AFTER build artifacts exist, then
		// immediately deploy+launch. Deploy only takes seconds, well within dotnet-trace's
		// connection timeout.
		var (dsrouterProcess, dsrouterPid) = await StartDsrouterAsync(dsrouterKind, cancellationToken);
		using var dsrouterOwner = dsrouterProcess; // ensure cleanup on scope exit

		using var traceProcess = StartTraceCollector(
			project.ProjectDirectory,
			outputPath,
			dsrouterPid,
			device.Id,
			traceProfile,
			duration,
			stoppingEventProvider,
			stoppingEventName,
			stoppingEventPayloadFilter,
			formatter,
			useJson,
			verbose,
			cancellationToken);

		await EnsureTraceCollectorStartedAsync(traceProcess, cancellationToken);

		var launchArgs = BuildLaunchArguments(
			project.ProjectPath,
			framework,
			configuration,
			device,
			diagnosticAddress,
			diagnosticPort);

		if (!useJson)
			formatter.WriteInfo("Deploying and launching the app with startup diagnostics enabled...");

		var launchResult = formatter is SpectreOutputFormatter spectre && !useJson
			? await spectre.StatusAsync(
				"Deploying and launching the app...",
				() => ProcessRunner.RunAsync("dotnet", launchArgs, project.ProjectDirectory, timeout: s_buildLaunchTimeout, environmentVariablesToRemove: s_msbuildSdkEnvVars, cancellationToken: cancellationToken))
			: await ProcessRunner.RunAsync("dotnet", launchArgs, project.ProjectDirectory, timeout: s_buildLaunchTimeout, environmentVariablesToRemove: s_msbuildSdkEnvVars, cancellationToken: cancellationToken);

		if (!launchResult.Success)
		{
			await RequestTraceStopAsync(traceProcess.Process);
			await traceProcess.WaitForExitAsync();
			throw CreateProcessFailureException("dotnet build -t:Run", launchResult);
		}

		if (!useJson)
		{
			formatter.WriteInfo(
				string.IsNullOrWhiteSpace(stoppingEventProvider)
					? "Startup trace is running. Press Enter or Ctrl+C to stop and finalize the .nettrace."
					: "Waiting for the configured stopping event. Press Enter or Ctrl+C to stop early.");
		}

		await WaitForTraceCompletionAsync(
			traceProcess,
			allowManualStop: !useJson,
			formatter,
			cancellationToken);

		if (!File.Exists(outputPath))
		{
			throw new MauiToolException(
				ErrorCodes.InternalError,
				$"Trace collection completed, but '{outputPath}' was not created.");
		}

		return new MauiProfileResult
		{
			ProjectPath = project.ProjectPath,
			ProjectName = project.ProjectName,
			Framework = framework,
			Platform = Platforms.Android,
			DeviceId = device.Id,
			DeviceName = device.Name,
			Configuration = configuration,
			OutputPath = outputPath,
			DsrouterKind = dsrouterKind,
			DiagnosticAddress = diagnosticAddress,
			DiagnosticPort = diagnosticPort,
			UsedStoppingEvent = !string.IsNullOrWhiteSpace(stoppingEventProvider),
			StartedAtUtc = startedAtUtc,
			CompletedAtUtc = DateTimeOffset.UtcNow
		};
	}

	static string[] BuildCompileArguments(string projectPath, string framework, string configuration) =>
	[
		"build",
		projectPath,
		"-c", configuration,
		"-f", framework,
		"--nologo"
	];

	static string[] BuildLaunchArguments(
		string projectPath,
		string framework,
		string configuration,
		Device device,
		string diagnosticAddress,
		int diagnosticPort)
	{
		return
		[
			"build",
			projectPath,
			"-t:Run",
			"-c", configuration,
			"-f", framework,
			$"-p:AdbTarget=-s {device.Id}",
			$"-p:DiagnosticAddress={diagnosticAddress}",
			$"-p:DiagnosticPort={diagnosticPort}",
			"-p:DiagnosticSuspend=true",
			"-p:DiagnosticListenMode=connect",
			"-p:EnableDiagnostics=true",
			"-p:AndroidEnableProfiler=true",
			"-p:WaitForExit=false",
			// Phase 1 already compiled+packaged; incremental check here is near-instant.
			// Do NOT pass NoBuild=true — it triggers NETSDK1085 when Build is invoked via -t:Run.
		];
	}

	static MonitoredProcess StartTraceCollector(
		string workingDirectory,
		string outputPath,
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
			FileName = "dnx",
			WorkingDirectory = workingDirectory,
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			RedirectStandardInput = true,
			CreateNoWindow = true
		};

		// "dnx -y dotnet-trace -- collect ..."
		startInfo.ArgumentList.Add("-y");
		startInfo.ArgumentList.Add("dotnet-trace");
		startInfo.ArgumentList.Add("--");

		foreach (var arg in BuildTraceArguments(outputPath, dsrouterPid, traceProfile, duration, stoppingEventProvider, stoppingEventName, stoppingEventPayloadFilter))
			startInfo.ArgumentList.Add(arg);

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

	internal static IEnumerable<string> BuildTraceArguments(
		string outputPath,
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
			"NetTrace",
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
			// explicitly include the default collection profiles.  dotnet-trace normally applies
			// "dotnet-common,dotnet-sampled-thread-time" when neither --profile nor --providers
			// are given, but adding --providers (required below) suppresses those defaults.
			// Note: --profile cpu-sampling is Linux-only and fails with --dsrouter; these two
			// profiles use EventPipe and work correctly with --dsrouter.
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

	static async Task WaitForTraceCompletionAsync(
		MonitoredProcess traceProcess,
		bool allowManualStop,
		IOutputFormatter formatter,
		CancellationToken cancellationToken)
	{
		var processWaitTask = traceProcess.WaitForExitAsync();
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
			}, cancellationToken);

			try
			{
				var completedTask = await Task.WhenAny(processWaitTask, manualStopSignal.Task);
				if (completedTask == manualStopSignal.Task && !traceProcess.Process.HasExited)
				{
					formatter.WriteInfo("Stopping trace and finalizing output...");
					await RequestTraceStopAsync(traceProcess.Process);
					await processWaitTask;
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

		if (traceProcess.Process.ExitCode != 0)
		{
			throw new MauiToolException(
				ErrorCodes.InternalError,
				$"dotnet-trace exited with code {traceProcess.Process.ExitCode}.",
				nativeError: traceProcess.GetCombinedOutput());
		}
	}

	static async Task RequestTraceStopAsync(Process traceProcess)
	{
		if (traceProcess.HasExited)
			return;

		await traceProcess.StandardInput.WriteLineAsync();
		await traceProcess.StandardInput.FlushAsync();
	}

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

	internal static bool IsTargetFrameworkCompatible(string tfm, string platform) => platform switch
	{
		Platforms.Android => tfm.Contains("-android", StringComparison.OrdinalIgnoreCase),
		Platforms.iOS => tfm.Contains("-ios", StringComparison.OrdinalIgnoreCase),
		Platforms.MacCatalyst => tfm.Contains("-maccatalyst", StringComparison.OrdinalIgnoreCase),
		Platforms.Windows => tfm.Contains("-windows", StringComparison.OrdinalIgnoreCase),
		_ => false
	};

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
		CancellationToken cancellationToken)
	{
		var stdout = new StringBuilder();
		var stderr = new StringBuilder();

		var stdoutPump = PumpStreamAsync(
			process.StandardOutput,
			stdout,
			verbose && !useJson ? line => formatter.WriteProgress($"[{prefix}] {line}") : null,
			cancellationToken);

		var stderrPump = PumpStreamAsync(
			process.StandardError,
			stderr,
			!useJson ? line => formatter.WriteWarning($"[{prefix}] {line}") : null,
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
	public required string OutputPath { get; init; }
	public required string DsrouterKind { get; init; }
	public required string DiagnosticAddress { get; init; }
	public required int DiagnosticPort { get; init; }
	public required bool UsedStoppingEvent { get; init; }
	public required DateTimeOffset StartedAtUtc { get; init; }
	public required DateTimeOffset CompletedAtUtc { get; init; }
}
