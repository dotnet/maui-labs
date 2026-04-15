// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui.Cli.Errors;
using Microsoft.Maui.Cli.Models;
using Microsoft.Maui.Cli.Providers.Android;
using Microsoft.Maui.Cli.Utils;
using System.Text.Json;

namespace Microsoft.Maui.Cli.Services;

/// <summary>
/// Manages devices across all platforms.
/// </summary>
public class DeviceManager : IDeviceManager
{
	readonly IAndroidProvider? _androidProvider;
	readonly Func<CancellationToken, Task<IReadOnlyList<Device>>>? _appleDeviceProvider;

	public DeviceManager(
		IAndroidProvider? androidProvider = null,
		Func<CancellationToken, Task<IReadOnlyList<Device>>>? appleDeviceProvider = null)
	{
		_androidProvider = androidProvider;
		_appleDeviceProvider = appleDeviceProvider;
	}

	public async Task<IReadOnlyList<Device>> GetAllDevicesAsync(CancellationToken cancellationToken = default)
	{
		var devices = new List<Device>();

		devices.AddRange(await GetAndroidDevicesAsync(cancellationToken));

		devices.AddRange(await GetAppleDevicesAsync(cancellationToken));

		// TODO: Get Windows devices when WindowsProvider is implemented

		return devices;
	}

	public async Task<IReadOnlyList<Device>> GetDevicesByPlatformAsync(string platform, CancellationToken cancellationToken = default)
	{
		return Platforms.Normalize(platform) switch
		{
			Platforms.Android => await GetAndroidDevicesAsync(cancellationToken),
			Platforms.iOS => await GetAppleDevicesAsync(cancellationToken),
			Platforms.All => await GetAllDevicesAsync(cancellationToken),
			_ => []
		};
	}

	public async Task<Device?> GetDeviceByIdAsync(string deviceId, CancellationToken cancellationToken = default)
	{
		var allDevices = await GetAllDevicesAsync(cancellationToken);
		return allDevices.FirstOrDefault(d => d.Id.Equals(deviceId, StringComparison.OrdinalIgnoreCase));
	}

	public async Task<Device> GetRunningDeviceOrThrowAsync(CancellationToken cancellationToken = default)
	{
		var devices = await GetAllDevicesAsync(cancellationToken);
		var runningDevice = devices.FirstOrDefault(d => d.IsRunning);

		if (runningDevice == null)
		{
			throw new MauiToolException(
				ErrorCodes.DeviceNotFound,
				"No running device found. Start a device or specify one with --device");
		}

		return runningDevice;
	}

	/// <summary>
	/// Parses a system image path like "system-images;android-35;google_apis_playstore;arm64-v8a"
	/// to extract API level, tag ID, and ABI.
	/// </summary>
	static (string? ApiLevel, string? TagId, string? Abi) ParseSystemImage(string? systemImage)
	{
		if (string.IsNullOrEmpty(systemImage))
			return (null, null, null);

		var parts = systemImage.Split(';', '/');
		string? apiLevel = null;
		string? tagId = null;
		string? abi = null;

		foreach (var part in parts)
		{
			if (part.StartsWith("android-", StringComparison.OrdinalIgnoreCase))
				apiLevel = part.Substring("android-".Length);
			else if (part.Contains("google_apis", StringComparison.OrdinalIgnoreCase) || part == "default")
				tagId = part;
			else if (part is "arm64-v8a" or "x86_64" or "x86" or "armeabi-v7a")
				abi = part;
		}

		return (apiLevel, tagId, abi);
	}

	async Task<IReadOnlyList<Device>> GetAndroidDevicesAsync(CancellationToken cancellationToken)
	{
		if (_androidProvider is null)
			return [];

		var devices = new List<Device>();
		var androidDevices = await _androidProvider.GetDevicesAsync(cancellationToken);
		devices.AddRange(androidDevices);

		// Also get AVDs (virtual devices that may not be running)
		var avds = await _androidProvider.GetAvdsAsync(cancellationToken);
		foreach (var avd in avds)
		{
			// Check if this AVD is already in the running devices list
			// Match by AVD name in details dict or by EmulatorId
			var runningIndex = devices.FindIndex(d =>
				d.Platforms.Contains("android") &&
				d.IsEmulator &&
				(
					(d.Details != null &&
					 d.Details.TryGetValue("avd", out var avdName) &&
					 string.Equals(avdName?.ToString(), avd.Name, StringComparison.OrdinalIgnoreCase))
					||
					string.Equals(d.EmulatorId, avd.Name, StringComparison.OrdinalIgnoreCase)
				));

			// Extract metadata from system image path (e.g., "system-images;android-35;google_apis_playstore;arm64-v8a")
			var (apiLevel, tagId, abi) = ParseSystemImage(avd.SystemImage);
			var playStoreEnabled = tagId?.Contains("playstore", StringComparison.OrdinalIgnoreCase) ?? false;

			if (runningIndex >= 0)
			{
				// Merge AVD metadata into the running emulator device
				var running = devices[runningIndex];
				var subModel = AndroidEnvironment.MapTagIdToSubModel(tagId, playStoreEnabled);
				var details = running.Details != null
					? new Dictionary<string, object>(running.Details)
					: new Dictionary<string, object>();
				details["tag_id"] = tagId ?? "default";
				details["target"] = avd.Target ?? "unknown";

				devices[runningIndex] = running with
				{
					EmulatorId = avd.Name,
					SubModel = subModel,
					Details = details
				};
			}
			else
			{
				var architecture = AndroidEnvironment.MapAbiToArchitecture(abi) ?? (PlatformDetector.IsArm64 ? "arm64" : "x64");
				var resolvedAbi = abi ?? (PlatformDetector.IsArm64 ? "arm64-v8a" : "x86_64");
				var versionName = AndroidEnvironment.MapApiLevelToVersion(apiLevel);
				var subModel = AndroidEnvironment.MapTagIdToSubModel(tagId, playStoreEnabled);
				devices.Add(new Device
				{
					Id = avd.Name,
					Name = avd.Name,
					Platforms = ["android"],
					Type = DeviceType.Emulator,
					State = DeviceState.Shutdown,
					IsEmulator = true,
					IsRunning = false,
					ConnectionType = Models.ConnectionType.Local,
					EmulatorId = avd.Name,
					Model = avd.DeviceProfile,
					SubModel = subModel,
					Manufacturer = "Google",
					Version = apiLevel,
					VersionName = versionName,
					Architecture = architecture,
					PlatformArchitecture = resolvedAbi,
					RuntimeIdentifiers = AndroidEnvironment.GetRuntimeIdentifiers(architecture),
					Idiom = DeviceIdiom.Phone,
					Details = new Dictionary<string, object>
					{
						["avd"] = avd.Name,
						["target"] = avd.Target ?? "unknown",
						["api_level"] = apiLevel ?? "unknown",
						["abi"] = resolvedAbi,
						["tag_id"] = tagId ?? "default"
					}
				});
			}
		}

		return devices;
	}

	async Task<IReadOnlyList<Device>> GetAppleDevicesAsync(CancellationToken cancellationToken)
		=> _appleDeviceProvider is not null
			? await _appleDeviceProvider(cancellationToken)
			: await GetAppleSimulatorDevicesAsync(cancellationToken);

	internal static async Task<IReadOnlyList<Device>> GetAppleSimulatorDevicesAsync(CancellationToken cancellationToken = default)
	{
		if (!OperatingSystem.IsMacOS())
			return [];

		var xcrunPath = ProcessRunner.GetCommandPath("xcrun");
		if (string.IsNullOrWhiteSpace(xcrunPath))
			return [];

		var result = await ProcessRunner.RunAsync(
			xcrunPath,
			["simctl", "list", "devices", "available", "-j"],
			timeout: TimeSpan.FromSeconds(15),
			cancellationToken: cancellationToken);

		if (!result.Success || string.IsNullOrWhiteSpace(result.StandardOutput))
			return [];

		return ParseAppleSimulatorDevices(result.StandardOutput);
	}

	internal static IReadOnlyList<Device> ParseAppleSimulatorDevices(string json)
	{
		if (string.IsNullOrWhiteSpace(json))
			return [];

		using var document = JsonDocument.Parse(json);
		if (!document.RootElement.TryGetProperty("devices", out var devicesByRuntime) || devicesByRuntime.ValueKind != JsonValueKind.Object)
			return [];

		var architecture = PlatformDetector.IsArm64 ? "arm64" : "x64";
		var runtimeIdentifier = PlatformDetector.IsArm64 ? "iossimulator-arm64" : "iossimulator-x64";
		var devices = new List<Device>();

		foreach (var runtime in devicesByRuntime.EnumerateObject())
		{
			if (!runtime.Name.Contains("iOS", StringComparison.OrdinalIgnoreCase) || runtime.Value.ValueKind != JsonValueKind.Array)
				continue;

			var version = TryParseAppleRuntimeVersion(runtime.Name);
			foreach (var simulator in runtime.Value.EnumerateArray())
			{
				if (simulator.TryGetProperty("isAvailable", out var isAvailableElement) &&
					isAvailableElement.ValueKind == JsonValueKind.False)
				{
					continue;
				}

				var udid = simulator.TryGetProperty("udid", out var udidElement) ? udidElement.GetString() : null;
				var name = simulator.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
				var state = simulator.TryGetProperty("state", out var stateElement) ? stateElement.GetString() : null;
				if (string.IsNullOrWhiteSpace(udid) || string.IsNullOrWhiteSpace(name))
					continue;

				var details = new Dictionary<string, object>
				{
					["runtime"] = runtime.Name
				};

				if (simulator.TryGetProperty("deviceTypeIdentifier", out var deviceTypeElement) &&
					!string.IsNullOrWhiteSpace(deviceTypeElement.GetString()))
				{
					details["device_type"] = deviceTypeElement.GetString()!;
				}

				if (!string.IsNullOrWhiteSpace(state))
					details["state"] = state!;

				devices.Add(new Device
				{
					Id = udid,
					Name = name,
					Platforms = [Platforms.iOS],
					Type = DeviceType.Simulator,
					State = ParseAppleSimulatorState(state),
					IsEmulator = true,
					IsRunning = string.Equals(state, "Booted", StringComparison.OrdinalIgnoreCase),
					ConnectionType = Models.ConnectionType.Local,
					EmulatorId = udid,
					Model = name,
					Manufacturer = "Apple",
					Version = version,
					VersionName = version is null ? null : $"iOS {version}",
					Architecture = architecture,
					PlatformArchitecture = architecture,
					RuntimeIdentifiers = [runtimeIdentifier],
					Idiom = InferAppleIdiom(name),
					Details = details
				});
			}
		}

		return devices
			.OrderByDescending(device => device.IsRunning)
			.ThenBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	static DeviceState ParseAppleSimulatorState(string? state) => state?.ToLowerInvariant() switch
	{
		"booted" => DeviceState.Booted,
		"shutdown" => DeviceState.Shutdown,
		"booting" => DeviceState.Booting,
		"shuttingdown" or "shutting down" => DeviceState.ShuttingDown,
		_ => DeviceState.Unknown
	};

	static string InferAppleIdiom(string name) =>
		name.Contains("iPad", StringComparison.OrdinalIgnoreCase)
			? DeviceIdiom.Tablet
			: DeviceIdiom.Phone;

	static string? TryParseAppleRuntimeVersion(string runtimeName)
	{
		var markerIndex = runtimeName.IndexOf("iOS-", StringComparison.OrdinalIgnoreCase);
		if (markerIndex < 0)
			return null;

		var rawVersion = runtimeName[(markerIndex + "iOS-".Length)..];
		return rawVersion.Replace('-', '.');
	}
}
