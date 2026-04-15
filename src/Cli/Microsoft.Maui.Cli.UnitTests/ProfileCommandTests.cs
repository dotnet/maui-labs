// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using System.Net;
using System.Net.Sockets;
using Microsoft.Maui.Cli.Commands;
using Microsoft.Maui.Cli.Errors;
using Microsoft.Maui.Cli.Models;
using Microsoft.Maui.Cli.Utils;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class ProfileCommandTests
{
	// ── Command construction ──────────────────────────────────────────────────

	[Fact]
	public void ProfileCommand_CanBeConstructed()
	{
		var command = ProfileCommand.Create();
		Assert.NotNull(command);
		Assert.Equal("profile", command.Name);
		Assert.Contains(command.Subcommands, c => c.Name == "startup");
		Assert.DoesNotContain(command.Options, o => o.Name == "--project");
	}

	[Fact]
	public void ProfileCommand_UsesStartupSubcommandForExecutionSurface()
	{
		var command = ProfileCommand.Create();
		var startup = command.Subcommands.Single(c => c.Name == "startup");

		Assert.Contains(startup.Options, o => o.Name == "--project");
	}

	[Fact]
	public void ProfileStartupCommand_HasExpectedOptions()
	{
		var startup = ProfileCommand.Create().Subcommands.Single(c => c.Name == "startup");
		Assert.Contains(startup.Options, o => o.Name == "--project");
		Assert.Contains(startup.Options, o => o.Name == "--framework");
		Assert.Contains(startup.Options, o => o.Name == "--device");
		Assert.Contains(startup.Options, o => o.Name == "--output");
		Assert.Contains(startup.Options, o => o.Name == "--format");
		Assert.Contains(startup.Options, o => o.Name == "--configuration");
		Assert.Contains(startup.Options, o => o.Name == "--platform");
		Assert.Contains(startup.Options, o => o.Name == "--duration");
		Assert.Contains(startup.Options, o => o.Name == "--trace-profile");
		Assert.Contains(startup.Options, o => o.Name == "--no-build");
		Assert.Contains(startup.Options, o => o.Name == "--diagnostic-port");
		Assert.Contains(startup.Options, o => o.Name == "--stopping-event-provider-name");
		Assert.Contains(startup.Options, o => o.Name == "--stopping-event-event-name");
		Assert.Contains(startup.Options, o => o.Name == "--stopping-event-payload-filter");
	}

	[Fact]
	public void ProfileCommand_DefaultConfigurationIsRelease()
	{
		var command = ProfileCommand.Create();
		var startup = command.Subcommands.Single(c => c.Name == "startup");
		var configOption = (Option<string>)startup.Options.First(o => o.Name == "--configuration");
		var parseResult = command.Parse("profile startup");
		Assert.Equal("Release", parseResult.GetValue(configOption));
	}

	[Fact]
	public void ProfileCommand_DefaultFormatIsNetTrace()
	{
		var command = ProfileCommand.Create();
		var startup = command.Subcommands.Single(c => c.Name == "startup");
		var formatOption = (Option<string>)startup.Options.First(o => o.Name == "--format");
		var parseResult = command.Parse("profile startup");
		Assert.Equal("nettrace", parseResult.GetValue(formatOption));
	}

	[Fact]
	public void ProfileCommand_FormatOptionIsNotExplicitWhenOmitted()
	{
		var command = ProfileCommand.Create();
		var startup = command.Subcommands.Single(c => c.Name == "startup");
		var formatOption = (Option<string>)startup.Options.First(o => o.Name == "--format");
		var parseResult = command.Parse("profile startup");

		Assert.False(ProfileCommand.WasOptionExplicitlySpecified(parseResult, formatOption));
	}

	[Fact]
	public void ProfileCommand_FormatOptionIsExplicitWhenProvided()
	{
		var command = ProfileCommand.Create();
		var startup = command.Subcommands.Single(c => c.Name == "startup");
		var formatOption = (Option<string>)startup.Options.First(o => o.Name == "--format");
		var parseResult = command.Parse("profile startup --format speedscope");

		Assert.True(ProfileCommand.WasOptionExplicitlySpecified(parseResult, formatOption));
	}

	[Fact]
	public void ResolveTraceOutputFormat_DefaultsToNetTraceWhenOmittedNonInteractive()
	{
		var result = ProfileCommand.ResolveTraceOutputFormat(
			requestedFormat: null,
			explicitlySpecified: false,
			nonInteractive: true,
			spectre: null);

		Assert.Equal(TraceOutputFormat.NetTrace, result);
	}

	[Fact]
	public void ResolveTraceOutputFormat_UsesExplicitSpeedscopeValue()
	{
		var result = ProfileCommand.ResolveTraceOutputFormat(
			requestedFormat: "speedscope",
			explicitlySpecified: true,
			nonInteractive: false,
			spectre: null);

		Assert.Equal(TraceOutputFormat.Speedscope, result);
	}

	[Fact]
	public void ProfileCommand_DefaultPlatformIsAll()
	{
		var command = ProfileCommand.Create();
		var startup = command.Subcommands.Single(c => c.Name == "startup");
		var platformOption = (Option<string>)startup.Options.First(o => o.Name == "--platform");
		var parseResult = command.Parse("profile startup");
		Assert.Equal("all", parseResult.GetValue(platformOption));
	}

	[Fact]
	public void ProfileCommand_DefaultDiagnosticPortIs9000()
	{
		var command = ProfileCommand.Create();
		var startup = command.Subcommands.Single(c => c.Name == "startup");
		var portOption = (Option<int>)startup.Options.First(o => o.Name == "--diagnostic-port");
		var parseResult = command.Parse("profile startup");
		Assert.Equal(9000, parseResult.GetValue(portOption));
	}

	[Fact]
	public void ProfileCommand_NoBuildDefaultIsFalse()
	{
		var command = ProfileCommand.Create();
		var startup = command.Subcommands.Single(c => c.Name == "startup");
		var noBuildOption = (Option<bool>)startup.Options.First(o => o.Name == "--no-build");
		var parseResult = command.Parse("profile startup");
		Assert.False(parseResult.GetValue(noBuildOption));
	}

	// ── Target framework resolution ──────────────────────────────────────────

	[Fact]
	public void ResolveTargetFramework_PicksExplicitlyRequestedFramework()
	{
		var project = FakeProject(["net10.0-android", "net10.0-ios"]);
		var result = ProfileCommand.ResolveTargetFramework(project, "net10.0-ios", "ios", nonInteractive: true, spectre: null);
		Assert.Equal("net10.0-ios", result);
	}

	[Fact]
	public void ResolveTargetFramework_ThrowsWhenExplicitFrameworkNotInProject()
	{
		var project = FakeProject(["net10.0-android"]);
		Assert.Throws<MauiToolException>(() =>
			ProfileCommand.ResolveTargetFramework(project, "net10.0-ios", "ios", nonInteractive: true, spectre: null));
	}

	[Fact]
	public void ResolveTargetFramework_ThrowsWhenExplicitFrameworkDoesNotMatchPlatform()
	{
		var project = FakeProject(["net10.0-android", "net10.0-ios"]);
		Assert.Throws<MauiToolException>(() =>
			ProfileCommand.ResolveTargetFramework(project, "net10.0-ios", "android", nonInteractive: true, spectre: null));
	}

	[Theory]
	[InlineData("net10.0-android", "android", true)]
	[InlineData("net10.0-ios", "ios", true)]
	[InlineData("net10.0-maccatalyst", "maccatalyst", true)]
	[InlineData("net10.0-windows10.0.19041.0", "windows", true)]
	[InlineData("net10.0", "android", false)]
	[InlineData("net10.0-android", "ios", false)]
	[InlineData("net10.0-android", "maccatalyst", false)]
	[InlineData("net10.0-ios", "android", false)]
	public void IsTargetFrameworkCompatible_ReturnsExpected(string tfm, string platform, bool expected)
	{
		Assert.Equal(expected, ProfileCommand.IsTargetFrameworkCompatible(tfm, platform));
	}

	[Fact]
	public void ResolveTargetFramework_SelectsHighestVersionWhenNonInteractive()
	{
		var project = FakeProject(["net9.0-android", "net10.0-android"]);
		var result = ProfileCommand.ResolveTargetFramework(project, null, "android", nonInteractive: true, spectre: null);
		Assert.Equal("net10.0-android", result);
	}

	[Fact]
	public void ResolveTargetFramework_SelectsAcrossPlatformsWhenPlatformIsAll()
	{
		var project = FakeProject(["net11.0-ios", "net11.0-android"]);
		var result = ProfileCommand.ResolveTargetFramework(project, null, "all", nonInteractive: true, spectre: null);
		Assert.Equal("net11.0-android", result);
	}

	[Theory]
	[InlineData("net10.0-android", "android")]
	[InlineData("net10.0-ios", "ios")]
	[InlineData("net10.0-maccatalyst", "maccatalyst")]
	[InlineData("net10.0-windows10.0.19041.0", "windows")]
	[InlineData("net10.0", null)]
	public void InferPlatformFromTargetFramework_ReturnsExpected(string tfm, string? expected)
	{
		Assert.Equal(expected, ProfileCommand.InferPlatformFromTargetFramework(tfm));
	}

	[Fact]
	public void ResolveTargetFramework_ThrowsWhenNoCandidatesMatchPlatform()
	{
		var project = FakeProject(["net10.0-ios", "net10.0-maccatalyst"]);
		Assert.Throws<MauiToolException>(() =>
			ProfileCommand.ResolveTargetFramework(project, null, "android", nonInteractive: true, spectre: null));
	}

	// ── Framework sort key ────────────────────────────────────────────────────

	[Theory]
	[InlineData("net10.0-android", 10, 0)]
	[InlineData("net9.0-android", 9, 0)]
	[InlineData("net10.5-ios", 10, 5)]
	[InlineData("notaframework", 0, 0)]
	public void GetFrameworkSortKey_ExtractsVersion(string tfm, int major, int minor)
	{
		var key = ProfileCommand.GetFrameworkSortKey(tfm);
		Assert.Equal(new Version(major, minor), key);
	}

	// ── Output path resolution ────────────────────────────────────────────────

	[Fact]
	public void ResolveOutputPath_UsesExplicitPath()
	{
		var path = ProfileCommand.ResolveOutputPath("MyApp", "/tmp/my-trace.nettrace", TraceOutputFormat.NetTrace);
		Assert.Equal(Path.GetFullPath("/tmp/my-trace.nettrace"), path);
	}

	[Fact]
	public void ResolveOutputPath_AddsNettraceExtensionWhenMissing()
	{
		var path = ProfileCommand.ResolveOutputPath("MyApp", "/tmp/my-trace", TraceOutputFormat.NetTrace);
		Assert.EndsWith(".nettrace", path, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void ResolveOutputPath_DefaultNameIncludesProjectName()
	{
		var path = ProfileCommand.ResolveOutputPath("MyApp", null, TraceOutputFormat.NetTrace);
		var fileName = Path.GetFileName(path);
		Assert.StartsWith("MyApp_", fileName, StringComparison.OrdinalIgnoreCase);
		Assert.EndsWith(".nettrace", fileName, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void ResolveOutputPath_FallsBackWhenProjectNameIsEmpty()
	{
		var path = ProfileCommand.ResolveOutputPath(string.Empty, null, TraceOutputFormat.NetTrace);
		var fileName = Path.GetFileName(path);
		Assert.StartsWith("maui-startup-profile_", fileName, StringComparison.OrdinalIgnoreCase);
		Assert.EndsWith(".nettrace", fileName, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void ResolveOutputPath_SpeedscopeStripsRequestedSpeedscopeSuffix()
	{
		var path = ProfileCommand.ResolveOutputPath("MyApp", "/tmp/my-trace.speedscope.json", TraceOutputFormat.Speedscope);
		Assert.Equal(Path.GetFullPath("/tmp/my-trace.nettrace"), path);
	}

	[Fact]
	public void GetPrimaryOutputPath_SpeedscopeUsesSidecarJsonFile()
	{
		var path = ProfileCommand.GetPrimaryOutputPath("/tmp/my-trace.nettrace", TraceOutputFormat.Speedscope);
		Assert.Equal("/tmp/my-trace.speedscope.json", path);
	}

	// ── Tool version parsing ──────────────────────────────────────────────────

	// ── Project resolver ──────────────────────────────────────────────────────

	[Fact]
	public void GetTargetFrameworks_ParsesSingleTargetFramework()
	{
		var csprojContent = """
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup>
			    <TargetFramework>net10.0-android</TargetFramework>
			  </PropertyGroup>
			</Project>
			""";
		using var tempProject = TempProjectFile(csprojContent);
		var frameworks = MauiProjectResolver.GetTargetFrameworks(tempProject.Path);
		Assert.Equal(["net10.0-android"], frameworks);
	}

	[Fact]
	public void GetTargetFrameworks_ParsesMultipleTargetFrameworks()
	{
		var csprojContent = """
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup>
			    <TargetFrameworks>net10.0-android;net10.0-ios;net10.0-maccatalyst</TargetFrameworks>
			  </PropertyGroup>
			</Project>
			""";
		using var tempProject = TempProjectFile(csprojContent);
		var frameworks = MauiProjectResolver.GetTargetFrameworks(tempProject.Path);
		Assert.Equal(3, frameworks.Count);
		Assert.Contains("net10.0-android", frameworks);
		Assert.Contains("net10.0-ios", frameworks);
		Assert.Contains("net10.0-maccatalyst", frameworks);
	}

	[Fact]
	public void GetTargetFrameworks_IgnoresMSBuildVariableExpressions()
	{
		var csprojContent = """
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup>
			    <TargetFrameworks>net10.0-android;$(AdditionalFrameworks)</TargetFrameworks>
			  </PropertyGroup>
			</Project>
			""";
		using var tempProject = TempProjectFile(csprojContent);
		var frameworks = MauiProjectResolver.GetTargetFrameworks(tempProject.Path);
		Assert.All(frameworks, f => Assert.DoesNotContain("$(", f, StringComparison.Ordinal));
	}

	[Fact]
	public void GetAndroidApplicationId_ReadsApplicationIdFromProjectFile()
	{
		var csprojContent = """
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup>
			    <TargetFramework>net10.0-android</TargetFramework>
			    <ApplicationId>com.example.myapp</ApplicationId>
			  </PropertyGroup>
			</Project>
			""";

		using var tempProject = TempProjectFile(csprojContent);
		var applicationId = MauiProjectResolver.GetAndroidApplicationId(tempProject.Path, "net10.0-android", "Debug");

		Assert.Equal("com.example.myapp", applicationId);
	}

	[Fact]
	public void GetAndroidApplicationId_PrefersBuiltManifestPackage()
	{
		var csprojContent = """
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup>
			    <TargetFramework>net10.0-android</TargetFramework>
			    <ApplicationId>com.example.fromproject</ApplicationId>
			  </PropertyGroup>
			</Project>
			""";

		using var tempProject = TempProjectFile(csprojContent);
		var projectDirectory = Path.GetDirectoryName(tempProject.Path)!;
		var manifestDirectory = Path.Combine(projectDirectory, "obj", "Debug", "net10.0-android");
		Directory.CreateDirectory(manifestDirectory);
		File.WriteAllText(
			Path.Combine(manifestDirectory, "AndroidManifest.xml"),
			"""<manifest xmlns:android="http://schemas.android.com/apk/res/android" package="com.example.frommanifest" />""");

		var applicationId = MauiProjectResolver.GetAndroidApplicationId(tempProject.Path, "net10.0-android", "Debug");

		Assert.Equal("com.example.frommanifest", applicationId);
	}

	// ── Helpers ───────────────────────────────────────────────────────────────

	static ResolvedMauiProject FakeProject(IReadOnlyList<string> targetFrameworks) =>
		new()
		{
			ProjectPath = "/fake/MyApp.csproj",
			ProjectDirectory = "/fake",
			ProjectName = "MyApp",
			TargetFrameworks = targetFrameworks
		};

	static Device CreateDevice(string platform, bool isEmulator) =>
		new()
		{
			Id = isEmulator ? $"{platform}-emu" : $"{platform}-device",
			Name = isEmulator ? $"{platform} emulator" : $"{platform} device",
			Platforms = [platform],
			IsEmulator = isEmulator,
			IsRunning = true,
			Type = isEmulator ? DeviceType.Emulator : DeviceType.Physical,
			State = DeviceState.Booted
		};

	static TempFile TempProjectFile(string content)
	{
		var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
		var path = Path.Combine(directory, "TestProject.csproj");
		File.WriteAllText(path, content);
		return new TempFile(path);
	}

	// ── BuildTraceArguments ───────────────────────────────────────────────────

	[Fact]
	public void BuildTraceArguments_NoStoppingEvent_UsesDefaultProviders()
	{
		// When no stopping event is specified and no trace profile is given,
		// no --profile or --providers flags should be passed so dotnet-trace
		// applies its own defaults (dotnet-common + dotnet-sampled-thread-time).
		var device = CreateDevice(Platforms.Android, isEmulator: true);
		var transport = ProfileCommand.ResolveProfileTransport(Platforms.Android, device);
		var args = ProfileCommand.BuildTraceArguments(
			outputPath: "/out.nettrace",
			outputFormat: TraceOutputFormat.NetTrace,
			transport: transport,
			traceProfile: null,
			duration: null,
			stoppingEventProvider: null,
			stoppingEventName: null,
			stoppingEventPayloadFilter: null).ToArray();

		Assert.DoesNotContain("--profile", args);
		Assert.DoesNotContain("--providers", args);
		Assert.DoesNotContain("--stopping-event-provider-name", args);
		Assert.DoesNotContain("--process-id", args);
		Assert.Contains("--dsrouter", args);
		Assert.Contains("android-emu", args);
		Assert.Equal("NetTrace", args[Array.IndexOf(args, "--format") + 1]);
	}

	[Fact]
	public void BuildTraceArguments_WithStoppingEvent_InjectsDefaultProfilesAndProvider()
	{
		// When a stopping event provider is specified, --profile must include the
		// default profiles so runtime/sampling events are still collected, and
		// --providers must enable both the startup marker provider and the
		// runtime JIT/R2R provider so dotnet-pgo can later create a richer MIBC.
		var device = CreateDevice(Platforms.Android, isEmulator: true);
		var transport = ProfileCommand.ResolveProfileTransport(Platforms.Android, device);
		var args = ProfileCommand.BuildTraceArguments(
			outputPath: "/out.nettrace",
			outputFormat: TraceOutputFormat.NetTrace,
			transport: transport,
			traceProfile: null,
			duration: null,
			stoppingEventProvider: "Microsoft.Maui.StartupProfiling",
			stoppingEventName: "StartupComplete",
			stoppingEventPayloadFilter: null).ToArray();

		// Default profiles injected
		var profileIdx = Array.IndexOf(args, "--profile");
		Assert.True(profileIdx >= 0, "--profile flag should be present");
		Assert.Equal("dotnet-common,dotnet-sampled-thread-time", args[profileIdx + 1]);

		// Stopping event provider enabled via --providers
		var providersIdx = Array.IndexOf(args, "--providers");
		Assert.True(providersIdx >= 0, "--providers flag should be present");
		Assert.Contains("Microsoft.Maui.StartupProfiling", args[providersIdx + 1]);
		Assert.Contains("Microsoft-Windows-DotNETRuntime", args[providersIdx + 1]);
		Assert.Contains("0x1F000080018:5", args[providersIdx + 1]);

		// Stopping event flags present
		Assert.Contains("--stopping-event-provider-name", args);
		Assert.Contains("--stopping-event-event-name", args);
	}

	[Fact]
	public void BuildTraceArguments_WithUserTraceProfile_UsesUserProfileNotDefaults()
	{
		// When the user explicitly specifies a trace profile, we must not override it
		// with the default profiles.
		var device = CreateDevice(Platforms.Android, isEmulator: true);
		var transport = ProfileCommand.ResolveProfileTransport(Platforms.Android, device);
		var args = ProfileCommand.BuildTraceArguments(
			outputPath: "/out.nettrace",
			outputFormat: TraceOutputFormat.NetTrace,
			transport: transport,
			traceProfile: "gc-verbose",
			duration: null,
			stoppingEventProvider: null,
			stoppingEventName: null,
			stoppingEventPayloadFilter: null).ToArray();

		var profileIdx = Array.IndexOf(args, "--profile");
		Assert.True(profileIdx >= 0);
		Assert.Equal("gc-verbose", args[profileIdx + 1]);

		// No injected providers when no stopping event is specified
		Assert.DoesNotContain("--providers", args);
	}

	[Fact]
	public void BuildTraceArguments_UserProfileWithStoppingEvent_KeepsUserProfileAddsProviders()
	{
		// When the user specifies both a profile AND a stopping event provider,
		// we use their profile (not the defaults) but still inject the stopping
		// event provider via --providers.
		var device = CreateDevice(Platforms.Android, isEmulator: true);
		var transport = ProfileCommand.ResolveProfileTransport(Platforms.Android, device);
		var args = ProfileCommand.BuildTraceArguments(
			outputPath: "/out.nettrace",
			outputFormat: TraceOutputFormat.NetTrace,
			transport: transport,
			traceProfile: "gc-verbose",
			duration: null,
			stoppingEventProvider: "Microsoft.Maui.StartupProfiling",
			stoppingEventName: "StartupComplete",
			stoppingEventPayloadFilter: null).ToArray();

		var profileIdx = Array.IndexOf(args, "--profile");
		Assert.True(profileIdx >= 0);
		Assert.Equal("gc-verbose", args[profileIdx + 1]);

		// Stopping event provider still injected
		var providersIdx = Array.IndexOf(args, "--providers");
		Assert.True(providersIdx >= 0);
		Assert.Contains("Microsoft.Maui.StartupProfiling", args[providersIdx + 1]);
		Assert.Contains("Microsoft-Windows-DotNETRuntime", args[providersIdx + 1]);
		Assert.Contains("0x1F000080018:5", args[providersIdx + 1]);
	}

	[Fact]
	public void BuildTraceArguments_Speedscope_UsesSpeedscopeFormat()
	{
		var device = CreateDevice(Platforms.Android, isEmulator: true);
		var transport = ProfileCommand.ResolveProfileTransport(Platforms.Android, device);
		var args = ProfileCommand.BuildTraceArguments(
			outputPath: "/out.nettrace",
			outputFormat: TraceOutputFormat.Speedscope,
			transport: transport,
			traceProfile: null,
			duration: null,
			stoppingEventProvider: null,
			stoppingEventName: null,
			stoppingEventPayloadFilter: null).ToArray();

		var formatIdx = Array.IndexOf(args, "--format");
		Assert.True(formatIdx >= 0);
		Assert.Equal("Speedscope", args[formatIdx + 1]);
	}


	[Fact]
	public void ResolveProfileTransport_AndroidEmulator_UsesEmulatorLoopbackAlias()
	{
		var transport = ProfileCommand.ResolveProfileTransport(
			Platforms.Android,
			CreateDevice(Platforms.Android, isEmulator: true));

		Assert.Equal("10.0.2.2", transport.DiagnosticAddress);
		Assert.Equal("connect", transport.DiagnosticListenMode);
		Assert.Equal("android-emu", transport.DsrouterKind);
		Assert.False(transport.RequiresManualExitControlPortRouting);
	}

	[Fact]
	public void ResolveProfileTransport_AndroidDevice_UsesLoopbackAndManualExitRouting()
	{
		var transport = ProfileCommand.ResolveProfileTransport(
			Platforms.Android,
			CreateDevice(Platforms.Android, isEmulator: false));

		Assert.Equal("127.0.0.1", transport.DiagnosticAddress);
		Assert.Equal("connect", transport.DiagnosticListenMode);
		Assert.Equal("android", transport.DsrouterKind);
		Assert.True(transport.RequiresManualExitControlPortRouting);
	}

	[Fact]
	public void ResolveProfileTransport_Ios_UsesListenModeAndTcpClient()
	{
		var transport = ProfileCommand.ResolveProfileTransport(
			Platforms.iOS,
			CreateDevice(Platforms.iOS, isEmulator: true));

		Assert.Equal("127.0.0.1", transport.DiagnosticAddress);
		Assert.Equal("listen", transport.DiagnosticListenMode);
		Assert.Equal("ios-sim", transport.DsrouterKind);
		Assert.False(transport.RequiresManualExitControlPortRouting);
	}

	[Fact]
	public void ResolveProfileTransport_IosDevice_UsesUsbForwarding()
	{
		var transport = ProfileCommand.ResolveProfileTransport(
			Platforms.iOS,
			CreateDevice(Platforms.iOS, isEmulator: false));

		Assert.Equal("ios", transport.DsrouterKind);
	}

	[Fact]
	public void BuildLaunchArguments_IosSimulator_UsesCrossPlatformDevicePropertyAndNonBlockingMlaunchFlag()
	{
		var device = CreateDevice(Platforms.iOS, isEmulator: true) with { Id = "ios-sim-udid" };
		var transport = ProfileCommand.ResolveProfileTransport(Platforms.iOS, device);

		var args = ProfileCommand.BuildLaunchArguments(
			"/fake/MyApp.csproj",
			"net10.0-ios",
			"Release",
			device,
			transport,
			9000,
			buildInjection: null);

		Assert.Contains("-p:Device=ios-sim-udid", args);
		Assert.Contains("-p:_MlaunchWaitForExit=false", args);
	}

	[Fact]
	public void BuildCompileArguments_IosSimulator_EmbedsDiagnosticConfiguration()
	{
		var device = CreateDevice(Platforms.iOS, isEmulator: true) with { Id = "ios-sim-udid" };
		var transport = ProfileCommand.ResolveProfileTransport(Platforms.iOS, device);

		var args = ProfileCommand.BuildCompileArguments(
			"/fake/MyApp.csproj",
			"net10.0-ios",
			"Release",
			transport,
			9000,
			buildInjection: null);

		Assert.Contains("-p:DiagnosticAddress=127.0.0.1", args);
		Assert.Contains("-p:DiagnosticPort=9000", args);
		Assert.Contains("-p:DiagnosticSuspend=true", args);
		Assert.Contains("-p:DiagnosticListenMode=listen", args);
		Assert.Contains("-p:EnableDiagnostics=true", args);
	}

	[Fact]
	public void BuildCompileArguments_WithRuntimeOwnedEventPipe_SkipsDiagnosticArgsAndAddsRuntimePgoProperties()
	{
		var device = CreateDevice(Platforms.Android, isEmulator: true);
		var transport = ProfileCommand.ResolveProfileTransport(Platforms.Android, device);
		var buildInjection = new ProfilingBuildInjection(
			TargetsPath: "/fake/MauiStartupProfilingInjection.targets",
			AssemblyPath: "/fake/Microsoft.Maui.StartupProfiling.dll",
			ExitControlHost: "10.0.2.2",
			ExitControlPort: 9001,
			InjectBootstrap: true,
			EnableRuntimePgo: true,
			EventPipeOutputPath: "/storage/emulated/0/Android/data/com.example/files/startup.nettrace");

		var args = ProfileCommand.BuildCompileArguments(
			"/fake/MyApp.csproj",
			"net10.0-android",
			"Release",
			transport,
			9000,
			buildInjection);

		Assert.DoesNotContain("-p:DiagnosticSuspend=true", args);
		Assert.Contains("-p:EnableDiagnostics=true", args);
		Assert.Contains("-p:MauiStartupProfilingExitHost=10.0.2.2", args);
		Assert.Contains("-p:MauiStartupProfilingExitPort=9001", args);
		Assert.Contains("-p:MauiStartupProfilingEnableRuntimePgo=true", args);
		Assert.Contains("-p:MauiStartupProfilingEventPipeOutputPath=/storage/emulated/0/Android/data/com.example/files/startup.nettrace", args);
	}

	[Fact]
	public void BuildLaunchArguments_WithRuntimeOwnedEventPipe_UsesCrossPlatformDeviceProperty()
	{
		var device = CreateDevice(Platforms.Android, isEmulator: true);
		var transport = ProfileCommand.ResolveProfileTransport(Platforms.Android, device);
		var buildInjection = new ProfilingBuildInjection(
			TargetsPath: "/fake/MauiStartupProfilingInjection.targets",
			AssemblyPath: "/fake/Microsoft.Maui.StartupProfiling.dll",
			ExitControlHost: "10.0.2.2",
			ExitControlPort: 9001,
			InjectBootstrap: true,
			EnableRuntimePgo: true,
			EventPipeOutputPath: "/storage/emulated/0/Android/data/com.example/files/startup.nettrace");

		var args = ProfileCommand.BuildLaunchArguments(
			"/fake/MyApp.csproj",
			"net10.0-android",
			"Release",
			device,
			transport,
			9000,
			buildInjection);

		Assert.DoesNotContain("-p:AndroidEnableProfiler=true", args);
		Assert.Contains("-p:EnableDiagnostics=true", args);
		Assert.Contains("-p:Device=android-emu", args);
		Assert.Contains("-p:MauiStartupProfilingExitHost=10.0.2.2", args);
		Assert.Contains("-p:MauiStartupProfilingExitPort=9001", args);
	}

	[Fact]
	public void FindAvailableTcpPort_SkipsBusyPort()
	{
		using var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();

		var busyPort = ((IPEndPoint)listener.LocalEndpoint).Port;
		var selectedPort = ProfileCommand.FindAvailableTcpPort(busyPort, busyPort + 20);

		Assert.NotEqual(busyPort, selectedPort);
		Assert.InRange(selectedPort, busyPort + 1, busyPort + 20);
	}

	[Theory]
	[InlineData("System.IO.EndOfStreamException: Attempted to read past the end of the stream.")]
	[InlineData("Microsoft.Diagnostics.NETCore.Client.ServerNotAvailableException: Unable to connect to the server. Connection refused")]
	[InlineData("SocketException (49): Can't assign requested address")]
	public void IsRetryableTraceStartupFailure_KnownConnectionErrors_ReturnTrue(string details)
	{
		Assert.True(ProfileCommand.IsRetryableTraceStartupFailure(details));
	}

	[Fact]
	public void IsRetryableTraceStartupFailure_UnrelatedError_ReturnsFalse()
	{
		Assert.False(ProfileCommand.IsRetryableTraceStartupFailure("dotnet-trace exited with code 1."));
	}

	[Fact]
	public void CanResolveDiagnosticsTool_ReturnsTrueWhenEitherInstalledOrCached()
	{
		Assert.True(ProfileCommand.CanResolveDiagnosticsTool("/Users/test/.dotnet/tools/dotnet-trace", null));
		Assert.True(ProfileCommand.CanResolveDiagnosticsTool(null, "/Users/test/.nuget/packages/dotnet-trace/tools/net8.0/any/dotnet-trace.dll"));
	}

	[Fact]
	public void CanUseDiagnosticsTooling_MixedInstalledAndCachedTools_ReturnsTrue()
	{
		var hasDotnetTrace = ProfileCommand.CanResolveDiagnosticsTool("/Users/test/.dotnet/tools/dotnet-trace", null);
		var hasDotnetDsrouter = ProfileCommand.CanResolveDiagnosticsTool(null, "/Users/test/.nuget/packages/dotnet-dsrouter/tools/net8.0/any/dotnet-dsrouter.dll");

		Assert.True(ProfileCommand.CanUseDiagnosticsTooling(
			hasDnx: false,
			hasDotnetTrace: hasDotnetTrace,
			hasDotnetDsrouter: hasDotnetDsrouter));
	}

	[Fact]
	public void CanUseDiagnosticsTooling_MissingRequiredToolWithoutDnx_ReturnsFalse()
	{
		Assert.False(ProfileCommand.CanUseDiagnosticsTooling(hasDnx: false, hasDotnetTrace: true, hasDotnetDsrouter: false));
	}

	[Fact]
	public void ResolveProfileConfiguration_IosWithoutExplicitOverride_DefaultsToRelease()
	{
		var configuration = ProfileCommand.ResolveProfileConfiguration("Release", explicitlySpecified: false, Platforms.iOS);

		Assert.Equal("Release", configuration);
	}

	[Fact]
	public void ResolveProfileConfiguration_IosExplicitOverride_PreservesRequestedValue()
	{
		var configuration = ProfileCommand.ResolveProfileConfiguration("Release", explicitlySpecified: true, Platforms.iOS);

		Assert.Equal("Release", configuration);
	}

	[Fact]
	public void ResolveProfileConfiguration_AndroidWithoutExplicitOverride_RemainsRelease()
	{
		var configuration = ProfileCommand.ResolveProfileConfiguration("Release", explicitlySpecified: false, Platforms.Android);

		Assert.Equal("Release", configuration);
	}

	[Fact]
	public void ValidateTraceOutput_NonEmptyNettrace_ReturnsWithoutThrowing()
	{
		using var output = CreateTempFile("trace.nettrace");
		File.WriteAllBytes(output.Path, [0x01, 0x02, 0x03]);

		ProfileCommand.ValidateTraceOutput(output.Path, output.Path, TraceOutputFormat.NetTrace, Platforms.Android);
	}

	[Fact]
	public void ValidateTraceOutput_EmptyIosTrace_ThrowsHelpfulError()
	{
		using var output = CreateTempFile("trace.nettrace");

		var exception = Assert.Throws<MauiToolException>(() =>
			ProfileCommand.ValidateTraceOutput(output.Path, output.Path, TraceOutputFormat.NetTrace, Platforms.iOS));

		Assert.Contains("is empty", exception.Message);
		Assert.NotNull(exception.Remediation?.ManualSteps);
		Assert.Contains("dotnet-trace", string.Join(Environment.NewLine, exception.Remediation!.ManualSteps!));
	}

	[Fact]
	public void ShouldRequestManualStop_WhenTraceAlreadyExited_ReturnsFalse()
	{
		var processWaitTask = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously).Task;
		var manualStopTask = Task.CompletedTask;

		var result = ProfileTraceLifecycle.ShouldRequestManualStop(
			manualStopTask,
			processWaitTask,
			processHasExited: true);

		Assert.False(result);
	}

	[Fact]
	public void ShouldRequestManualStop_WhenManualStopWinsWhileTraceIsRunning_ReturnsTrue()
	{
		var processWaitTask = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously).Task;
		var manualStopTask = Task.CompletedTask;

		var result = ProfileTraceLifecycle.ShouldRequestManualStop(
			manualStopTask,
			processWaitTask,
			processHasExited: false);

		Assert.True(result);
	}

	[Fact]
	public void TryParseLongListingSize_ValidAndroidListing_ReturnsExpectedSize()
	{
		var output = "-rw-rw---- 1 10234 10234 473559 2026-04-15 17:20 startup.nettrace";

		var size = RuntimeOwnedTraceCollector.TryParseLongListingSize(output);

		Assert.Equal(473559, size);
	}

	[Fact]
	public void TryParseLongListingSize_InvalidListing_ReturnsZero()
	{
		var size = RuntimeOwnedTraceCollector.TryParseLongListingSize("No such file or directory");

		Assert.Equal(0, size);
	}

	[Fact]
	public void ShouldTreatAppAsExited_RequiresPriorRunningObservation()
	{
		var result = new ProcessResult { ExitCode = 0, StandardOutput = string.Empty };

		Assert.False(RuntimeOwnedTraceCollector.ShouldTreatAppAsExited(sawRunning: false, result));
		Assert.True(RuntimeOwnedTraceCollector.ShouldTreatAppAsExited(sawRunning: true, result));
	}

	[Fact]
	public void ShouldTreatAppAsExited_IgnoresTransientPidFailures()
	{
		var result = new ProcessResult { ExitCode = 1, StandardOutput = string.Empty, StandardError = "adb: device offline" };

		Assert.False(RuntimeOwnedTraceCollector.ShouldTreatAppAsExited(sawRunning: true, result));
	}

	[Fact]
	public void ResolveStoppingEventConfiguration_LeavesStoppingEventUnsetWithoutExplicitOptions()
	{
		var result = ProfileCommand.ResolveStoppingEventConfiguration(
			duration: null,
			providerName: null,
			eventName: null,
			payloadFilter: null);

		Assert.False(result.AutoSelected);
		Assert.Null(result.ProviderName);
		Assert.Null(result.EventName);
		Assert.Null(result.PayloadFilter);
	}

	[Fact]
	public void ResolveStoppingEventConfiguration_DoesNotOverrideExplicitOrTimedSettings()
	{
		var durationResult = ProfileCommand.ResolveStoppingEventConfiguration(
			duration: TimeSpan.FromSeconds(5),
			providerName: null,
			eventName: null,
			payloadFilter: null);

		Assert.False(durationResult.AutoSelected);
		Assert.Null(durationResult.ProviderName);

		var customResult = ProfileCommand.ResolveStoppingEventConfiguration(
			duration: null,
			providerName: "Custom.Provider",
			eventName: "Done",
			payloadFilter: "kind:start");

		Assert.False(customResult.AutoSelected);
		Assert.Equal("Custom.Provider", customResult.ProviderName);
		Assert.Equal("Done", customResult.EventName);
		Assert.Equal("kind:start", customResult.PayloadFilter);
	}

	static TempFile CreateTempFile(string fileName)
	{
		var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "maui-cli-profile-tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
		var path = System.IO.Path.Combine(directory, fileName);
		File.WriteAllBytes(path, []);
		return new TempFile(path);
	}

	sealed class TempFile(string path) : IDisposable
	{
		public string Path { get; } = path;
		public void Dispose()
		{
			try
			{
				var directory = System.IO.Path.GetDirectoryName(Path);
				if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
					Directory.Delete(directory, recursive: true);
				else
					File.Delete(Path);
			}
			catch { /* best-effort cleanup */ }
		}
	}
}
