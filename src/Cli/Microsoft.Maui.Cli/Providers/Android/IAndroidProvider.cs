// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui.Cli.Models;

namespace Microsoft.Maui.Cli.Providers.Android;

/// <summary>
/// Interface for Android SDK and device operations.
/// </summary>
public interface IAndroidProvider : IDisposable
{
	/// <summary>
	/// Gets the Android SDK path.
	/// </summary>
	string? SdkPath { get; }

	/// <summary>
	/// Gets the JDK path.
	/// </summary>
	string? JdkPath { get; }

	/// <summary>
	/// Checks if the Android SDK is installed.
	/// </summary>
	bool IsSdkInstalled { get; }

	/// <summary>
	/// Checks if a compatible JDK is installed.
	/// </summary>
	bool IsJdkInstalled { get; }

	/// <summary>
	/// Checks if the SDK is in a protected location that requires administrator access.
	/// </summary>
	bool SdkPathRequiresElevation { get; }

	/// <summary>
	/// Gets the health status of Android tooling.
	/// </summary>
	Task<List<HealthCheck>> CheckHealthAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Lists connected Android devices and running emulators.
	/// </summary>
	Task<List<Device>> GetDevicesAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Lists available AVDs.
	/// </summary>
	Task<List<AvdInfo>> GetAvdsAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Creates a new AVD.
	/// </summary>
	Task<AvdInfo> CreateAvdAsync(string name, string deviceProfile, string systemImage, bool force = false, CancellationToken cancellationToken = default);

	/// <summary>
	/// Deletes an AVD.
	/// </summary>
	Task DeleteAvdAsync(string name, CancellationToken cancellationToken = default);

	/// <summary>
	/// Starts an AVD.
	/// </summary>
	Task StartAvdAsync(string name, bool coldBoot = false, bool wait = false, CancellationToken cancellationToken = default);

	/// <summary>
	/// Stops a running emulator.
	/// </summary>
	Task StopEmulatorAsync(string deviceSerial, CancellationToken cancellationToken = default);

	/// <summary>
	/// Lists installed SDK packages.
	/// </summary>
	Task<List<SdkPackage>> GetInstalledPackagesAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Lists SDK packages available for installation.
	/// </summary>
	Task<List<SdkPackage>> GetAvailablePackagesAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Gets the most recent installed system image for AVD creation.
	/// Returns the system image path (e.g., "system-images;android-35;google_apis;arm64-v8a") or null if none found.
	/// </summary>
	Task<string?> GetMostRecentSystemImageAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Installs SDK packages.
	/// </summary>
	Task InstallPackagesAsync(IEnumerable<string> packages, bool acceptLicenses = false, CancellationToken cancellationToken = default);

	/// <summary>
	/// Installs SDK packages with per-package progress reporting.
	/// </summary>
	Task InstallPackagesAsync(IEnumerable<string> packages, bool acceptLicenses, Action<string, int, int>? onProgress, CancellationToken cancellationToken = default);

	/// <summary>
	/// Uninstalls SDK packages.
	/// </summary>
	Task UninstallPackagesAsync(IEnumerable<string> packages, CancellationToken cancellationToken = default);

	/// <summary>
	/// Accepts all SDK licenses.
	/// </summary>
	Task AcceptLicensesAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Accepts all SDK licenses with progress reporting.
	/// </summary>
	Task AcceptLicensesAsync(Action<string>? onProgress, CancellationToken cancellationToken = default);

	/// <summary>
	/// Checks if SDK licenses have been accepted.
	/// </summary>
	Task<bool> AreLicensesAcceptedAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Gets the command and arguments to run for interactive license acceptance.
	/// Returns (command, arguments) tuple for IDE terminal integration.
	/// </summary>
	(string Command, string Arguments)? GetLicenseAcceptanceCommand();

	/// <summary>
	/// Installs JDK if not present. When <paramref name="version"/> is null,
	/// <see cref="JdkManager.DefaultJdkVersion"/> is used.
	/// </summary>
	Task InstallJdkAsync(int? version = null, string? installPath = null, IProgress<string>? progress = null, CancellationToken cancellationToken = default);

	/// <summary>
	/// Installs the Android development environment. When <paramref name="jdkVersion"/> is null,
	/// <see cref="JdkManager.DefaultJdkVersion"/> is used.
	/// </summary>
	Task InstallAsync(string? sdkPath = null, string? jdkPath = null, int? jdkVersion = null, IEnumerable<string>? additionalPackages = null, bool acceptLicenses = false, IProgress<string>? progress = null, CancellationToken cancellationToken = default);

	/// <summary>
	/// Installs Android SDK command-line tools with structured progress reporting.
	/// </summary>
	Task InstallSdkToolsAsync(string targetPath, Action<string, int, string>? onProgress = null, CancellationToken cancellationToken = default);

	/// <summary>
	/// Overrides the Android SDK path for the current session.
	/// Rebuilds downstream tool wrappers (SdkManager, AvdManager, Adb) to use the new path.
	/// </summary>
	void OverrideSdkPath(string path);

	/// <summary>
	/// Overrides the JDK path for the current session.
	/// Rebuilds downstream tool wrappers so JAVA_HOME reflects the new path.
	/// </summary>
	void OverrideJdkPath(string path);

}

/// <summary>
/// Information about an Android Virtual Device.
/// </summary>
public record AvdInfo
{
	public required string Name { get; init; }
	public string? DeviceProfile { get; init; }

	/// <summary>
	/// Device manufacturer as configured in the AVD (e.g. "Google", "Samsung").
	/// Read from <c>hw.device.manufacturer</c> in config.ini.
	/// </summary>
	public string? Manufacturer { get; init; }

	public string? SystemImage { get; init; }
	public string? Target { get; init; }
	public string? Path { get; init; }

	/// <summary>
	/// True when the AVD directory contains a runtime lock file
	/// (e.g. <c>hardware-qemu.ini.lock</c>), indicating that an
	/// emulator instance is currently starting, booting, or running
	/// for this AVD.
	/// </summary>
	public bool IsLocked { get; init; }
}

/// <summary>
/// Information about an SDK package (installed or available).
/// </summary>
public record SdkPackage
{
	public required string Path { get; init; }
	public string? Version { get; init; }
	public string? Description { get; init; }
	public string? Location { get; init; }
	public bool IsInstalled { get; init; }
}
