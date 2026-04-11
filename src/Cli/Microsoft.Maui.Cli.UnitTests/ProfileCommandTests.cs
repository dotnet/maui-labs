// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using System.Net;
using System.Net.Sockets;
using Microsoft.Maui.Cli.Commands;
using Microsoft.Maui.Cli.Errors;
using Microsoft.Maui.Cli.Models;
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
	}

	[Fact]
	public void ProfileCommand_HasExpectedOptions()
	{
		var command = ProfileCommand.Create();
		Assert.Contains(command.Options, o => o.Name == "--project");
		Assert.Contains(command.Options, o => o.Name == "--framework");
		Assert.Contains(command.Options, o => o.Name == "--device");
		Assert.Contains(command.Options, o => o.Name == "--output");
		Assert.Contains(command.Options, o => o.Name == "--format");
		Assert.Contains(command.Options, o => o.Name == "--configuration");
		Assert.Contains(command.Options, o => o.Name == "--platform");
		Assert.Contains(command.Options, o => o.Name == "--duration");
		Assert.Contains(command.Options, o => o.Name == "--trace-profile");
		Assert.Contains(command.Options, o => o.Name == "--no-build");
		Assert.Contains(command.Options, o => o.Name == "--diagnostic-port");
		Assert.Contains(command.Options, o => o.Name == "--stopping-event-provider-name");
		Assert.Contains(command.Options, o => o.Name == "--stopping-event-event-name");
		Assert.Contains(command.Options, o => o.Name == "--stopping-event-payload-filter");
	}

	[Fact]
	public void ProfileCommand_DefaultConfigurationIsRelease()
	{
		var command = ProfileCommand.Create();
		var configOption = (Option<string>)command.Options.First(o => o.Name == "--configuration");
		var parseResult = command.Parse("profile");
		Assert.Equal("Release", parseResult.GetValue(configOption));
	}

	[Fact]
	public void ProfileCommand_DefaultFormatIsNetTrace()
	{
		var command = ProfileCommand.Create();
		var formatOption = (Option<string>)command.Options.First(o => o.Name == "--format");
		var parseResult = command.Parse("profile");
		Assert.Equal("nettrace", parseResult.GetValue(formatOption));
	}

	[Fact]
	public void ProfileCommand_FormatOptionIsNotExplicitWhenOmitted()
	{
		var command = ProfileCommand.Create();
		var formatOption = (Option<string>)command.Options.First(o => o.Name == "--format");
		var parseResult = command.Parse("profile");

		Assert.False(ProfileCommand.WasOptionExplicitlySpecified(parseResult, formatOption));
	}

	[Fact]
	public void ProfileCommand_FormatOptionIsExplicitWhenProvided()
	{
		var command = ProfileCommand.Create();
		var formatOption = (Option<string>)command.Options.First(o => o.Name == "--format");
		var parseResult = command.Parse("profile --format speedscope");

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
		var platformOption = (Option<string>)command.Options.First(o => o.Name == "--platform");
		var parseResult = command.Parse("profile");
		Assert.Equal("all", parseResult.GetValue(platformOption));
	}

	[Fact]
	public void ProfileCommand_DefaultDiagnosticPortIs9000()
	{
		var command = ProfileCommand.Create();
		var portOption = (Option<int>)command.Options.First(o => o.Name == "--diagnostic-port");
		var parseResult = command.Parse("profile");
		Assert.Equal(9000, parseResult.GetValue(portOption));
	}

	[Fact]
	public void ProfileCommand_NoBuildDefaultIsFalse()
	{
		var command = ProfileCommand.Create();
		var noBuildOption = (Option<bool>)command.Options.First(o => o.Name == "--no-build");
		var parseResult = command.Parse("profile");
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
		Assert.Equal("/tmp/my-trace.nettrace.speedscope.json", path);
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
		var args = ProfileCommand.BuildTraceArguments(
			outputPath: "/out.nettrace",
			outputFormat: TraceOutputFormat.NetTrace,
			dsrouterPid: 12345,
			traceProfile: null,
			duration: null,
			stoppingEventProvider: null,
			stoppingEventName: null,
			stoppingEventPayloadFilter: null).ToArray();

		Assert.DoesNotContain("--profile", args);
		Assert.DoesNotContain("--providers", args);
		Assert.DoesNotContain("--stopping-event-provider-name", args);
		// Should use --process-id, not --dsrouter
		Assert.Contains("--process-id", args);
		Assert.DoesNotContain("--dsrouter", args);
		Assert.Equal("NetTrace", args[Array.IndexOf(args, "--format") + 1]);
	}

	[Fact]
	public void BuildTraceArguments_WithStoppingEvent_InjectsDefaultProfilesAndProvider()
	{
		// When a stopping event provider is specified, --profile must include the
		// default profiles so runtime/sampling events are still collected, and
		// --providers must enable the stopping event provider so dotnet-trace
		// actually receives the event (--stopping-event-provider-name alone is not enough).
		var args = ProfileCommand.BuildTraceArguments(
			outputPath: "/out.nettrace",
			outputFormat: TraceOutputFormat.NetTrace,
			dsrouterPid: 12345,
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

		// Stopping event flags present
		Assert.Contains("--stopping-event-provider-name", args);
		Assert.Contains("--stopping-event-event-name", args);
	}

	[Fact]
	public void BuildTraceArguments_WithUserTraceProfile_UsesUserProfileNotDefaults()
	{
		// When the user explicitly specifies a trace profile, we must not override it
		// with the default profiles.
		var args = ProfileCommand.BuildTraceArguments(
			outputPath: "/out.nettrace",
			outputFormat: TraceOutputFormat.NetTrace,
			dsrouterPid: 12345,
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
		var args = ProfileCommand.BuildTraceArguments(
			outputPath: "/out.nettrace",
			outputFormat: TraceOutputFormat.NetTrace,
			dsrouterPid: 12345,
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
	}

	[Fact]
	public void BuildTraceArguments_Speedscope_UsesSpeedscopeFormat()
	{
		var args = ProfileCommand.BuildTraceArguments(
			outputPath: "/out.nettrace",
			outputFormat: TraceOutputFormat.Speedscope,
			dsrouterPid: 12345,
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
	public void BuildDsrouterArguments_UsesSelectedDiagnosticPort()
	{
		var transport = ProfileCommand.ResolveProfileTransport(
			Platforms.Android,
			CreateDevice(Platforms.Android, isEmulator: false));
		var args = ProfileCommand.BuildDsrouterArguments(transport, 9012);

		Assert.Equal("server-server", args[0]);

		var tcpServerIdx = Array.IndexOf(args, "-tcps");
		Assert.True(tcpServerIdx >= 0);
		Assert.Equal("127.0.0.1:9012", args[tcpServerIdx + 1]);
		Assert.Contains("--forward-port", args);
		Assert.Contains("Android", args);
	}

	[Fact]
	public void ResolveProfileTransport_AndroidEmulator_UsesEmulatorLoopbackAlias()
	{
		var transport = ProfileCommand.ResolveProfileTransport(
			Platforms.Android,
			CreateDevice(Platforms.Android, isEmulator: true));

		Assert.Equal("10.0.2.2", transport.DiagnosticAddress);
		Assert.Equal("connect", transport.DiagnosticListenMode);
		Assert.Equal("server-server", transport.DsrouterKind);
		Assert.Equal("-tcps", transport.DsrouterRuntimeEndpointOption);
		Assert.Equal("Android", transport.DsrouterForwardPort);
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
		Assert.Equal("server-client", transport.DsrouterKind);
		Assert.Equal("-tcpc", transport.DsrouterRuntimeEndpointOption);
		Assert.Null(transport.DsrouterForwardPort);
		Assert.False(transport.RequiresManualExitControlPortRouting);
	}

	[Fact]
	public void ResolveProfileTransport_IosDevice_UsesUsbForwarding()
	{
		var transport = ProfileCommand.ResolveProfileTransport(
			Platforms.iOS,
			CreateDevice(Platforms.iOS, isEmulator: false));

		Assert.Equal("iOS", transport.DsrouterForwardPort);
	}

	[Fact]
	public void BuildLaunchArguments_IosSimulator_AddsSimulatorUdidAndNonBlockingMlaunchFlag()
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

		Assert.Contains("-p:_DeviceName=:v2:udid=ios-sim-udid", args);
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
	public void ResolveProfileConfiguration_IosWithoutExplicitOverride_DefaultsToDebug()
	{
		var configuration = ProfileCommand.ResolveProfileConfiguration("Release", explicitlySpecified: false, Platforms.iOS);

		Assert.Equal("Debug", configuration);
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
		Assert.Contains("--configuration Debug", string.Join(Environment.NewLine, exception.Remediation!.ManualSteps!));
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
