// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Net;
using System.Runtime.InteropServices;
using Microsoft.Maui.Cli.Errors;
using Microsoft.Maui.Cli.Models;
using Microsoft.Maui.Cli.Output;
using Microsoft.Maui.Cli.Services;
using Microsoft.Maui.Cli.Utils;
using Spectre.Console;

namespace Microsoft.Maui.Cli.Commands;

/// <summary>
/// Implementation of <c>maui profile startup</c>.
/// </summary>
public static class ProfileCommand
{
	internal static readonly TimeSpan s_buildLaunchTimeout = TimeSpan.FromMinutes(15);
	internal static readonly TimeSpan s_dsrouterStartupTimeout = TimeSpan.FromSeconds(30);
	internal static readonly TimeSpan s_traceStartupRetryTimeout = TimeSpan.FromSeconds(15);
	internal static readonly TimeSpan s_traceStartupRetryDelay = TimeSpan.FromMilliseconds(500);
	internal static readonly TimeSpan s_adbPortForwardTimeout = TimeSpan.FromSeconds(15);
	internal static readonly TimeSpan s_exitControlConnectTimeout = TimeSpan.FromSeconds(5);
	internal static readonly TimeSpan s_exitControlCommandTimeout = TimeSpan.FromSeconds(10);
	internal static readonly TimeSpan s_traceStopInterruptDelay = TimeSpan.FromSeconds(5);
	internal static readonly TimeSpan s_traceStopTimeout = TimeSpan.FromSeconds(15);
	internal const int DefaultDiagnosticPort = 9000;
	internal const int ExitControlPortOffset = 1;
	internal const string StartupProfilingPackageId = "Microsoft.Maui.StartupProfiling";
	internal const string StartupProfilingProviderName = "Microsoft.Maui.StartupProfiling";
	internal const string StartupProfilingEventName = "StartupComplete";
	internal const string StartupProfilingAssemblyFileName = "Microsoft.Maui.StartupProfiling.dll";
	internal const string StartupProfilingInjectionTargetsFileName = "MauiStartupProfilingInjection.targets";
	internal const string StartupProfilingInjectionSourceFileName = "MauiStartupProfiling.AutoInitialize.cs";
	internal const string SpeedscopeExtension = ".speedscope.json";
	internal const string MibcExtension = ".mibc";
	internal const string DotnetPgoDisplayPath = "~/.maui/dotnet-pgo";
	internal const string MibcDotnetRuntimeProvider = "Microsoft-Windows-DotNETRuntime:0x6000080018:5";
	internal const string DotnetPgoRuntimeRepoUrl = "https://github.com/dotnet/runtime.git";
	internal const string DotnetPgoFallbackBranch = "release/10.0";
	internal const string DotnetPgoBranchEnvironmentVariable = "MAUI_DOTNET_PGO_BRANCH";
	internal const string DotnetPgoProjectPath = "src/coreclr/tools/dotnet-pgo/dotnet-pgo.csproj";

	// MSBuild SDK path env vars set by a parent `dotnet run` process that would otherwise
	// pin the child build to the wrong SDK version (e.g. the CLI's own SDK instead of the
	// user's project SDK). Removing them lets the child process discover the correct SDK
	// from the project directory's global.json or the latest installed SDK.
	internal static readonly string[] s_msbuildSdkEnvVars =
	[
		"MSBuildSDKsPath",
		"MSBUILD_EXE_PATH",
		"MSBuildExtensionsPath",
		"MSBuildStartupDirectory",
	];

	public static Command Create()
	{
		var command = new Command("profile", "Profile a .NET MAUI app");
		command.Add(CreateStartupCommand());
		return command;
	}

	static Command CreateStartupCommand()
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
			Description = "Output trace path (default: <project>_<timestamp>.nettrace in the current directory). Speedscope and MIBC also emit sibling derived files while keeping the raw .nettrace."
		};
		var formatOption = new Option<string>("--format")
		{
			Description = "Output format to generate: nettrace (default), speedscope, or mibc.",
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
				"When omitted, the startup trace waits for --duration or a manual Enter stop."
		};
		var stoppingEventNameOption = new Option<string?>("--stopping-event-event-name")
		{
			Description = "Optional event name to combine with --stopping-event-provider-name."
		};
		var stoppingEventPayloadFilterOption = new Option<string?>("--stopping-event-payload-filter")
		{
			Description = "Optional payload filter (key:value,key:value) to combine with the stopping event options"
		};

		var command = new Command("startup", "Collect a startup trace for a .NET MAUI app")
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

		command.SetAction((ParseResult parseResult, CancellationToken cancellationToken) =>
			ExecuteAsync(
				parseResult,
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
				stoppingEventPayloadFilterOption,
				cancellationToken));

		return command;
	}

	static async Task<int> ExecuteAsync(
		ParseResult parseResult,
		Option<string?> projectOption,
		Option<string?> frameworkOption,
		Option<string?> deviceOption,
		Option<string?> outputOption,
		Option<string> formatOption,
		Option<string> configurationOption,
		Option<string> platformOption,
		Option<TimeSpan?> durationOption,
		Option<string?> traceProfileOption,
		Option<bool> noBuildOption,
		Option<int> diagnosticPortOption,
		Option<string?> stoppingEventProviderOption,
		Option<string?> stoppingEventNameOption,
		Option<string?> stoppingEventPayloadFilterOption,
		CancellationToken cancellationToken)
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

			if (outputFormat == TraceOutputFormat.Mibc)
				_ = await EnsureDotnetPgoAvailableAsync(formatter, useJson, verbose, cancellationToken);

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
	}

	internal static string ResolveTargetFramework(
		ResolvedMauiProject project,
		string? requestedFramework,
		string platform,
		bool nonInteractive,
		SpectreOutputFormatter? spectre)
		=> ProfileTargetResolver.ResolveTargetFramework(project, requestedFramework, platform, nonInteractive, spectre);

	internal static bool WasOptionExplicitlySpecified<T>(ParseResult parseResult, Option<T> option)
		=> ProfileOutputResolver.WasOptionExplicitlySpecified(parseResult, option);

	internal static string ResolveProfileConfiguration(string? requestedConfiguration, bool explicitlySpecified, string platform)
		=> ProfileOutputResolver.ResolveProfileConfiguration(requestedConfiguration, explicitlySpecified, platform);

	static Task<Device> ResolveProfileDeviceAsync(
		string platform,
		string? requestedDevice,
		IDeviceManager deviceManager,
		bool nonInteractive,
		SpectreOutputFormatter? spectre,
		CancellationToken cancellationToken)
		=> ProfileTargetResolver.ResolveProfileDeviceAsync(
			platform,
			requestedDevice,
			deviceManager,
			nonInteractive,
			spectre,
			cancellationToken);

	static void ValidateDnxAvailable()
		=> ProfileCommandDiagnostics.ValidateDnxAvailable();

	internal static TraceOutputFormat ResolveTraceOutputFormat(
		string? requestedFormat,
		bool explicitlySpecified,
		bool nonInteractive,
		SpectreOutputFormatter? spectre)
		=> ProfileOutputResolver.ResolveTraceOutputFormat(requestedFormat, explicitlySpecified, nonInteractive, spectre);

	internal static TraceOutputFormat ResolveTraceOutputFormat(string? requestedFormat)
		=> ProfileOutputResolver.ResolveTraceOutputFormat(requestedFormat);

	internal static string ResolveOutputPath(string projectName, string? requestedOutput, TraceOutputFormat outputFormat)
		=> ProfileOutputResolver.ResolveOutputPath(projectName, requestedOutput, outputFormat);

	internal static string GetPrimaryOutputPath(string collectorOutputPath, TraceOutputFormat outputFormat)
		=> ProfileOutputResolver.GetPrimaryOutputPath(collectorOutputPath, outputFormat);

	static void ValidateStoppingEventOptions(string? providerName, string? eventName, string? payloadFilter)
		=> ProfileOutputResolver.ValidateStoppingEventOptions(providerName, eventName, payloadFilter);

	internal static StoppingEventConfiguration ResolveStoppingEventConfiguration(
		TimeSpan? duration,
		string? providerName,
		string? eventName,
		string? payloadFilter)
		=> ProfileOutputResolver.ResolveStoppingEventConfiguration(duration, providerName, eventName, payloadFilter);

	static Task<MauiProfileResult> RunProfileAsync(
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
		=> ProfileSessionRunner.RunAsync(
			new ProfileSessionRequest(
				project,
				framework,
				device,
				outputPath,
				outputFormat,
				configuration,
				traceProfile,
				noBuild,
				diagnosticPort,
				duration,
				stoppingEventProvider,
				stoppingEventName,
				stoppingEventPayloadFilter,
				autoSelectedStoppingEvent,
				formatter,
				useJson,
				verbose),
			cancellationToken);

	internal static string[] BuildCompileArguments(
		string projectPath,
		string framework,
		string configuration,
		ProfileTransportConfiguration transport,
		int diagnosticPort,
		ProfilingBuildInjection? buildInjection)
		=> ProfileCommandArguments.BuildCompileArguments(projectPath, framework, configuration, transport, diagnosticPort, buildInjection);

	internal static string[] BuildLaunchArguments(
		string projectPath,
		string framework,
		string configuration,
		Device device,
		ProfileTransportConfiguration transport,
		int diagnosticPort,
		ProfilingBuildInjection? buildInjection)
		=> ProfileCommandArguments.BuildLaunchArguments(projectPath, framework, configuration, device, transport, diagnosticPort, buildInjection);

	internal static IEnumerable<string> BuildTraceArguments(
		string outputPath,
		TraceOutputFormat outputFormat,
		ProfileTransportConfiguration transport,
		string? traceProfile,
		TimeSpan? duration,
		string? stoppingEventProvider,
		string? stoppingEventName,
		string? stoppingEventPayloadFilter)
		=> DotnetTraceRunner.BuildTraceArguments(
			outputPath,
			outputFormat,
			transport,
			traceProfile,
			duration,
			stoppingEventProvider,
			stoppingEventName,
			stoppingEventPayloadFilter);

	internal static bool CanResolveDiagnosticsTool(string? installedToolPath, string? cachedToolDll)
		=> ProfileCommandDiagnostics.CanResolveDiagnosticsTool(installedToolPath, cachedToolDll);

	internal static bool CanUseDiagnosticsTooling(bool hasDnx, bool hasDotnetTrace, bool hasDotnetDsrouter)
		=> ProfileCommandDiagnostics.CanUseDiagnosticsTooling(hasDnx, hasDotnetTrace, hasDotnetDsrouter);

	internal static bool IsRetryableTraceStartupFailure(string? details)
		=> DotnetTraceRunner.IsRetryableStartupFailure(details);

	internal static async Task ConvertNetTraceToMibcAsync(
		ResolvedMauiProject project,
		string framework,
		string configuration,
		string netTracePath,
		string mibcPath,
		IOutputFormatter formatter,
		bool useJson,
		bool verbose,
		CancellationToken cancellationToken)
	{
		if (!File.Exists(netTracePath))
		{
			throw new MauiToolException(
				ErrorCodes.InternalError,
				$"Raw trace '{netTracePath}' was not created, so MIBC conversion cannot continue.");
		}

		var dotnetPgoPath = ResolveDotnetPgoPath();
		var referenceAssemblies = ResolveMibcReferenceAssemblies(project, framework, configuration);
		if (referenceAssemblies.Count == 0)
		{
			throw MauiToolException.UserActionRequired(
				ErrorCodes.DiagnosticsToolNotFound,
				$"MIBC conversion could not find any reference assemblies for '{project.ProjectName}'.",
				[
					$"Build the target '{framework}' first so its output assemblies exist.",
					"Then run 'maui profile startup --format mibc' again."
				]);
		}

		if (!useJson)
			formatter.WriteInfo("Converting the raw trace to MIBC...");

		var args = BuildMibcArguments(netTracePath, mibcPath, referenceAssemblies).ToArray();
		ProfileCommandProcessHelpers.WriteVerbose(
			formatter,
			useJson,
			verbose,
			$"dotnet-pgo command: {ProfileCommandProcessHelpers.FormatCommandLine(dotnetPgoPath, args)}");

		var result = await ProcessRunner.RunAsync(
			dotnetPgoPath,
			args,
			project.ProjectDirectory,
			timeout: s_buildLaunchTimeout,
			cancellationToken: cancellationToken);

		if (!result.Success)
			throw ProfileCommandProcessHelpers.CreateProcessFailureException("dotnet-pgo create-mibc", result);
	}

	internal static IEnumerable<string> BuildMibcArguments(
		string netTracePath,
		string mibcPath,
		IReadOnlyList<string> referenceAssemblies)
	{
		var args = new List<string>
		{
			"create-mibc",
			"--trace",
			netTracePath,
			"--output",
			mibcPath
		};

		foreach (var referenceAssembly in referenceAssemblies)
		{
			args.Add("--reference");
			args.Add(referenceAssembly);
		}

		return args;
	}

	static string ResolveDotnetPgoPath()
	{
		var dotnetPgoPath = TryResolveDotnetPgoPath();
		if (!string.IsNullOrWhiteSpace(dotnetPgoPath))
			return dotnetPgoPath;

		var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		var installPath = string.IsNullOrWhiteSpace(userProfile)
			? DotnetPgoDisplayPath
			: GetDotnetPgoInstallPath(userProfile);

		throw MauiToolException.UserActionRequired(
			ErrorCodes.DiagnosticsToolNotFound,
			$"MIBC conversion requires dotnet-pgo at '{installPath}'.",
			[
				$"Install or copy the dotnet-pgo binary to {DotnetPgoDisplayPath}.",
				"Then rerun 'maui profile startup --format mibc'."
			]);
	}

	static async Task<string> EnsureDotnetPgoAvailableAsync(
		IOutputFormatter formatter,
		bool useJson,
		bool verbose,
		CancellationToken cancellationToken)
	{
		if (TryResolveDotnetPgoPath() is { } installedPath)
			return installedPath;

		if (!useJson)
		{
			formatter.WriteInfo($"dotnet-pgo was not found at {DotnetPgoDisplayPath}.");
			formatter.WriteInfo("Building dotnet-pgo from dotnet/runtime source...");
		}

		return await BuildDotnetPgoFromSourceAsync(formatter, useJson, verbose, cancellationToken);
	}

	static string? TryResolveDotnetPgoPath()
	{
		var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		if (string.IsNullOrWhiteSpace(userProfile))
			return null;

		var expectedPath = GetDotnetPgoInstallPath(userProfile);
		if (File.Exists(expectedPath))
			return expectedPath;

		if (OperatingSystem.IsWindows())
		{
			var windowsPath = expectedPath + ".exe";
			if (File.Exists(windowsPath))
				return windowsPath;
		}

		return null;
	}

	internal static string GetDotnetPgoInstallPath(string userProfile)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(userProfile);
		return Path.Combine(userProfile, ".maui", "dotnet-pgo");
	}

	internal static string[] BuildDotnetPgoPublishArguments(string runtimeIdentifier, string outputDirectory) =>
	[
		"publish",
		DotnetPgoProjectPath,
		"-c", "Release",
		"-r", runtimeIdentifier,
		"--self-contained",
		"-p:PublishSingleFile=true",
		"-p:PublishTrimmed=false",
		"-p:TreatWarningsAsErrors=false",
		"-o", outputDirectory
	];

	internal static string? ParseLatestStableDotnetRuntimeReleaseBranch(string? lsRemoteOutput)
	{
		if (string.IsNullOrWhiteSpace(lsRemoteOutput))
			return null;

		return lsRemoteOutput
			.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Select(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).LastOrDefault())
			.Where(token => !string.IsNullOrWhiteSpace(token))
			.Select(token =>
			{
				var value = token!;
				const string prefix = "refs/heads/";
				if (value.StartsWith(prefix, StringComparison.Ordinal))
					value = value[prefix.Length..];

				if (!value.StartsWith("release/", StringComparison.Ordinal))
					return null;

				var versionText = value["release/".Length..];
				return Version.TryParse(versionText, out var version)
					? new { Branch = value, Version = version }
					: null;
			})
			.Where(candidate => candidate is not null)
			.OrderByDescending(candidate => candidate!.Version)
			.Select(candidate => candidate!.Branch)
			.FirstOrDefault();
	}

	static async Task<string> ResolveDotnetPgoSourceBranchAsync(
		string gitPath,
		IOutputFormatter formatter,
		bool useJson,
		bool verbose,
		CancellationToken cancellationToken)
	{
		var overrideBranch = Environment.GetEnvironmentVariable(DotnetPgoBranchEnvironmentVariable);
		if (!string.IsNullOrWhiteSpace(overrideBranch))
		{
			ProfileCommandProcessHelpers.WriteVerbose(
				formatter,
				useJson,
				verbose,
				$"Using dotnet-pgo source branch override from {DotnetPgoBranchEnvironmentVariable}: {overrideBranch}");
			return overrideBranch.Trim();
		}

		var lsRemoteResult = await ProcessRunner.RunAsync(
			gitPath,
			["ls-remote", "--heads", DotnetPgoRuntimeRepoUrl, "refs/heads/release/*"],
			timeout: TimeSpan.FromSeconds(30),
			cancellationToken: cancellationToken);

		if (lsRemoteResult.Success &&
			ParseLatestStableDotnetRuntimeReleaseBranch(lsRemoteResult.StandardOutput) is { } latestStableBranch)
		{
			ProfileCommandProcessHelpers.WriteVerbose(
				formatter,
				useJson,
				verbose,
				$"Selected latest stable dotnet/runtime release branch for dotnet-pgo: {latestStableBranch}");
			return latestStableBranch;
		}

		if (!useJson)
			formatter.WriteWarning($"Could not detect the latest stable dotnet/runtime release branch automatically. Falling back to {DotnetPgoFallbackBranch}.");

		return DotnetPgoFallbackBranch;
	}

	internal static string GetCurrentRuntimeIdentifier()
	{
		var os = OperatingSystem.IsMacOS() ? "osx"
			: OperatingSystem.IsLinux() ? "linux"
			: OperatingSystem.IsWindows() ? "win"
			: throw new MauiToolException(
				ErrorCodes.PlatformNotSupported,
				"Building dotnet-pgo from source is only supported on macOS, Linux, and Windows.");

		var arch = RuntimeInformation.OSArchitecture switch
		{
			Architecture.X64 => "x64",
			Architecture.Arm64 => "arm64",
			_ => throw new MauiToolException(
				ErrorCodes.PlatformNotSupported,
				$"Building dotnet-pgo from source is not supported on architecture '{RuntimeInformation.OSArchitecture}'.")
		};

		return $"{os}-{arch}";
	}

	static async Task<string> BuildDotnetPgoFromSourceAsync(
		IOutputFormatter formatter,
		bool useJson,
		bool verbose,
		CancellationToken cancellationToken)
	{
		var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		if (string.IsNullOrWhiteSpace(userProfile))
		{
			throw new MauiToolException(
				ErrorCodes.DiagnosticsToolNotFound,
				"Could not resolve the current user's home directory to locate or build dotnet-pgo.");
		}

		var gitPath = ProcessRunner.GetCommandPath("git");
		if (gitPath is null)
		{
			throw MauiToolException.UserActionRequired(
				ErrorCodes.DiagnosticsToolNotFound,
				"Building dotnet-pgo from source requires 'git', but it was not found in PATH.",
				[
					"Install git and rerun 'maui profile startup --format mibc'.",
					$"Or manually place the dotnet-pgo binary at {DotnetPgoDisplayPath}."
				]);
		}

		var dotnetPath = ProcessRunner.GetCommandPath("dotnet") ?? "dotnet";
		var cloneDirectory = Path.Combine(Path.GetTempPath(), $"dotnet-runtime-{Guid.NewGuid():N}");
		var publishDirectory = Path.Combine(cloneDirectory, "artifacts", "dotnet-pgo");

		try
		{
			var branch = await ResolveDotnetPgoSourceBranchAsync(gitPath, formatter, useJson, verbose, cancellationToken);

			ProfileCommandProcessHelpers.WriteVerbose(
				formatter,
				useJson,
				verbose,
				$"Cloning {DotnetPgoRuntimeRepoUrl} ({branch}) into {cloneDirectory}.");

			var cloneResult = await ProcessRunner.RunAsync(
				gitPath,
				["clone", "--depth", "1", "--single-branch", "--branch", branch, DotnetPgoRuntimeRepoUrl, cloneDirectory],
				timeout: s_buildLaunchTimeout,
				cancellationToken: cancellationToken);
			if (!cloneResult.Success)
				throw ProfileCommandProcessHelpers.CreateProcessFailureException("git clone", cloneResult);

			var publishArgs = BuildDotnetPgoPublishArguments(GetCurrentRuntimeIdentifier(), publishDirectory);
			ProfileCommandProcessHelpers.WriteVerbose(
				formatter,
				useJson,
				verbose,
				$"Publishing dotnet-pgo via {ProfileCommandProcessHelpers.FormatCommandLine(dotnetPath, publishArgs)}");

			var publishResult = await ProcessRunner.RunAsync(
				dotnetPath,
				publishArgs,
				cloneDirectory,
				timeout: s_buildLaunchTimeout,
				cancellationToken: cancellationToken);
			if (!publishResult.Success)
				throw ProfileCommandProcessHelpers.CreateProcessFailureException("dotnet publish", publishResult);

			var builtBinaryPath = Directory.EnumerateFiles(
				publishDirectory,
				OperatingSystem.IsWindows() ? "dotnet-pgo.exe" : "dotnet-pgo",
				SearchOption.AllDirectories).FirstOrDefault();

			if (string.IsNullOrWhiteSpace(builtBinaryPath))
			{
				throw new MauiToolException(
					ErrorCodes.DiagnosticsToolNotFound,
					$"dotnet publish completed, but the dotnet-pgo binary was not found under '{publishDirectory}'.");
			}

			var installDirectory = Path.Combine(userProfile, ".maui");
			Directory.CreateDirectory(installDirectory);

			var installPath = OperatingSystem.IsWindows()
				? GetDotnetPgoInstallPath(userProfile) + ".exe"
				: GetDotnetPgoInstallPath(userProfile);

			File.Copy(builtBinaryPath, installPath, overwrite: true);

			if (!OperatingSystem.IsWindows())
			{
				File.SetUnixFileMode(
					installPath,
					UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
					UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
					UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
			}

			if (!useJson)
				formatter.WriteInfo($"Installed dotnet-pgo to {installPath}.");

			return installPath;
		}
		finally
		{
			try
			{
				if (Directory.Exists(cloneDirectory))
					Directory.Delete(cloneDirectory, recursive: true);
			}
			catch
			{
				// Best-effort cleanup for the temporary runtime clone.
			}
		}
	}

	static IReadOnlyList<string> ResolveMibcReferenceAssemblies(
		ResolvedMauiProject project,
		string framework,
		string configuration)
	{
		var candidateRoots = GetMibcReferenceSearchRoots(project, framework, configuration).ToArray();
		if (candidateRoots.Length == 0)
			return [];

		var linkedOrShrunkAssemblies = candidateRoots
			.SelectMany(root => Directory.EnumerateFiles(root, "*.dll", SearchOption.AllDirectories))
			.Where(static path => path.Contains($"{Path.DirectorySeparatorChar}linked{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
				|| path.Contains($"{Path.AltDirectorySeparatorChar}linked{Path.AltDirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
				|| path.Contains($"{Path.DirectorySeparatorChar}shrunk{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
				|| path.Contains($"{Path.AltDirectorySeparatorChar}shrunk{Path.AltDirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
			.ToArray();

		if (linkedOrShrunkAssemblies.Length > 0)
			return linkedOrShrunkAssemblies;

		return candidateRoots
			.SelectMany(root => Directory.EnumerateFiles(root, "*.dll", SearchOption.AllDirectories))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
			.ToArray();
	}

	static IEnumerable<string> GetMibcReferenceSearchRoots(
		ResolvedMauiProject project,
		string framework,
		string configuration)
	{
		var searchRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			Path.Combine(project.ProjectDirectory, "obj", configuration, framework),
			Path.Combine(project.ProjectDirectory, "bin", configuration, framework)
		};

		for (var current = new DirectoryInfo(project.ProjectDirectory); current is not null; current = current.Parent)
		{
			searchRoots.Add(Path.Combine(current.FullName, "artifacts", "obj", project.ProjectName, configuration, framework));
			searchRoots.Add(Path.Combine(current.FullName, "artifacts", "bin", project.ProjectName, configuration, framework));
		}

		return searchRoots.Where(Directory.Exists);
	}

	internal static int FindAvailableTcpPort(int startingPort, int maxPort = IPEndPoint.MaxPort)
		=> ProfileCommandPortRouter.FindAvailableTcpPort(startingPort, maxPort);

	internal static void ValidateTraceOutput(string primaryOutputPath, string collectorOutputPath, TraceOutputFormat outputFormat, string platform)
		=> ProfileTraceOutputValidation.ValidateTraceOutput(primaryOutputPath, collectorOutputPath, outputFormat, platform);

	internal static bool IsTargetFrameworkCompatible(string tfm, string platform)
		=> ProfileTargetResolver.IsTargetFrameworkCompatible(tfm, platform);

	internal static string ResolveProfilePlatform(string requestedPlatform, string framework)
		=> ProfileTargetResolver.ResolveProfilePlatform(requestedPlatform, framework);

	internal static ProfileTransportConfiguration ResolveProfileTransport(string platform, Device device)
	{
		var normalizedPlatform = Platforms.Normalize(platform);
		return normalizedPlatform switch
		{
			Platforms.Android => new ProfileTransportConfiguration(
				Platform: Platforms.Android,
				DiagnosticAddress: device.IsEmulator ? "10.0.2.2" : IPAddress.Loopback.ToString(),
				DiagnosticListenMode: "connect",
				DsrouterKind: device.IsEmulator ? "android-emu" : "android",
				RequiresManualExitControlPortRouting: !device.IsEmulator),
			Platforms.iOS => new ProfileTransportConfiguration(
				Platform: Platforms.iOS,
				DiagnosticAddress: IPAddress.Loopback.ToString(),
				DiagnosticListenMode: "listen",
				DsrouterKind: device.IsEmulator ? "ios-sim" : "ios",
				RequiresManualExitControlPortRouting: false),
			_ => throw new MauiToolException(
				ErrorCodes.PlatformNotSupported,
				$"Startup profiling is not implemented yet for platform '{platform}'.")
		};
	}

	internal static string? InferPlatformFromTargetFramework(string tfm)
		=> ProfileTargetResolver.InferPlatformFromTargetFramework(tfm);

	internal static Version GetFrameworkSortKey(string tfm)
		=> ProfileTargetResolver.GetFrameworkSortKey(tfm);
}
