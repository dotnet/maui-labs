// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui.Cli.Models;

namespace Microsoft.Maui.Cli.Providers.Apple;

/// <summary>
/// Interface for Apple platform operations (Xcode, simulators, runtimes).
/// Wraps the Xamarin.Apple.Tools.MaciOS package APIs.
/// </summary>
public interface IAppleProvider
{
	/// <summary>
	/// Lists installed Xcode installations.
	/// </summary>
	List<XcodeInstallation> GetXcodeInstallations();

	/// <summary>
	/// Gets the currently selected Xcode installation.
	/// </summary>
	XcodeInstallation? GetSelectedXcode();

	/// <summary>
	/// Selects an Xcode installation by path.
	/// </summary>
	bool SelectXcode(string path);

	/// <summary>
	/// Gets the Command Line Tools installation info.
	/// </summary>
	CommandLineToolsStatus GetCommandLineToolsStatus();

	/// <summary>
	/// Lists simulator runtimes. Optionally filter by platform (e.g., "iOS").
	/// </summary>
	List<RuntimeInfo> GetRuntimes(string? platform = null, bool availableOnly = false);

	/// <summary>
	/// Lists simulator devices. Optionally filters by availability.
	/// </summary>
	List<SimulatorInfo> GetSimulators(bool availableOnly = false);

	/// <summary>
	/// Boots a simulator device.
	/// </summary>
	bool BootSimulator(string udidOrName);

	/// <summary>
	/// Shuts down a simulator device. Pass "all" to shut down all.
	/// </summary>
	bool ShutdownSimulator(string udidOrName);

	/// <summary>
	/// Deletes a simulator device.
	/// </summary>
	bool DeleteSimulator(string udidOrName);

	/// <summary>
	/// Creates a new simulator device.
	/// </summary>
	string? CreateSimulator(string name, string deviceTypeIdentifier, string? runtimeIdentifier = null);

	/// <summary>
	/// Gets the health status of Apple tooling (Xcode, CLT, simulators).
	/// </summary>
	List<HealthCheck> CheckHealth();

	/// <summary>
	/// Lists simulator devices as <see cref="Device"/> models for device manager integration.
	/// </summary>
	List<Device> GetDevices();
}

/// <summary>
/// Information about an Xcode installation.
/// </summary>
public record XcodeInstallation
{
	public required string Path { get; init; }
	public string? Version { get; init; }
	public string? Build { get; init; }
	public bool IsSelected { get; init; }
}

/// <summary>
/// Status of the Xcode Command Line Tools installation.
/// </summary>
public record CommandLineToolsStatus
{
	public bool IsInstalled { get; init; }
	public string? Version { get; init; }
	public string? Path { get; init; }
}

/// <summary>
/// Information about a simulator runtime.
/// </summary>
public record RuntimeInfo
{
	public required string Name { get; init; }
	public required string Identifier { get; init; }
	public string? Platform { get; init; }
	public string? Version { get; init; }
	public string? BuildVersion { get; init; }
	public bool IsAvailable { get; init; }
	public bool IsBundled { get; init; }
}

/// <summary>
/// Information about a simulator device.
/// </summary>
public record SimulatorInfo
{
	public required string Name { get; init; }
	public required string Udid { get; init; }
	public string? State { get; init; }
	public string? Platform { get; init; }
	public string? OSVersion { get; init; }
	public string? RuntimeIdentifier { get; init; }
	public string? DeviceTypeIdentifier { get; init; }
	public bool IsAvailable { get; init; }
	public bool IsBooted { get; init; }
}
