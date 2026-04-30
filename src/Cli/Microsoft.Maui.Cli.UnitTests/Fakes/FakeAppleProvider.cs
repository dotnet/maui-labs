// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui.Cli.Models;
using Microsoft.Maui.Cli.Providers.Apple;

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

	// --- Call tracking ---

	public List<string> SelectedXcodePaths { get; } = new();
	public List<string> BootedSimulators { get; } = new();
	public List<string> ShutdownSimulators { get; } = new();
	public List<string> DeletedSimulators { get; } = new();
	public List<(string Name, string DeviceType, string? Runtime)> CreatedSimulators { get; } = new();
	public List<(IEnumerable<string>? Platforms, bool DryRun)> InstallCalls { get; } = new();
	public List<string> ErasedSimulators { get; } = new();

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

	public List<HealthCheck> CheckHealth() => HealthChecks;

	public Task<AppleInstallResult> InstallEnvironmentAsync(IEnumerable<string>? platforms = null, bool dryRun = false, CancellationToken cancellationToken = default)
	{
		InstallCalls.Add((platforms, dryRun));
		return Task.FromResult(InstallResult);
	}

	public List<Device> GetDevices() => Devices;
}
