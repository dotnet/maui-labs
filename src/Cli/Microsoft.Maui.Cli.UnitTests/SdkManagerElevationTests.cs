// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui.Cli.Models;
using Microsoft.Maui.Cli.Providers.Android;
using Microsoft.Maui.Cli.UnitTests.Fakes;
using System.Text.Json.Nodes;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

/// <summary>
/// Tests for <see cref="SdkManager.SdkPathRequiresElevation"/> and the
/// <c>requiresElevation</c> field surfaced in <see cref="AndroidProvider.CheckHealthAsync"/>.
/// </summary>
public class SdkManagerElevationTests : IDisposable
{
	readonly string _tempDir;
	readonly string? _savedAndroidHome;

	public SdkManagerElevationTests()
	{
		_tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
		Directory.CreateDirectory(_tempDir);

		// Save the existing ANDROID_HOME so we can restore it after each test.
		_savedAndroidHome = Environment.GetEnvironmentVariable("ANDROID_HOME");
	}

	public void Dispose()
	{
		// Restore ANDROID_HOME to avoid leaking state to other tests.
		Environment.SetEnvironmentVariable("ANDROID_HOME", _savedAndroidHome);

		if (Directory.Exists(_tempDir))
			Directory.Delete(_tempDir, recursive: true);
	}

	// -----------------------------------------------------------------------
	// CanWriteToDirectory — cross-platform helper
	// -----------------------------------------------------------------------

	[Fact]
	public void CanWriteToDirectory_ReturnsTrue_ForWritableDirectory()
	{
		// A freshly created temp directory is always writable by the current user.
		Assert.True(SdkManager.CanWriteToDirectory(_tempDir));
	}

	[Fact]
	public void CanWriteToDirectory_LeavesNoArtifact_AfterSuccessfulProbe()
	{
		// The probe file must be cleaned up — verify no stray files remain.
		SdkManager.CanWriteToDirectory(_tempDir);

		Assert.Empty(Directory.GetFiles(_tempDir));
	}

	// -----------------------------------------------------------------------
	// SdkPathRequiresElevation — non-Windows always returns false
	// -----------------------------------------------------------------------

	[Fact]
	public void SdkPathRequiresElevation_ReturnsFalse_OnNonWindows()
	{
		if (OperatingSystem.IsWindows())
			return; // skip — platform-specific behaviour tested on Windows CI

		var manager = new SdkManager(() => _tempDir, () => null);
		Assert.False(manager.SdkPathRequiresElevation());
	}

	[Fact]
	public void SdkPathRequiresElevation_ReturnsFalse_WhenSdkPathIsNull()
	{
		var manager = new SdkManager(() => null, () => null);
		Assert.False(manager.SdkPathRequiresElevation());
	}

	[Fact]
	public void SdkPathRequiresElevation_ReturnsFalse_WhenSdkPathIsEmpty()
	{
		var manager = new SdkManager(() => string.Empty, () => null);
		Assert.False(manager.SdkPathRequiresElevation());
	}

	// -----------------------------------------------------------------------
	// Windows-only: existing writable directory must not require elevation
	// -----------------------------------------------------------------------

	[Fact]
	public void SdkPathRequiresElevation_ReturnsFalse_WhenExistingDirIsWritable()
	{
		if (!OperatingSystem.IsWindows())
			return; // writability probe is only exercised on Windows

		// _tempDir is writable by the current user, so elevation is not needed.
		var manager = new SdkManager(() => _tempDir, () => null);
		Assert.False(manager.SdkPathRequiresElevation());
	}

	// -----------------------------------------------------------------------
	// requiresElevation in AndroidProvider.CheckHealthAsync details
	// -----------------------------------------------------------------------

	[Fact]
	public async Task CheckHealthAsync_IncludesRequiresElevation_WhenSdkIsInstalled()
	{
		// Arrange: set ANDROID_HOME to _tempDir and create cmdline-tools so IsSdkInstalled = true.
		Directory.CreateDirectory(Path.Combine(_tempDir, "cmdline-tools"));
		Environment.SetEnvironmentVariable("ANDROID_HOME", _tempDir);

		var jdkManager = new FakeJdkManager { IsInstalled = true };
		var provider = new AndroidProvider(jdkManager);

		// Act
		var checks = await provider.CheckHealthAsync();
		var sdkCheck = checks.FirstOrDefault(c => c.Name == "Android SDK");

		// Assert: details must contain requiresElevation
		Assert.NotNull(sdkCheck);
		Assert.NotNull(sdkCheck.Details);
		Assert.True(sdkCheck.Details.ContainsKey("requiresElevation"),
			"HealthCheck.Details must contain 'requiresElevation' when SDK is installed");
	}

	[Fact]
	public async Task CheckHealthAsync_RequiresElevationIsFalse_ForWritablePath()
	{
		// Arrange: writable temp dir — should not require elevation on any platform.
		Directory.CreateDirectory(Path.Combine(_tempDir, "cmdline-tools"));
		Environment.SetEnvironmentVariable("ANDROID_HOME", _tempDir);

		var jdkManager = new FakeJdkManager { IsInstalled = true };
		var provider = new AndroidProvider(jdkManager);

		// Act
		var checks = await provider.CheckHealthAsync();
		var sdkCheck = checks.FirstOrDefault(c => c.Name == "Android SDK");

		// Assert: a writable temp dir must never be flagged as requiring elevation.
		Assert.NotNull(sdkCheck);
		Assert.NotNull(sdkCheck.Details);
		Assert.False(sdkCheck.Details["requiresElevation"]?.GetValue<bool>());
	}

	[Fact]
	public async Task CheckHealthAsync_IncludesRequiresElevation_WhenSdkIsNotFound()
	{
		// Arrange: point ANDROID_HOME to a directory that exists but has no cmdline-tools,
		// making IsSdkInstalled = false regardless of what is installed in the environment.
		var noSdkDir = Path.Combine(_tempDir, "no-sdk");
		Directory.CreateDirectory(noSdkDir);
		Environment.SetEnvironmentVariable("ANDROID_HOME", noSdkDir);

		var jdkManager = new FakeJdkManager { IsInstalled = true };
		var provider = new AndroidProvider(jdkManager);

		// Act
		var checks = await provider.CheckHealthAsync();
		var sdkCheck = checks.FirstOrDefault(c => c.Name == "Android SDK");

		// Assert: requiresElevation must still be present in the error check details.
		Assert.NotNull(sdkCheck);
		Assert.Equal(CheckStatus.Error, sdkCheck.Status);
		Assert.NotNull(sdkCheck.Details);
		Assert.True(sdkCheck.Details.ContainsKey("requiresElevation"),
			"requiresElevation must be present in error check details too");
	}
}
