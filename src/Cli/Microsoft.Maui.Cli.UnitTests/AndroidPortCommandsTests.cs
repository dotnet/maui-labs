// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui.Cli;
using Microsoft.Maui.Cli.Commands;
using Microsoft.Maui.Cli.Models;
using Microsoft.Maui.Cli.UnitTests.Fakes;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

[Collection("CLI")]
public class AndroidPortCommandsTests
{
	// --- Parsing / structure ---

	[Fact]
	public void PortCommand_Exists()
	{
		var android = AndroidCommands.Create();
		Assert.Contains(android.Subcommands, c => c.Name == "port");
	}

	[Theory]
	[InlineData("list")]
	[InlineData("forward")]
	[InlineData("reverse")]
	[InlineData("clear")]
	public void PortCommand_HasSubcommand(string name)
	{
		var android = AndroidCommands.Create();
		var port = android.Subcommands.First(c => c.Name == "port");
		Assert.Contains(port.Subcommands, c => c.Name == name);
	}

	[Fact]
	public void PortCommand_HasRecursiveDeviceOption()
	{
		var android = AndroidCommands.Create();
		var port = android.Subcommands.First(c => c.Name == "port");
		Assert.Contains(port.Options, o => o.Name == "--device");
	}

	[Fact]
	public void ForwardCommand_HasAgentPortOption()
	{
		var android = AndroidCommands.Create();
		var port = android.Subcommands.First(c => c.Name == "port");
		var forward = port.Subcommands.First(c => c.Name == "forward");
		Assert.Contains(forward.Options, o => o.Name == "--agent-port");
	}

	[Fact]
	public void ReverseCommand_PortArgumentIsOptional()
	{
		var android = AndroidCommands.Create();
		var rootPort = android.Subcommands.First(c => c.Name == "port");
		var reverse = rootPort.Subcommands.First(c => c.Name == "reverse");

		var parseResult = reverse.Parse("reverse");
		Assert.Empty(parseResult.Errors);
	}

	// --- Handler-level tests ---

	static FakeAndroidProvider WithOnlineDevice(string serial = "emulator-5554")
		=> new()
		{
			Devices =
			{
				new Device
				{
					Name = serial,
					Id = serial,
					Platforms = new[] { "android" },
					State = DeviceState.Connected
				}
			}
		};

	static async Task<(int ExitCode, string StdOut, FakeAndroidProvider Fake)> InvokePortAsync(
		FakeAndroidProvider fake,
		string args,
		string? androidSerial = null)
	{
		var testProvider = ServiceConfiguration.CreateTestServiceProvider(androidProvider: fake);
		var originalOut = Console.Out;
		var originalSerial = Environment.GetEnvironmentVariable("ANDROID_SERIAL");
		var stdOut = new StringWriter();
		try
		{
			Environment.SetEnvironmentVariable("ANDROID_SERIAL", androidSerial);
			Program.Services = testProvider;
			Console.SetOut(stdOut);

			var rootCommand = Program.BuildRootCommand();
			var parseResult = rootCommand.Parse(args);
			var exitCode = await parseResult.InvokeAsync();
			return (exitCode, stdOut.ToString(), fake);
		}
		finally
		{
			Console.SetOut(originalOut);
			Environment.SetEnvironmentVariable("ANDROID_SERIAL", originalSerial);
			Program.ResetServices();
		}
	}

	[Fact]
	public async Task Forward_SinglePort_UsesSamePortOnBothSides()
	{
		var fake = WithOnlineDevice();

		var (exitCode, _, _) = await InvokePortAsync(fake, "android port forward 8080 --json");

		Assert.Equal(0, exitCode);
		var call = Assert.Single(fake.AddedForwardPorts);
		Assert.Equal("emulator-5554", call.Serial);
		Assert.Equal(8080, call.HostPort);
		Assert.Equal(8080, call.DevicePort);
	}

	[Fact]
	public async Task Forward_WithRemote_MapsHostToDevice()
	{
		var fake = WithOnlineDevice();

		var (exitCode, _, _) = await InvokePortAsync(fake, "android port forward 8080 9090 --json");

		Assert.Equal(0, exitCode);
		var call = Assert.Single(fake.AddedForwardPorts);
		Assert.Equal(8080, call.HostPort);
		Assert.Equal(9090, call.DevicePort);
	}

	[Fact]
	public async Task Forward_AgentPort_ForwardsOnBothSides()
	{
		var fake = WithOnlineDevice();

		var (exitCode, _, _) = await InvokePortAsync(fake, "android port forward --agent-port 19223 --json");

		Assert.Equal(0, exitCode);
		var call = Assert.Single(fake.AddedForwardPorts);
		Assert.Equal(19223, call.HostPort);
		Assert.Equal(19223, call.DevicePort);
	}

	[Fact]
	public async Task Forward_PortAndAgentPort_IsRejected()
	{
		var fake = WithOnlineDevice();

		var (exitCode, _, _) = await InvokePortAsync(fake, "android port forward 8080 --agent-port 19223 --json");

		Assert.Equal(1, exitCode);
		Assert.Empty(fake.AddedForwardPorts);
	}

	[Fact]
	public async Task Forward_MissingPort_IsRejected()
	{
		var fake = WithOnlineDevice();

		var (exitCode, _, _) = await InvokePortAsync(fake, "android port forward --json");

		Assert.Equal(1, exitCode);
		Assert.Empty(fake.AddedForwardPorts);
	}

	[Fact]
	public async Task Forward_InvalidPort_IsRejected()
	{
		var fake = WithOnlineDevice();

		var (exitCode, _, _) = await InvokePortAsync(fake, "android port forward 70000 --json");

		Assert.Equal(1, exitCode);
		Assert.Empty(fake.AddedForwardPorts);
	}

	[Fact]
	public async Task Reverse_NoArgs_DefaultsToBrokerPort()
	{
		var fake = WithOnlineDevice();

		var (exitCode, _, _) = await InvokePortAsync(fake, "android port reverse --json");

		Assert.Equal(0, exitCode);
		var call = Assert.Single(fake.AddedReversePorts);
		Assert.Equal(19223, call.DevicePort);
		Assert.Equal(19223, call.HostPort);
	}

	[Fact]
	public async Task Reverse_WithPortAndHost_MapsDeviceToHost()
	{
		var fake = WithOnlineDevice();

		var (exitCode, _, _) = await InvokePortAsync(fake, "android port reverse 5000 6000 --json");

		Assert.Equal(0, exitCode);
		var call = Assert.Single(fake.AddedReversePorts);
		Assert.Equal(5000, call.DevicePort);
		Assert.Equal(6000, call.HostPort);
	}

	[Fact]
	public async Task List_Json_IncludesSerialAndMappings()
	{
		var fake = WithOnlineDevice();
		fake.ForwardPorts.Add(new AndroidPortMapping { Local = 8080, Remote = 9090 });
		fake.ReversePorts.Add(new AndroidPortMapping { Local = 19223, Remote = 19223 });

		var (exitCode, stdOut, _) = await InvokePortAsync(fake, "android port list --json");

		Assert.Equal(0, exitCode);
		Assert.Contains("emulator-5554", stdOut);
		Assert.Contains("8080", stdOut);
		Assert.Contains("19223", stdOut);
	}

	[Fact]
	public async Task Clear_NoFlags_ClearsBothDirections()
	{
		var fake = WithOnlineDevice();

		var (exitCode, _, _) = await InvokePortAsync(fake, "android port clear --json");

		Assert.Equal(0, exitCode);
		Assert.Single(fake.ClearedForwardPorts);
		Assert.Single(fake.ClearedReversePorts);
	}

	[Fact]
	public async Task Clear_ForwardOnly_LeavesReverseUntouched()
	{
		var fake = WithOnlineDevice();

		var (exitCode, _, _) = await InvokePortAsync(fake, "android port clear --forward --json");

		Assert.Equal(0, exitCode);
		Assert.Single(fake.ClearedForwardPorts);
		Assert.Empty(fake.ClearedReversePorts);
	}

	[Fact]
	public async Task Forward_NoOnlineDevice_Fails()
	{
		var fake = new FakeAndroidProvider();

		var (exitCode, _, _) = await InvokePortAsync(fake, "android port forward 8080 --json");

		Assert.Equal(1, exitCode);
		Assert.Empty(fake.AddedForwardPorts);
	}

	[Fact]
	public async Task Forward_MultipleDevicesWithoutSelector_Fails()
	{
		var fake = new FakeAndroidProvider
		{
			Devices =
			{
				new Device { Name = "a", Id = "emulator-5554", Platforms = new[] { "android" }, State = DeviceState.Connected },
				new Device { Name = "b", Id = "emulator-5556", Platforms = new[] { "android" }, State = DeviceState.Booted }
			}
		};

		var (exitCode, _, _) = await InvokePortAsync(fake, "android port forward 8080 --json");

		Assert.Equal(1, exitCode);
		Assert.Empty(fake.AddedForwardPorts);
	}

	[Fact]
	public async Task Forward_MultipleDevicesWithDeviceSelector_Succeeds()
	{
		var fake = new FakeAndroidProvider
		{
			Devices =
			{
				new Device { Name = "a", Id = "emulator-5554", Platforms = new[] { "android" }, State = DeviceState.Connected },
				new Device { Name = "b", Id = "emulator-5556", Platforms = new[] { "android" }, State = DeviceState.Booted }
			}
		};

		var (exitCode, _, _) = await InvokePortAsync(fake, "android port forward 8080 --device emulator-5556 --json");

		Assert.Equal(0, exitCode);
		var call = Assert.Single(fake.AddedForwardPorts);
		Assert.Equal("emulator-5556", call.Serial);
	}

	[Fact]
	public async Task Forward_UnknownDeviceSelector_Fails()
	{
		var fake = WithOnlineDevice();

		var (exitCode, _, _) = await InvokePortAsync(fake, "android port forward 8080 --device does-not-exist --json");

		Assert.Equal(1, exitCode);
		Assert.Empty(fake.AddedForwardPorts);
	}

	[Fact]
	public async Task Forward_AndroidSerialEnvVar_SelectsDevice()
	{
		var fake = new FakeAndroidProvider
		{
			Devices =
			{
				new Device { Name = "a", Id = "emulator-5554", Platforms = new[] { "android" }, State = DeviceState.Connected },
				new Device { Name = "b", Id = "emulator-5556", Platforms = new[] { "android" }, State = DeviceState.Booted }
			}
		};

		var (exitCode, _, _) = await InvokePortAsync(fake, "android port forward 8080 --json", androidSerial: "emulator-5556");

		Assert.Equal(0, exitCode);
		var call = Assert.Single(fake.AddedForwardPorts);
		Assert.Equal("emulator-5556", call.Serial);
	}
}
