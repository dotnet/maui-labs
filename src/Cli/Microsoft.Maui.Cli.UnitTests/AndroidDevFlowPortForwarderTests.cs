using Microsoft.Maui.Cli.DevFlow.Android;
using Microsoft.Maui.Cli.Models;
using Microsoft.Maui.Cli.UnitTests.Fakes;
using Xamarin.Android.Tools;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class AndroidDevFlowPortForwarderTests
{
	[Fact]
	public void Constructor_WithAdbPathButNullRunner_ThrowsArgumentNullException()
	{
		var provider = new FakeAndroidProvider();

		var ex = Assert.Throws<ArgumentNullException>(() =>
			new AndroidDevFlowPortForwarder(provider, "/android-sdk/platform-tools/adb", adbRunner: null));

		Assert.Equal("adbRunner", ex.ParamName);
	}

	/// <summary>
	/// Verifies that an <see cref="OperationCanceledException"/> raised while a runner call is in
	/// flight propagates out of <see cref="AndroidDevFlowPortForwarder.EnsureAsync"/> untouched
	/// (i.e. it isn't caught and reported as an error result). This is exercised via
	/// <see cref="FakeAdbRunner.OnReversePort"/> throwing unconditionally, not by the pre-cancelled
	/// <c>cts.Token</c> actually being observed - <see cref="FakeAdbRunner"/> accepts a
	/// <see cref="CancellationToken"/> parameter on each call but never checks it, so this test does
	/// not prove that a caller-supplied token threads through to cancel a real adb process.
	/// </summary>
	[Fact]
	public async Task EnsureAsync_WhenCallerCancels_PropagatesOperationCanceledException()
	{
		var provider = CreateProvider(Device("emulator-5554"));
		var runner = new FakeAdbRunner
		{
			OnReversePort = () => throw new OperationCanceledException()
		};
		var forwarder = new AndroidDevFlowPortForwarder(provider, "/android-sdk/platform-tools/adb", runner);
		using var cts = new CancellationTokenSource();
		cts.Cancel();

		await Assert.ThrowsAsync<OperationCanceledException>(() => forwarder.EnsureAsync(
			new AndroidDevFlowForwardingRequest
			{
				AgentPorts = [9223],
				EnsureBrokerReverse = true,
				Repair = true
			},
			cts.Token));
	}

	[Fact]
	public async Task EnsureAsync_WhenAdbCallTimesOutInternally_ReportsErrorInsteadOfPropagating()
	{
		var provider = CreateProvider(Device("emulator-5554"));
		var runner = new FakeAdbRunner
		{
			// Simulates the internal per-call timeout firing - not the caller's own token.
			OnReversePort = () => throw new OperationCanceledException("adb reverse timed out")
		};
		var forwarder = new AndroidDevFlowPortForwarder(provider, "/android-sdk/platform-tools/adb", runner);

		var report = await forwarder.EnsureAsync(new AndroidDevFlowForwardingRequest
		{
			AgentPorts = [9223],
			EnsureBrokerReverse = true,
			Repair = true
		}, CancellationToken.None);

		Assert.Equal(AndroidDevFlowForwardingStatus.Error, report.Status);
		Assert.Contains("adb reverse tcp:19223 tcp:19223 failed", report.Message);
	}

	[Fact]
	public async Task EnsureAsync_WithoutSdkPath_ReportsNoAdb()
	{
		var provider = new FakeAndroidProvider();
		var forwarder = new AndroidDevFlowPortForwarder(provider, null, adbRunner: null);

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
		var runner = new FakeAdbRunner();
		var forwarder = new AndroidDevFlowPortForwarder(provider, "/android-sdk/platform-tools/adb", runner);

		var report = await forwarder.EnsureAsync(new AndroidDevFlowForwardingRequest { AgentPorts = [9223] });

		Assert.Equal(AndroidDevFlowForwardingStatus.MultipleDevices, report.Status);
		Assert.Null(report.SelectedSerial);
		Assert.Equal("Multiple online Android devices or emulators were found. Specify --device or ANDROID_SERIAL.", report.Message);
		Assert.Empty(runner.Commands);
	}

	[Fact]
	public async Task EnsureAsync_WithExplicitSerial_SelectsMatchingDevice()
	{
		var provider = CreateProvider(
			Device("emulator-5554"),
			Device("RZ8T123456A", isEmulator: false));
		var forwardRules = new HashSet<int> { 9223 };
		var reverseRules = new HashSet<int> { 19223 };
		var runner = new FakeAdbRunner(forwardRules, reverseRules);
		var forwarder = new AndroidDevFlowPortForwarder(provider, "/android-sdk/platform-tools/adb", runner);

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
		var runner = new FakeAdbRunner(forwardRules, reverseRules);
		var forwarder = new AndroidDevFlowPortForwarder(provider, "/android-sdk/platform-tools/adb", runner);

		var report = await forwarder.EnsureAsync(new AndroidDevFlowForwardingRequest
		{
			AgentPorts = [9223],
			EnsureBrokerReverse = true,
			Repair = true
		});

		Assert.Equal(AndroidDevFlowForwardingStatus.Ok, report.Status);
		Assert.False(report.BrokerReverseAdded);
		Assert.DoesNotContain("-s emulator-5554 reverse tcp:19223 tcp:19223", runner.Commands);
		Assert.DoesNotContain("-s emulator-5554 forward tcp:9223 tcp:9223", runner.Commands);
	}

	[Fact]
	public async Task EnsureAsync_WithRepair_AddsMissingReverseAndForward()
	{
		var provider = CreateProvider(Device("emulator-5554"));
		var runner = new FakeAdbRunner();
		var forwarder = new AndroidDevFlowPortForwarder(provider, "/android-sdk/platform-tools/adb", runner);

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
		Assert.Contains("-s emulator-5554 reverse tcp:19223 tcp:19223", runner.Commands);
		Assert.Contains("-s emulator-5554 forward tcp:9223 tcp:9223", runner.Commands);
	}

	[Fact]
	public async Task EnsureAsync_WithForeignMappings_RefusesReplacement()
	{
		var provider = CreateProvider(Device("emulator-5554"));
		var runner = new FakeAdbRunner(
			forwardRules:
			[
				new AdbPortRule(
					new AdbPortSpec(AdbProtocol.Tcp, 8123),
					new AdbPortSpec(AdbProtocol.Tcp, 9223)),
			],
			reverseRules:
			[
				new AdbPortRule(
					new AdbPortSpec(AdbProtocol.Tcp, 19224),
					new AdbPortSpec(AdbProtocol.Tcp, 19223)),
			]);
		var forwarder = new AndroidDevFlowPortForwarder(provider, "/android-sdk/platform-tools/adb", runner);

		var report = await forwarder.EnsureAsync(new AndroidDevFlowForwardingRequest
		{
			AgentPorts = [9223],
			EnsureBrokerReverse = true,
			Repair = true,
		});

		Assert.Equal(AndroidDevFlowForwardingStatus.Error, report.Status);
		Assert.Contains("Refusing to replace existing adb reverse", report.Message);
		Assert.Contains("Refusing to replace existing adb forward", report.Message);
		Assert.DoesNotContain(runner.Commands, command =>
			command == "-s emulator-5554 reverse tcp:19223 tcp:19223");
		Assert.DoesNotContain(runner.Commands, command =>
			command == "-s emulator-5554 forward tcp:9223 tcp:9223");
	}

	[Fact]
	public async Task EnsureAsync_WithoutRepair_ReportsMissingMappings()
	{
		var provider = CreateProvider(Device("emulator-5554"));
		var forwarder = new AndroidDevFlowPortForwarder(provider, "/android-sdk/platform-tools/adb", new FakeAdbRunner());

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
		var runner = new FakeAdbRunner();
		var forwarder = new AndroidDevFlowPortForwarder(provider, "/android-sdk/platform-tools/adb", runner);

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
		Assert.Contains("-s emulator-5554 reverse tcp:19225 tcp:19225", runner.Commands);
		Assert.DoesNotContain("-s emulator-5554 reverse tcp:19223 tcp:19223", runner.Commands);
	}

	[Fact]
	public async Task EnsureAsync_WithCustomBrokerPortAndMissingReverse_SuggestsCustomPort()
	{
		var provider = CreateProvider(Device("emulator-5554"));
		var forwarder = new AndroidDevFlowPortForwarder(provider, "/android-sdk/platform-tools/adb", new FakeAdbRunner());

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
		var runner = new FakeAdbRunner(forwardRules, reverseRules);
		var forwarder = new AndroidDevFlowPortForwarder(provider, "/android-sdk/platform-tools/adb", runner);

		var report = await forwarder.EnsureAsync(new AndroidDevFlowForwardingRequest
		{
			AgentPorts = [9223],
			EnsureBrokerReverse = false,
			Repair = true
		});

		Assert.Equal(AndroidDevFlowForwardingStatus.Ok, report.Status);
		Assert.False(report.BrokerReverseChecked);
		Assert.False(report.BrokerReversePresent);
		Assert.DoesNotContain("-s emulator-5554 reverse --list", runner.Commands);
	}

	[Fact]
	public async Task ResolveDeviceForForwardedPortAsync_ReturnsTheUniqueOwningDevice()
	{
		var provider = CreateProvider(Device("emulator-5554"), Device("RZ8T123456A", isEmulator: false));
		var runner = new FakeAdbRunner(forwardPortsBySerial: new Dictionary<string, HashSet<int>>
		{
			["emulator-5554"] = [10223],
			["RZ8T123456A"] = [10224]
		});
		var forwarder = new AndroidDevFlowPortForwarder(provider, "/android-sdk/platform-tools/adb", runner);

		var resolution = await forwarder.ResolveDeviceForForwardedPortAsync(10224);

		Assert.True(resolution.IsResolved);
		Assert.Equal("RZ8T123456A", resolution.Serial);
	}

	[Fact]
	public async Task ResolveDeviceForForwardedPortAsync_RefusesMissingOrAmbiguousOwnership()
	{
		var provider = CreateProvider(Device("emulator-5554"), Device("RZ8T123456A", isEmulator: false));
		var runner = new FakeAdbRunner(forwardPortsBySerial: new Dictionary<string, HashSet<int>>
		{
			["emulator-5554"] = [10223],
			["RZ8T123456A"] = [10223]
		});
		var forwarder = new AndroidDevFlowPortForwarder(provider, "/android-sdk/platform-tools/adb", runner);

		var ambiguous = await forwarder.ResolveDeviceForForwardedPortAsync(10223);
		var missing = await forwarder.ResolveDeviceForForwardedPortAsync(10224);

		Assert.False(ambiguous.IsResolved);
		Assert.Contains("multiple Android devices", ambiguous.Error);
		Assert.False(missing.IsResolved);
		Assert.Contains("No online Android device owns", missing.Error);
	}

	[Fact]
	public async Task ResolveDeviceForForwardedPortAsync_RefusesPartialDeviceInspection()
	{
		var provider = CreateProvider(Device("emulator-5554"), Device("RZ8T123456A", isEmulator: false));
		var runner = new FakeAdbRunner(
			forwardPortsBySerial: new Dictionary<string, HashSet<int>>
			{
				["emulator-5554"] = [10223],
				["RZ8T123456A"] = []
			},
			forwardListFailures: new HashSet<string> { "RZ8T123456A" });
		var forwarder = new AndroidDevFlowPortForwarder(provider, "/android-sdk/platform-tools/adb", runner);

		var resolution = await forwarder.ResolveDeviceForForwardedPortAsync(10223);

		Assert.False(resolution.IsResolved);
		Assert.Contains("Could not identify", resolution.Error);
		Assert.Contains("RZ8T123456A", resolution.Error);
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
}
