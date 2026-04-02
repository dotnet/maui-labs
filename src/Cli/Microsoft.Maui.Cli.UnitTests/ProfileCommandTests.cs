// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using Microsoft.Maui.Cli.Commands;
using Microsoft.Maui.Cli.Errors;
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
	public void ProfileCommand_DefaultPlatformIsAndroid()
	{
		var command = ProfileCommand.Create();
		var platformOption = (Option<string>)command.Options.First(o => o.Name == "--platform");
		var parseResult = command.Parse("profile");
		Assert.Equal("android", parseResult.GetValue(platformOption));
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
		var path = ProfileCommand.ResolveOutputPath("MyApp", "/tmp/my-trace.nettrace");
		Assert.Equal(Path.GetFullPath("/tmp/my-trace.nettrace"), path);
	}

	[Fact]
	public void ResolveOutputPath_AddsNettraceExtensionWhenMissing()
	{
		var path = ProfileCommand.ResolveOutputPath("MyApp", "/tmp/my-trace");
		Assert.EndsWith(".nettrace", path, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void ResolveOutputPath_DefaultNameIncludesProjectName()
	{
		var path = ProfileCommand.ResolveOutputPath("MyApp", null);
		var fileName = Path.GetFileName(path);
		Assert.StartsWith("MyApp_", fileName, StringComparison.OrdinalIgnoreCase);
		Assert.EndsWith(".nettrace", fileName, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void ResolveOutputPath_FallsBackWhenProjectNameIsEmpty()
	{
		var path = ProfileCommand.ResolveOutputPath(string.Empty, null);
		var fileName = Path.GetFileName(path);
		Assert.StartsWith("maui-startup-profile_", fileName, StringComparison.OrdinalIgnoreCase);
		Assert.EndsWith(".nettrace", fileName, StringComparison.OrdinalIgnoreCase);
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

	// ── Helpers ───────────────────────────────────────────────────────────────

	static ResolvedMauiProject FakeProject(IReadOnlyList<string> targetFrameworks) =>
		new()
		{
			ProjectPath = "/fake/MyApp.csproj",
			ProjectDirectory = "/fake",
			ProjectName = "MyApp",
			TargetFrameworks = targetFrameworks
		};

	static TempFile TempProjectFile(string content)
	{
		var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.csproj");
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

	sealed class TempFile(string path) : IDisposable
	{
		public string Path { get; } = path;
		public void Dispose()
		{
			try { File.Delete(Path); }
			catch { /* best-effort cleanup */ }
		}
	}
}
