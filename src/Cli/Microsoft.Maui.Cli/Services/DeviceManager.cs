// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui.Cli.Errors;
using Microsoft.Maui.Cli.Models;
using Microsoft.Maui.Cli.Providers.Android;
using Microsoft.Maui.Cli.Providers.Apple;
using Microsoft.Maui.Cli.Utils;
using System.Text.Json.Nodes;

namespace Microsoft.Maui.Cli.Services;

/// <summary>
/// Manages devices across all platforms.
/// </summary>
public class DeviceManager : IDeviceManager
{
	readonly IAndroidProvider? _androidProvider;
	readonly IAppleProvider? _appleProvider;

	public DeviceManager(IAndroidProvider? androidProvider = null, IAppleProvider? appleProvider = null)
	{
		_androidProvider = androidProvider;
		_appleProvider = appleProvider;
	}

	public async Task<IReadOnlyList<Device>> GetAllDevicesAsync(CancellationToken cancellationToken = default)
	{
		var devices = new List<Device>();

		// Get Android devices
		if (_androidProvider != null)
		{
			var androidDevices = await _androidProvider.GetDevicesAsync(cancellationToken);
			devices.AddRange(androidDevices);

			// Also get AVDs (virtual devices that may not be running)
			var avds = await _androidProvider.GetAvdsAsync(cancellationToken);

			// Track which running emulator entries we've already merged with an
			// AVD, so we can pair "offline" emulator-NNNN serials (whose AVD name
			// has not yet been resolved by adb) with locked AVDs as a fallback.
			var mergedIndices = new HashSet<int>();

			foreach (var avd in avds)
			{
				// First pass: match by AVD name (requires adb to have resolved it,
				// which only happens once the device is fully online).
				var runningIndex = devices.FindIndex(d =>
					d.Platforms.Contains("android") &&
					d.IsEmulator &&
					(
						(d.Details != null &&
						 d.Details.TryGetPropertyValue("avd", out var avdName) &&
						 string.Equals(avdName?.ToString(), avd.Name, StringComparison.OrdinalIgnoreCase))
						||
						string.Equals(d.EmulatorId, avd.Name, StringComparison.OrdinalIgnoreCase)
					));

				// Second pass: if this AVD has an active lock file it is currently
				// starting / booting. Pair it with the first unmerged offline
				// emulator-* serial that has no resolved AVD name — that is the
				// device produced by this AVD's qemu instance.
				//
				// Known limitation: when multiple AVDs are booted at exactly the
				// same moment, pairing happens by iteration order and names can
				// be swapped across the resulting entries. We accept this rather
				// than leaving duplicates, and a future improvement could read
				// the emulator console port from the lock files to disambiguate.
				if (runningIndex < 0 && avd.IsLocked)
				{
					for (var i = 0; i < devices.Count; i++)
					{
						if (mergedIndices.Contains(i))
							continue;

						var d = devices[i];
						if (!d.Platforms.Contains("android") || !d.IsEmulator)
							continue;

						// Only pair with serials adb currently reports as Offline —
						// an online/Booted/Connected emulator with a transient empty
						// AVD name must not be hijacked by a locked AVD.
						if (d.State != DeviceState.Offline)
							continue;

						var hasAvdName =
							(d.Details != null &&
							 d.Details.TryGetPropertyValue("avd", out var existingAvd) &&
							 !string.IsNullOrEmpty(existingAvd?.ToString())) ||
							!string.IsNullOrEmpty(d.EmulatorId);
						if (hasAvdName)
							continue;

						runningIndex = i;
						break;
					}
				}

				// Extract metadata from system image path (e.g., "system-images;android-35;google_apis_playstore;arm64-v8a")
				var (apiLevel, tagId, abi) = ParseSystemImage(avd.SystemImage);
				var playStoreEnabled = tagId?.Contains("playstore", StringComparison.OrdinalIgnoreCase) ?? false;

				if (runningIndex >= 0)
				{
					// Merge AVD metadata into the running emulator device
					var running = devices[runningIndex];
					var subModel = AndroidEnvironment.MapTagIdToSubModel(tagId, playStoreEnabled);
					var details = running.Details?.DeepClone() as JsonObject ?? new JsonObject();
					details["avd"] = avd.Name;
					details["tag_id"] = tagId ?? "default";
					details["target"] = avd.Target ?? "unknown";

					// If the running device didn't have an AVD-resolved name yet
					// (e.g. still offline/booting), prefer the AVD name as the
					// display name while keeping the adb serial as the Id so
					// subsequent adb commands still work.
					var displayName = string.IsNullOrEmpty(running.EmulatorId) ? avd.Name : running.Name;
					var state = running.State == DeviceState.Offline && avd.IsLocked
						? DeviceState.Booting
						: running.State;

					// A locked AVD paired with an adb-visible serial means qemu
					// is alive. Keep IsRunning in sync with the promoted State
					// so we never surface "Booting + IsRunning=false", which is
					// internally inconsistent and confuses consumers.
					var isRunning = running.IsRunning
						|| state == DeviceState.Booting
						|| state == DeviceState.Booted
						|| state == DeviceState.Connected;

					devices[runningIndex] = running with
					{
						Name = displayName,
						EmulatorId = avd.Name,
						Model = string.IsNullOrWhiteSpace(avd.DeviceProfile) ? running.Model : avd.DeviceProfile,
						Manufacturer = !string.IsNullOrWhiteSpace(avd.Manufacturer)
							? avd.Manufacturer
							: (running.Manufacturer ?? "Google"),
						SubModel = subModel,
						State = state,
						IsRunning = isRunning,
						Details = details
					};
					mergedIndices.Add(runningIndex);
				}
				else
				{
					var architecture = AndroidEnvironment.MapAbiToArchitecture(abi) ?? (PlatformDetector.IsArm64 ? "arm64" : "x64");
					var resolvedAbi = abi ?? (PlatformDetector.IsArm64 ? "arm64-v8a" : "x86_64");
					var versionName = AndroidEnvironment.MapApiLevelToVersion(apiLevel);
					var subModel = AndroidEnvironment.MapTagIdToSubModel(tagId, playStoreEnabled);

					// Lock files signal that the emulator qemu process is alive.
					// They cannot distinguish "still booting" from "fully
					// booted"; only adb can, via the Offline -> Online
					// transition surfaced by the merge path above. When we
					// reach this else branch adb listed no serial for this
					// AVD - almost always because the emulator is in the
					// early boot window before adb has registered it.
					// Report Booting so users know it's not yet ready; on
					// the next refresh the merge path will take over and
					// surface Booted once adb is online.
					//
					// The less common cause is a healthy emulator with a
					// broken adb server (blind to a fully booted device).
					// That is a user-environment issue (restart adb) rather
					// than something we can reliably detect from lock files.
					// IsRunning stays false in this branch even when the AVD
					// is locked: we have no adb serial, so the entry is NOT
					// addressable via `adb -s <id>`. Consumers like the
					// profile command filter on IsRunning and then pass
					// device.Id to adb — marking this IsRunning=true would
					// let `maui profile --device <avd_name>` auto-select an
					// un-addressable target and fail downstream.
					devices.Add(new Device
					{
						Id = avd.Name,
						Name = avd.Name,
						Platforms = new[] { "android" },
						Type = DeviceType.Emulator,
						State = avd.IsLocked ? DeviceState.Booting : DeviceState.Shutdown,
						IsEmulator = true,
						IsRunning = false,
						ConnectionType = Models.ConnectionType.Local,
						EmulatorId = avd.Name,
						Model = avd.DeviceProfile,
						SubModel = subModel,
						Manufacturer = avd.Manufacturer ?? "Google",
						Version = apiLevel,
						VersionName = versionName,
						Architecture = architecture,
						PlatformArchitecture = resolvedAbi,
						RuntimeIdentifiers = AndroidEnvironment.GetRuntimeIdentifiers(architecture),
						Idiom = DeviceIdiom.Phone,
						Details = new JsonObject
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
		}

		// Get Apple devices (simulators) when on macOS
		if (_appleProvider != null)
		{
			var appleDevices = _appleProvider.GetDevices();
			devices.AddRange(appleDevices);
		}

		// TODO: Get Windows devices when WindowsProvider is implemented

		return devices;
	}

	public async Task<IReadOnlyList<Device>> GetDevicesByPlatformAsync(string platform, CancellationToken cancellationToken = default)
	{
		var allDevices = await GetAllDevicesAsync(cancellationToken);
		return allDevices.Where(d => d.Platforms.Any(p => p.Equals(platform, StringComparison.OrdinalIgnoreCase))).ToList();
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
}
