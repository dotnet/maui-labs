// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui.Cli.Models;
using Microsoft.Maui.Cli.Providers.Android;
using Microsoft.Maui.Cli.Services;
using Microsoft.Maui.Cli.UnitTests.Fakes;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class DeviceManagerTests
{
	static DeviceManager CreateManager(FakeAndroidProvider? androidProvider = null, FakeAppleProvider? appleProvider = null) =>
		new(androidProvider, appleProvider ?? new FakeAppleProvider());

	[Fact]
	public async Task GetAllDevicesAsync_ReturnsAndroidDevices()
	{
		// Arrange
		var fakeAndroid = new FakeAndroidProvider
		{
			Devices = new List<Device>
			{
				new Device { Id = "emulator-5554", Name = "Pixel 6", Platforms = new[] { "android" }, Type = DeviceType.Emulator, State = DeviceState.Booted, IsEmulator = true, IsRunning = true }
			}
		};

		var manager = CreateManager(fakeAndroid);

		// Act
		var devices = await manager.GetAllDevicesAsync();

		// Assert
		Assert.Single(devices);
		Assert.Contains(devices, d => d.Platforms.Contains("android"));
	}

	[Fact]
	public async Task GetAllDevicesAsync_ReturnsAppleSimulators()
	{
		// Arrange
		var fakeApple = new FakeAppleProvider
		{
			Devices = new List<Device>
			{
				new Device
				{
					Id = "sim-udid-1234",
					Name = "iPhone 15 Pro",
					Platforms = new[] { "ios" },
					Type = DeviceType.Simulator,
					State = DeviceState.Booted,
					IsEmulator = true,
					IsRunning = true,
					EmulatorId = "sim-udid-1234",
					Version = "18.0"
				}
			}
		};

		var manager = new DeviceManager(appleProvider: fakeApple);

		// Act
		var devices = await manager.GetAllDevicesAsync();

		// Assert
		Assert.Single(devices);
		Assert.Contains(devices, d => d.Platforms.Contains("ios"));
		Assert.Equal(DeviceType.Simulator, devices[0].Type);
	}

	[Fact]
	public async Task GetAllDevicesAsync_ReturnsBothAndroidAndApple()
	{
		// Arrange
		var fakeAndroid = new FakeAndroidProvider
		{
			Devices = new List<Device>
			{
				new Device { Id = "emulator-5554", Name = "Pixel 6", Platforms = new[] { "android" }, Type = DeviceType.Emulator, State = DeviceState.Booted, IsEmulator = true, IsRunning = true }
			}
		};
		var fakeApple = new FakeAppleProvider
		{
			Devices = new List<Device>
			{
				new Device { Id = "sim-udid", Name = "iPhone 15", Platforms = new[] { "ios" }, Type = DeviceType.Simulator, State = DeviceState.Booted, IsEmulator = true, IsRunning = true }
			}
		};

		var manager = new DeviceManager(fakeAndroid, fakeApple);

		// Act
		var devices = await manager.GetAllDevicesAsync();

		// Assert
		Assert.Equal(2, devices.Count);
		Assert.Contains(devices, d => d.Platforms.Contains("android"));
		Assert.Contains(devices, d => d.Platforms.Contains("ios"));
	}

	[Fact]
	public async Task GetDevicesByPlatformAsync_FiltersCorrectly()
	{
		// Arrange
		var fakeAndroid = new FakeAndroidProvider
		{
			Devices = new List<Device>
			{
				new Device { Id = "emulator-5554", Name = "Pixel 6", Platforms = new[] { "android" }, Type = DeviceType.Emulator, State = DeviceState.Booted, IsEmulator = true, IsRunning = true }
			}
		};

		var manager = CreateManager(fakeAndroid);

		// Act
		var androidOnly = await manager.GetDevicesByPlatformAsync("android");

		// Assert
		Assert.Single(androidOnly);
		Assert.All(androidOnly, d => Assert.Contains("android", d.Platforms));
	}

	[Fact]
	public async Task GetDevicesByPlatformAsync_Android_DoesNotQueryAppleProvider()
	{
		var fakeAndroid = new FakeAndroidProvider
		{
			Devices =
			[
				new Device { Id = "emulator-5554", Name = "Pixel 6", Platforms = ["android"], Type = DeviceType.Emulator, State = DeviceState.Booted, IsEmulator = true, IsRunning = true }
			]
		};
		var fakeApple = new FakeAppleProvider
		{
			Devices =
			[
				new Device { Id = "ios-sim", Name = "iPhone", Platforms = [Platforms.iOS], Type = DeviceType.Simulator, State = DeviceState.Booted, IsEmulator = true, IsRunning = true }
			]
		};
		var manager = new DeviceManager(fakeAndroid, fakeApple);

		var devices = await manager.GetDevicesByPlatformAsync(Platforms.Android);

		Assert.Single(devices);
		Assert.Equal(0, fakeApple.GetDevicesCalled);
		Assert.Equal(1, fakeAndroid.GetDevicesCalled);
	}

	[Fact]
	public async Task GetDevicesByPlatformAsync_Ios_DoesNotQueryAndroidProvider()
	{
		var fakeAndroid = new FakeAndroidProvider
		{
			Devices =
			[
				new Device { Id = "emulator-5554", Name = "Pixel 6", Platforms = ["android"], Type = DeviceType.Emulator, State = DeviceState.Booted, IsEmulator = true, IsRunning = true }
			]
		};
		var fakeApple = new FakeAppleProvider
		{
			Devices =
			[
				new Device { Id = "ios-sim", Name = "iPhone", Platforms = [Platforms.iOS], Type = DeviceType.Simulator, State = DeviceState.Booted, IsEmulator = true, IsRunning = true }
			]
		};
		var manager = new DeviceManager(fakeAndroid, fakeApple);

		var devices = await manager.GetDevicesByPlatformAsync(Platforms.iOS);

		Assert.Single(devices);
		Assert.Equal(0, fakeAndroid.GetDevicesCalled);
		Assert.Equal(0, fakeAndroid.GetAvdsCalled);
		Assert.Equal(1, fakeApple.GetDevicesCalled);
		Assert.Equal(Platforms.iOS, devices[0].Platform);
	}

	[Fact]
	public async Task GetDevicesByPlatformAsync_FiltersIosDevices()
	{
		// Arrange
		var fakeAndroid = new FakeAndroidProvider
		{
			Devices = new List<Device>
			{
				new Device { Id = "emulator-5554", Name = "Pixel 6", Platforms = new[] { "android" }, Type = DeviceType.Emulator, State = DeviceState.Booted, IsEmulator = true, IsRunning = true }
			}
		};
		var fakeApple = new FakeAppleProvider
		{
			Devices = new List<Device>
			{
				new Device { Id = "sim-udid", Name = "iPhone 15", Platforms = new[] { "ios" }, Type = DeviceType.Simulator, State = DeviceState.Booted, IsEmulator = true, IsRunning = true }
			}
		};

		var manager = new DeviceManager(fakeAndroid, fakeApple);

		// Act
		var iosOnly = await manager.GetDevicesByPlatformAsync("ios");

		// Assert
		Assert.Single(iosOnly);
		Assert.All(iosOnly, d => Assert.Contains("ios", d.Platforms));
	}

	[Fact]
	public async Task GetDeviceByIdAsync_FindsCorrectDevice()
	{
		// Arrange
		var fakeAndroid = new FakeAndroidProvider
		{
			Devices = new List<Device>
			{
				new Device { Id = "device-1", Name = "Device 1", Platforms = new[] { "android" }, Type = DeviceType.Physical, State = DeviceState.Booted, IsEmulator = false, IsRunning = true },
				new Device { Id = "device-2", Name = "Device 2", Platforms = new[] { "android" }, Type = DeviceType.Emulator, State = DeviceState.Shutdown, IsEmulator = true, IsRunning = false }
			}
		};

		var manager = CreateManager(fakeAndroid);

		// Act
		var device = await manager.GetDeviceByIdAsync("device-2");

		// Assert
		Assert.NotNull(device);
		Assert.Equal("device-2", device.Id);
		Assert.Equal("Device 2", device.Name);
	}

	[Fact]
	public async Task GetDeviceByIdAsync_ReturnsNull_WhenNotFound()
	{
		// Arrange
		var fakeAndroid = new FakeAndroidProvider();
		var manager = CreateManager(fakeAndroid);

		// Act
		var device = await manager.GetDeviceByIdAsync("nonexistent");

		// Assert
		Assert.Null(device);
	}

	[Fact]
	public async Task GetAllDevicesAsync_IncludesShutdownAvds()
	{
		// Arrange
		var fakeAndroid = new FakeAndroidProvider
		{
			Avds = new List<AvdInfo>
			{
				new AvdInfo { Name = "Pixel_6_API_35", Target = "android-35" }
			}
		};

		var manager = CreateManager(fakeAndroid);

		// Act
		var devices = await manager.GetAllDevicesAsync();

		// Assert
		Assert.Single(devices);
		Assert.Equal("Pixel_6_API_35", devices[0].Id);
		Assert.Equal(DeviceState.Shutdown, devices[0].State);
		Assert.Equal(DeviceType.Emulator, devices[0].Type);
	}

	[Fact]
	public async Task GetAllDevicesAsync_MergesRunningEmulatorWithAvd()
	{
		// Arrange: ADB returns a running emulator with AVD name in details
		var fakeAndroid = new FakeAndroidProvider
		{
			Devices = new List<Device>
			{
				new Device
				{
					Id = "emulator-5554",
					Name = "Google sdk_gphone64_arm64",
					Platforms = new[] { "android" },
					Type = DeviceType.Emulator,
					State = DeviceState.Booted,
					IsEmulator = true,
					IsRunning = true,
					EmulatorId = "Pixel_6_API_35",
					Details = new Dictionary<string, object> { ["avd"] = "Pixel_6_API_35" }
				}
			},
			Avds = new List<AvdInfo>
			{
				new AvdInfo { Name = "Pixel_6_API_35", Target = "android-35", DeviceProfile = "pixel_6" }
			}
		};

		var manager = CreateManager(fakeAndroid);

		// Act
		var devices = await manager.GetAllDevicesAsync();

		// Assert: should be merged into a single entry, not two
		Assert.Single(devices);
		Assert.Equal("emulator-5554", devices[0].Id);
		Assert.Equal("Pixel_6_API_35", devices[0].EmulatorId);
		Assert.True(devices[0].IsRunning);
	}

	[Fact]
	public async Task GetAllDevicesAsync_MergesRunningEmulatorWithAvd_ByEmulatorId()
	{
		// Arrange: ADB returns a running emulator with EmulatorId set but no "avd" in Details
		var fakeAndroid = new FakeAndroidProvider
		{
			Devices = new List<Device>
			{
				new Device
				{
					Id = "emulator-5554",
					Name = "Google sdk_gphone64_arm64",
					Platforms = new[] { "android" },
					Type = DeviceType.Emulator,
					State = DeviceState.Booted,
					IsEmulator = true,
					IsRunning = true,
					EmulatorId = "Pixel_6_API_35",
					Details = new Dictionary<string, object>()
				}
			},
			Avds = new List<AvdInfo>
			{
				new AvdInfo { Name = "Pixel_6_API_35", Target = "android-35", DeviceProfile = "pixel_6" }
			}
		};

		var manager = CreateManager(fakeAndroid);

		// Act
		var devices = await manager.GetAllDevicesAsync();

		// Assert: should still merge via EmulatorId fallback
		Assert.Single(devices);
		Assert.Equal("emulator-5554", devices[0].Id);
		Assert.Equal("Pixel_6_API_35", devices[0].EmulatorId);
		Assert.True(devices[0].IsRunning);
	}

	[Fact]
	public void ParseAppleSimulatorDevices_ReturnsBootedAndShutdownIosSimulators()
	{
		const string json =
			"""
			{
			  "devices": {
			    "com.apple.CoreSimulator.SimRuntime.iOS-26-2": [
			      {
			        "udid": "BOOTED-SIM",
			        "name": "iPhone 17 Pro",
			        "state": "Booted",
			        "isAvailable": true,
			        "deviceTypeIdentifier": "com.apple.CoreSimulator.SimDeviceType.iPhone-17-Pro"
			      },
			      {
			        "udid": "SHUTDOWN-SIM",
			        "name": "iPad Air 11-inch (M3)",
			        "state": "Shutdown",
			        "isAvailable": true,
			        "deviceTypeIdentifier": "com.apple.CoreSimulator.SimDeviceType.iPad-Air-11-inch-M3"
			      }
			    ],
			    "com.apple.CoreSimulator.SimRuntime.tvOS-26-2": [
			      {
			        "udid": "TV-SIM",
			        "name": "Apple TV",
			        "state": "Booted",
			        "isAvailable": true
			      }
			    ]
			  }
			}
			""";

		var devices = DeviceManager.ParseAppleSimulatorDevices(json);

		Assert.Equal(2, devices.Count);
		Assert.Equal("BOOTED-SIM", devices[0].Id);
		Assert.True(devices[0].IsRunning);
		Assert.Equal(Platforms.iOS, devices[0].Platform);
		Assert.Equal("iOS 26.2", devices[0].VersionName);

		Assert.Equal("SHUTDOWN-SIM", devices[1].Id);
		Assert.False(devices[1].IsRunning);
		Assert.Equal(DeviceIdiom.Tablet, devices[1].Idiom);
	}
}
