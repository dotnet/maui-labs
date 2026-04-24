// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui.Cli.Errors;
using Microsoft.Maui.Cli.Models;
using Microsoft.Maui.Cli.Providers.Android;
using Microsoft.Maui.Cli.UnitTests.Fakes;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class AndroidProviderIsSdkInstalledTests : IDisposable
{
	readonly string _tempDir;

	public AndroidProviderIsSdkInstalledTests()
	{
		_tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
		Directory.CreateDirectory(_tempDir);
	}

	public void Dispose()
	{
		if (Directory.Exists(_tempDir))
			Directory.Delete(_tempDir, recursive: true);
	}

	/// <summary>
	/// Mirrors the <c>AndroidProvider.IsSdkInstalled</c> check so tests stay in sync with the implementation.
	/// </summary>
	static bool IsSdkInstalledCheck(string? sdkPath) =>
		!string.IsNullOrEmpty(sdkPath)
		&& Directory.Exists(sdkPath);

	[Fact]
	public void IsSdkInstalled_ReturnsTrue_WhenSdkDirectoryExistsButCmdlineToolsAreMissing()
	{
		// The SDK directory exists but cmdline-tools subdirectory does NOT exist —
		// this should still count as "SDK directory present" so the health check can
		// surface the more specific sdkmanager-missing error.
		Assert.True(Directory.Exists(_tempDir));
		Assert.False(Directory.Exists(Path.Combine(_tempDir, "cmdline-tools")));

		Assert.True(IsSdkInstalledCheck(_tempDir));
	}

	[Fact]
	public void IsSdkInstalled_ReturnsTrue_WhenSdkDirectoryAndCmdlineToolsBothExist()
	{
		Directory.CreateDirectory(Path.Combine(_tempDir, "cmdline-tools"));

		Assert.True(IsSdkInstalledCheck(_tempDir));
	}

	[Fact]
	public void IsSdkInstalled_ReturnsFalse_WhenSdkDirectoryDoesNotExist()
	{
		var nonExistentPath = Path.Combine(_tempDir, "nonexistent");

		Assert.False(IsSdkInstalledCheck(nonExistentPath));
	}

	[Fact]
	public void IsSdkInstalled_ReturnsFalse_WhenSdkPathIsNull()
	{
		Assert.False(IsSdkInstalledCheck(null));
	}
}

public class SdkManagerTests : IDisposable
{
	readonly string _tempDir;

	public SdkManagerTests()
	{
		_tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
		Directory.CreateDirectory(_tempDir);
	}

	public void Dispose()
	{
		if (Directory.Exists(_tempDir))
			Directory.Delete(_tempDir, recursive: true);
	}

	[Fact]
	public void SdkManagerPath_FindsVersionedCmdlineToolsLayout()
	{
		var sdkManagerPath = Path.Combine(_tempDir, "cmdline-tools", "16.0", "bin",
			OperatingSystem.IsWindows() ? "sdkmanager.bat" : "sdkmanager");
		Directory.CreateDirectory(Path.GetDirectoryName(sdkManagerPath)!);
		File.WriteAllText(sdkManagerPath, string.Empty);

		using var sdkManager = new SdkManager(() => _tempDir, () => null);

		Assert.Equal(sdkManagerPath, sdkManager.SdkManagerPath);
	}

	[Fact]
	public void ResolveSdkManagerPath_ReturnsNull_WhenSdkManagerIsMissing()
	{
		Directory.CreateDirectory(Path.Combine(_tempDir, "cmdline-tools", "latest", "bin"));

		Assert.Null(SdkManager.ResolveSdkManagerPath(_tempDir));
	}
}

[Collection("AndroidEnvironment")]
public class AndroidProviderTests
{
	sealed class StubJdkManager : IJdkManager
	{
		public string? DetectedJdkPath { get; init; } = Path.GetTempPath();
		public int? DetectedJdkVersion { get; init; } = 17;
		public bool IsInstalled => true;

		public Task<HealthCheck> CheckHealthAsync(CancellationToken cancellationToken = default) =>
			Task.FromResult(new HealthCheck
			{
				Category = "android",
				Name = "JDK",
				Status = CheckStatus.Ok,
				Message = "JDK 17"
			});

		public Task InstallAsync(int? version = null, string? installPath = null, CancellationToken cancellationToken = default) =>
			Task.CompletedTask;

		public Task InstallAsync(int? version, string? installPath, Action<double, string>? onProgress, CancellationToken cancellationToken = default) =>
			Task.CompletedTask;

		public IEnumerable<int> GetAvailableVersions() => JdkManager.SupportedInstallVersions;
	}

	[Fact]
	public async Task CheckHealthAsync_ReturnsSdkManagerError_WhenSdkDirectoryExistsButManagerIsMissing()
	{
		var tempSdk = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
		Directory.CreateDirectory(tempSdk);

		var originalAndroidHome = Environment.GetEnvironmentVariable("ANDROID_HOME");
		var originalAndroidSdkRoot = Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");
		var originalPath = Environment.GetEnvironmentVariable("PATH");

		try
		{
			Environment.SetEnvironmentVariable("ANDROID_HOME", tempSdk);
			Environment.SetEnvironmentVariable("ANDROID_SDK_ROOT", null);
			// Clear PATH so the fallback SdkManager discovery doesn't find a
			// system-wide sdkmanager (e.g. from a CI runner's Android SDK).
			Environment.SetEnvironmentVariable("PATH", "");

			using var provider = new AndroidProvider(new StubJdkManager());
			var checks = await provider.CheckHealthAsync();

			Assert.Contains(checks, c => c.Name == "Android SDK" && c.Status == CheckStatus.Ok);
			Assert.Contains(checks, c =>
				c.Name == "SDK Manager"
				&& c.Status == CheckStatus.Error
				&& c.Fix?.IssueId == ErrorCodes.AndroidSdkManagerNotFound);
		}
		finally
		{
			Environment.SetEnvironmentVariable("ANDROID_HOME", originalAndroidHome);
			Environment.SetEnvironmentVariable("ANDROID_SDK_ROOT", originalAndroidSdkRoot);
			Environment.SetEnvironmentVariable("PATH", originalPath);

			if (Directory.Exists(tempSdk))
				Directory.Delete(tempSdk, recursive: true);
		}
	}

	[Fact]
	public async Task GetMostRecentSystemImageAsync_ReturnsHighestApiLevel()
	{
		// Arrange
		var packages = new List<SdkPackage>
		{
			new SdkPackage { Path = "system-images;android-33;google_apis;arm64-v8a" },
			new SdkPackage { Path = "system-images;android-35;google_apis;arm64-v8a" },
			new SdkPackage { Path = "system-images;android-34;google_apis;arm64-v8a" },
			new SdkPackage { Path = "platform-tools" }, // Not a system image
			new SdkPackage { Path = "build-tools;34.0.0" } // Not a system image
		};

		var provider = new FakeAndroidProvider
		{
			InstalledPackages = packages,
			GetMostRecentSystemImageFunc = async ct =>
			{
				var pkgs = packages;
				return pkgs
					.Where(p => p.Path.StartsWith("system-images;android-", StringComparison.OrdinalIgnoreCase))
					.Select(p => new { Package = p, ApiLevel = ExtractApiLevel(p.Path) })
					.Where(x => x.ApiLevel > 0)
					.OrderByDescending(x => x.ApiLevel)
					.FirstOrDefault()?.Package.Path;
			}
		};

		// Act
		var result = await provider.GetMostRecentSystemImageAsync();

		// Assert
		Assert.Equal("system-images;android-35;google_apis;arm64-v8a", result);
	}

	[Fact]
	public async Task GetMostRecentSystemImageAsync_ReturnsNull_WhenNoSystemImages()
	{
		// Arrange
		var provider = new FakeAndroidProvider
		{
			InstalledPackages = new List<SdkPackage>
			{
				new SdkPackage { Path = "platform-tools" },
				new SdkPackage { Path = "build-tools;34.0.0" }
			},
			MostRecentSystemImage = null
		};

		// Act
		var result = await provider.GetMostRecentSystemImageAsync();

		// Assert
		Assert.Null(result);
	}

	[Fact]
	public async Task CreateAvdAsync_CreatesAvdWithCorrectParameters()
	{
		// Arrange
		var provider = new FakeAndroidProvider();

		// Act
		var result = await provider.CreateAvdAsync(
			"TestEmulator",
			"pixel_6",
			"system-images;android-35;google_apis;arm64-v8a");

		// Assert
		Assert.Equal("TestEmulator", result.Name);
		Assert.Equal("pixel_6", result.DeviceProfile);
		Assert.Equal("system-images;android-35;google_apis;arm64-v8a", result.SystemImage);
	}

	[Fact]
	public async Task DeleteAvdAsync_CallsDeleteWithCorrectName()
	{
		// Arrange
		var provider = new FakeAndroidProvider();

		// Act
		await provider.DeleteAvdAsync("MyEmulator");

		// Assert
		Assert.Single(provider.DeletedAvds);
		Assert.Equal("MyEmulator", provider.DeletedAvds[0]);
	}

	[Fact]
	public async Task StartAvdAsync_StartsWithCorrectOptions()
	{
		// Arrange
		var provider = new FakeAndroidProvider();

		// Act
		await provider.StartAvdAsync("TestEmulator", coldBoot: true, wait: true);

		// Assert
		Assert.Single(provider.StartedAvds);
		Assert.Equal(("TestEmulator", true, true), provider.StartedAvds[0]);
	}

	[Fact]
	public async Task GetAvdsAsync_ReturnsAvdList()
	{
		// Arrange
		var provider = new FakeAndroidProvider
		{
			Avds = new List<AvdInfo>
			{
				new AvdInfo { Name = "Pixel_6_API_35", Target = "android-35", DeviceProfile = "pixel_6" },
				new AvdInfo { Name = "Pixel_7_API_34", Target = "android-34", DeviceProfile = "pixel_7" }
			}
		};

		// Act
		var result = await provider.GetAvdsAsync();

		// Assert
		Assert.Equal(2, result.Count);
		Assert.Contains(result, a => a.Name == "Pixel_6_API_35");
		Assert.Contains(result, a => a.Name == "Pixel_7_API_34");
	}

	[Fact]
	public async Task InstallAsync_ReportsProgress()
	{
		// Arrange
		var progressMessages = new List<string>();
		var progress = new Progress<string>(msg => progressMessages.Add(msg));

		var provider = new FakeAndroidProvider
		{
			InstallCallback = (sdk, jdk, ver, pkgs, prog, ct) =>
			{
				prog?.Report("Step 1/4: Installing JDK...");
				prog?.Report("Step 2/4: Installing Android SDK...");
				prog?.Report("Step 3/4: Accepting licenses...");
				prog?.Report("Step 4/4: Installing packages...");
			}
		};

		// Act
		await provider.InstallAsync(progress: progress);

		// Allow progress callbacks to complete
		await Task.Delay(100);

		// Assert
		Assert.Contains(progressMessages, m => m.Contains("Step 1/4"));
		Assert.Contains(progressMessages, m => m.Contains("Step 2/4"));
		Assert.Contains(progressMessages, m => m.Contains("Step 3/4"));
		Assert.Contains(progressMessages, m => m.Contains("Step 4/4"));
	}

	[Fact]
	public async Task InstallPackagesAsync_InstallsMultiplePackages()
	{
		// Arrange
		var provider = new FakeAndroidProvider();
		var packages = new[] { "platform-tools", "build-tools;35.0.0", "platforms;android-35" };

		// Act
		await provider.InstallPackagesAsync(packages, acceptLicenses: true);

		// Assert
		Assert.Single(provider.InstalledPackageSets);
		var installedPackages = provider.InstalledPackageSets[0];
		Assert.Equal(3, installedPackages.Count);
		Assert.Contains("platform-tools", installedPackages);
		Assert.Contains("build-tools;35.0.0", installedPackages);
		Assert.Contains("platforms;android-35", installedPackages);
	}

	[Fact]
	public async Task InstallPackagesAsync_InvokesOnProgressForEachPackage()
	{
		// Arrange
		var provider = new FakeAndroidProvider();
		var packages = new[] { "platform-tools", "build-tools;35.0.0", "platforms;android-35" };
		var progressCalls = new List<(string pkg, int idx, int total)>();

		// Act
		await provider.InstallPackagesAsync(packages, acceptLicenses: true,
			onProgress: (pkg, idx, total) => progressCalls.Add((pkg, idx, total)));

		// Assert
		Assert.Equal(3, progressCalls.Count);
		Assert.Equal(("platform-tools", 1, 3), progressCalls[0]);
		Assert.Equal(("build-tools;35.0.0", 2, 3), progressCalls[1]);
		Assert.Equal(("platforms;android-35", 3, 3), progressCalls[2]);
	}

	[Fact]
	public async Task GetAvailablePackagesAsync_ReturnsAvailablePackages()
	{
		// Arrange
		var provider = new FakeAndroidProvider
		{
			AvailablePackages = new List<SdkPackage>
			{
				new SdkPackage { Path = "platforms;android-36", Version = "1", Description = "Android SDK Platform 36", IsInstalled = false },
				new SdkPackage { Path = "system-images;android-36;google_apis;arm64-v8a", Version = "1", IsInstalled = false },
				new SdkPackage { Path = "build-tools;36.0.0", Version = "36.0.0", IsInstalled = false }
			}
		};

		// Act
		var result = await provider.GetAvailablePackagesAsync();

		// Assert
		Assert.Equal(3, result.Count);
		Assert.All(result, pkg => Assert.False(pkg.IsInstalled));
		Assert.Contains(result, p => p.Path == "platforms;android-36");
		Assert.Contains(result, p => p.Path == "system-images;android-36;google_apis;arm64-v8a");
	}

	[Fact]
	public async Task GetInstalledPackagesAsync_SetsIsInstalledToTrue()
	{
		// Arrange
		var provider = new FakeAndroidProvider
		{
			InstalledPackages = new List<SdkPackage>
			{
				new SdkPackage { Path = "platforms;android-35", Version = "3", IsInstalled = true },
				new SdkPackage { Path = "build-tools;35.0.0", Version = "35.0.0", IsInstalled = true },
				new SdkPackage { Path = "platform-tools", Version = "35.0.2", IsInstalled = true }
			}
		};

		// Act
		var result = await provider.GetInstalledPackagesAsync();

		// Assert
		Assert.Equal(3, result.Count);
		Assert.All(result, pkg => Assert.True(pkg.IsInstalled));
	}

	[Fact]
	public async Task GetAvailableAndInstalledPackages_CanBeCombined()
	{
		// Arrange
		var provider = new FakeAndroidProvider
		{
			InstalledPackages = new List<SdkPackage>
			{
				new SdkPackage { Path = "platforms;android-35", Version = "3", IsInstalled = true },
				new SdkPackage { Path = "build-tools;35.0.0", Version = "35.0.0", IsInstalled = true }
			},
			AvailablePackages = new List<SdkPackage>
			{
				new SdkPackage { Path = "platforms;android-36", Version = "1", IsInstalled = false },
				new SdkPackage { Path = "build-tools;36.0.0", Version = "36.0.0", IsInstalled = false }
			}
		};

		// Act
		var installed = await provider.GetInstalledPackagesAsync();
		var available = await provider.GetAvailablePackagesAsync();
		var allPackages = installed.Concat(available).ToList();

		// Assert
		Assert.Equal(4, allPackages.Count);
		Assert.Equal(2, allPackages.Count(p => p.IsInstalled));
		Assert.Equal(2, allPackages.Count(p => !p.IsInstalled));
	}

	// Helper method to extract API level from system image path
	private static int ExtractApiLevel(string systemImagePath)
	{
		var parts = systemImagePath.Split(';');
		if (parts.Length >= 2)
		{
			var androidPart = parts[1];
			if (androidPart.StartsWith("android-", StringComparison.OrdinalIgnoreCase))
			{
				var levelStr = androidPart.Substring(8);
				if (int.TryParse(levelStr, out var level))
					return level;
			}
		}
		return 0;
	}
}
