// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui.Cli.Commands;
using Microsoft.Maui.Cli.Errors;
using Microsoft.Maui.Cli.Models;
using Microsoft.Maui.Cli.Output;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public sealed class ProfileMibcReferenceResolverTests : IDisposable
{
	readonly string _root = Path.Combine(Path.GetTempPath(), $"maui-mibc-tests-{Guid.NewGuid():N}");

	[Theory]
	[InlineData("android-arm")]
	[InlineData("android-arm64")]
	[InlineData("android-x86")]
	[InlineData("android-x64")]
	[InlineData("iossimulator-arm64")]
	[InlineData("iossimulator-x64")]
	[InlineData("ios-arm64")]
	public void ResolveReferenceAssemblies_MixedAbis_ReturnsOnlySelectedRid(string rid)
	{
		var output = Path.Combine(_root, "obj", "Application-hash", "release_net11.0-android");
		foreach (var otherRid in new[] { "android-arm", "android-arm64", "android-x86", "android-x64", "iossimulator-arm64", "iossimulator-x64", "ios-arm64" })
			WriteAssembly(output, otherRid, "R2R", "shrunk", "System.Private.CoreLib.dll");
		var expectedCoreLib = Path.Combine(output, rid, "R2R", "shrunk", "System.Private.CoreLib.dll");
		var expectedApp = WriteAssembly(output, rid, "R2R", "shrunk", "Application.dll");
		WriteAssembly(output, "ref", "Application.dll");

		var references = ProfileMibcReferenceResolver.ResolveReferenceAssemblies([output], rid, ProfileMibcAssemblyStage.Shrunk);
		var arguments = ProfileCommand.BuildMibcArguments("startup.nettrace", "startup.mibc", references).ToArray();

		Assert.Equal(new[] { expectedApp, expectedCoreLib }, references);
		Assert.Equal(new[] { "create-mibc", "--trace", "startup.nettrace", "--output", "startup.mibc",
			"--reference", expectedApp, "--reference", expectedCoreLib }, arguments);
	}

	[Fact]
	public void ResolveReferenceAssemblies_OtherAbiIsShrunk_UsesMatchingUntrimmedAssemblies()
	{
		var output = Path.Combine(_root, "obj");
		WriteAssembly(output, "android-arm", "R2R", "shrunk", "System.Private.CoreLib.dll");
		var expected = WriteAssembly(output, "android-arm64", "R2R", "System.Private.CoreLib.dll");

		var references = ProfileMibcReferenceResolver.ResolveReferenceAssemblies([output], "android-arm64", ProfileMibcAssemblyStage.Untrimmed);

		Assert.Equal(new[] { expected }, references);
	}

	[Fact]
	public void ResolveReferenceAssemblies_Shrinking_UsesOnlyShrunkAssemblies()
	{
		var output = Path.Combine(_root, "obj");
		WriteAssembly(output, "android-arm64", "R2R", "System.Private.CoreLib.dll");
		var expected = WriteAssembly(output, "android-arm64", "R2R", "shrunk", "System.Private.CoreLib.dll");

		Assert.Equal(new[] { expected }, ProfileMibcReferenceResolver.ResolveReferenceAssemblies([output, output], "android-arm64", ProfileMibcAssemblyStage.Shrunk));
	}

	[Fact]
	public void ResolveReferenceAssemblies_MatchingAbiHasLinkedAndShrunkCopies_UsesFinalShrunkCopies()
	{
		var output = Path.Combine(_root, "obj");
		WriteAssembly(output, "android-arm64", "linked", "System.Private.CoreLib.dll");
		var expected = WriteAssembly(output, "android-arm64", "R2R", "shrunk", "System.Private.CoreLib.dll");

		Assert.Equal(new[] { expected }, ProfileMibcReferenceResolver.ResolveReferenceAssemblies([output], "android-arm64", ProfileMibcAssemblyStage.Shrunk));
	}

	[Fact]
	public void ResolveReferenceAssemblies_UntrimmedOutput_ExcludesReferenceOnlyAssemblies()
	{
		var output = Path.Combine(_root, "obj");
		var expected = WriteAssembly(output, "Application.dll");
		WriteAssembly(output, "ref", "Application.dll");
		WriteAssembly(output, "refint", "Application.dll");

		Assert.Equal(new[] { expected }, ProfileMibcReferenceResolver.ResolveReferenceAssemblies([output], "android-arm64", ProfileMibcAssemblyStage.Untrimmed));
	}

	[Fact]
	public void ResolveReferenceAssemblies_OnlyWrongAbiExists_ThrowsInsteadOfUsingNeutralAssemblies()
	{
		var output = Path.Combine(_root, "obj");
		WriteAssembly(output, "android-arm", "R2R", "shrunk", "System.Private.CoreLib.dll");
		WriteAssembly(output, "Application.dll");

		var exception = Assert.Throws<MauiToolException>(() =>
			ProfileMibcReferenceResolver.ResolveReferenceAssemblies([output], "android-arm64", ProfileMibcAssemblyStage.Shrunk));

		Assert.Contains("android-arm64", exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void ResolveReferenceAssemblies_RidSpecificOutputMissing_DoesNotUseOuterBuildAssembly()
	{
		var outerOutput = Path.Combine(_root, "obj", "release");
		WriteAssembly(outerOutput, "Application.dll");
		var ridOutput = Path.Combine(_root, "obj", "release_android-arm64");

		var exception = Assert.Throws<MauiToolException>(() =>
			ProfileMibcReferenceResolver.ResolveReferenceAssemblies([outerOutput], "android-arm64", ProfileMibcAssemblyStage.Untrimmed, [ridOutput]));

		Assert.Contains("android-arm64", exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void ResolveReferenceAssemblies_Linking_UsesOnlyLinkedAssemblies()
	{
		var output = Path.Combine(_root, "obj", "Application_android-arm-hash", "release_net11.0-android_android-arm64");
		var expected = WriteAssembly(output, "linked", "System.Private.CoreLib.dll");
		WriteAssembly(output, "R2R", "shrunk", "System.Private.CoreLib.dll");
		WriteAssembly(output, "System.Private.CoreLib.dll");

		Assert.Equal(new[] { expected }, ProfileMibcReferenceResolver.ResolveReferenceAssemblies([output], "android-arm64", ProfileMibcAssemblyStage.Linked));
	}

	[Theory]
	[InlineData("linked", "shrunk")]
	[InlineData("shrunk", "linked")]
	public void ResolveReferenceAssemblies_RequiredStageMissing_ThrowsInsteadOfFallingBack(
		string expectedDirectory, string availableDirectory)
	{
		var stage = Enum.Parse<ProfileMibcAssemblyStage>(expectedDirectory, ignoreCase: true);
		var output = Path.Combine(_root, "obj");
		WriteAssembly(output, "android-arm64", availableDirectory, "System.Private.CoreLib.dll");
		WriteAssembly(output, "android-arm64", "System.Private.CoreLib.dll");

		var exception = Assert.Throws<MauiToolException>(() =>
			ProfileMibcReferenceResolver.ResolveReferenceAssemblies([output], "android-arm64", stage));

		Assert.Contains(expectedDirectory, exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void ResolveReferenceAssemblies_UntrimmedBuild_IgnoresLinkedAndShrunkOutputs()
	{
		var output = Path.Combine(_root, "obj");
		var expected = WriteAssembly(output, "android-arm64", "R2R", "System.Private.CoreLib.dll");
		WriteAssembly(output, "android-arm64", "linked", "System.Private.CoreLib.dll");
		WriteAssembly(output, "android-arm64", "R2R", "shrunk", "System.Private.CoreLib.dll");

		Assert.Equal(new[] { expected }, ProfileMibcReferenceResolver.ResolveReferenceAssemblies(
			[output], "android-arm64", ProfileMibcAssemblyStage.Untrimmed));
	}

	[Fact]
	public void ResolveReferenceAssemblies_MissingOutput_ReturnsEmpty()
	{
		Assert.Empty(ProfileMibcReferenceResolver.ResolveReferenceAssemblies([_root], "android-arm64", ProfileMibcAssemblyStage.Untrimmed));
	}

	[Theory]
	[InlineData(Platforms.Android, "true", "SdkOnly", "false", "Shrunk")]
	[InlineData(Platforms.Android, "TRUE", "Full", "FALSE", "Shrunk")]
	[InlineData(Platforms.Android, "true", "SdkOnly", "true", "Linked")]
	[InlineData(Platforms.Android, "true", "None", "false", "Linked")]
	[InlineData(Platforms.Android, "false", "None", "false", "Untrimmed")]
	[InlineData(Platforms.Android, null, null, null, "Untrimmed")]
	[InlineData(Platforms.iOS, "true", null, null, "Linked")]
	[InlineData(Platforms.iOS, "false", null, null, "Untrimmed")]
	public void ResolveAssemblyStage_UsesBuildSettings(
		string platform, string? publishTrimmed, string? linkMode, string? debugSymbols, string expected)
	{
		Assert.Equal(expected, ProfileMibcReferenceResolver.ResolveAssemblyStage(platform, publishTrimmed, linkMode, debugSymbols).ToString());
	}

	[Theory]
	[InlineData("android-arm", "android-arm;android-arm64", "android-arm")]
	[InlineData(null, "android-arm", "android-arm")]
	[InlineData(null, "android-arm;android-arm64;android-x64", "android-arm64")]
	[InlineData(null, " android-arm64 ; android-arm64 ", "android-arm64")]
	public void ResolveRuntimeIdentifier_RespectsProjectRidBeforeDeviceAbi(string? rid, string rids, string expected)
	{
		Assert.Equal(expected, ProfileMibcReferenceResolver.ResolveRuntimeIdentifier(rid, rids, CreateDevice()));
	}

	[Theory]
	[InlineData("arm64", null)]
	[InlineData(null, "arm64-v8a")]
	public void ResolveRuntimeIdentifier_DeviceHasNoRids_UsesArchitecture(string? architecture, string? abi)
	{
		var device = CreateDevice() with { RuntimeIdentifiers = null, Architecture = architecture, PlatformArchitecture = abi };

		Assert.Equal("android-arm64", ProfileMibcReferenceResolver.ResolveRuntimeIdentifier(null, "android-arm;android-arm64", device));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("android-arm;android-x64")]
	public void ResolveRuntimeIdentifier_NoMatchingRid_Throws(string? rids)
	{
		var exception = Assert.Throws<MauiToolException>(() =>
			ProfileMibcReferenceResolver.ResolveRuntimeIdentifier(null, rids, CreateDevice()));

		Assert.Contains("runtime identifier", exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void ResolveRuntimeIdentifier_UnknownDeviceAbiWithMultipleRids_Throws()
	{
		Assert.Throws<MauiToolException>(() => ProfileMibcReferenceResolver.ResolveRuntimeIdentifier(
			null, "android-arm;android-arm64", CreateDevice() with { RuntimeIdentifiers = null }));
	}

	[Fact]
	public void ResolveRuntimeIdentifier_IosSimulatorWithoutArchitecture_UsesHostSimulatorRid()
	{
		var device = CreateDevice() with { Platforms = [Platforms.iOS], RuntimeIdentifiers = null, IsEmulator = true };
		var expected = Microsoft.Maui.Cli.Utils.PlatformDetector.IsArm64 ? "iossimulator-arm64" : "iossimulator-x64";

		Assert.Equal(expected, ProfileMibcReferenceResolver.ResolveRuntimeIdentifier(
			null, "iossimulator-x64;iossimulator-arm64", device));
	}

	[Fact]
	public async Task ResolveAsync_EvaluatesSelectedFrameworkAndConfiguration_IgnoresOtherOutputs()
	{
		var projectPath = WriteProject("""
			<Project>
			  <PropertyGroup>
			    <RuntimeIdentifiers>android-arm;android-arm64;android-x64</RuntimeIdentifiers>
			    <PublishTrimmed>true</PublishTrimmed>
			    <AndroidLinkMode>SdkOnly</AndroidLinkMode>
			    <AndroidIncludeDebugSymbols>false</AndroidIncludeDebugSymbols>
			    <IntermediateOutputPath>$(ArtifactsPath)/obj/Application/$(Configuration)_$(TargetFramework)/</IntermediateOutputPath>
			    <OutputPath>$(ArtifactsPath)/bin/Application/$(Configuration)_$(TargetFramework)/</OutputPath>
			  </PropertyGroup>
			</Project>
			""");
		var formatter = new JsonOutputFormatter(TextWriter.Null);
		var workspace = ProfileBuildWorkspace.Create(_root, formatter, useJson: true, verbose: false);
		workspace.ConfigureBuildTargets(profilingInjectionTargetsPath: null);
		try
		{
			var selectedOutput = Path.Combine(workspace.Path, "obj", "Application", "Release_net11.0-android");
			var expected = WriteAssembly(selectedOutput, "android-arm64", "R2R", "shrunk", "System.Private.CoreLib.dll");
			WriteAssembly(selectedOutput, "android-arm", "R2R", "shrunk", "System.Private.CoreLib.dll");
			WriteAssembly(workspace.Path, "obj", "Application", "Release_net10.0-android", "android-arm64", "R2R", "shrunk", "System.Private.CoreLib.dll");
			WriteAssembly(workspace.Path, "obj", "Application", "Debug_net11.0-android", "android-arm64", "R2R", "shrunk", "System.Private.CoreLib.dll");
			WriteAssembly(_root, "obj", "Release", "net11.0-android", "android-arm64", "R2R", "shrunk", "System.Private.CoreLib.dll");
			var context = CreateContext(projectPath, workspace);
			var arguments = ProfileCommandArguments.BuildMibcPropertyArguments(context);

			Assert.Contains("-p:TargetFramework=net11.0-android", arguments);
			Assert.Contains("-p:Configuration=Release", arguments);
			Assert.Contains($"-p:ArtifactsPath={workspace.Path}", arguments);
			Assert.Contains($"-p:DirectoryBuildPropsPath={workspace.DirectoryBuildPropsPath}", arguments);
			Assert.Contains($"-p:CustomAfterDirectoryBuildTargets={workspace.DirectoryBuildTargetsPath}", arguments);
			Assert.Contains($"-p:CustomAfterMicrosoftCommonCrossTargetingTargets={workspace.DirectoryBuildTargetsPath}", arguments);
			Assert.Contains("-p:Device=android-device", arguments);
			Assert.Contains("-p:EnableDiagnostics=true", arguments);
			Assert.Equal(new[] { expected }, await ProfileMibcReferenceResolver.ResolveAsync(context, CancellationToken.None));
		}
		finally
		{
			workspace.Cleanup(formatter, useJson: true, verbose: false);
		}
	}

	[Fact]
	public async Task ResolveAsync_SdkArtifactPivots_UsesEvaluatedOutputPaths()
	{
		var projectPath = WriteProject("""
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup>
			    <TargetFrameworks>net10.0;netstandard2.1</TargetFrameworks>
			    <RuntimeIdentifier>android-arm64</RuntimeIdentifier>
			    <PublishTrimmed>true</PublishTrimmed>
			    <AndroidLinkMode>None</AndroidLinkMode>
			    <ArtifactsPivots>custom_$(TargetFramework)_$(RuntimeIdentifier)</ArtifactsPivots>
			  </PropertyGroup>
			</Project>
			""");
		var formatter = new JsonOutputFormatter(TextWriter.Null);
		var workspace = ProfileBuildWorkspace.Create(_root, formatter, useJson: true, verbose: false);
		workspace.ConfigureBuildTargets(profilingInjectionTargetsPath: null);
		try
		{
			var context = CreateContext(projectPath, workspace, framework: "net10.0");
			var artifactsRoot = Path.Combine(workspace.Path, "obj");
			// The isolated project identity is evaluated by MSBuild, not inferred by the resolver.
			WriteAssembly(artifactsRoot, "Application-not-the-project", "custom_net10.0_android-arm64", "linked", "Wrong.dll");
			var projectIdentity = await GetArtifactsProjectNameAsync(context);
			var output = Path.Combine(artifactsRoot, projectIdentity, "custom_net10.0_android-arm64");
			var expected = WriteAssembly(output, "linked", "Application.dll");
			WriteAssembly(artifactsRoot, projectIdentity, "custom_netstandard2.1_android-arm64", "linked", "Wrong.dll");

			Assert.Equal(new[] { expected }, await ProfileMibcReferenceResolver.ResolveAsync(context, CancellationToken.None));
		}
		finally
		{
			workspace.Cleanup(formatter, useJson: true, verbose: false);
		}
	}

	[Fact]
	public async Task ResolveAsync_MultiRidSdkArtifactPivots_FindsSelectedRidSiblingOutput()
	{
		var projectPath = WriteProject("""
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup>
			    <TargetFrameworks>net10.0;netstandard2.1</TargetFrameworks>
			    <RuntimeIdentifiers>android-arm;android-arm64;android-x64</RuntimeIdentifiers>
			    <PublishTrimmed>true</PublishTrimmed>
			    <AndroidLinkMode>None</AndroidLinkMode>
			    <ArtifactsPivots>custom_$(TargetFramework)</ArtifactsPivots>
			    <ArtifactsPivots Condition="'$(RuntimeIdentifier)' != ''">$(ArtifactsPivots)_$(RuntimeIdentifier)</ArtifactsPivots>
			  </PropertyGroup>
			</Project>
			""");
		var formatter = new JsonOutputFormatter(TextWriter.Null);
		var workspace = ProfileBuildWorkspace.Create(_root, formatter, useJson: true, verbose: false);
		workspace.ConfigureBuildTargets(profilingInjectionTargetsPath: null);
		try
		{
			var context = CreateContext(projectPath, workspace, framework: "net10.0");
			var ridArguments = ProfileCommandArguments.BuildMibcPropertyArguments(context, "android-arm64");
			Assert.Contains("-p:RuntimeIdentifier=android-arm64", ridArguments);
			Assert.Contains("-p:RuntimeIdentifiers=", ridArguments);
			Assert.Contains("-p:AppendRuntimeIdentifierToOutputPath=true", ridArguments);
			var projectIdentity = await GetArtifactsProjectNameAsync(context);
			var artifactsRoot = Path.Combine(workspace.Path, "obj", projectIdentity);
			WriteAssembly(artifactsRoot, "custom_net10.0", "Application.dll");
			WriteAssembly(artifactsRoot, "custom_net10.0_android-arm", "linked", "System.Private.CoreLib.dll");
			var expected = WriteAssembly(artifactsRoot, "custom_net10.0_android-arm64", "linked", "System.Private.CoreLib.dll");
			WriteAssembly(artifactsRoot, "custom_netstandard2.1_android-arm64", "linked", "WrongFramework.dll");

			Assert.Equal(new[] { expected }, await ProfileMibcReferenceResolver.ResolveAsync(context, CancellationToken.None));
		}
		finally
		{
			workspace.Cleanup(formatter, useJson: true, verbose: false);
		}
	}

	[Fact]
	public async Task ResolveAsync_OutputOutsideWorkspace_Throws()
	{
		var projectPath = WriteProject("""
			<Project>
			  <PropertyGroup>
			    <RuntimeIdentifier>android-arm64</RuntimeIdentifier>
			    <IntermediateOutputPath>obj/Release/net11.0-android/</IntermediateOutputPath>
			    <OutputPath>bin/Release/net11.0-android/</OutputPath>
			  </PropertyGroup>
			</Project>
			""");
		var formatter = new JsonOutputFormatter(TextWriter.Null);
		var workspace = ProfileBuildWorkspace.Create(_root, formatter, useJson: true, verbose: false);
		workspace.ConfigureBuildTargets(profilingInjectionTargetsPath: null);
		try
		{
			var exception = await Assert.ThrowsAsync<MauiToolException>(() =>
				ProfileMibcReferenceResolver.ResolveAsync(CreateContext(projectPath, workspace), CancellationToken.None));

			Assert.Contains("outside the isolated profiling workspace", exception.Message, StringComparison.Ordinal);
		}
		finally
		{
			workspace.Cleanup(formatter, useJson: true, verbose: false);
		}
	}

	static async Task<string> GetArtifactsProjectNameAsync(ProfileSessionContext context)
	{
		var result = await Microsoft.Maui.Cli.Utils.ProcessRunner.RunAsync(
			"dotnet", [.. ProfileCommandArguments.BuildMibcPropertyArguments(context), "-getProperty:ArtifactsProjectName"],
			context.Project.ProjectDirectory);
		Assert.True(result.Success, result.StandardError);
		using var document = System.Text.Json.JsonDocument.Parse(result.StandardOutput);
		return document.RootElement.GetProperty("Properties").GetProperty("ArtifactsProjectName").GetString()!;
	}

	[Fact]
	public async Task ResolveAsync_IosTrimmingComputedDuringTargetExecution_UsesLinkedAssemblies()
	{
		var projectPath = WriteProject("""
			<Project>
			  <PropertyGroup>
			    <RuntimeIdentifier>iossimulator-arm64</RuntimeIdentifier>
			    <IntermediateOutputPath>$(ArtifactsPath)/obj/Release/$(TargetFramework)/$(RuntimeIdentifier)/</IntermediateOutputPath>
			    <OutputPath>$(ArtifactsPath)/bin/Release/$(TargetFramework)/$(RuntimeIdentifier)/</OutputPath>
			  </PropertyGroup>
			  <Target Name="_ComputePublishTrimmed">
			    <PropertyGroup>
			      <PublishTrimmed>true</PublishTrimmed>
			    </PropertyGroup>
			  </Target>
			</Project>
			""");
		var formatter = new JsonOutputFormatter(TextWriter.Null);
		var workspace = ProfileBuildWorkspace.Create(_root, formatter, useJson: true, verbose: false);
		workspace.ConfigureBuildTargets(profilingInjectionTargetsPath: null);
		try
		{
			var context = CreateContext(projectPath, workspace, framework: "net11.0-ios");
			var output = Path.Combine(workspace.Path, "obj", "Release", "net11.0-ios", "iossimulator-arm64");
			var expected = WriteAssembly(output, "linked", "Application.dll");
			WriteAssembly(output, "Application.dll");
			WriteAssembly(output, "shrunk", "Application.dll");

			Assert.Contains("-target:_ComputePublishTrimmed", ProfileCommandArguments.BuildMibcPropertyArguments(context));
			Assert.Equal(new[] { expected }, await ProfileMibcReferenceResolver.ResolveAsync(context, CancellationToken.None));
		}
		finally
		{
			workspace.Cleanup(formatter, useJson: true, verbose: false);
		}
	}

	ProfileSessionContext CreateContext(string projectPath, ProfileBuildWorkspace workspace, string framework = "net11.0-android")
	{
		var platform = ProfileTargetResolver.InferPlatformFromTargetFramework(framework) ?? Platforms.Android;
		var device = CreateDevice() with
		{
			Platforms = [platform],
			IsEmulator = true,
			RuntimeIdentifiers = [platform == Platforms.iOS ? "iossimulator-arm64" : "android-arm64"]
		};
		var request = new ProfileSessionRequest(
			new ResolvedMauiProject
			{
				ProjectPath = projectPath,
				ProjectDirectory = _root,
				ProjectName = "Application",
				TargetFrameworks = [framework]
			},
			framework, device, Path.Combine(_root, "startup.nettrace"), TraceOutputFormat.Mibc, "Release",
			null, false, 9000, TimeSpan.FromMinutes(2), null, null, null, null, false,
			new JsonOutputFormatter(TextWriter.Null), true, false);
		return new ProfileSessionContext(request, Path.Combine(_root, "startup.mibc"), platform,
			ProfileCommand.ResolveProfileTransport(platform, device), workspace);
	}

	static Device CreateDevice() => new()
	{
		Id = "android-device",
		Name = "Android device",
		Platforms = [Platforms.Android],
		RuntimeIdentifiers = ["android-arm64"],
		IsRunning = true
	};

	string WriteProject(string content)
	{
		Directory.CreateDirectory(_root);
		var path = Path.Combine(_root, "Application.csproj");
		File.WriteAllText(path, content);
		return path;
	}

	static string WriteAssembly(string root, params string[] parts)
	{
		var path = Path.Combine([root, .. parts]);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, string.Empty);
		return path;
	}

	public void Dispose()
	{
		if (Directory.Exists(_root))
			Directory.Delete(_root, recursive: true);
	}
}
