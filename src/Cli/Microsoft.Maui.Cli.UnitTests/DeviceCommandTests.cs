// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using System.Text.Json;
using Microsoft.Maui.Cli.Providers.Apple;
using Microsoft.Maui.Cli.UnitTests.Fakes;
using Xunit;
using static Microsoft.Maui.Cli.UnitTests.Fixtures.DeviceCtlFixtures;

namespace Microsoft.Maui.Cli.UnitTests;

[Collection("CLI")]
public class DeviceCommandTests
{
	[Fact]
	public async Task DeviceList_Json_ReportsConnectedPhysicalIosDevice()
	{
		var (exitCode, stdOut) = await InvokeAsync(new FakePhysicalDeviceService(IPhone("COREDEVICE-1")), "device", "list", "--json");

		Assert.Equal(0, exitCode);
		using var json = JsonDocument.Parse(stdOut);
		Assert.Equal(JsonValueKind.Array, json.RootElement.ValueKind);
		var device = Assert.Single(json.RootElement.EnumerateArray());
		Assert.Equal(IPhoneUdid, device.GetProperty("identifier").GetString());
		Assert.Equal("Test iPhone", device.GetProperty("name").GetString());
		Assert.Equal("ios", device.GetProperty("platform").GetString());
		Assert.Equal("Physical", device.GetProperty("type").GetString());
		Assert.Equal("Connected", device.GetProperty("state").GetString());
		Assert.Equal("18.1", device.GetProperty("os_version").GetString());
		Assert.False(device.GetProperty("is_emulator").GetBoolean());
		Assert.True(device.GetProperty("is_running").GetBoolean());
		Assert.Equal("usb", device.GetProperty("connection_type").GetString());
	}

	static async Task<(int ExitCode, string StdOut)> InvokeAsync(FakePhysicalDeviceService devicectl, params string[] args)
	{
		var services = ServiceConfiguration.CreateTestServiceProvider(
			androidProvider: new FakeAndroidProvider(),
			appleProvider: new AppleProvider(devicectl));
		var stdOut = new StringWriter();
		var originalOut = Console.Out;
		try
		{
			Program.Services = services;
			Console.SetOut(stdOut);

			var exitCode = await Program.BuildRootCommand().Parse(args).InvokeAsync();
			return (exitCode, stdOut.ToString());
		}
		finally
		{
			Console.SetOut(originalOut);
			Program.ResetServices();
		}
	}
}
