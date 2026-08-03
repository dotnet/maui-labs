// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json.Nodes;
using Microsoft.Maui.Cli.Models;
using Microsoft.Maui.Cli.Providers.Android;
using Microsoft.Maui.Cli.Services;
using Microsoft.Maui.Cli.UnitTests.Fakes;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class DeviceManagerTests
{
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

		var manager = new DeviceManager(fakeAndroid);

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

		var manager = new DeviceManager(fakeAndroid);

		// Act
		var androidOnly = await manager.GetDevicesByPlatformAsync("android");

		// Assert
		Assert.Single(androidOnly);
		Assert.All(androidOnly, d => Assert.Contains("android", d.Platforms));
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

		var manager = new DeviceManager(fakeAndroid);

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
		var manager = new DeviceManager(fakeAndroid);

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

		var manager = new DeviceManager(fakeAndroid);

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
					Details = new JsonObject { ["avd"] = "Pixel_6_API_35" }
				}
			},
			Avds = new List<AvdInfo>
			{
				new AvdInfo { Name = "Pixel_6_API_35", Target = "android-35", DeviceProfile = "pixel_6" }
			}
		};

		var manager = new DeviceManager(fakeAndroid);

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
					Details = new JsonObject()
				}
			},
			Avds = new List<AvdInfo>
			{
				new AvdInfo { Name = "Pixel_6_API_35", Target = "android-35", DeviceProfile = "pixel_6" }
			}
		};

		var manager = new DeviceManager(fakeAndroid);

		// Act
		var devices = await manager.GetAllDevicesAsync();

		// Assert: should still merge via EmulatorId fallback
		Assert.Single(devices);
		Assert.Equal("emulator-5554", devices[0].Id);
		Assert.Equal("Pixel_6_API_35", devices[0].EmulatorId);
		Assert.True(devices[0].IsRunning);
	}

	[Fact]
	public async Task GetAllDevicesAsync_RunningEmulator_UsesAvdModelAndManufacturer()
	{
		// Regression: running emulators used to report model from adb (e.g.
		// "sdk_gphone64_x86_64") instead of from the AVD config (e.g. "pixel_6").
		// After merging, model and manufacturer must reflect the AVD profile.
		var fakeAndroid = new FakeAndroidProvider
		{
			Devices = new List<Device>
			{
				new Device
				{
					Id = "emulator-5554",
					Name = "MAUI_Emulator_API_36",
					Platforms = new[] { "android" },
					Type = DeviceType.Emulator,
					State = DeviceState.Booted,
					IsEmulator = true,
					IsRunning = true,
					EmulatorId = "MAUI_Emulator_API_36",
					Model = "sdk_gphone64_x86_64",
					Details = new JsonObject { ["avd"] = "MAUI_Emulator_API_36" }
				}
			},
			Avds = new List<AvdInfo>
			{
				new AvdInfo
				{
					Name = "MAUI_Emulator_API_36",
					DeviceProfile = "pixel_6",
					Manufacturer = "Google",
					Target = "android-36"
				}
			}
		};

		var manager = new DeviceManager(fakeAndroid);

		var devices = await manager.GetAllDevicesAsync();

		Assert.Single(devices);
		Assert.Equal("pixel_6", devices[0].Model);
		Assert.Equal("Google", devices[0].Manufacturer);
		Assert.True(devices[0].IsRunning);
	}

	[Fact]
	public async Task GetAllDevicesAsync_RunningEmulator_FallsBackToGoogleManufacturer_WhenAvdManufacturerUnknown()
	{
		// When the AVD has no manufacturer set in config.ini, fall back to "Google"
		// (same as the stopped-AVD path).
		var fakeAndroid = new FakeAndroidProvider
		{
			Devices = new List<Device>
			{
				new Device
				{
					Id = "emulator-5554",
					Name = "Pixel_6_API_35",
					Platforms = new[] { "android" },
					Type = DeviceType.Emulator,
					State = DeviceState.Booted,
					IsEmulator = true,
					IsRunning = true,
					EmulatorId = "Pixel_6_API_35",
					Details = new JsonObject { ["avd"] = "Pixel_6_API_35" }
				}
			},
			Avds = new List<AvdInfo>
			{
				new AvdInfo
				{
					Name = "Pixel_6_API_35",
					DeviceProfile = "pixel_6",
					Manufacturer = null,  // not set in config.ini
					Target = "android-35"
				}
			}
		};

		var manager = new DeviceManager(fakeAndroid);

		var devices = await manager.GetAllDevicesAsync();

		Assert.Single(devices);
		Assert.Equal("pixel_6", devices[0].Model);
		Assert.Equal("Google", devices[0].Manufacturer);
	}

	[Fact]
	public async Task GetAllDevicesAsync_RunningEmulator_FallsBackToGoogle_WhenAvdManufacturerEmpty()
	{
		// Regression: config.ini may contain "hw.device.manufacturer=" with a blank value.
		// AvdManager normalizes that to null at parse time, but DeviceManager must also
		// treat empty/whitespace as missing (the ?? operator alone does NOT catch "").
		var fakeAndroid = new FakeAndroidProvider
		{
			Devices = new List<Device>
			{
				new Device
				{
					Id = "emulator-5554",
					Name = "Pixel_6_API_35",
					Platforms = new[] { "android" },
					Type = DeviceType.Emulator,
					State = DeviceState.Booted,
					IsEmulator = true,
					IsRunning = true,
					EmulatorId = "Pixel_6_API_35",
					Model = "sdk_gphone64_x86_64",
					Details = new JsonObject { ["avd"] = "Pixel_6_API_35" }
				}
			},
			Avds = new List<AvdInfo>
			{
				new AvdInfo
				{
					Name = "Pixel_6_API_35",
					DeviceProfile = "pixel_6",
					Manufacturer = "",  // key present but blank in config.ini
					Target = "android-35"
				}
			}
		};

		var manager = new DeviceManager(fakeAndroid);

		var devices = await manager.GetAllDevicesAsync();

		Assert.Single(devices);
		Assert.Equal("pixel_6", devices[0].Model);
		Assert.Equal("Google", devices[0].Manufacturer);
	}

	[Fact]
	public async Task GetAllDevicesAsync_RunningEmulator_KeepsAdbModel_WhenAvdDeviceProfileEmpty()
	{
		// Regression: an empty DeviceProfile must not overwrite a valid running adb model.
		var fakeAndroid = new FakeAndroidProvider
		{
			Devices = new List<Device>
			{
				new Device
				{
					Id = "emulator-5554",
					Name = "MyAvd",
					Platforms = new[] { "android" },
					Type = DeviceType.Emulator,
					State = DeviceState.Booted,
					IsEmulator = true,
					IsRunning = true,
					EmulatorId = "MyAvd",
					Model = "sdk_gphone64_x86_64",
					Details = new JsonObject { ["avd"] = "MyAvd" }
				}
			},
			Avds = new List<AvdInfo>
			{
				new AvdInfo
				{
					Name = "MyAvd",
					DeviceProfile = "",  // missing/blank in config
					Manufacturer = "Google",
					Target = "android-36"
				}
			}
		};

		var manager = new DeviceManager(fakeAndroid);

		var devices = await manager.GetAllDevicesAsync();

		Assert.Single(devices);
		Assert.Equal("sdk_gphone64_x86_64", devices[0].Model);
	}

	[Fact]
	public async Task GetAllDevicesAsync_MergesOfflineEmulatorWithLockedAvd()
	{
		// Regression: while an emulator is booting, adb reports it as "Offline"
		// with no AVD name populated. Previously this produced two entries
		// (an unnamed offline serial plus a "Shutdown" AVD). The AVD's lock
		// file lets us pair them into a single booting device.
		var fakeAndroid = new FakeAndroidProvider
		{
			Devices = new List<Device>
			{
				new Device
				{
					Id = "emulator-5554",
					Name = "emulator-5554",
					Platforms = new[] { "android" },
					Type = DeviceType.Emulator,
					State = DeviceState.Offline,
					IsEmulator = true,
					IsRunning = false,
					Details = new JsonObject()
				}
			},
			Avds = new List<AvdInfo>
			{
				new AvdInfo { Name = "Pixel_6_API_35", Target = "android-35", DeviceProfile = "pixel_6", IsLocked = true }
			}
		};

		var manager = new DeviceManager(fakeAndroid);

		var devices = await manager.GetAllDevicesAsync();

		Assert.Single(devices);
		Assert.Equal("emulator-5554", devices[0].Id);
		Assert.Equal("Pixel_6_API_35", devices[0].EmulatorId);
		Assert.Equal("Pixel_6_API_35", devices[0].Name);
		Assert.Equal(DeviceState.Booting, devices[0].State);
		Assert.True(devices[0].IsRunning, "Booting emulator must report IsRunning=true (qemu is alive)");
		Assert.NotNull(devices[0].Details);
		Assert.Equal("Pixel_6_API_35", devices[0].Details!["avd"]?.ToString());
	}

	[Fact]
	public async Task GetAllDevicesAsync_UnlockedAvdDoesNotMergeWithOfflineEmulator()
	{
		// If the AVD has no lock file we cannot safely pair it with an unnamed
		// offline serial (it could belong to a different AVD). Leave both
		// entries separate.
		var fakeAndroid = new FakeAndroidProvider
		{
			Devices = new List<Device>
			{
				new Device
				{
					Id = "emulator-5554",
					Name = "emulator-5554",
					Platforms = new[] { "android" },
					Type = DeviceType.Emulator,
					State = DeviceState.Offline,
					IsEmulator = true,
					IsRunning = false,
					Details = new JsonObject()
				}
			},
			Avds = new List<AvdInfo>
			{
				new AvdInfo { Name = "Pixel_6_API_35", Target = "android-35", IsLocked = false }
			}
		};

		var manager = new DeviceManager(fakeAndroid);

		var devices = await manager.GetAllDevicesAsync();

		Assert.Equal(2, devices.Count);
	}

	[Fact]
	public async Task GetAllDevicesAsync_LockedAvdDoesNotHijackAlreadyNamedEmulator()
	{
		// A named running emulator must not be "stolen" by a locked AVD with a
		// different name. Each locked AVD only pairs with unnamed offline serials.
		var fakeAndroid = new FakeAndroidProvider
		{
			Devices = new List<Device>
			{
				new Device
				{
					Id = "emulator-5554",
					Name = "Pixel_6_API_35",
					Platforms = new[] { "android" },
					Type = DeviceType.Emulator,
					State = DeviceState.Booted,
					IsEmulator = true,
					IsRunning = true,
					EmulatorId = "Pixel_6_API_35",
					Details = new JsonObject { ["avd"] = "Pixel_6_API_35" }
				}
			},
			Avds = new List<AvdInfo>
			{
				new AvdInfo { Name = "Pixel_6_API_35", Target = "android-35", IsLocked = true },
				new AvdInfo { Name = "Other_AVD", Target = "android-34", IsLocked = true }
			}
		};

		var manager = new DeviceManager(fakeAndroid);

		var devices = await manager.GetAllDevicesAsync();

		// Pixel_6_API_35 merged into the running entry. Other_AVD has a lock
		// file but no offline serial to pair with, so it surfaces as its own
		// Booting entry (we can't confirm boot completion without adb; the
		// merge path will flip it to Booted on the next refresh once adb
		// registers the serial).
		Assert.Equal(2, devices.Count);
		Assert.Contains(devices, d => d.EmulatorId == "Pixel_6_API_35" && d.IsRunning && d.State == DeviceState.Booted);
		Assert.Contains(devices, d => d.EmulatorId == "Other_AVD" && d.State == DeviceState.Booting && !d.IsRunning);
	}

	[Fact]
	public async Task GetAllDevicesAsync_LockedAvdWithoutMatchingSerialReportsBooting()
	{
		// Early boot window: a locked AVD exists but adb has not yet listed
		// any serial for it. Lock files prove the qemu process is alive but
		// can't confirm boot completion. Report Booting (the common case
		// during startup); the merge path will surface Booted once adb
		// registers the emulator on a later refresh.
		var fakeAndroid = new FakeAndroidProvider
		{
			Devices = new List<Device>(),
			Avds = new List<AvdInfo>
			{
				new AvdInfo { Name = "Pixel_6_API_35", Target = "android-35", IsLocked = true }
			}
		};

		var manager = new DeviceManager(fakeAndroid);

		var devices = await manager.GetAllDevicesAsync();

		Assert.Single(devices);
		Assert.Equal(DeviceState.Booting, devices[0].State);
		// IsRunning stays false: we have no adb serial, so the entry is not
		// addressable via `adb -s <id>`. Display shows Booting for UX, but
		// running-device consumers must not auto-select this target.
		Assert.False(devices[0].IsRunning);
	}

	[Fact]
	public async Task GetAllDevicesAsync_LockedAvdDoesNotHijackNonOfflineEmulator()
	{
		// A Booted/Connected emulator that momentarily lacks an AVD name (e.g.
		// transient adb gap) must not be paired with a locked AVD. Only Offline
		// serials are eligible for lock-based fallback pairing.
		var fakeAndroid = new FakeAndroidProvider
		{
			Devices = new List<Device>
			{
				new Device
				{
					Id = "emulator-5554",
					Name = "emulator-5554",
					Platforms = new[] { "android" },
					Type = DeviceType.Emulator,
					State = DeviceState.Booted,
					IsEmulator = true,
					IsRunning = true,
					Details = new JsonObject()
				}
			},
			Avds = new List<AvdInfo>
			{
				new AvdInfo { Name = "Pixel_6_API_35", Target = "android-35", IsLocked = true }
			}
		};

		var manager = new DeviceManager(fakeAndroid);

		var devices = await manager.GetAllDevicesAsync();

		// Two separate entries: the booted serial (untouched) and the locked AVD
		// surfaced as its own Booting entry (the else branch defaults to
		// Booting when adb has no serial to pair with).
		Assert.Equal(2, devices.Count);
		Assert.Contains(devices, d => d.Id == "emulator-5554" && d.State == DeviceState.Booted && string.IsNullOrEmpty(d.EmulatorId));
		Assert.Contains(devices, d => d.EmulatorId == "Pixel_6_API_35" && d.State == DeviceState.Booting);
	}

	// --- Provider scoping (issue #416) ---
	//
	// `--platform` must decide *which providers get queried*, not just filter the combined
	// result. On macOS the Apple provider shells out to `xcrun simctl list devices`, which
	// takes 8-16s and can hang indefinitely when CoreSimulator is wedged. Asking for Android
	// devices must never pay that cost, so these tests assert on provider invocation counts
	// rather than only on the returned devices.

	static FakeAndroidProvider CreateAndroidProvider() => new()
	{
		Devices = new List<Device>
		{
			new Device { Id = "emulator-5554", Name = "Pixel 6", Platforms = new[] { "android" }, Type = DeviceType.Emulator, State = DeviceState.Booted, IsEmulator = true, IsRunning = true }
		}
	};

	static FakeAppleProvider CreateAppleProvider() => new()
	{
		Devices = new List<Device>
		{
			new Device { Id = "sim-udid-1234", Name = "iPhone 15 Pro", Platforms = new[] { "ios" }, Type = DeviceType.Simulator, State = DeviceState.Booted, IsEmulator = true, IsRunning = true }
		}
	};

	[Theory]
	[InlineData("android")]
	[InlineData("ANDROID")]
	public async Task GetDevicesByPlatformAsync_Android_DoesNotQueryAppleProvider(string platform)
	{
		var fakeAndroid = CreateAndroidProvider();
		var fakeApple = CreateAppleProvider();
		var manager = new DeviceManager(fakeAndroid, fakeApple);

		var devices = await manager.GetDevicesByPlatformAsync(platform);

		Assert.Equal(0, fakeApple.GetDevicesCallCount);
		Assert.Equal(1, fakeAndroid.GetDevicesCallCount);
		Assert.Single(devices);
		Assert.Equal("emulator-5554", devices[0].Id);
	}

	[Theory]
	[InlineData("ios")]
	[InlineData("apple")]
	[InlineData("iphone")]
	[InlineData("ipad")]
	public async Task GetDevicesByPlatformAsync_Ios_DoesNotQueryAndroidProvider(string platform)
	{
		var fakeAndroid = CreateAndroidProvider();
		var fakeApple = CreateAppleProvider();
		var manager = new DeviceManager(fakeAndroid, fakeApple);

		var devices = await manager.GetDevicesByPlatformAsync(platform);

		Assert.Equal(0, fakeAndroid.GetDevicesCallCount);
		Assert.Equal(0, fakeAndroid.GetAvdsCallCount);
		Assert.Equal(1, fakeApple.GetDevicesCallCount);
		Assert.Single(devices);
		Assert.Equal("sim-udid-1234", devices[0].Id);
	}

	[Theory]
	[InlineData("maccatalyst")]
	[InlineData("windows")]
	public async Task GetDevicesByPlatformAsync_UnbackedPlatform_QueriesNoProvider(string platform)
	{
		var fakeAndroid = CreateAndroidProvider();
		var fakeApple = CreateAppleProvider();
		var manager = new DeviceManager(fakeAndroid, fakeApple);

		var devices = await manager.GetDevicesByPlatformAsync(platform);

		Assert.Equal(0, fakeAndroid.GetDevicesCallCount);
		Assert.Equal(0, fakeAndroid.GetAvdsCallCount);
		Assert.Equal(0, fakeApple.GetDevicesCallCount);
		Assert.Empty(devices);
	}

	[Fact]
	public async Task GetDevicesByPlatformAsync_All_QueriesEveryProvider()
	{
		var fakeAndroid = CreateAndroidProvider();
		var fakeApple = CreateAppleProvider();
		var manager = new DeviceManager(fakeAndroid, fakeApple);

		var devices = await manager.GetDevicesByPlatformAsync(Platforms.All);

		Assert.Equal(1, fakeAndroid.GetDevicesCallCount);
		Assert.Equal(1, fakeApple.GetDevicesCallCount);
		Assert.Equal(2, devices.Count);
	}

	[Fact]
	public async Task GetAllDevicesAsync_QueriesEveryProvider()
	{
		var fakeAndroid = CreateAndroidProvider();
		var fakeApple = CreateAppleProvider();
		var manager = new DeviceManager(fakeAndroid, fakeApple);

		var devices = await manager.GetAllDevicesAsync();

		Assert.Equal(1, fakeAndroid.GetDevicesCallCount);
		Assert.Equal(1, fakeApple.GetDevicesCallCount);
		Assert.Equal(2, devices.Count);
	}

	[Fact]
	public async Task GetDevicesByPlatformAsync_Android_SkipsAvdEnumerationForOtherPlatforms()
	{
		// The AVD query is a second adb/emulator round-trip; iOS must not trigger it either.
		var fakeAndroid = CreateAndroidProvider();
		var manager = new DeviceManager(fakeAndroid, CreateAppleProvider());

		await manager.GetDevicesByPlatformAsync(Platforms.iOS);

		Assert.Equal(0, fakeAndroid.GetAvdsCallCount);

		await manager.GetDevicesByPlatformAsync(Platforms.Android);

		Assert.Equal(1, fakeAndroid.GetAvdsCallCount);
	}

	// Queries* take an already-normalized platform; alias handling ("apple", "iphone", ...) is
	// covered end-to-end by GetDevicesByPlatformAsync above.
	[Theory]
	[InlineData(Platforms.All, true)]
	[InlineData(Platforms.Android, true)]
	[InlineData(Platforms.iOS, false)]
	[InlineData(Platforms.MacCatalyst, false)]
	[InlineData(Platforms.Windows, false)]
	public void QueriesAndroid_ReturnsExpected(string platform, bool expected)
		=> Assert.Equal(expected, DeviceManager.QueriesAndroid(platform));

	[Theory]
	[InlineData(Platforms.All, true)]
	[InlineData(Platforms.iOS, true)]
	[InlineData(Platforms.Android, false)]
	[InlineData(Platforms.MacCatalyst, false)]
	[InlineData(Platforms.Windows, false)]
	public void QueriesApple_ReturnsExpected(string platform, bool expected)
		=> Assert.Equal(expected, DeviceManager.QueriesApple(platform));

	[Theory]
	[InlineData(Platforms.All, true)]
	[InlineData(Platforms.Android, true)]
	[InlineData(Platforms.iOS, true)]
	[InlineData("apple", true)]
	[InlineData(null, true)]
	[InlineData(Platforms.MacCatalyst, false)]
	[InlineData(Platforms.Windows, false)]
	public void HasProviderFor_ReturnsExpected(string? platform, bool expected)
		=> Assert.Equal(expected, DeviceManager.HasProviderFor(platform));

	[Fact]
	public void HasProviderFor_CoversEveryPlatformThatQueriesAProvider()
	{
		// Guards the mapping from drifting: a platform that reaches a provider must report as
		// supported, and one that reaches none must not.
		foreach (var platform in Platforms.Supported)
		{
			var reachesProvider = DeviceManager.QueriesAndroid(platform) || DeviceManager.QueriesApple(platform);
			Assert.Equal(reachesProvider, DeviceManager.HasProviderFor(platform));
		}
	}
}
