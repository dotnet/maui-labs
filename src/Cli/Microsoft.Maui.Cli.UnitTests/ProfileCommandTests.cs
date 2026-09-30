// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Maui.Cli.Commands;
using Microsoft.Maui.Cli.Errors;
using Microsoft.Maui.Cli.Models;
using Microsoft.Maui.Cli.Output;
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
		Assert.Contains(startup.Options, o => o.Name == "--trace-stop-timeout");
		Assert.Contains(startup.Options, o => o.Name == "--stopping-event-provider-name");
		Assert.Contains(startup.Options, o => o.Name == "--stopping-event-event-name");
		Assert.Contains(startup.Options, o => o.Name == "--stopping-event-payload-filter");
	}

	[Fact]
	public void ProfileCommand_DefaultTraceStopTimeoutIsTwoMinutes()
	{
		var command = ProfileCommand.Create();
		var startup = command.Subcommands.Single(c => c.Name == "startup");
		var timeoutOption = (Option<TimeSpan>)startup.Options.First(o => o.Name == "--trace-stop-timeout");
		var parseResult = command.Parse("profile startup");

		Assert.Equal(TimeSpan.FromMinutes(2), parseResult.GetValue(timeoutOption));
	}

	[Fact]
	public void ProfileCommand_ParsesExplicitTraceStopTimeout()
	{
		var command = ProfileCommand.Create();
		var startup = command.Subcommands.Single(c => c.Name == "startup");
		var timeoutOption = (Option<TimeSpan>)startup.Options.First(o => o.Name == "--trace-stop-timeout");
		var parseResult = command.Parse("profile startup --trace-stop-timeout 00:05:00");

		Assert.Equal(TimeSpan.FromMinutes(5), parseResult.GetValue(timeoutOption));
	}

	[Fact]
	public void ProfileCommand_ManualSubcommandHasTraceStopTimeout()
	{
		var manual = ProfileCommand.Create().Subcommands.Single(c => c.Name == "manual");

		Assert.Contains(manual.Options, o => o.Name == "--trace-stop-timeout");
	}

	[Fact]
	public void ValidateTraceStopTimeout_RejectsNonPositiveValues()
	{
		Assert.Throws<MauiToolException>(() => ProfileCommand.ValidateTraceStopTimeout(TimeSpan.Zero));
		Assert.Throws<MauiToolException>(() => ProfileCommand.ValidateTraceStopTimeout(TimeSpan.FromSeconds(-1)));
		ProfileCommand.ValidateTraceStopTimeout(TimeSpan.FromSeconds(1));
	}

	[Fact]
	public async Task StopAndWaitForFinalizationAsync_TimesOutWithCollectorOutput()
	{
		await using var testProcess = StartProfileTestProcess("ignore-stdin");
		await testProcess.Ready.WaitAsync(TimeSpan.FromMinutes(1));

		var exception = await Assert.ThrowsAsync<MauiToolException>(() =>
			ProfileTraceLifecycle.StopAndWaitForFinalizationAsync(
				testProcess.MonitoredProcess,
				testProcess.MonitoredProcess.WaitForExitAsync(),
				Task.CompletedTask,
				TimeSpan.FromMilliseconds(50),
				new JsonOutputFormatter(TextWriter.Null),
				useJson: true,
				verbose: false,
				traceStopInterruptDelay: TimeSpan.FromMilliseconds(10)));

		Assert.Contains("did not exit within", exception.Message, StringComparison.Ordinal);
		Assert.Contains("collector-output", exception.NativeError, StringComparison.Ordinal);
	}

	[Fact]
	public async Task StopAndWaitForFinalizationAsync_ReturnsWhenCollectorExitsBeforeInterruptDelay()
	{
		await using var testProcess = StartProfileTestProcess("exit-on-stdin");
		await testProcess.Ready.WaitAsync(TimeSpan.FromMinutes(1));

		await ProfileTraceLifecycle.StopAndWaitForFinalizationAsync(
			testProcess.MonitoredProcess,
			testProcess.MonitoredProcess.WaitForExitAsync(),
			Task.Delay(Timeout.InfiniteTimeSpan),
			TimeSpan.FromSeconds(2),
			new JsonOutputFormatter(TextWriter.Null),
			useJson: true,
			verbose: false,
			traceStopInterruptDelay: TimeSpan.FromSeconds(1));

		Assert.True(testProcess.Process.HasExited);
	}

	[Fact]
	public async Task StopAndWaitForFinalizationAsync_AcknowledgedRundownGetsFullTimeoutWithoutInterrupt()
	{
		var releasePath = Path.Combine(Path.GetTempPath(), $"maui-profile-test-release-{Guid.NewGuid():N}");
		try
		{
			await using var testProcess = StartProfileTestProcess("finalize-on-stdin", releasePath);
			await testProcess.Ready.WaitAsync(TimeSpan.FromMinutes(1));
			var interruptDelay = TimeSpan.FromMilliseconds(100);
			var stopTask = ProfileTraceLifecycle.StopAndWaitForFinalizationAsync(
				testProcess.MonitoredProcess,
				testProcess.MonitoredProcess.WaitForExitAsync(),
				testProcess.FinalizationStarted,
				TimeSpan.FromSeconds(5),
				new JsonOutputFormatter(TextWriter.Null),
				useJson: true,
				verbose: false,
				traceStopInterruptDelay: interruptDelay);

			await testProcess.FinalizationStarted.WaitAsync(TimeSpan.FromSeconds(10));
			await Task.Delay(interruptDelay + interruptDelay);
			await File.WriteAllTextAsync(releasePath, string.Empty);

			Assert.False(await stopTask);
			Assert.True(testProcess.Process.HasExited);
		}
		finally
		{
			File.Delete(releasePath);
		}
	}

	[Fact]
	public async Task StopAndWaitForFinalizationAsync_DescendantInterruptCountsWhenWrapperExitsFirst()
	{
		if (OperatingSystem.IsWindows())
			return;

		await using var testProcess = StartProfileTestProcess("wrap-ignore-stdin");
		await testProcess.Ready.WaitAsync(TimeSpan.FromMinutes(1));

		var interrupted = await ProfileTraceLifecycle.StopAndWaitForFinalizationAsync(
			testProcess.MonitoredProcess,
			testProcess.MonitoredProcess.WaitForExitAsync(),
			Task.Delay(Timeout.InfiniteTimeSpan),
			TimeSpan.FromSeconds(5),
			new JsonOutputFormatter(TextWriter.Null),
			useJson: true,
			verbose: false,
			traceStopInterruptDelay: TimeSpan.FromMilliseconds(10));

		Assert.True(interrupted);
		Assert.Equal(130, testProcess.Process.ExitCode);
	}

	[Theory]
	[InlineData("Stopping the trace. This may take several minutes depending on the application being traced.", true)]
	[InlineData("Trace completed.", false)]
	public void IsFinalizationStartedMessage_RecognizesDotnetTraceRundownOutput(string line, bool expected)
	{
		Assert.Equal(expected, DotnetTraceRunner.IsFinalizationStartedMessage(line));
	}

	[Fact]
	public async Task WaitForCompletionAsync_TimedStopUsesTraceStopTimeout()
	{
		var releasePath = Path.Combine(Path.GetTempPath(), $"maui-profile-test-release-{Guid.NewGuid():N}");
		try
		{
			await using var testProcess = StartProfileTestProcess("finalize-on-stdin", releasePath);
			await testProcess.Ready.WaitAsync(TimeSpan.FromMinutes(1));
			var exception = await Assert.ThrowsAsync<MauiToolException>(() =>
				ProfileTraceLifecycle.WaitForCompletionAsync(
					testProcess.MonitoredProcess,
					allowManualStop: false,
					duration: TimeSpan.FromMilliseconds(10),
					finalizationStartedTask: testProcess.FinalizationStarted,
					traceStopTimeout: TimeSpan.FromMilliseconds(50),
					new JsonOutputFormatter(TextWriter.Null),
					useJson: true,
					verbose: false,
					CancellationToken.None,
					traceStopInterruptDelay: TimeSpan.FromMilliseconds(100)));

			Assert.Contains("after finalization started", exception.Message, StringComparison.Ordinal);
			Assert.Contains("collector-output", exception.NativeError, StringComparison.Ordinal);
		}
		finally
		{
			File.Delete(releasePath);
		}
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
	public void ResolveTraceOutputFormat_UsesExplicitMibcValue()
	{
		var result = ProfileCommand.ResolveTraceOutputFormat(
			requestedFormat: "mibc",
			explicitlySpecified: true,
			nonInteractive: false,
			spectre: null);

		Assert.Equal(TraceOutputFormat.Mibc, result);
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

	[Fact]
	public void ValidateBuildIsolationOptions_NoBuild_RequiresAnIsolatedBuild()
	{
		var exception = Assert.Throws<MauiToolException>(() =>
			ProfileSessionSetup.ValidateBuildIsolationOptions(noBuild: true));

		Assert.Contains("--no-build cannot be used", exception.Message, StringComparison.Ordinal);
		ProfileSessionSetup.ValidateBuildIsolationOptions(noBuild: false);
	}

	// ── Isolated build workspaces ─────────────────────────────────────────────

	[Fact]
	public void ProfileBuildWorkspace_Create_UsesUniqueSessionDirectoriesUnderProjectObj()
	{
		using var tempProject = TempProjectFile("<Project />");
		var projectDirectory = Path.GetDirectoryName(tempProject.Path)!;
		var formatter = new JsonOutputFormatter(TextWriter.Null);
		var first = ProfileBuildWorkspace.Create(projectDirectory, formatter, useJson: true, verbose: false);
		var second = ProfileBuildWorkspace.Create(projectDirectory, formatter, useJson: true, verbose: false);
		first.ConfigureBuildTargets(TestPath("fake", "MauiProfilingHelperInjection.targets"));
		second.ConfigureBuildTargets(profilingInjectionTargetsPath: null);

		try
		{
			var expectedRoot = Path.Combine(projectDirectory, "obj", ProfileBuildWorkspace.RootDirectoryName);
			Assert.StartsWith(expectedRoot + Path.DirectorySeparatorChar, first.Path, StringComparison.Ordinal);
			Assert.StartsWith(expectedRoot + Path.DirectorySeparatorChar, second.Path, StringComparison.Ordinal);
			Assert.NotEqual(first.Path, second.Path);
			Assert.Equal(16, first.SessionId.Length);
			Assert.Equal(16, second.SessionId.Length);
			Assert.True(File.Exists(Path.Combine(first.Path, ProfileBuildWorkspace.OwnershipFileName)));
			Assert.True(File.Exists(Path.Combine(second.Path, ProfileBuildWorkspace.OwnershipFileName)));
			Assert.True(File.Exists(first.DirectoryBuildPropsPath));
			Assert.True(File.Exists(first.DirectoryBuildTargetsPath));
			Assert.True(File.Exists(first.IsolationPropsPath));
			_ = System.Xml.Linq.XDocument.Load(first.DirectoryBuildPropsPath);
			_ = System.Xml.Linq.XDocument.Load(first.DirectoryBuildTargetsPath);
			_ = System.Xml.Linq.XDocument.Load(first.IsolationPropsPath);
			Assert.Contains(
				"MauiProfilingHelperInjection.targets",
				File.ReadAllText(first.DirectoryBuildTargetsPath),
				StringComparison.Ordinal);

			var recovered = ProfileBuildWorkspace.RecoverStaleWorkspaces(
				projectDirectory,
				formatter,
				useJson: true,
				verbose: false,
				now: DateTimeOffset.UtcNow + ProfileBuildWorkspace.StaleWorkspaceMinimumAge + TimeSpan.FromHours(1));

			Assert.Equal(0, recovered);
			Assert.True(Directory.Exists(first.Path));
			Assert.True(Directory.Exists(second.Path));
		}
		finally
		{
			first.Cleanup(formatter, useJson: true, verbose: false);
			second.Cleanup(formatter, useJson: true, verbose: false);
		}

		Assert.False(Directory.Exists(Path.Combine(projectDirectory, "obj", ProfileBuildWorkspace.RootDirectoryName)));
	}

	[Fact]
	public void RecoverStaleWorkspaces_OldOwnedWorkspaceWithExitedOwner_IsDeleted()
	{
		using var tempProject = TempProjectFile("<Project />");
		var projectDirectory = Path.GetDirectoryName(tempProject.Path)!;
		var createdAt = DateTimeOffset.UtcNow - ProfileBuildWorkspace.StaleWorkspaceMinimumAge - TimeSpan.FromHours(1);
		var workspacePath = CreateWorkspaceOwnershipFile(
			projectDirectory,
			Guid.NewGuid().ToString("N"),
			createdAt,
			processId: int.MaxValue);

		var recovered = ProfileBuildWorkspace.RecoverStaleWorkspaces(
			projectDirectory,
			new JsonOutputFormatter(TextWriter.Null),
			useJson: true,
			verbose: false,
			now: DateTimeOffset.UtcNow);

		Assert.Equal(1, recovered);
		Assert.False(Directory.Exists(workspacePath));
	}

	[Fact]
	public void RecoverStaleWorkspaces_RecentOrUnownedWorkspace_IsPreserved()
	{
		using var tempProject = TempProjectFile("<Project />");
		var projectDirectory = Path.GetDirectoryName(tempProject.Path)!;
		var recentWorkspace = CreateWorkspaceOwnershipFile(
			projectDirectory,
			Guid.NewGuid().ToString("N"),
			DateTimeOffset.UtcNow,
			processId: int.MaxValue);
		var unownedWorkspace = CreateWorkspaceOwnershipFile(
			projectDirectory,
			Guid.NewGuid().ToString("N"),
			DateTimeOffset.UtcNow - ProfileBuildWorkspace.StaleWorkspaceMinimumAge - TimeSpan.FromHours(1),
			processId: int.MaxValue,
			kind: "not-owned-by-maui-cli");

		var recovered = ProfileBuildWorkspace.RecoverStaleWorkspaces(
			projectDirectory,
			new JsonOutputFormatter(TextWriter.Null),
			useJson: true,
			verbose: false,
			now: DateTimeOffset.UtcNow);

		Assert.Equal(0, recovered);
		Assert.True(Directory.Exists(recentWorkspace));
		Assert.True(Directory.Exists(unownedWorkspace));
	}

	[Fact]
	public async Task ProfileBuildWorkspace_SameNamedReferencedProjects_UseUniqueArtifactDirectories()
	{
		using var tempRoot = TempProjectFile("<Project />");
		var rootDirectory = Path.GetDirectoryName(tempRoot.Path)!;
		var appDirectory = Path.Combine(rootDirectory, "App");
		var leftDirectory = Path.Combine(rootDirectory, "Left");
		var rightDirectory = Path.Combine(rootDirectory, "Right");
		Directory.CreateDirectory(appDirectory);
		Directory.CreateDirectory(leftDirectory);
		Directory.CreateDirectory(rightDirectory);

		var appProjectPath = Path.Combine(appDirectory, "App.csproj");
		File.WriteAllText(appProjectPath, """
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup>
			    <TargetFramework>net10.0</TargetFramework>
			  </PropertyGroup>
			  <ItemGroup>
			    <ProjectReference Include="../Left/Shared.csproj" />
			    <ProjectReference Include="../Right/Shared.csproj" />
			  </ItemGroup>
			</Project>
			""");
		File.WriteAllText(Path.Combine(leftDirectory, "Shared.csproj"), CreateClassLibraryProject("LeftLibrary"));
		File.WriteAllText(Path.Combine(rightDirectory, "Shared.csproj"), CreateClassLibraryProject("RightLibrary"));
		File.WriteAllText(Path.Combine(leftDirectory, "Left.cs"), "public sealed class LeftType { }");
		File.WriteAllText(Path.Combine(rightDirectory, "Right.cs"), "public sealed class RightType { }");

		var formatter = new JsonOutputFormatter(TextWriter.Null);
		var workspace = ProfileBuildWorkspace.Create(appDirectory, formatter, useJson: true, verbose: false);
		workspace.ConfigureBuildTargets(profilingInjectionTargetsPath: null);
		try
		{
			var result = await RunProfileBuildAsync(appProjectPath, workspace);

			Assert.True(result.ExitCode == 0, result.Output);
			var sharedIntermediateDirectories = Directory.GetDirectories(
				Path.Combine(workspace.Path, "obj"),
				"Shared-*",
				SearchOption.TopDirectoryOnly);
			var sharedOutputDirectories = Directory.GetDirectories(
				Path.Combine(workspace.Path, "bin"),
				"Shared-*",
				SearchOption.TopDirectoryOnly);
			Assert.Equal(2, sharedIntermediateDirectories.Length);
			Assert.Equal(2, sharedOutputDirectories.Length);
			Assert.Equal(2, sharedIntermediateDirectories.Select(Path.GetFileName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
			Assert.Contains(Directory.EnumerateFiles(workspace.Path, "LeftLibrary.dll", SearchOption.AllDirectories), File.Exists);
			Assert.Contains(Directory.EnumerateFiles(workspace.Path, "RightLibrary.dll", SearchOption.AllDirectories), File.Exists);
		}
		finally
		{
			workspace.Cleanup(formatter, useJson: true, verbose: false);
		}
	}

	[Fact]
	public async Task ProfileBuildWorkspace_PreservesCustomBeforeDirectoryBuildProps()
	{
		using var tempProject = TempProjectFile("""
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup>
			    <TargetFramework>net10.0</TargetFramework>
			  </PropertyGroup>
			  <Target Name="ValidateBuildPropsImports" BeforeTargets="BeforeBuild">
			    <Error Condition="'$(EarlyBuildPropsImported)' != 'true'" Text="CustomBeforeDirectoryBuildProps was not imported." />
			    <Error Condition="'$(NormalDirectoryBuildPropsImported)' != 'true'" Text="Directory.Build.props was not imported." />
			  </Target>
			</Project>
			""");
		var projectDirectory = Path.GetDirectoryName(tempProject.Path)!;
		var earlyPropsPath = Path.Combine(projectDirectory, "early.props");
		File.WriteAllText(
			earlyPropsPath,
			"<Project><PropertyGroup><EarlyBuildPropsImported>true</EarlyBuildPropsImported></PropertyGroup></Project>");
		File.WriteAllText(
			Path.Combine(projectDirectory, "Directory.Build.props"),
			"<Project><PropertyGroup><NormalDirectoryBuildPropsImported>true</NormalDirectoryBuildPropsImported></PropertyGroup></Project>");

		var formatter = new JsonOutputFormatter(TextWriter.Null);
		var workspace = ProfileBuildWorkspace.Create(projectDirectory, formatter, useJson: true, verbose: false);
		workspace.ConfigureBuildTargets(profilingInjectionTargetsPath: null);
		try
		{
			var result = await RunProfileBuildAsync(
				tempProject.Path,
				workspace,
				$"-p:CustomBeforeDirectoryBuildProps={earlyPropsPath}");

			Assert.True(result.ExitCode == 0, result.Output);
		}
		finally
		{
			workspace.Cleanup(formatter, useJson: true, verbose: false);
		}
	}

	[Fact]
	public async Task ProfileBuildWorkspace_OverridesExternalRestorePathBeforeRestoreWrites()
	{
		using var tempProject = TempProjectFile("""
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup>
			    <TargetFramework>net10.0</TargetFramework>
			  </PropertyGroup>
			</Project>
			""");
		var projectDirectory = Path.GetDirectoryName(tempProject.Path)!;
		var externalRestorePath = Path.Combine(projectDirectory, "external-restore");
		File.WriteAllText(
			Path.Combine(projectDirectory, "Directory.Build.props"),
			$"""
			<Project>
			  <PropertyGroup>
			    <MSBuildProjectExtensionsPath>{System.Security.SecurityElement.Escape(externalRestorePath + Path.DirectorySeparatorChar)}</MSBuildProjectExtensionsPath>
			  </PropertyGroup>
			</Project>
			""");

		var formatter = new JsonOutputFormatter(TextWriter.Null);
		var workspace = ProfileBuildWorkspace.Create(projectDirectory, formatter, useJson: true, verbose: false);
		workspace.ConfigureBuildTargets(profilingInjectionTargetsPath: null);
		try
		{
			var result = await RunProfileBuildAsync(tempProject.Path, workspace);

			Assert.True(result.ExitCode == 0, result.Output);
			Assert.False(Directory.Exists(externalRestorePath));
			Assert.Contains(
				Directory.EnumerateFiles(workspace.Path, "project.assets.json", SearchOption.AllDirectories),
				File.Exists);
		}
		finally
		{
			workspace.Cleanup(formatter, useJson: true, verbose: false);
		}
	}

	[Fact]
	public async Task ProfileBuildWorkspace_ProjectBodyRestoreOutputPath_FailsBeforeRestoreWrites()
	{
		using var tempProject = TempProjectFile("<Project Sdk=\"Microsoft.NET.Sdk\" />");
		var projectDirectory = Path.GetDirectoryName(tempProject.Path)!;
		var externalRestorePath = Path.Combine(projectDirectory, "external-restore");
		File.WriteAllText(
			tempProject.Path,
			$"""
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup>
			    <TargetFramework>net10.0</TargetFramework>
			    <RestoreOutputPath>{System.Security.SecurityElement.Escape(externalRestorePath + Path.DirectorySeparatorChar)}</RestoreOutputPath>
			  </PropertyGroup>
			</Project>
			""");

		var formatter = new JsonOutputFormatter(TextWriter.Null);
		var workspace = ProfileBuildWorkspace.Create(projectDirectory, formatter, useJson: true, verbose: false);
		workspace.ConfigureBuildTargets(profilingInjectionTargetsPath: null);
		try
		{
			var result = await RunProfileBuildAsync(tempProject.Path, workspace);

			Assert.NotEqual(0, result.ExitCode);
			Assert.Contains("could not isolate RestoreOutputPath", result.Output, StringComparison.OrdinalIgnoreCase);
			Assert.False(Directory.Exists(externalRestorePath));
		}
		finally
		{
			workspace.Cleanup(formatter, useJson: true, verbose: false);
		}
	}

	[Fact]
	public async Task ProfileBuildWorkspace_ProjectBodySdkHook_CannotRemoveValidationOrInjection()
	{
		using var tempProject = TempProjectFile("""
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup>
			    <TargetFramework>net10.0</TargetFramework>
			    <ImportDirectoryBuildTargets>false</ImportDirectoryBuildTargets>
			    <BeforeMicrosoftNETSdkTargets>$(BeforeMicrosoftNETSdkTargets);$(MSBuildProjectDirectory)/custom-before.targets</BeforeMicrosoftNETSdkTargets>
			  </PropertyGroup>
			</Project>
			""");
		var projectDirectory = Path.GetDirectoryName(tempProject.Path)!;
		var customHookMarker = Path.Combine(projectDirectory, "custom-hook.marker");
		var injectionMarker = Path.Combine(projectDirectory, "injection.marker");
		File.WriteAllText(
			Path.Combine(projectDirectory, "custom-before.targets"),
			$"""
			<Project>
			  <Target Name="MarkCustomBeforeHook" BeforeTargets="BeforeBuild">
			    <WriteLinesToFile File="{System.Security.SecurityElement.Escape(customHookMarker)}" Lines="custom" Overwrite="true" />
			  </Target>
			</Project>
			""");
		var injectionTargetsPath = Path.Combine(projectDirectory, "profiling-injection.targets");
		File.WriteAllText(
			injectionTargetsPath,
			$"""
			<Project>
			  <Target Name="MarkProfilingInjection" BeforeTargets="BeforeBuild">
			    <WriteLinesToFile File="{System.Security.SecurityElement.Escape(injectionMarker)}" Lines="injected" Overwrite="true" />
			  </Target>
			</Project>
			""");

		var formatter = new JsonOutputFormatter(TextWriter.Null);
		var workspace = ProfileBuildWorkspace.Create(projectDirectory, formatter, useJson: true, verbose: false);
		workspace.ConfigureBuildTargets(injectionTargetsPath);
		try
		{
			var result = await RunProfileBuildAsync(tempProject.Path, workspace);

			Assert.True(result.ExitCode == 0, result.Output);
			Assert.True(File.Exists(customHookMarker));
			Assert.True(File.Exists(injectionMarker));
		}
		finally
		{
			workspace.Cleanup(formatter, useJson: true, verbose: false);
		}
	}

	[Fact]
	public async Task ProcessRunner_CallerCancellation_StopsWorkspaceWriterBeforeReturning()
	{
		using var tempProject = TempProjectFile("<Project />");
		var projectDirectory = Path.GetDirectoryName(tempProject.Path)!;
		var formatter = new JsonOutputFormatter(TextWriter.Null);
		var workspace = ProfileBuildWorkspace.Create(projectDirectory, formatter, useJson: true, verbose: false);
		var childOutputPath = Path.Combine(workspace.Path, "child-output");
		var childReadyPath = Path.Combine(projectDirectory, "child-ready.marker");
		var helperSource = Path.Combine(AppContext.BaseDirectory, "ProfileTestProcess.cs");
		using var cts = new CancellationTokenSource();

		var runTask = ProcessRunner.RunAsync(
			GetDotnetHostPath(),
			["run", "--file", helperSource, "--no-launch-profile", "--", "wrap-write-until-killed", helperSource, childOutputPath, childReadyPath],
			projectDirectory,
			timeout: TimeSpan.FromMinutes(1),
			cancellationToken: cts.Token);
		await WaitForFileAsync(childReadyPath, TimeSpan.FromSeconds(30));
		cts.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runTask);

		workspace.Cleanup(formatter, useJson: true, verbose: false);
		await Task.Delay(250);
		Assert.False(Directory.Exists(workspace.Path));
	}

	[Theory]
	[InlineData("OutDir")]
	[InlineData("TargetDir")]
	public async Task ProfileBuildWorkspace_ExternalFinalOutputPath_FailsBeforeProducingOutputs(string propertyName)
	{
		using var tempProject = TempProjectFile("""
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup>
			    <TargetFramework>net10.0</TargetFramework>
			  </PropertyGroup>
			</Project>
			""");
		var projectDirectory = Path.GetDirectoryName(tempProject.Path)!;
		var externalOutputPath = Path.Combine(projectDirectory, "external-output");
		var formatter = new JsonOutputFormatter(TextWriter.Null);
		var workspace = ProfileBuildWorkspace.Create(projectDirectory, formatter, useJson: true, verbose: false);
		workspace.ConfigureBuildTargets(profilingInjectionTargetsPath: null);
		try
		{
			var result = await RunProfileBuildAsync(
				tempProject.Path,
				workspace,
				$"-p:{propertyName}={externalOutputPath}{Path.DirectorySeparatorChar}");

			Assert.NotEqual(0, result.ExitCode);
			Assert.Contains($"could not isolate {propertyName}", result.Output, StringComparison.OrdinalIgnoreCase);
			Assert.False(Directory.Exists(externalOutputPath)
				&& Directory.EnumerateFiles(externalOutputPath, "*.dll", SearchOption.AllDirectories).Any());
		}
		finally
		{
			workspace.Cleanup(formatter, useJson: true, verbose: false);
		}
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
		var requestedPath = TestPath("my-trace.nettrace");
		var path = ProfileCommand.ResolveOutputPath("MyApp", requestedPath, TraceOutputFormat.NetTrace);
		Assert.Equal(Path.GetFullPath(requestedPath), path);
	}

	[Fact]
	public void ResolveOutputPath_AddsNettraceExtensionWhenMissing()
	{
		var path = ProfileCommand.ResolveOutputPath("MyApp", TestPath("my-trace"), TraceOutputFormat.NetTrace);
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
		var requestedPath = TestPath("my-trace.speedscope.json");
		var path = ProfileCommand.ResolveOutputPath("MyApp", requestedPath, TraceOutputFormat.Speedscope);
		Assert.Equal(Path.GetFullPath(TestPath("my-trace.nettrace")), path);
	}

	[Fact]
	public void GetPrimaryOutputPath_SpeedscopeUsesSidecarJsonFile()
	{
		var sourcePath = TestPath("my-trace.nettrace");
		var path = ProfileCommand.GetPrimaryOutputPath(sourcePath, TraceOutputFormat.Speedscope);
		Assert.Equal(TestPath("my-trace.speedscope.json"), path);
	}

	[Fact]
	public void ResolveOutputPath_MibcStripsRequestedMibcSuffix()
	{
		var requestedPath = TestPath("my-trace.mibc");
		var path = ProfileCommand.ResolveOutputPath("MyApp", requestedPath, TraceOutputFormat.Mibc);
		Assert.Equal(Path.GetFullPath(TestPath("my-trace.nettrace")), path);
	}

	[Fact]
	public void GetPrimaryOutputPath_MibcUsesSiblingMibcFile()
	{
		var sourcePath = TestPath("my-trace.nettrace");
		var path = ProfileCommand.GetPrimaryOutputPath(sourcePath, TraceOutputFormat.Mibc);
		Assert.Equal(TestPath("my-trace.mibc"), path);
	}

	[Fact]
	public void GetDotnetPgoInstallPath_UsesMauiHomeLocation()
	{
		var userProfile = TestPath("users", "tester");
		var path = DotnetPgoInstaller.GetInstallPath(userProfile);
		Assert.Equal(Path.Combine(userProfile, ".maui", "dotnet-pgo"), path);
	}

	[Fact]
	public void DotnetPgoInstaller_BuildPublishArguments_UsesSingleFileSelfContainedPublish()
	{
		var outputDirectory = TestPath("dotnet-pgo-build");
		var runtimeIdentifier = DotnetPgoInstaller.GetCurrentRuntimeIdentifier();
		var args = DotnetPgoInstaller.BuildPublishArguments(runtimeIdentifier, outputDirectory);

		Assert.Contains("publish", args);
		Assert.Contains("src/coreclr/tools/dotnet-pgo/dotnet-pgo.csproj", args);
		Assert.Contains(runtimeIdentifier, args);
		Assert.Contains("--self-contained", args);
		Assert.Contains("-p:UseAppHost=true", args);
		Assert.Contains("-p:PublishSingleFile=true", args);
		Assert.Contains("-p:PublishTrimmed=false", args);
		Assert.Contains(outputDirectory, args);
	}

	[Fact]
	public void ParseLatestStableDotnetRuntimeReleaseBranch_PicksHighestRelease()
	{
		var lsRemoteOutput = """
			abc123	refs/heads/release/9.0
			def456	refs/heads/release/10.0
			ghi789	refs/heads/release/8.0
			jkl012	refs/heads/main
			""";

		var branch = DotnetPgoInstaller.ParseLatestStableReleaseBranch(lsRemoteOutput);

		Assert.Equal("release/10.0", branch);
	}

	[Fact]
	public void ParseLatestStableDotnetRuntimeReleaseBranch_ReturnsNullWithoutStableRelease()
	{
		var lsRemoteOutput = """
			abc123	refs/heads/main
			def456	refs/heads/feature/test
			""";

		var branch = DotnetPgoInstaller.ParseLatestStableReleaseBranch(lsRemoteOutput);

		Assert.Null(branch);
	}

	[Fact]
	public void GetCurrentRuntimeIdentifier_ReturnsSupportedRidFormat()
	{
		var rid = DotnetPgoInstaller.GetCurrentRuntimeIdentifier();
		Assert.Matches("^(osx|linux|win)-(x64|arm64)$", rid);
	}

	[Fact]
	public void AppendStatusTailLine_KeepsOnlyTheMostRecentLines()
	{
		var lines = new Queue<string>();

		for (var i = 1; i <= 7; i++)
			DotnetPgoInstaller.AppendStatusTailLine(lines, $"line {i}");

		Assert.Equal(["line 3", "line 4", "line 5", "line 6", "line 7"], lines.ToArray());
	}

	[Fact]
	public void FormatStatusMessage_IncludesEscapedRecentOutput()
	{
		var message = DotnetPgoInstaller.FormatStatusMessage(
			"Publishing dotnet-pgo...",
			["Restored [package]", "Build succeeded"]);

		Assert.Contains("Publishing dotnet-pgo...", message);
		Assert.Contains("Restored", message);
		Assert.Contains("[grey]", message);
		Assert.Contains("Build succeeded", message);
	}

	[Fact]
	public void ResolvePostProcessingCancellationToken_PreservesCancellationForNormalCompletion()
	{
		using var cts = new CancellationTokenSource();
		cts.Cancel();

		var token = ProfileCommand.ResolvePostProcessingCancellationToken(stopRequestedByUser: false, cts.Token);

		Assert.True(token.IsCancellationRequested);
	}

	[Fact]
	public void ResolvePostProcessingCancellationToken_IgnoresCtrlCCancellationAfterManualStop()
	{
		using var cts = new CancellationTokenSource();
		cts.Cancel();

		var token = ProfileCommand.ResolvePostProcessingCancellationToken(stopRequestedByUser: true, cts.Token);

		Assert.False(token.IsCancellationRequested);
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
			ProjectPath = TestPath("fake", "MyApp.csproj"),
			ProjectDirectory = TestPath("fake"),
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

	static string CreateWorkspaceOwnershipFile(
		string projectDirectory,
		string sessionId,
		DateTimeOffset createdAt,
		int processId,
		string kind = ProfileBuildWorkspace.OwnershipKind)
	{
		var workspacePath = Path.Combine(projectDirectory, "obj", ProfileBuildWorkspace.RootDirectoryName, sessionId);
		Directory.CreateDirectory(workspacePath);
		var ownership = new ProfileBuildWorkspaceOwnership
		{
			Kind = kind,
			Version = ProfileBuildWorkspace.OwnershipVersion,
			SessionId = sessionId,
			WorkspacePath = Path.GetFullPath(workspacePath),
			ProjectDirectory = Path.GetFullPath(projectDirectory),
			ProcessId = processId,
			ProcessStartTimeUtcTicks = 1,
			CreatedAtUtc = createdAt
		};
		File.WriteAllText(
			Path.Combine(workspacePath, ProfileBuildWorkspace.OwnershipFileName),
			JsonSerializer.Serialize(ownership));
		return workspacePath;
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
			outputPath: TestPath("out.nettrace"),
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
			outputPath: TestPath("out.nettrace"),
			outputFormat: TraceOutputFormat.NetTrace,
			transport: transport,
			traceProfile: null,
			duration: null,
			stoppingEventProvider: "Microsoft.Maui.ProfilingHelper",
			stoppingEventName: "StartupComplete",
			stoppingEventPayloadFilter: null).ToArray();

		// Default profiles injected
		var profileIdx = Array.IndexOf(args, "--profile");
		Assert.True(profileIdx >= 0, "--profile flag should be present");
		Assert.Equal("dotnet-common,dotnet-sampled-thread-time", args[profileIdx + 1]);

		// Stopping event provider enabled via --providers
		var providersIdx = Array.IndexOf(args, "--providers");
		Assert.True(providersIdx >= 0, "--providers flag should be present");
		Assert.Contains("Microsoft.Maui.ProfilingHelper", args[providersIdx + 1]);
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
			stoppingEventProvider: "Microsoft.Maui.ProfilingHelper",
			stoppingEventName: "StartupComplete",
			stoppingEventPayloadFilter: null).ToArray();

		var profileIdx = Array.IndexOf(args, "--profile");
		Assert.True(profileIdx >= 0);
		Assert.Equal("gc-verbose", args[profileIdx + 1]);

		// Stopping event provider still injected
		var providersIdx = Array.IndexOf(args, "--providers");
		Assert.True(providersIdx >= 0);
		Assert.Contains("Microsoft.Maui.ProfilingHelper", args[providersIdx + 1]);
		Assert.Contains("Microsoft-Windows-DotNETRuntime", args[providersIdx + 1]);
		Assert.Contains("0x1F000080018:5", args[providersIdx + 1]);
	}

	[Fact]
	public void BuildTraceArguments_Speedscope_UsesSpeedscopeFormat()
	{
		var device = CreateDevice(Platforms.Android, isEmulator: true);
		var transport = ProfileCommand.ResolveProfileTransport(Platforms.Android, device);
		var args = ProfileCommand.BuildTraceArguments(
			outputPath: TestPath("out.nettrace"),
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
	public void BuildTraceArguments_Mibc_UsesNetTraceCollectorFormat()
	{
		var device = CreateDevice(Platforms.Android, isEmulator: true);
		var transport = ProfileCommand.ResolveProfileTransport(Platforms.Android, device);
		var args = ProfileCommand.BuildTraceArguments(
			outputPath: TestPath("out.nettrace"),
			outputFormat: TraceOutputFormat.Mibc,
			transport: transport,
			traceProfile: null,
			duration: null,
			stoppingEventProvider: null,
			stoppingEventName: null,
			stoppingEventPayloadFilter: null).ToArray();

		var formatIdx = Array.IndexOf(args, "--format");
		Assert.True(formatIdx >= 0);
		Assert.Equal("NetTrace", args[formatIdx + 1]);

		var profileIdx = Array.IndexOf(args, "--profile");
		Assert.True(profileIdx >= 0);
		Assert.Equal("dotnet-common,dotnet-sampled-thread-time", args[profileIdx + 1]);

		var providersIdx = Array.IndexOf(args, "--providers");
		Assert.True(providersIdx >= 0);
		Assert.Contains("Microsoft-Windows-DotNETRuntime:0x1F000080018:5", args[providersIdx + 1]);
	}

	[Fact]
	public void BuildTraceArguments_MibcWithUserProfile_KeepsProfileAndAddsRuntimeProvider()
	{
		var device = CreateDevice(Platforms.Android, isEmulator: true);
		var transport = ProfileCommand.ResolveProfileTransport(Platforms.Android, device);
		var args = ProfileCommand.BuildTraceArguments(
			outputPath: TestPath("out.nettrace"),
			outputFormat: TraceOutputFormat.Mibc,
			transport: transport,
			traceProfile: "gc-verbose",
			duration: null,
			stoppingEventProvider: null,
			stoppingEventName: null,
			stoppingEventPayloadFilter: null).ToArray();

		var profileIdx = Array.IndexOf(args, "--profile");
		Assert.True(profileIdx >= 0);
		Assert.Equal("gc-verbose", args[profileIdx + 1]);

		var providersIdx = Array.IndexOf(args, "--providers");
		Assert.True(providersIdx >= 0);
		Assert.Contains("Microsoft-Windows-DotNETRuntime:0x1F000080018:5", args[providersIdx + 1]);
	}

	[Fact]
	public void MauiProfilingHelperInjectionTargets_IncludeDynamicPgoEnvironmentVariables()
	{
		var targetsPath = Path.GetFullPath(Path.Combine(
			AppContext.BaseDirectory,
			"../../../../../src/Cli/Microsoft.Maui.Cli/Build/MauiProfilingHelperInjection.targets"));

		var contents = File.ReadAllText(targetsPath);

		Assert.Contains("MauiProfilingHelperEnableRuntimePgo", contents);
		Assert.Contains("DOTNET_TieredPGO=1", contents);
		Assert.Contains("DOTNET_ReadyToRun=0", contents);
		Assert.Contains("DOTNET_JitMinimalJitProfiling=1", contents);
	}

	[Fact]
	public async Task MauiProfilingHelperInjectionTargets_GeneratesEnvironmentOnlyForSelectedProject()
	{
		var targetsPath = Path.GetFullPath(Path.Combine(
			AppContext.BaseDirectory,
			"../../../../../src/Cli/Microsoft.Maui.Cli/Build/MauiProfilingHelperInjection.targets"));
		var tempDirectory = Path.Combine(Path.GetTempPath(), "maui-profile-injection-tests", Guid.NewGuid().ToString("N"));
		var appProjectPath = Path.Combine(tempDirectory, "App.proj");
		var libraryProjectPath = Path.Combine(tempDirectory, "Library.proj");
		var appIntermediatePath = Path.Combine(tempDirectory, "app-obj");
		var libraryIntermediatePath = Path.Combine(tempDirectory, "library-obj");

		Directory.CreateDirectory(tempDirectory);
		try
		{
			File.WriteAllText(appProjectPath, CreateProfilingInjectionTestProject(targetsPath, appIntermediatePath));
			File.WriteAllText(libraryProjectPath, CreateProfilingInjectionTestProject(targetsPath, libraryIntermediatePath));

			var appResult = await RunProfilingInjectionTargetAsync(appProjectPath, appProjectPath);
			var libraryResult = await RunProfilingInjectionTargetAsync(libraryProjectPath, appProjectPath);

			Assert.True(appResult.ExitCode == 0, appResult.Output);
			Assert.True(libraryResult.ExitCode == 0, libraryResult.Output);
			Assert.True(File.Exists(Path.Combine(appIntermediatePath, "MauiProfilingHelper.env")));
			Assert.False(File.Exists(Path.Combine(libraryIntermediatePath, "MauiProfilingHelper.env")));
		}
		finally
		{
			Directory.Delete(tempDirectory, recursive: true);
		}
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
		Assert.True(transport.RequiresExplicitDsrouter);
	}

	[Fact]
	public void PhysicalAndroidPorts_ReserveSeparateRouterAndExitControlPorts()
	{
		var transport = ProfileCommand.ResolveProfileTransport(
			Platforms.Android,
			CreateDevice(Platforms.Android, isEmulator: false));

		Assert.Equal(9001, ProfileCommandPortRouter.GetDsrouterTcpPort(9000));
		Assert.Equal(9002, ProfileCommandPortRouter.GetExitControlPort(9000, transport));
	}

	[Fact]
	public void ParseAdbReverseMappings_ParsesTcpMappingsAndIgnoresMalformedLines()
	{
		var mappings = ProfileCommandPortRouter.ParseAdbReverseMappings(
			"""
			device-123 tcp:9000 tcp:9001
			UsbFfs tcp:9002 tcp:9002
			device-123 localabstract:not-tcp tcp:9003
			malformed
			""");

		Assert.Equal(
			[
				new ProfileCommandPortRouter.AdbReverseMapping(9000, 9001),
				new ProfileCommandPortRouter.AdbReverseMapping(9002, 9002)
			],
			mappings);
	}

	[Fact]
	public void AdbReverseMappingOwnership_RequiresOneExactMapping()
	{
		ProfileCommandPortRouter.AdbReverseMapping[] ownedMapping = [new(9000, 9001)];
		ProfileCommandPortRouter.AdbReverseMapping[] replacedMapping = [new(9000, 9101)];
		ProfileCommandPortRouter.AdbReverseMapping[] duplicateMappings = [new(9000, 9001), new(9000, 9101)];

		Assert.True(ProfileCommandPortRouter.HasAdbReverseMapping(ownedMapping, 9000));
		Assert.True(ProfileCommandPortRouter.IsOwnedAdbReverseMapping(ownedMapping, 9000, 9001));
		Assert.False(ProfileCommandPortRouter.IsOwnedAdbReverseMapping(replacedMapping, 9000, 9001));
		Assert.False(ProfileCommandPortRouter.IsOwnedAdbReverseMapping(duplicateMappings, 9000, 9001));
		Assert.False(ProfileCommandPortRouter.HasAdbReverseMapping(ownedMapping, 9002));
	}

	[Fact]
	public void BuildAdbReverseArguments_RefusesToReplaceAnExistingMapping()
	{
		Assert.Equal(
			["-s", "device-123", "reverse", "--no-rebind", "tcp:9000", "tcp:9001"],
			ProfileCommandPortRouter.BuildAdbReverseArguments("device-123", 9000, 9001));
	}

	[Fact]
	public async Task ReserveProfilePorts_ExplicitDsrouterSkipsCollidingPortSet()
	{
		using var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		var busyPort = ((IPEndPoint)listener.LocalEndpoint).Port;
		var startingPort = busyPort - 1;
		var transport = new ProfileTransportConfiguration(
			Platforms.Android,
			"127.0.0.1",
			"connect",
			"android",
			RequiresManualExitControlPortRouting: false,
			RequiresExplicitDsrouter: true);

		using var ports = await ProfileCommandPortRouter.ReserveProfilePortsAndConfigureRoutingAsync(
			CreateDevice(Platforms.Android, isEmulator: false),
			transport,
			startingPort,
			new JsonOutputFormatter(TextWriter.Null),
			useJson: false,
			verbose: false,
			CancellationToken.None);

		Assert.True(ports.DiagnosticPort > busyPort);
		Assert.Equal(ports.DiagnosticPort + 1, ports.DsrouterTcpPort);
		Assert.Equal(ports.DiagnosticPort + 2, ports.ExitControlPort);
		Assert.NotNull(ports.DsrouterTcpReservation);
	}

	[Fact]
	public void EmulatorExitControlPort_RemainsAdjacentToDiagnosticPort()
	{
		var transport = ProfileCommand.ResolveProfileTransport(
			Platforms.Android,
			CreateDevice(Platforms.Android, isEmulator: true));

		Assert.Equal(9001, ProfileCommandPortRouter.GetExitControlPort(9000, transport));
	}

	[Fact]
	public void BuildDsrouterArguments_UsesSelectedRouterPortAndUniqueIpcEndpoint()
	{
		var args = ProfileDsrouterRunner.BuildArguments("maui-profile-test", 9101);

		Assert.Equal(
			[
				"server-server",
				"--ipc-server", "maui-profile-test",
				"--tcp-server", "127.0.0.1:9101",
				"--forward-port", "Android"
			],
			args);
	}

	[Fact]
	public void CreateDsrouterIpcEndpoint_UnixPathFitsSocketLimit()
	{
		if (OperatingSystem.IsWindows())
			return;

		var endpoint = ProfileDsrouterRunner.CreateIpcEndpoint();

		Assert.StartsWith("/tmp/", endpoint, StringComparison.Ordinal);
		Assert.True(endpoint.Length < 100);
	}

	[Fact]
	public void BuildTraceArguments_WithExplicitDsrouterIpc_DoesNotLaunchImplicitRouter()
	{
		var transport = ProfileCommand.ResolveProfileTransport(
			Platforms.Android,
			CreateDevice(Platforms.Android, isEmulator: false));

		var args = ProfileCommand.BuildTraceArguments(
			"trace.nettrace",
			TraceOutputFormat.NetTrace,
			transport,
			traceProfile: null,
			duration: null,
			stoppingEventProvider: null,
			stoppingEventName: null,
			stoppingEventPayloadFilter: null,
			diagnosticPortEndpoint: "maui-profile-test").ToArray();

		Assert.DoesNotContain("--dsrouter", args);
		var diagnosticPortIndex = Array.IndexOf(args, "--diagnostic-port");
		Assert.True(diagnosticPortIndex >= 0);
		Assert.Equal("maui-profile-test,connect", args[diagnosticPortIndex + 1]);
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
		var artifactsPath = TestPath("fake", "obj", ProfileBuildWorkspace.RootDirectoryName, "session");
		var directoryBuildPropsPath = TestPath("fake", ProfileBuildWorkspace.DirectoryBuildPropsFileName);
		var directoryBuildTargetsPath = TestPath("fake", ProfileBuildWorkspace.DirectoryBuildTargetsFileName);

		var args = ProfileCommand.BuildLaunchArguments(
			TestPath("fake", "MyApp.csproj"),
			artifactsPath,
			directoryBuildPropsPath,
			directoryBuildTargetsPath,
			"net10.0-ios",
			"Release",
			device,
			transport,
			9000,
			buildInjection: null);

		Assert.Contains("-p:Device=ios-sim-udid", args);
		Assert.Contains("-p:_MlaunchWaitForExit=false", args);
		Assert.Contains($"-p:ArtifactsPath={artifactsPath}", args);
		Assert.Contains($"-p:DirectoryBuildPropsPath={directoryBuildPropsPath}", args);
		Assert.Contains($"-p:DirectoryBuildTargetsPath={directoryBuildTargetsPath}", args);
		Assert.Contains("-p:ImportDirectoryBuildTargets=true", args);
	}

	[Fact]
	public void BuildCompileArguments_IosSimulator_EmbedsDiagnosticConfiguration()
	{
		var device = CreateDevice(Platforms.iOS, isEmulator: true) with { Id = "ios-sim-udid" };
		var transport = ProfileCommand.ResolveProfileTransport(Platforms.iOS, device);
		var artifactsPath = TestPath("fake", "obj", ProfileBuildWorkspace.RootDirectoryName, "session");
		var directoryBuildPropsPath = TestPath("fake", ProfileBuildWorkspace.DirectoryBuildPropsFileName);
		var directoryBuildTargetsPath = TestPath("fake", ProfileBuildWorkspace.DirectoryBuildTargetsFileName);

		var args = ProfileCommand.BuildCompileArguments(
			TestPath("fake", "MyApp.csproj"),
			artifactsPath,
			directoryBuildPropsPath,
			directoryBuildTargetsPath,
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
		Assert.Contains($"-p:ArtifactsPath={artifactsPath}", args);
		Assert.Contains($"-p:DirectoryBuildPropsPath={directoryBuildPropsPath}", args);
		Assert.Contains($"-p:DirectoryBuildTargetsPath={directoryBuildTargetsPath}", args);
		Assert.Contains("-p:ImportDirectoryBuildTargets=true", args);
	}

	[Fact]
	public void BuildCompileArguments_WithRuntimeOwnedEventPipe_SkipsDiagnosticArgsAndAddsRuntimePgoProperties()
	{
		var device = CreateDevice(Platforms.Android, isEmulator: true);
		var transport = ProfileCommand.ResolveProfileTransport(Platforms.Android, device);
		var projectPath = TestPath("fake", "MyApp.csproj");
		var buildInjection = new ProfilingBuildInjection(
			TargetsPath: TestPath("fake", "MauiProfilingHelperInjection.targets"),
			AssemblyPath: TestPath("fake", "Microsoft.Maui.ProfilingHelper.dll"),
			ExitControlHost: "10.0.2.2",
			ExitControlPort: 9001,
			InjectBootstrap: true,
			EnableRuntimePgo: true,
			EventPipeOutputPath: "/storage/emulated/0/Android/data/com.example/files/startup.nettrace");

		var args = ProfileCommand.BuildCompileArguments(
			projectPath,
			TestPath("fake", "obj", ProfileBuildWorkspace.RootDirectoryName, "session"),
			TestPath("fake", ProfileBuildWorkspace.DirectoryBuildPropsFileName),
			TestPath("fake", ProfileBuildWorkspace.DirectoryBuildTargetsFileName),
			"net10.0-android",
			"Release",
			transport,
			9000,
			buildInjection);

		Assert.DoesNotContain("-p:DiagnosticSuspend=true", args);
		Assert.Contains("-p:EnableDiagnostics=true", args);
		Assert.Contains("-p:MauiProfilingHelperExitHost=10.0.2.2", args);
		Assert.Contains("-p:MauiProfilingHelperExitPort=9001", args);
		Assert.Contains($"-p:MauiProfilingHelperProjectFullPath={Path.GetFullPath(projectPath)}", args);
		Assert.Contains("-p:MauiProfilingHelperEnableRuntimePgo=true", args);
		Assert.Contains("-p:MauiProfilingHelperEventPipeOutputPath=/storage/emulated/0/Android/data/com.example/files/startup.nettrace", args);
	}

	[Fact]
	public void BuildLaunchArguments_WithRuntimeOwnedEventPipe_UsesCrossPlatformDeviceProperty()
	{
		var device = CreateDevice(Platforms.Android, isEmulator: true);
		var transport = ProfileCommand.ResolveProfileTransport(Platforms.Android, device);
		var buildInjection = new ProfilingBuildInjection(
			TargetsPath: TestPath("fake", "MauiProfilingHelperInjection.targets"),
			AssemblyPath: TestPath("fake", "Microsoft.Maui.ProfilingHelper.dll"),
			ExitControlHost: "10.0.2.2",
			ExitControlPort: 9001,
			InjectBootstrap: true,
			EnableRuntimePgo: true,
			EventPipeOutputPath: "/storage/emulated/0/Android/data/com.example/files/startup.nettrace");

		var args = ProfileCommand.BuildLaunchArguments(
			TestPath("fake", "MyApp.csproj"),
			TestPath("fake", "obj", ProfileBuildWorkspace.RootDirectoryName, "session"),
			TestPath("fake", ProfileBuildWorkspace.DirectoryBuildPropsFileName),
			TestPath("fake", ProfileBuildWorkspace.DirectoryBuildTargetsFileName),
			"net10.0-android",
			"Release",
			device,
			transport,
			9000,
			buildInjection);

		Assert.DoesNotContain("-p:AndroidEnableProfiler=true", args);
		Assert.Contains("-p:EnableDiagnostics=true", args);
		Assert.Contains("-p:Device=android-emu", args);
		Assert.Contains("-p:MauiProfilingHelperExitHost=10.0.2.2", args);
		Assert.Contains("-p:MauiProfilingHelperExitPort=9001", args);
	}

	[Fact]
	public void BuildCompileArguments_ManualMode_EmitsDiagnosticSuspendFalse()
	{
		var device = CreateDevice(Platforms.Android, isEmulator: true);
		var transport = ProfileCommand.ResolveProfileTransport(Platforms.Android, device);

		var args = ProfileCommand.BuildCompileArguments(
			TestPath("fake", "MyApp.csproj"),
			TestPath("fake", "obj", ProfileBuildWorkspace.RootDirectoryName, "session"),
			TestPath("fake", ProfileBuildWorkspace.DirectoryBuildPropsFileName),
			TestPath("fake", ProfileBuildWorkspace.DirectoryBuildTargetsFileName),
			"net10.0-android",
			"Release",
			transport,
			9000,
			buildInjection: null,
			diagnosticSuspend: false);

		Assert.Contains("-p:DiagnosticSuspend=false", args);
		Assert.DoesNotContain("-p:DiagnosticSuspend=true", args);
		Assert.Contains("-p:EnableDiagnostics=true", args);
		Assert.Contains("-p:DiagnosticPort=9000", args);
	}

	[Fact]
	public void BuildLaunchArguments_ManualMode_EmitsDiagnosticSuspendFalse()
	{
		var device = CreateDevice(Platforms.iOS, isEmulator: true) with { Id = "ios-sim-udid" };
		var transport = ProfileCommand.ResolveProfileTransport(Platforms.iOS, device);

		var args = ProfileCommand.BuildLaunchArguments(
			TestPath("fake", "MyApp.csproj"),
			TestPath("fake", "obj", ProfileBuildWorkspace.RootDirectoryName, "session"),
			TestPath("fake", ProfileBuildWorkspace.DirectoryBuildPropsFileName),
			TestPath("fake", ProfileBuildWorkspace.DirectoryBuildTargetsFileName),
			"net10.0-ios",
			"Release",
			device,
			transport,
			9000,
			buildInjection: null,
			diagnosticSuspend: false);

		Assert.Contains("-p:DiagnosticSuspend=false", args);
		Assert.DoesNotContain("-p:DiagnosticSuspend=true", args);
	}

	[Fact]
	public void BuildCompileArguments_StartupMode_DefaultsToDiagnosticSuspendTrue()
	{
		// Regression: startup callers don't pass diagnosticSuspend, so the default must remain true
		// to preserve the suspended-startup handshake.
		var device = CreateDevice(Platforms.Android, isEmulator: true);
		var transport = ProfileCommand.ResolveProfileTransport(Platforms.Android, device);

		var args = ProfileCommand.BuildCompileArguments(
			TestPath("fake", "MyApp.csproj"),
			TestPath("fake", "obj", ProfileBuildWorkspace.RootDirectoryName, "session"),
			TestPath("fake", ProfileBuildWorkspace.DirectoryBuildPropsFileName),
			TestPath("fake", ProfileBuildWorkspace.DirectoryBuildTargetsFileName),
			"net10.0-android",
			"Release",
			transport,
			9000,
			buildInjection: null);

		Assert.Contains("-p:DiagnosticSuspend=true", args);
	}

	[Fact]
	public void ProfileCommand_ManualSubcommand_IsRegistered()
	{
		var profile = ProfileCommand.Create();
		Assert.Contains(profile.Subcommands, c => c.Name == "manual");
		Assert.Contains(profile.Subcommands, c => c.Name == "startup");
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
		Assert.True(ProfileCommand.CanResolveDiagnosticsTool(TestPath(".dotnet", "tools", "dotnet-trace"), null));
		Assert.True(ProfileCommand.CanResolveDiagnosticsTool(null, TestPath(".nuget", "packages", "dotnet-trace", "tools", "net8.0", "any", "dotnet-trace.dll")));
	}

	[Fact]
	public void CanUseDiagnosticsTooling_MixedInstalledAndCachedTools_ReturnsTrue()
	{
		var hasDotnetTrace = ProfileCommand.CanResolveDiagnosticsTool(TestPath(".dotnet", "tools", "dotnet-trace"), null);
		var hasDotnetDsrouter = ProfileCommand.CanResolveDiagnosticsTool(null, TestPath(".nuget", "packages", "dotnet-dsrouter", "tools", "net8.0", "any", "dotnet-dsrouter.dll"));

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
	public void ConfigureDnxStartInfo_UsesResolvedCommandPath()
	{
		var startInfo = new ProcessStartInfo();
		var dnxPath = TestPath("dotnet", "dnx");

		ProfileCommandDiagnostics.ConfigureDnxStartInfo(
			startInfo,
			dnxPath,
			"dotnet-trace",
			["collect", "--output", "trace.nettrace"],
			out var commandLine);

		Assert.Equal(dnxPath, startInfo.FileName);
		Assert.Equal(
			["-y", "dotnet-trace", "--", "collect", "--output", "trace.nettrace"],
			startInfo.ArgumentList);
		Assert.Contains(dnxPath, commandLine);
	}

	[Theory]
	[InlineData(".cmd")]
	[InlineData(".bat")]
	public void ConfigureDnxStartInfo_WindowsCommandWrapperUsesDotnetExecutable(string extension)
	{
		var startInfo = new ProcessStartInfo
		{
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};
		var dnxPath = TestPath("Program Files", "dotnet", "dnx" + extension);
		var outputPath = TestPath("trace output", "%TEMP% ^ & | < > ( ) \"quoted\".nettrace");

		ProfileCommandDiagnostics.ConfigureDnxStartInfo(
			startInfo,
			dnxPath,
			"dotnet-trace",
			["collect", "--output", outputPath],
			out var commandLine,
			isWindows: true);

		Assert.Equal(Path.Combine(Path.GetDirectoryName(dnxPath)!, "dotnet.exe"), startInfo.FileName);
		Assert.Equal(
			["dnx", "-y", "dotnet-trace", "--", "collect", "--output", outputPath],
			startInfo.ArgumentList);
		Assert.True(startInfo.RedirectStandardInput);
		Assert.True(startInfo.RedirectStandardOutput);
		Assert.True(startInfo.RedirectStandardError);
		Assert.Contains(startInfo.FileName, commandLine);
	}

	[Fact]
	public async Task ConfigureDnxStartInfo_WindowsCommandWrapperPreservesExclamationMarksWhenExecuted()
	{
		if (!OperatingSystem.IsWindows())
			return;

		var tempDirectory = Path.Combine(Path.GetTempPath(), $"maui-dnx!test-{Guid.NewGuid():N}");
		var helperProject = Path.Combine(tempDirectory, "DnxEcho.csproj");
		var outputDirectory = Path.Combine(tempDirectory, "sdk!path");
		Directory.CreateDirectory(tempDirectory);
		try
		{
			File.WriteAllText(helperProject, """
				<Project Sdk="Microsoft.NET.Sdk">
				  <PropertyGroup>
				    <OutputType>Exe</OutputType>
				    <TargetFramework>net10.0</TargetFramework>
				    <AssemblyName>dotnet</AssemblyName>
				    <UseAppHost>true</UseAppHost>
				  </PropertyGroup>
				</Project>
				""");
			File.WriteAllText(
				Path.Combine(tempDirectory, "Program.cs"),
				"""
				using System;

				Console.Write(System.Text.Json.JsonSerializer.Serialize(args));
				""");

			var dotnetPath = Path.GetFullPath(Path.Combine(
				System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(),
				"..",
				"..",
				"..",
				"dotnet.exe"));
			Assert.True(File.Exists(dotnetPath), $"Could not find the active .NET host at '{dotnetPath}'.");
			var buildStartInfo = new ProcessStartInfo(dotnetPath)
			{
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			};
			foreach (var arg in new[] { "build", helperProject, "--nologo", "--configuration", "Release", "--output", outputDirectory })
				buildStartInfo.ArgumentList.Add(arg);

			using (var buildProcess = Process.Start(buildStartInfo)!)
			{
				var buildOutputTask = buildProcess.StandardOutput.ReadToEndAsync();
				var buildErrorTask = buildProcess.StandardError.ReadToEndAsync();
				await buildProcess.WaitForExitAsync();
				var buildOutput = await buildOutputTask;
				var buildError = await buildErrorTask;
				Assert.True(buildProcess.ExitCode == 0, buildOutput + buildError);
			}

			var dnxPath = Path.Combine(outputDirectory, "dnx.cmd");
			var outputPath = Path.Combine(tempDirectory, "trace!output.nettrace");
			File.WriteAllText(dnxPath, "@exit /b 99");
			var startInfo = new ProcessStartInfo
			{
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				RedirectStandardInput = true
			};
			ProfileCommandDiagnostics.ConfigureDnxStartInfo(
				startInfo,
				dnxPath,
				"dotnet-trace",
				["collect", "--output", outputPath],
				out _,
				isWindows: true);

			using (var process = Process.Start(startInfo)!)
			{
				var standardOutput = await process.StandardOutput.ReadToEndAsync();
				var standardError = await process.StandardError.ReadToEndAsync();
				await process.WaitForExitAsync();

				Assert.True(process.ExitCode == 0, standardError);
				var actualArgs = System.Text.Json.JsonSerializer.Deserialize<string[]>(standardOutput);
				Assert.NotNull(actualArgs);
				Assert.Equal(
					["dnx", "-y", "dotnet-trace", "--", "collect", "--output", outputPath],
					actualArgs);
			}
		}
		finally
		{
			Directory.Delete(tempDirectory, recursive: true);
		}
	}

	[Fact]
	public void ProfilingInjectionAssets_AreSelfContainedAndConfigureIosLaunchEnvironment()
	{
		var buildDirectory = Path.GetFullPath(Path.Combine(
			AppContext.BaseDirectory,
			"../../../../../src/Cli/Microsoft.Maui.Cli/Build"));
		var source = File.ReadAllText(Path.Combine(buildDirectory, "MauiProfilingHelper.AutoInitialize.cs"));
		var targets = File.ReadAllText(Path.Combine(buildDirectory, "MauiProfilingHelperInjection.targets"));

		Assert.Contains("using System;", source);
		Assert.Contains("MlaunchEnvironmentVariables Include=\"MAUI_PROFILING_HELPER=1\"", targets);
		Assert.Contains("MlaunchEnvironmentVariables Include=\"MAUI_PROFILING_HELPER_EXIT_HOST=", targets);
		Assert.Contains("MlaunchEnvironmentVariables Include=\"MAUI_PROFILING_HELPER_EXIT_PORT=", targets);
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

	static ProfileTestProcess StartProfileTestProcess(string mode, string? releasePath = null)
	{
		var helperSource = Path.Combine(
			AppContext.BaseDirectory,
			"ProfileTestProcess.cs");
		if (!File.Exists(helperSource))
			throw new FileNotFoundException("The profile test process helper was not copied.", helperSource);

		var dotnetHost = Path.GetFullPath(Path.Combine(
			System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(),
			"..",
			"..",
			"..",
			OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
		var startInfo = new ProcessStartInfo(dotnetHost)
		{
			UseShellExecute = false,
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true
		};
		startInfo.ArgumentList.Add("run");
		startInfo.ArgumentList.Add("--file");
		startInfo.ArgumentList.Add(helperSource);
		startInfo.ArgumentList.Add("--no-launch-profile");
		startInfo.ArgumentList.Add("--");
		startInfo.ArgumentList.Add(mode);
		if (mode == "wrap-ignore-stdin")
			startInfo.ArgumentList.Add(helperSource);
		if (releasePath is not null)
			startInfo.ArgumentList.Add(releasePath);

		var process = Process.Start(startInfo)
			?? throw new InvalidOperationException("Failed to start the profile test process helper.");
		var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var finalizationStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		var monitoredProcess = MonitoredProcess.Attach(
			process,
			new JsonOutputFormatter(TextWriter.Null),
			useJson: true,
			verbose: false,
			"trace",
			CancellationToken.None,
			onStdoutLine: line =>
			{
				if (line == "ready")
					ready.TrySetResult(true);
				else if (line == "finalizing")
					finalizationStarted.TrySetResult(true);
			});

		return new ProfileTestProcess(monitoredProcess, ready.Task, finalizationStarted.Task);
	}

	static string CreateProfilingInjectionTestProject(string targetsPath, string intermediateOutputPath)
		=> $"""
			<Project>
			  <PropertyGroup>
			    <UseMaui>true</UseMaui>
			    <TargetFramework>net10.0-android</TargetFramework>
			    <IntermediateOutputPath>{System.Security.SecurityElement.Escape(intermediateOutputPath + Path.DirectorySeparatorChar)}</IntermediateOutputPath>
			  </PropertyGroup>
			  <Import Project="{System.Security.SecurityElement.Escape(targetsPath)}" />
			</Project>
			""";

	static string CreateClassLibraryProject(string assemblyName)
		=> $"""
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup>
			    <TargetFramework>net10.0</TargetFramework>
			    <AssemblyName>{assemblyName}</AssemblyName>
			  </PropertyGroup>
			</Project>
			""";

	static async Task<(int ExitCode, string Output)> RunProfileBuildAsync(
		string projectPath,
		ProfileBuildWorkspace workspace,
		params string[] additionalArguments)
	{
		var arguments = new List<string>
		{
			"build",
			projectPath,
			"--nologo",
			$"-p:ArtifactsPath={workspace.Path}",
			$"-p:DirectoryBuildPropsPath={workspace.DirectoryBuildPropsPath}",
			$"-p:DirectoryBuildTargetsPath={workspace.DirectoryBuildTargetsPath}",
			"-p:ImportDirectoryBuildTargets=true"
		};
		arguments.AddRange(additionalArguments);

		var result = await ProcessRunner.RunAsync(
			GetDotnetHostPath(),
			[.. arguments],
			Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../..")),
			timeout: TimeSpan.FromMinutes(2),
			environmentVariablesToRemove: ProfileCommand.s_msbuildSdkEnvVars);
		return (result.ExitCode, result.StandardOutput + result.StandardError);
	}

	static async Task<(int ExitCode, string Output)> RunProfilingInjectionTargetAsync(string projectPath, string selectedProjectPath)
	{
		var startInfo = new ProcessStartInfo(GetDotnetHostPath())
		{
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true
		};
		foreach (var argument in new[]
		{
			"msbuild",
			projectPath,
			"-t:GenerateMauiProfilingHelperEnvironment",
			"--nologo",
			"-p:MauiProfilingHelperInject=true",
			$"-p:MauiProfilingHelperProjectFullPath={selectedProjectPath}"
		})
		{
			startInfo.ArgumentList.Add(argument);
		}

		using var process = Process.Start(startInfo)
			?? throw new InvalidOperationException("Failed to start the profiling injection MSBuild test.");
		var standardOutput = process.StandardOutput.ReadToEndAsync();
		var standardError = process.StandardError.ReadToEndAsync();
		await process.WaitForExitAsync();
		var output = await standardOutput + await standardError;

		return (process.ExitCode, output);
	}

	static string GetDotnetHostPath()
		=> Path.GetFullPath(Path.Combine(
			System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(),
			"..",
			"..",
			"..",
			OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));

	static async Task WaitForFileAsync(string path, TimeSpan timeout)
	{
		using var cts = new CancellationTokenSource(timeout);
		while (!File.Exists(path))
			await Task.Delay(20, cts.Token);
	}

	static TempFile CreateTempFile(string fileName)
	{
		var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "maui-cli-profile-tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
		var path = System.IO.Path.Combine(directory, fileName);
		File.WriteAllBytes(path, []);
		return new TempFile(path);
	}

	static string TestPath(params string[] segments)
	{
		var allSegments = new string[segments.Length + 1];
		allSegments[0] = Path.GetTempPath();
		Array.Copy(segments, 0, allSegments, 1, segments.Length);
		return Path.Combine(allSegments);
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

	sealed class ProfileTestProcess(
		MonitoredProcess monitoredProcess,
		Task ready,
		Task finalizationStarted) : IAsyncDisposable
	{
		public MonitoredProcess MonitoredProcess { get; } = monitoredProcess;
		public Process Process => MonitoredProcess.Process;
		public Task Ready { get; } = ready;
		public Task FinalizationStarted { get; } = finalizationStarted;

		public async ValueTask DisposeAsync()
		{
			if (!Process.HasExited)
				Process.Kill(entireProcessTree: true);
			await Process.WaitForExitAsync();
			MonitoredProcess.Dispose();
		}
	}
}
