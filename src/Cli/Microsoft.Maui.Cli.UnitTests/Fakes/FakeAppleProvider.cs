// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui.Cli.Models;
using Microsoft.Maui.Cli.Providers.Apple;
using Xamarin.MacDev;

namespace Microsoft.Maui.Cli.UnitTests.Fakes;

/// <summary>
/// Hand-written fake for <see cref="IAppleProvider"/> used in unit tests.
/// Set the public properties to control return values; inspect the tracking
/// lists to verify which methods were called and with what arguments.
/// </summary>
public class FakeAppleProvider : IAppleProvider
{
	// --- Configurable return values ---

	public List<XcodeInstallation> XcodeInstallations { get; set; } = new();
	public XcodeInstallation? SelectedXcode { get; set; }
	public CommandLineToolsStatus CltStatus { get; set; } = new();
	public List<RuntimeInfo> Runtimes { get; set; } = new();
	public List<SimulatorInfo> Simulators { get; set; } = new();
	public List<HealthCheck> HealthChecks { get; set; } = new();
	public List<Device> Devices { get; set; } = new();
	public AppleInstallResult InstallResult { get; set; } = new() { Status = "ok" };

	public bool SelectXcodeResult { get; set; } = true;
	public bool BootSimulatorResult { get; set; } = true;
	public bool ShutdownSimulatorResult { get; set; } = true;
	public bool DeleteSimulatorResult { get; set; } = true;
	public string? CreateSimulatorResult { get; set; } = "new-udid";
	public bool EraseSimulatorResult { get; set; } = true;
	public bool InstallAppResult { get; set; } = true;
	public bool UninstallAppResult { get; set; } = true;
	public bool LaunchAppResult { get; set; } = true;
	public bool TerminateAppResult { get; set; } = true;
	public string? GetAppContainerResult { get; set; } = "/path/to/container";

	public bool SetPrivacyResult { get; set; } = true;
	public bool SetAppearanceResult { get; set; } = true;
	public SimulatorAppearance? GetAppearanceResult { get; set; } = SimulatorAppearance.Light;
	public bool OverrideStatusBarResult { get; set; } = true;
	public bool ClearStatusBarResult { get; set; } = true;
	public bool OpenUrlResult { get; set; } = true;
	public bool PushNotificationResult { get; set; } = true;
	public bool SetLocationResult { get; set; } = true;
	public bool ClearLocationResult { get; set; } = true;
	public bool RunLocationResult { get; set; } = true;
	public bool AddMediaResult { get; set; } = true;
	public bool ScreenshotResult { get; set; } = true;
	public IDisposable? StartRecordingResult { get; set; } = new NoopDisposable();

	// --- Call tracking ---

	public List<string> SelectedXcodePaths { get; } = new();
	public List<string> BootedSimulators { get; } = new();
	public List<string> ShutdownSimulators { get; } = new();
	public List<string> DeletedSimulators { get; } = new();
	public List<(string Name, string DeviceType, string? Runtime)> CreatedSimulators { get; } = new();
	public List<(IEnumerable<string>? Platforms, bool DryRun)> InstallCalls { get; } = new();
	public List<string> ErasedSimulators { get; } = new();
	public List<(string Udid, string AppPath)> InstalledApps { get; } = new();
	public List<(string Udid, string BundleId)> UninstalledApps { get; } = new();
	public List<(string Udid, string BundleId, string[] Args)> LaunchedApps { get; } = new();
	public List<(string Udid, string BundleId)> TerminatedApps { get; } = new();
	public List<(string Udid, string BundleId, string? ContainerType)> GetAppContainerCalls { get; } = new();
	public List<(string Action, string Udid, PrivacyPermission Permission, string? BundleId)> PrivacyCalls { get; } = new();
	public List<(string Udid, SimulatorAppearance Appearance)> SetAppearanceCalls { get; } = new();
	public List<string> GetAppearanceCalls { get; } = new();
	public List<(string Udid, StatusBarOverrides Overrides)> StatusBarOverrideCalls { get; } = new();
	public List<string> StatusBarClearCalls { get; } = new();
	public List<(string Udid, string Url)> OpenUrlCalls { get; } = new();
	public List<(string Udid, string BundleId, string Payload)> PushCalls { get; } = new();
	public List<(string Udid, double Lat, double Lng)> SetLocationCalls { get; } = new();
	public List<string> ClearLocationCalls { get; } = new();
	public List<(string Udid, string GpxPath)> RunLocationCalls { get; } = new();
	public List<(string Udid, List<string> Paths)> AddMediaCalls { get; } = new();
	public List<(string Udid, string OutputPath, ScreenshotFormat Format)> ScreenshotCalls { get; } = new();
	public List<(string Udid, string OutputPath, RecordingOptions? Options)> StartRecordingCalls { get; } = new();

	// --- IAppleProvider implementation ---

	public List<XcodeInstallation> GetXcodeInstallations() => XcodeInstallations;

	public XcodeInstallation? GetSelectedXcode() => SelectedXcode;

	public bool SelectXcode(string path)
	{
		SelectedXcodePaths.Add(path);
		return SelectXcodeResult;
	}

	public CommandLineToolsStatus GetCommandLineToolsStatus() => CltStatus;

	public List<RuntimeInfo> GetRuntimes(string? platform = null, bool availableOnly = false)
	{
		var result = Runtimes;
		if (platform is not null)
			result = result.Where(r => string.Equals(r.Platform, platform, StringComparison.OrdinalIgnoreCase)).ToList();
		if (availableOnly)
			result = result.Where(r => r.IsAvailable).ToList();
		return result;
	}

	public List<SimulatorInfo> GetSimulators(bool availableOnly = false)
	{
		return availableOnly ? Simulators.Where(s => s.IsAvailable).ToList() : Simulators;
	}

	public bool BootSimulator(string udidOrName)
	{
		BootedSimulators.Add(udidOrName);
		return BootSimulatorResult;
	}

	public void OpenSimulatorApp() { }

	public bool ShutdownSimulator(string udidOrName)
	{
		ShutdownSimulators.Add(udidOrName);
		return ShutdownSimulatorResult;
	}

	public bool DeleteSimulator(string udidOrName)
	{
		DeletedSimulators.Add(udidOrName);
		return DeleteSimulatorResult;
	}

	public string? CreateSimulator(string name, string deviceTypeIdentifier, string? runtimeIdentifier = null)
	{
		CreatedSimulators.Add((name, deviceTypeIdentifier, runtimeIdentifier));
		return CreateSimulatorResult;
	}

	public bool EraseSimulator(string udidOrName)
	{
		ErasedSimulators.Add(udidOrName);
		return EraseSimulatorResult;
	}

	public bool InstallApp(string udid, string appBundlePath)
	{
		InstalledApps.Add((udid, appBundlePath));
		return InstallAppResult;
	}

	public bool UninstallApp(string udid, string bundleIdentifier)
	{
		UninstalledApps.Add((udid, bundleIdentifier));
		return UninstallAppResult;
	}

	public bool LaunchApp(string udid, string bundleIdentifier, params string[] extraArgs)
	{
		LaunchedApps.Add((udid, bundleIdentifier, extraArgs));
		return LaunchAppResult;
	}

	public bool TerminateApp(string udid, string bundleIdentifier)
	{
		TerminatedApps.Add((udid, bundleIdentifier));
		return TerminateAppResult;
	}

	public string? GetAppContainer(string udid, string bundleIdentifier, string? containerType = null)
	{
		GetAppContainerCalls.Add((udid, bundleIdentifier, containerType));
		return GetAppContainerResult;
	}

	public bool SetPrivacy(string action, string udid, PrivacyPermission permission, string? bundleIdentifier = null)
	{
		PrivacyCalls.Add((action, udid, permission, bundleIdentifier));
		return SetPrivacyResult;
	}

	public bool SetAppearance(string udid, SimulatorAppearance appearance)
	{
		SetAppearanceCalls.Add((udid, appearance));
		return SetAppearanceResult;
	}

	public SimulatorAppearance? GetAppearance(string udid)
	{
		GetAppearanceCalls.Add(udid);
		return GetAppearanceResult;
	}

	public bool OverrideStatusBar(string udid, StatusBarOverrides overrides)
	{
		StatusBarOverrideCalls.Add((udid, overrides));
		return OverrideStatusBarResult;
	}

	public bool ClearStatusBar(string udid)
	{
		StatusBarClearCalls.Add(udid);
		return ClearStatusBarResult;
	}

	public bool OpenUrl(string udid, string url)
	{
		OpenUrlCalls.Add((udid, url));
		return OpenUrlResult;
	}

	public bool PushNotification(string udid, string bundleIdentifier, string payloadJsonOrPath)
	{
		PushCalls.Add((udid, bundleIdentifier, payloadJsonOrPath));
		return PushNotificationResult;
	}

	public bool SetLocation(string udid, double latitude, double longitude)
	{
		SetLocationCalls.Add((udid, latitude, longitude));
		return SetLocationResult;
	}

	public bool ClearLocation(string udid)
	{
		ClearLocationCalls.Add(udid);
		return ClearLocationResult;
	}

	public bool RunLocation(string udid, string gpxPath)
	{
		RunLocationCalls.Add((udid, gpxPath));
		return RunLocationResult;
	}

	public bool AddMedia(string udid, IEnumerable<string> paths)
	{
		AddMediaCalls.Add((udid, paths.ToList()));
		return AddMediaResult;
	}

	public bool Screenshot(string udid, string outputPath, ScreenshotFormat format = ScreenshotFormat.Png)
	{
		ScreenshotCalls.Add((udid, outputPath, format));
		return ScreenshotResult;
	}

	public IDisposable? StartRecording(string udid, string outputPath, RecordingOptions? options = null)
	{
		StartRecordingCalls.Add((udid, outputPath, options));
		return StartRecordingResult;
	}

	public List<HealthCheck> CheckHealth() => HealthChecks;

	public Task<AppleInstallResult> InstallEnvironmentAsync(IEnumerable<string>? platforms = null, bool dryRun = false, CancellationToken cancellationToken = default)
	{
		InstallCalls.Add((platforms, dryRun));
		return Task.FromResult(InstallResult);
	}

	public List<Device> GetDevices() => Devices;

	sealed class NoopDisposable : IDisposable
	{
		public void Dispose() { }
	}
}
