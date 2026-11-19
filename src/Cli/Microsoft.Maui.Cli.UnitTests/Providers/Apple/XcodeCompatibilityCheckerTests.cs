// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui.Cli.Models;
using Microsoft.Maui.Cli.Providers.Apple;
using Microsoft.Maui.Cli.Services;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests.Providers.Apple;

public sealed class XcodeCompatibilityCheckerTests : IDisposable
{
	readonly string _tempDirectory = Path.Combine(
		Path.GetTempPath(),
		$"maui-xcode-compatibility-{Guid.NewGuid():N}");

	[Fact]
	public void CheckXcodeCompatibility_WithNullXcodeManager_ReturnsSkipped()
	{
		var result = new XcodeCompatibilityChecker().CheckXcodeCompatibility();

		Assert.Equal(CheckStatus.Skipped, result.Status);
		Assert.Contains("not available", result.Message);
	}

	[Fact]
	public void CheckXcodeCompatibility_WithCompatibleSelectedXcode_ReturnsOk()
	{
		CreateSdkPack("Microsoft.iOS.Sdk.net10.0_26.5", "26.5.10284", "26.5");
		var environment = CreateEnvironment(selectedVersion: "26.5.1");

		var result = new XcodeCompatibilityChecker(environment).CheckXcodeCompatibility();

		Assert.Equal(CheckStatus.Ok, result.Status);
		Assert.Null(result.Fix);
		Assert.Equal(1, result.Details!["sdk_count"]!.GetValue<int>());
	}

	[Fact]
	public void CheckXcodeCompatibility_WithMatchingInstalledXcode_ReturnsQuotedElevatedAutoFix()
	{
		CreateSdkPack("Microsoft.iOS.Sdk.net10.0_26.5", "26.5.10284", "26.5");
		const string matchingPath = "/Applications/Xcode 26.5.app";
		var environment = CreateEnvironment(
			selectedVersion: "26.4",
			installedXcodes:
			[
				CreateXcode(matchingPath, "26.5.1")
			]);

		var result = new XcodeCompatibilityChecker(environment).CheckXcodeCompatibility();

		Assert.Equal(CheckStatus.Warning, result.Status);
		Assert.True(result.Fix!.AutoFixable);
		Assert.Equal($"sudo xcode-select --switch \"{matchingPath}\"", result.Fix.Command);

		var command = Assert.IsType<string>(result.Fix.Command);
		var (fileName, arguments) = DoctorService.ParseCommand(command);
		Assert.Equal("sudo", fileName);
		Assert.Equal(["xcode-select", "--switch", matchingPath], arguments);
	}

	[Fact]
	public void CheckXcodeCompatibility_WithoutMatchingInstalledXcode_ReturnsManualFix()
	{
		CreateSdkPack("Microsoft.iOS.Sdk.net10.0_26.5", "26.5.10284", "26.5");
		var environment = CreateEnvironment(selectedVersion: "26.4");

		var result = new XcodeCompatibilityChecker(environment).CheckXcodeCompatibility();

		Assert.Equal(CheckStatus.Warning, result.Status);
		Assert.False(result.Fix!.AutoFixable);
		Assert.Null(result.Fix.Command);
		Assert.Contains(result.Fix.ManualSteps!, step => step.Contains("Install Xcode 26.5", StringComparison.Ordinal));
	}

	[Fact]
	public void CheckXcodeCompatibility_WithNoSelectedXcode_ReportsSelectionProblem()
	{
		CreateSdkPack("Microsoft.iOS.Sdk.net10.0_26.5", "26.5.10284", "26.5");
		var environment = CreateEnvironment(
			selectedVersion: null,
			installedXcodes:
			[
				CreateXcode("/Applications/Xcode.app", "26.5")
			]);

		var result = new XcodeCompatibilityChecker(environment).CheckXcodeCompatibility();

		Assert.Equal(CheckStatus.Warning, result.Status);
		Assert.Contains("no Xcode is selected", result.Message);
		Assert.True(result.Fix!.AutoFixable);
	}

	[Fact]
	public void CheckXcodeCompatibility_WithConflictingPlatformRequirements_DoesNotAutoFix()
	{
		CreateSdkPack("Microsoft.iOS.Sdk.net10.0_26.5", "26.5.10284", "26.5");
		CreateSdkPack("Microsoft.MacCatalyst.Sdk.net10.0_26.4", "26.4.10259", "26.4");
		var environment = CreateEnvironment(
			selectedVersion: "26.5",
			installedXcodes:
			[
				CreateXcode("/Applications/Xcode-26.5.app", "26.5"),
				CreateXcode("/Applications/Xcode-26.4.app", "26.4")
			]);

		var result = new XcodeCompatibilityChecker(environment).CheckXcodeCompatibility();

		Assert.Equal(CheckStatus.Warning, result.Status);
		Assert.Contains("require different Xcode versions (26.4, 26.5)", result.Message);
		Assert.False(result.Fix!.AutoFixable);
		Assert.Null(result.Fix.Command);
	}

	[Fact]
	public void CheckXcodeCompatibility_SelectsLatestPackPerPlatform()
	{
		CreateSdkPack("Microsoft.iOS.Sdk.net10.0_26.4", "26.4.10259", "26.4");
		CreateSdkPack("Microsoft.iOS.Sdk.net10.0_26.5", "26.5.10284", "26.5");
		var environment = CreateEnvironment(selectedVersion: "26.5");

		var result = new XcodeCompatibilityChecker(environment).CheckXcodeCompatibility();

		Assert.Equal(CheckStatus.Ok, result.Status);
		Assert.Equal(1, result.Details!["sdk_count"]!.GetValue<int>());
	}

	[Fact]
	public void CheckXcodeCompatibility_MergesPackRoots()
	{
		var secondRoot = Path.Combine(_tempDirectory, "second-root");
		CreateSdkPack("Microsoft.iOS.Sdk.net10.0_26.5", "26.5.10284", "26.5");
		CreateSdkPack(
			"Microsoft.MacCatalyst.Sdk.net10.0_26.5",
			"26.5.10284",
			"26.5",
			secondRoot);
		var environment = CreateEnvironment(selectedVersion: "26.5", packRoots: [_tempDirectory, secondRoot]);

		var result = new XcodeCompatibilityChecker(environment).CheckXcodeCompatibility();

		Assert.Equal(CheckStatus.Ok, result.Status);
		Assert.Equal(2, result.Details!["sdk_count"]!.GetValue<int>());
	}

	[Fact]
	public void CheckXcodeCompatibility_WithFlatPackName_DetectsRequirement()
	{
		CreateSdkPack("Microsoft.iOS.Sdk", "26.5.10284", "26.5");
		var environment = CreateEnvironment(selectedVersion: "26.5");

		var result = new XcodeCompatibilityChecker(environment).CheckXcodeCompatibility();

		Assert.Equal(CheckStatus.Ok, result.Status);
	}

	[Fact]
	public void CheckXcodeCompatibility_IgnoresPacksForOtherTargetFrameworks()
	{
		CreateSdkPack("Microsoft.iOS.Sdk.net11.0_26.5", "26.5.11514-net11-p4", "26.5");
		var environment = CreateEnvironment(selectedVersion: "26.5");

		var result = new XcodeCompatibilityChecker(environment).CheckXcodeCompatibility();

		Assert.Equal(CheckStatus.Skipped, result.Status);
		Assert.Contains("current .NET runtime", result.Message);
	}

	[Theory]
	[InlineData("26.5.1", "26.5")]
	[InlineData("26.5", "26.5")]
	[InlineData("26", "26")]
	[InlineData(null, null)]
	[InlineData("", null)]
	public void ExtractMajorMinor_ReturnsExpectedVersion(string? input, string? expected)
	{
		Assert.Equal(expected, XcodeCompatibilityChecker.ExtractMajorMinor(input));
	}

	public void Dispose()
	{
		if (Directory.Exists(_tempDirectory))
			Directory.Delete(_tempDirectory, recursive: true);
	}

	FakeXcodeCompatibilityEnvironment CreateEnvironment(
		string? selectedVersion,
		IReadOnlyList<XcodeInstallation>? installedXcodes = null,
		IReadOnlyList<string>? packRoots = null) =>
		new()
		{
			SelectedXcode = selectedVersion is null
				? null
				: CreateXcode("/Applications/Xcode.app", selectedVersion, isSelected: true),
			InstalledXcodes = installedXcodes ?? [],
			PackRoots = packRoots ?? [_tempDirectory]
		};

	void CreateSdkPack(
		string packName,
		string packVersion,
		string requiredXcodeVersion,
		string? root = null)
	{
		var targetsDirectory = Path.Combine(root ?? _tempDirectory, packName, packVersion, "targets");
		Directory.CreateDirectory(targetsDirectory);
		File.WriteAllText(
			Path.Combine(targetsDirectory, "Microsoft.Apple.Sdk.Versions.props"),
			$"""
			<?xml version="1.0" encoding="utf-8"?>
			<Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
			  <PropertyGroup>
			    <_RecommendedXcodeVersion>{requiredXcodeVersion}</_RecommendedXcodeVersion>
			  </PropertyGroup>
			</Project>
			""");
	}

	static XcodeInstallation CreateXcode(string path, string version, bool isSelected = false) =>
		new()
		{
			Path = path,
			Version = version,
			IsSelected = isSelected
		};

	sealed class FakeXcodeCompatibilityEnvironment : IXcodeCompatibilityEnvironment
	{
		public string TargetFramework { get; init; } = "net10.0";
		public XcodeInstallation? SelectedXcode { get; init; }
		public IReadOnlyList<XcodeInstallation> InstalledXcodes { get; init; } = [];
		public IReadOnlyList<string> PackRoots { get; init; } = [];

		public XcodeInstallation? GetSelectedXcode() => SelectedXcode;
		public IReadOnlyList<XcodeInstallation> GetInstalledXcodes() => InstalledXcodes;
		public IReadOnlyList<string> GetPackRoots() => PackRoots;
	}
}
