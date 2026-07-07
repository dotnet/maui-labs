// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui.Cli.Models;

namespace Microsoft.Maui.Cli.Providers.Android;

/// <summary>
/// Enriches <see cref="Device"/> entries produced from <c>adb devices</c> with the
/// device metadata that <c>adb shell getprop</c> reports (CPU ABI, .NET architecture,
/// runtime identifiers, OS version, manufacturer, model). The same enrichment the
/// legacy ServiceHub <c>AndroidDeviceManager.PopulateDeviceAsync</c> performed.
///
/// Without this, USB-attached physical devices surface with empty
/// <see cref="Device.PlatformArchitecture"/>, <see cref="Device.Architecture"/>,
/// <see cref="Device.RuntimeIdentifiers"/>, <see cref="Device.Version"/>,
/// <see cref="Device.VersionName"/> and <see cref="Device.Manufacturer"/> — which
/// breaks downstream consumers (e.g. the VS Code MAUI debugger builds an
/// <c>assets/&lt;arch&gt;/</c> path that collapses to <c>assets//</c>).
/// </summary>
internal static class AndroidDeviceEnricher
{
	/// <summary>
	/// Delegate that returns the value of an adb shell property for a given
	/// device serial, or <c>null</c> when the property is unset / unreachable.
	/// Modelled after <c>AdbRunner.GetShellPropertyAsync(serial, name, ct)</c>.
	/// </summary>
	public delegate Task<string?> GetPropertyAsync(string serial, string propertyName, CancellationToken cancellationToken);

	/// <summary>
	/// Returns an enriched copy of <paramref name="devices"/>. Only devices that
	/// are addressable via adb (<see cref="DeviceState.Connected"/> /
	/// <see cref="DeviceState.Booted"/>) and report the <c>android</c> platform
	/// are queried; everything else passes through unchanged.
	/// </summary>
	public static async Task<List<Device>> EnrichAsync(
		IReadOnlyList<Device> devices,
		GetPropertyAsync getProperty,
		CancellationToken cancellationToken = default)
	{
		var result = devices.ToList();

		// Enrich addressable devices in parallel — typical adb topologies hold
		// 1-3 devices and each getprop round-trip is in the tens of ms, so we
		// gain meaningful latency by overlapping work across devices.
		var enrichTasks = new List<Task<(int Index, Device Enriched)>>();
		for (var i = 0; i < result.Count; i++)
		{
			var device = result[i];
			if (!device.Platforms.Contains("android"))
				continue;
			if (device.State != DeviceState.Connected && device.State != DeviceState.Booted)
				continue;
			if (string.IsNullOrEmpty(device.Id))
				continue;

			var index = i;
			enrichTasks.Add(EnrichOneAsync(index, device, getProperty, cancellationToken));
		}

		if (enrichTasks.Count == 0)
			return result;

		var enriched = await Task.WhenAll(enrichTasks);
		foreach (var (index, device) in enriched)
			result[index] = device;

		return result;
	}

	static async Task<(int Index, Device Enriched)> EnrichOneAsync(
		int index,
		Device device,
		GetPropertyAsync getProperty,
		CancellationToken cancellationToken)
	{
		// Read every property in parallel — they are independent, and the per-device
		// property set is small and fixed (≤10 props), so the wall-clock cost is
		// dominated by a few adb round-trips rather than process spawn overhead.
		// Note: adb does NOT serialise commands per-device — each ReadPropAsync
		// spawns its own `adb -s <serial> shell getprop` subprocess. A future
		// optimisation could issue a single `adb shell getprop` and parse the
		// `[key]: [value]` dump, eliminating per-property process overhead; see
		// https://github.com/dotnet/android-tools/issues/384.
		var abi = ReadPropAsync(getProperty, device.Id, "ro.product.cpu.abi", cancellationToken);
		var abiList = ReadPropAsync(getProperty, device.Id, "ro.product.cpu.abilist", cancellationToken);
		var sdk = ReadPropAsync(getProperty, device.Id, "ro.build.version.sdk", cancellationToken);
		var sdkFallback = ReadPropAsync(getProperty, device.Id, "ro.product.build.version.sdk", cancellationToken);
		var release = ReadPropAsync(getProperty, device.Id, "ro.build.version.release", cancellationToken);
		var releaseFallback = ReadPropAsync(getProperty, device.Id, "ro.product.build.version.release", cancellationToken);
		var manufacturer = ReadPropAsync(getProperty, device.Id, "ro.product.manufacturer", cancellationToken);
		var brand = ReadPropAsync(getProperty, device.Id, "ro.product.brand", cancellationToken);
		var model = ReadPropAsync(getProperty, device.Id, "ro.product.model", cancellationToken);
		var productName = ReadPropAsync(getProperty, device.Id, "ro.product.name", cancellationToken);

		await Task.WhenAll(abi, abiList, sdk, sdkFallback, release, releaseFallback, manufacturer, brand, model, productName);

		var resolvedAbi = FirstNonEmpty(abi.Result, FirstAbi(abiList.Result));
		var architecture = AndroidEnvironment.MapAbiToArchitecture(resolvedAbi);
		var runtimeIdentifiers = AndroidEnvironment.GetRuntimeIdentifiers(architecture);
		var version = FirstNonEmpty(sdk.Result, sdkFallback.Result);
		var versionName = FirstNonEmpty(release.Result, releaseFallback.Result);
		var manufacturerValue = FirstNonEmpty(manufacturer.Result, brand.Result);
		var modelValue = FirstNonEmpty(model.Result, productName.Result);

		// Honour existing values when getprop returns nothing — we never want
		// enrichment to *remove* data the caller already populated (e.g. AVD
		// merge for emulators that pre-populates Model/Manufacturer).
		var enriched = device with
		{
			PlatformArchitecture = resolvedAbi ?? device.PlatformArchitecture,
			Architecture = architecture ?? device.Architecture,
			RuntimeIdentifiers = runtimeIdentifiers ?? device.RuntimeIdentifiers,
			Version = version ?? device.Version,
			VersionName = versionName ?? device.VersionName,
			Manufacturer = manufacturerValue ?? device.Manufacturer,
			Model = modelValue ?? device.Model,
		};

		return (index, enriched);
	}

	static async Task<string?> ReadPropAsync(
		GetPropertyAsync getProperty,
		string serial,
		string property,
		CancellationToken cancellationToken)
	{
		try
		{
			var raw = await getProperty(serial, property, cancellationToken);
			return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			// Caller-initiated cancellation (Ctrl+C, etc.) must propagate so the
			// whole enrichment aborts. Cancellation originating elsewhere — e.g. a
			// future internal timeout CTS inside AdbRunner — falls through to the
			// general catch below so a single slow device doesn't kill the batch.
			throw;
		}
		catch (Exception ex)
		{
			System.Diagnostics.Trace.WriteLine($"getprop {property} failed for {serial}: {ex.Message}");
			return null;
		}
	}

	static string? FirstNonEmpty(string? a, string? b)
		=> !string.IsNullOrWhiteSpace(a) ? a : (!string.IsNullOrWhiteSpace(b) ? b : null);

	static string? FirstAbi(string? abiList)
	{
		if (string.IsNullOrWhiteSpace(abiList))
			return null;
		var first = abiList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.FirstOrDefault();
		return string.IsNullOrWhiteSpace(first) ? null : first;
	}
}
