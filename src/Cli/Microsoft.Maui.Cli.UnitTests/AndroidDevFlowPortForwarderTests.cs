using Microsoft.Maui.Cli.DevFlow.Android;
using Microsoft.Maui.Cli.Models;
using Microsoft.Maui.Cli.UnitTests.Fakes;
using Microsoft.Maui.Cli.Utils;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class AndroidDevFlowPortForwarderTests
{
	[Fact]
	public async Task EnsureAsync_WithoutSdkPath_ReportsNoAdb()
	{
		var provider = new FakeAndroidProvider();
		var forwarder = new AndroidDevFlowPortForwarder(provider, null, (_, _, _) => throw new InvalidOperationException("adb should not run"));

		var report = await forwarder.EnsureAsync(new AndroidDevFlowForwardingRequest { AgentPorts = [9223] });

		Assert.Equal(AndroidDevFlowForwardingStatus.AdbNotFound, report.Status);
		Assert.False(report.AdbAvailable);
		Assert.Equal("ADB was not found. Install Android platform-tools or set ANDROID_HOME.", report.Message);
	}

	[Fact]
	public async Task EnsureAsync_WithMultipleOnlineDevices_ReportsMultipleDevices()
	{
		var provider = CreateProvider(
			Device("emulator-5554"),
			Device("RZ8T123456A", isEmulator: false));
		var commands = new List<string>();
		var forwarder = new AndroidDevFlowPortForwarder(provider, "/android-sdk/platform-tools/adb", (_, args, _) =>
		{
			commands.Add(string.Join(' ', args));
			return Task.FromResult(new ProcessResult { ExitCode = 0 });
		});

		var report = await forwarder.EnsureAsync(new AndroidDevFlowForwardingRequest { AgentPorts = [9223] });

		Assert.Equal(AndroidDevFlowForwardingStatus.MultipleDevices, report.Status);
		Assert.Null(report.SelectedSerial);
		Assert.Equal("Multiple online Android devices or emulators were found. Specify --device or ANDROID_SERIAL.", report.Message);
		Assert.Empty(commands);
	}

	[Fact]
	public async Task EnsureAsync_WithExplicitSerial_SelectsMatchingDevice()
	{
		var provider = CreateProvider(
			Device("emulator-5554"),
			Device("RZ8T123456A", isEmulator: false));
		var forwardRules = new HashSet<int> { 9223 };
		var reverseRules = new HashSet<int> { 19223 };
		var forwarder = new AndroidDevFlowPortForwarder(provider, "/android-sdk/platform-tools/adb", CreateAdbRunner(forwardRules, reverseRules));

		var report = await forwarder.EnsureAsync(new AndroidDevFlowForwardingRequest
		{
			DeviceSerial = "RZ8T123456A",
			AgentPorts = [9223],
			EnsureBrokerReverse = true
		});

		Assert.Equal(AndroidDevFlowForwardingStatus.Ok, report.Status);
		Assert.Equal("RZ8T123456A", report.SelectedSerial);
		Assert.True(report.BrokerReversePresent);
		Assert.True(report.AgentForwards.Single(f => f.Port == 9223).PresentAfter);
	}

	[Fact]
	public async Task EnsureAsync_WhenMappingsAlreadyExist_DoesNotRepair()
	{
		var provider = CreateProvider(Device("emulator-5554"));
		var forwardRules = new HashSet<int> { 9223 };
		var reverseRules = new HashSet<int> { 19223 };
		var commands = new List<string>();
		var forwarder = new AndroidDevFlowPortForwarder(provider, "/android-sdk/platform-tools/adb", CreateAdbRunner(forwardRules, reverseRules, commands));

		var report = await forwarder.EnsureAsync(new AndroidDevFlowForwardingRequest
		{
			AgentPorts = [9223],
			EnsureBrokerReverse = true,
			Repair = true
		});

		Assert.Equal(AndroidDevFlowForwardingStatus.Ok, report.Status);
		Assert.False(report.BrokerReverseAdded);
		Assert.DoesNotContain("-s emulator-5554 reverse tcp:19223 tcp:19223", commands);
		Assert.DoesNotContain("-s emulator-5554 forward tcp:9223 tcp:9223", commands);
	}

	[Fact]
	public async Task EnsureAsync_WithRepair_AddsMissingReverseAndForward()
	{
		var provider = CreateProvider(Device("emulator-5554"));
		var forwardRules = new HashSet<int>();
		var reverseRules = new HashSet<int>();
		var commands = new List<string>();
		var forwarder = new AndroidDevFlowPortForwarder(provider, "/android-sdk/platform-tools/adb", CreateAdbRunner(forwardRules, reverseRules, commands));

		var report = await forwarder.EnsureAsync(new AndroidDevFlowForwardingRequest
		{
			AgentPorts = [9223],
			EnsureBrokerReverse = true,
			Repair = true
		});

		Assert.Equal(AndroidDevFlowForwardingStatus.Repaired, report.Status);
		Assert.True(report.BrokerReversePresent);
		Assert.True(report.BrokerReverseAdded);
		Assert.True(report.AgentForwards.Single(f => f.Port == 9223).PresentAfter);
		Assert.Contains("-s emulator-5554 reverse tcp:19223 tcp:19223", commands);
		Assert.Contains("-s emulator-5554 forward tcp:9223 tcp:9223", commands);
	}

	[Fact]
	public async Task EnsureAsync_WithoutRepair_ReportsMissingMappings()
	{
		var provider = CreateProvider(Device("emulator-5554"));
		var forwarder = new AndroidDevFlowPortForwarder(provider, "/android-sdk/platform-tools/adb", CreateAdbRunner([], []));

		var report = await forwarder.EnsureAsync(new AndroidDevFlowForwardingRequest
		{
			AgentPorts = [9223],
			EnsureBrokerReverse = true,
			Repair = false
		});

		Assert.Equal(AndroidDevFlowForwardingStatus.Missing, report.Status);
		Assert.False(report.BrokerReversePresent);
		Assert.False(report.AgentForwards.Single(f => f.Port == 9223).PresentAfter);
		Assert.Contains("adb -s emulator-5554 reverse tcp:19223 tcp:19223", report.Suggestions);
		Assert.Contains("adb -s emulator-5554 forward tcp:9223 tcp:9223", report.Suggestions);
	}

	[Fact]
	public async Task EnsureAsync_WithCustomBrokerPort_UsesProvidedPortForReverse()
	{
		var provider = CreateProvider(Device("emulator-5554"));
		var forwardRules = new HashSet<int>();
		var reverseRules = new HashSet<int>();
		var commands = new List<string>();
		var forwarder = new AndroidDevFlowPortForwarder(provider, "/android-sdk/platform-tools/adb", CreateAdbRunner(forwardRules, reverseRules, commands));

		var report = await forwarder.EnsureAsync(new AndroidDevFlowForwardingRequest
		{
			AgentPorts = [9223],
			EnsureBrokerReverse = true,
			BrokerPort = 19225,
			Repair = true
		});

		Assert.Equal(AndroidDevFlowForwardingStatus.Repaired, report.Status);
		Assert.Equal(19225, report.BrokerPort);
		Assert.True(report.BrokerReversePresent);
		Assert.Contains("-s emulator-5554 reverse tcp:19225 tcp:19225", commands);
		Assert.DoesNotContain("-s emulator-5554 reverse tcp:19223 tcp:19223", commands);
	}

	[Fact]
	public async Task EnsureAsync_WithCustomBrokerPortAndMissingReverse_SuggestsCustomPort()
	{
		var provider = CreateProvider(Device("emulator-5554"));
		var forwarder = new AndroidDevFlowPortForwarder(provider, "/android-sdk/platform-tools/adb", CreateAdbRunner([], []));

		var report = await forwarder.EnsureAsync(new AndroidDevFlowForwardingRequest
		{
			AgentPorts = [9223],
			EnsureBrokerReverse = true,
			BrokerPort = 19225,
			Repair = false
		});

		Assert.Equal(AndroidDevFlowForwardingStatus.Missing, report.Status);
		Assert.Contains("adb -s emulator-5554 reverse tcp:19225 tcp:19225", report.Suggestions);
	}

	[Fact]
	public async Task EnsureAsync_WithoutBrokerReverseRequest_DoesNotCheckOrReportReverse()
	{
		var provider = CreateProvider(Device("emulator-5554"));
		var forwardRules = new HashSet<int> { 9223 };
		var reverseRules = new HashSet<int> { 19223 };
		var commands = new List<string>();
		var forwarder = new AndroidDevFlowPortForwarder(provider, "/android-sdk/platform-tools/adb", CreateAdbRunner(forwardRules, reverseRules, commands));

		var report = await forwarder.EnsureAsync(new AndroidDevFlowForwardingRequest
		{
			AgentPorts = [9223],
			EnsureBrokerReverse = false,
			Repair = true
		});

		Assert.Equal(AndroidDevFlowForwardingStatus.Ok, report.Status);
		Assert.False(report.BrokerReverseChecked);
		Assert.False(report.BrokerReversePresent);
		Assert.DoesNotContain("-s emulator-5554 reverse --list", commands);
	}

	static FakeAndroidProvider CreateProvider(params Device[] devices)
		=> new()
		{
			SdkPath = "/android-sdk",
			IsSdkInstalled = true,
			Devices = devices.ToList()
		};

	static Device Device(string serial, bool isEmulator = true)
		=> new()
		{
			Id = serial,
			Name = serial,
			Platforms = ["android"],
			Type = isEmulator ? DeviceType.Emulator : DeviceType.Physical,
			State = DeviceState.Connected,
			IsEmulator = isEmulator,
			IsRunning = true
		};

	static Func<string, string[], CancellationToken, Task<ProcessResult>> CreateAdbRunner(
		HashSet<int> forwardRules,
		HashSet<int> reverseRules,
		List<string>? commands = null)
		=> (_, args, _) =>
		{
			commands?.Add(string.Join(' ', args));

			if (args is ["-s", var forwardListSerial, "forward", "--list"])
			{
				var output = string.Join(Environment.NewLine, forwardRules.Select(port => $"{forwardListSerial} tcp:{port} tcp:{port}"));
				return Task.FromResult(new ProcessResult { ExitCode = 0, StandardOutput = output });
			}

			if (args is ["-s", var reverseListSerial, "reverse", "--list"])
			{
				var output = string.Join(Environment.NewLine, reverseRules.Select(port => $"{reverseListSerial} tcp:{port} tcp:{port}"));
				return Task.FromResult(new ProcessResult { ExitCode = 0, StandardOutput = output });
			}

			if (args is ["-s", _, "forward", var forwardLocal, var forwardRemote] &&
				TryParseTcpPort(forwardLocal, out var forwardLocalPort) &&
				TryParseTcpPort(forwardRemote, out var forwardRemotePort) &&
				forwardLocalPort == forwardRemotePort)
			{
				forwardRules.Add(forwardLocalPort);
				return Task.FromResult(new ProcessResult { ExitCode = 0 });
			}

			if (args is ["-s", _, "reverse", var reverseLocal, var reverseRemote] &&
				TryParseTcpPort(reverseLocal, out var reverseLocalPort) &&
				TryParseTcpPort(reverseRemote, out var reverseRemotePort) &&
				reverseLocalPort == reverseRemotePort)
			{
				reverseRules.Add(reverseLocalPort);
				return Task.FromResult(new ProcessResult { ExitCode = 0 });
			}

			return Task.FromResult(new ProcessResult { ExitCode = 1, StandardError = $"Unexpected adb command: {string.Join(' ', args)}" });
		};

	static bool TryParseTcpPort(string value, out int port)
	{
		const string Prefix = "tcp:";
		if (value.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
			return int.TryParse(value[Prefix.Length..], out port);

		port = 0;
		return false;
	}
}
