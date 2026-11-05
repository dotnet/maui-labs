// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui.Cli.Models;
using Microsoft.Maui.Cli.Providers.Apple;
using Microsoft.Maui.Cli.UnitTests.Fakes;
using Xamarin.MacDev;
using Xunit;
using static Microsoft.Maui.Cli.UnitTests.Fixtures.DeviceCtlFixtures;

namespace Microsoft.Maui.Cli.UnitTests.Providers.Apple;

/// <summary>
/// Physical device discovery in <see cref="AppleProvider.GetDevices"/> (issue #722). devicectl output
/// is faked, so these run on any host. Simulators come from <c>simctl</c> and are not part of the seam.
/// </summary>
public sealed class AppleProviderDeviceTests
{
	[Fact]
	public void GetDevices_ConnectedIPhone_ReportsPhysicalIosDevice()
	{
		var provider = new AppleProvider(new FakePhysicalDeviceService(IPhone("COREDEVICE-1")));

		var device = Assert.Single(provider.GetDevices());

		Assert.Equal(IPhoneUdid, device.Id);
		Assert.Equal("Test iPhone", device.Name);
		Assert.Equal(new[] { Platforms.iOS }, device.Platforms);
		Assert.Equal(DeviceType.Physical, device.Type);
		Assert.Equal(DeviceState.Connected, device.State);
		Assert.False(device.IsEmulator);
		Assert.True(device.IsRunning);
		Assert.Equal(Models.ConnectionType.Usb, device.ConnectionType);
		Assert.Equal("iPhone 16 Pro", device.Model);
		Assert.Equal("Apple", device.Manufacturer);
		Assert.Equal("18.1", device.Version);
		Assert.Equal("iOS 18.1", device.VersionName);
		Assert.Equal(DeviceIdiom.Phone, device.Idiom);
		Assert.Null(device.EmulatorId);
	}

	[Fact]
	public void GetDevices_PluggedInDeviceWithoutActiveTunnel_IsReportedConnected()
	{
		// A plugged-in phone at rest reports connection state "disconnected" over a wired transport
		// (devicectl labels it "available"). It is ready for use, so it must still count as connected.
		var provider = new AppleProvider(new FakePhysicalDeviceService(IPhone("COREDEVICE-1", connectionState: "disconnected")));

		var device = Assert.Single(provider.GetDevices());

		Assert.Equal(DeviceState.Connected, device.State);
		Assert.True(device.IsRunning);
		Assert.Equal(Models.ConnectionType.Usb, device.ConnectionType);
	}

	[Fact]
	public void GetDevices_IPad_ReportsTabletIdiom()
	{
		var provider = new AppleProvider(new FakePhysicalDeviceService(IPad("COREDEVICE-2")));

		var device = Assert.Single(provider.GetDevices());

		Assert.Equal(IPadUdid, device.Id);
		Assert.Equal("iPad Pro 11-inch (4th generation)", device.Model);
		Assert.Equal(DeviceIdiom.Tablet, device.Idiom);
	}

	[Fact]
	public void GetDevices_DeviceOnLocalNetwork_ReportsWifiConnection()
	{
		var provider = new AppleProvider(new FakePhysicalDeviceService(IPhone("COREDEVICE-1", transportType: "localNetwork")));

		var device = Assert.Single(provider.GetDevices());

		Assert.Equal(Models.ConnectionType.Wifi, device.ConnectionType);
	}

	[Fact]
	public void GetDevices_UnpairedDevice_IsListedButNotRunning()
	{
		var provider = new AppleProvider(new FakePhysicalDeviceService(
			IPhone("COREDEVICE-1", pairingState: "unpaired", connectionState: "disconnected")));

		var device = Assert.Single(provider.GetDevices());

		Assert.Equal(IPhoneUdid, device.Id);
		Assert.Equal(DeviceState.Disconnected, device.State);
		Assert.False(device.IsRunning);
	}

	[Fact]
	public void GetDevices_DeviceReportedOverNetworkAndUsb_IsListedOnceUsingUsb()
	{
		var provider = new AppleProvider(new FakePhysicalDeviceService(
			IPhone("COREDEVICE-NETWORK", transportType: "localNetwork"),
			IPhone("COREDEVICE-USB", transportType: "wired")));

		var device = Assert.Single(provider.GetDevices());

		Assert.Equal(IPhoneUdid, device.Id);
		Assert.Equal(Models.ConnectionType.Usb, device.ConnectionType);
	}

	[Fact]
	public void GetDevices_SkipsUnavailableUnidentifiedAndNonIosDevices()
	{
		var provider = new AppleProvider(new FakePhysicalDeviceService(
			IPhone("COREDEVICE-1"),
			IPhone("COREDEVICE-2", udid: "00008030-0000000000000001", connectionState: "unavailable"),
			IPhone("COREDEVICE-3", udid: null),
			IPhone("COREDEVICE-4", udid: "00008310-0000000000000002", deviceType: "appleWatch", productType: "Watch7,1", platform: "watchOS", marketingName: null),
			IPhone("COREDEVICE-5", udid: "00008110-0000000000000003", deviceType: "appleTV", productType: "AppleTV14,1", platform: "tvOS", marketingName: null),
			IPhone("COREDEVICE-6", udid: "00006000-0000000000000004", deviceType: "mac", productType: "Mac15,3", platform: "macOS", marketingName: null)));

		var devices = provider.GetDevices();

		Assert.Equal([IPhoneUdid], devices.Select(d => d.Id));
	}

	[Fact]
	public void GetDevices_DevicectlFails_ReturnsEmptyList()
	{
		var provider = new AppleProvider(new FakePhysicalDeviceService(DeviceCtlRunResult.NotFound("devicectl was not found")));

		Assert.Empty(provider.GetDevices());
	}
}
