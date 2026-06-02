// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json.Nodes;
using Microsoft.Maui.Cli.Models;
using Microsoft.Maui.Cli.Providers.Android;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class AndroidDeviceEnricherTests
{
	static Device PhysicalConnected(string id = "39061FDJH00LYQ", string? model = null) => new()
	{
		Id = id,
		Name = id,
		Platforms = new[] { "android" },
		Type = DeviceType.Physical,
		State = DeviceState.Connected,
		IsEmulator = false,
		IsRunning = true,
		ConnectionType = ConnectionType.Usb,
		Model = model,
	};

	static AndroidDeviceEnricher.GetPropertyAsync MakeProps(Dictionary<string, string?> props) =>
		(serial, name, ct) =>
		{
			props.TryGetValue(name, out var value);
			return Task.FromResult(value);
		};

	[Fact]
	public async Task EnrichAsync_PopulatesAllFields_ForConnectedPhysicalDevice()
	{
		var props = new Dictionary<string, string?>
		{
			["ro.product.cpu.abi"] = "arm64-v8a",
			["ro.build.version.sdk"] = "36",
			["ro.build.version.release"] = "16",
			["ro.product.manufacturer"] = "Google",
			["ro.product.model"] = "Pixel 8",
		};

		var result = await AndroidDeviceEnricher.EnrichAsync(
			new[] { PhysicalConnected() },
			MakeProps(props));

		var device = Assert.Single(result);
		Assert.Equal("arm64-v8a", device.PlatformArchitecture);
		Assert.Equal("arm64", device.Architecture);
		Assert.Equal(new[] { "android-arm64" }, device.RuntimeIdentifiers);
		Assert.Equal("36", device.Version);
		Assert.Equal("16", device.VersionName);
		Assert.Equal("Google", device.Manufacturer);
		Assert.Equal("Pixel 8", device.Model);
	}

	[Fact]
	public async Task EnrichAsync_UsesFallbackProperties_WhenPrimaryUnset()
	{
		// All primary props blank — should fall back to ro.product.cpu.abilist (first),
		// ro.product.build.version.sdk, ro.product.build.version.release, ro.product.brand,
		// ro.product.name.
		var props = new Dictionary<string, string?>
		{
			["ro.product.cpu.abilist"] = "x86_64,arm64-v8a",
			["ro.product.build.version.sdk"] = "33",
			["ro.product.build.version.release"] = "13",
			["ro.product.brand"] = "google",
			["ro.product.name"] = "shiba",
		};

		var result = await AndroidDeviceEnricher.EnrichAsync(
			new[] { PhysicalConnected() },
			MakeProps(props));

		var device = Assert.Single(result);
		Assert.Equal("x86_64", device.PlatformArchitecture);
		Assert.Equal("x64", device.Architecture);
		Assert.Equal(new[] { "android-x64" }, device.RuntimeIdentifiers);
		Assert.Equal("33", device.Version);
		Assert.Equal("13", device.VersionName);
		Assert.Equal("google", device.Manufacturer);
		Assert.Equal("shiba", device.Model);
	}

	[Fact]
	public async Task EnrichAsync_SkipsDisconnectedDevices()
	{
		var called = false;
		AndroidDeviceEnricher.GetPropertyAsync getter = (s, n, ct) => { called = true; return Task.FromResult<string?>(null); };

		var offline = PhysicalConnected() with { State = DeviceState.Offline, IsRunning = false };

		var result = await AndroidDeviceEnricher.EnrichAsync(new[] { offline }, getter);

		Assert.Single(result);
		Assert.False(called, "getprop must not be called for offline devices");
		Assert.Null(result[0].Architecture);
	}

	[Fact]
	public async Task EnrichAsync_SkipsNonAndroidDevices()
	{
		var called = false;
		AndroidDeviceEnricher.GetPropertyAsync getter = (s, n, ct) => { called = true; return Task.FromResult<string?>(null); };

		var ios = new Device
		{
			Id = "ios-udid",
			Name = "iPhone",
			Platforms = new[] { "ios" },
			State = DeviceState.Booted,
			IsEmulator = true,
			IsRunning = true,
		};

		var result = await AndroidDeviceEnricher.EnrichAsync(new[] { ios }, getter);

		Assert.Single(result);
		Assert.False(called);
	}

	[Fact]
	public async Task EnrichAsync_PreservesExistingValues_WhenGetpropReturnsNull()
	{
		// Existing emulator entry from AVD merge has Model/Manufacturer set; if
		// getprop returns null/blank for those fields, do not overwrite them.
		AndroidDeviceEnricher.GetPropertyAsync getter = (s, n, ct) => Task.FromResult<string?>(null);

		var device = PhysicalConnected(model: "pixel_6") with
		{
			Manufacturer = "Google",
			Version = "35",
			VersionName = "15",
			PlatformArchitecture = "arm64-v8a",
			Architecture = "arm64",
			RuntimeIdentifiers = new[] { "android-arm64" },
		};

		var result = await AndroidDeviceEnricher.EnrichAsync(new[] { device }, getter);

		var enriched = Assert.Single(result);
		Assert.Equal("pixel_6", enriched.Model);
		Assert.Equal("Google", enriched.Manufacturer);
		Assert.Equal("35", enriched.Version);
		Assert.Equal("15", enriched.VersionName);
		Assert.Equal("arm64-v8a", enriched.PlatformArchitecture);
	}

	[Fact]
	public async Task EnrichAsync_DoesNotSwallowCancellation()
	{
		using var cts = new CancellationTokenSource();
		cts.Cancel();

		AndroidDeviceEnricher.GetPropertyAsync getter = (s, n, ct) =>
		{
			ct.ThrowIfCancellationRequested();
			return Task.FromResult<string?>(null);
		};

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
			AndroidDeviceEnricher.EnrichAsync(new[] { PhysicalConnected() }, getter, cts.Token));
	}

	[Fact]
	public async Task EnrichAsync_SwallowsPerPropertyFailures()
	{
		// A failing property (e.g. transport error) should not drop the entire
		// device — other properties should still populate.
		AndroidDeviceEnricher.GetPropertyAsync getter = (serial, name, ct) =>
		{
			if (name == "ro.product.manufacturer")
				throw new InvalidOperationException("transport error");
			return Task.FromResult<string?>(name switch
			{
				"ro.product.cpu.abi" => "arm64-v8a",
				"ro.build.version.sdk" => "36",
				"ro.product.model" => "Pixel 8",
				_ => null,
			});
		};

		var result = await AndroidDeviceEnricher.EnrichAsync(new[] { PhysicalConnected() }, getter);

		var device = Assert.Single(result);
		Assert.Equal("arm64", device.Architecture);
		Assert.Equal("36", device.Version);
		Assert.Equal("Pixel 8", device.Model);
		Assert.Null(device.Manufacturer);
	}

	[Fact]
	public async Task EnrichAsync_EnrichesOnlineEmulators()
	{
		// Emulators that are Booted/Connected should also pick up getprop data.
		// The downstream AVD merge in DeviceManager may then override Model and
		// Manufacturer, but architecture/version fields survive.
		var emulator = new Device
		{
			Id = "emulator-5554",
			Name = "emulator-5554",
			Platforms = new[] { "android" },
			Type = DeviceType.Emulator,
			State = DeviceState.Booted,
			IsEmulator = true,
			IsRunning = true,
			EmulatorId = "Pixel_6_API_35",
			Details = new JsonObject { ["avd"] = "Pixel_6_API_35" },
		};

		var props = new Dictionary<string, string?>
		{
			["ro.product.cpu.abi"] = "x86_64",
			["ro.build.version.sdk"] = "35",
			["ro.build.version.release"] = "15",
		};

		var result = await AndroidDeviceEnricher.EnrichAsync(new[] { emulator }, MakeProps(props));

		var device = Assert.Single(result);
		Assert.Equal("x86_64", device.PlatformArchitecture);
		Assert.Equal("x64", device.Architecture);
		Assert.Equal("35", device.Version);
		Assert.Equal("15", device.VersionName);
	}

	[Fact]
	public async Task EnrichAsync_TrimsWhitespaceFromPropertyValues()
	{
		// adb tends to terminate property output with \r\n on some hosts.
		var props = new Dictionary<string, string?>
		{
			["ro.product.cpu.abi"] = "arm64-v8a\r\n",
			["ro.product.model"] = " Pixel 8 \r",
		};

		var result = await AndroidDeviceEnricher.EnrichAsync(
			new[] { PhysicalConnected() },
			MakeProps(props));

		var device = Assert.Single(result);
		Assert.Equal("arm64-v8a", device.PlatformArchitecture);
		Assert.Equal("Pixel 8", device.Model);
	}
}
