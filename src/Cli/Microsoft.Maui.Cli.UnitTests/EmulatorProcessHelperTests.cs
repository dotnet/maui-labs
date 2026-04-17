// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui.Cli.Utils;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class EmulatorProcessHelperTests
{
	// ── ParseEmulatorPort ────────────────────────────────────────────────────

	[Theory]
	[InlineData("emulator-5554", 5554)]
	[InlineData("emulator-5556", 5556)]
	[InlineData("emulator-5558", 5558)]
	[InlineData("EMULATOR-5554", 5554)] // case-insensitive prefix
	public void ParseEmulatorPort_ValidSerial_ReturnsPort(string serial, int expectedPort)
	{
		var port = EmulatorProcessHelper.ParseEmulatorPort(serial);
		Assert.Equal(expectedPort, port);
	}

	[Theory]
	[InlineData("")]
	[InlineData("192.168.1.100:5555")]
	[InlineData("device123")]
	[InlineData("emulator-")]         // missing port number
	[InlineData("emulator-abc")]      // non-numeric port
	[InlineData("physical-5554")]     // wrong prefix
	public void ParseEmulatorPort_InvalidSerial_ReturnsNull(string serial)
	{
		var port = EmulatorProcessHelper.ParseEmulatorPort(serial);
		Assert.Null(port);
	}

	// ── ParsePidPpidOutput ───────────────────────────────────────────────────

	[Fact]
	public void ParsePidPpidOutput_ValidOutput_ReturnsMappings()
	{
		var output = "  PID  PPID\n  123   456\n  789   123\n   12     1\n";
		var result = EmulatorProcessHelper.ParsePidPpidOutput(output);
		Assert.Equal(3, result.Count);
		Assert.Equal(456, result[123]);
		Assert.Equal(123, result[789]);
		Assert.Equal(1, result[12]);
	}

	[Fact]
	public void ParsePidPpidOutput_HeaderLine_IsSkipped()
	{
		var output = "  PID  PPID\n 1234  5678\n";
		var result = EmulatorProcessHelper.ParsePidPpidOutput(output);
		Assert.Single(result);
		Assert.Equal(5678, result[1234]);
	}

	[Fact]
	public void ParsePidPpidOutput_EmptyOrWhitespaceOutput_ReturnsEmpty()
	{
		Assert.Empty(EmulatorProcessHelper.ParsePidPpidOutput(""));
		Assert.Empty(EmulatorProcessHelper.ParsePidPpidOutput("   \n  \n"));
	}

	// ── FindQemuPidFromPsOutput ──────────────────────────────────────────────

	[Theory]
	[InlineData(
		" 9999 /sdk/emulator/qemu/darwin-aarch64/qemu-system-aarch64 -port 5554 -avd MyAvd",
		5554, 9999)]
	[InlineData(
		" 8888 /sdk/emulator/qemu/linux-aarch64/qemu-system-aarch64 -avd MyAvd -port 5556 ",
		5556, 8888)]
	[InlineData(
		" 7777 /sdk/emulator/qemu/darwin-aarch64/qemu-system-aarch64 -avd MyAvd -port 5558",
		5558, 7777)]
	[InlineData(
		" 6666 /sdk/emulator/qemu/darwin-aarch64/qemu-system-aarch64 @5560 other-args",
		5560, 6666)]
	public void FindQemuPidFromPsOutput_MatchingProcess_ReturnsPid(
		string processLine, int port, int expectedPid)
	{
		var output = $"  PID ARGS\n{processLine}\n";
		var pid = EmulatorProcessHelper.FindQemuPidFromPsOutput(output, port);
		Assert.Equal(expectedPid, pid);
	}

	[Theory]
	[InlineData(
		" 9999 /usr/bin/python3 some-script.py -port 5554",
		5554)] // not a qemu-system process
	[InlineData(
		" 8888 /sdk/emulator/qemu/qemu-system-aarch64 -port 5556 ",
		5554)] // different port
	[InlineData(
		"",
		5554)] // empty output
	public void FindQemuPidFromPsOutput_NoMatch_ReturnsNull(string processLine, int port)
	{
		var output = $"  PID ARGS\n{processLine}\n";
		var pid = EmulatorProcessHelper.FindQemuPidFromPsOutput(output, port);
		Assert.Null(pid);
	}

	[Fact]
	public void FindQemuPidFromPsOutput_MultipleProcesses_ReturnsFirstMatch()
	{
		// Only the qemu-system-aarch64 process with the right port should match.
		var output = string.Join('\n',
			"  PID ARGS",
			" 1111 /sdk/emulator/qemu/darwin-aarch64/qemu-system-aarch64 -port 5554 -avd Avd1",
			" 2222 /sdk/emulator/qemu/darwin-aarch64/qemu-system-aarch64 -port 5556 -avd Avd2",
			" 3333 /sdk/emulator/crashpad_handler --database=/tmp/db",
			"");
		var pid = EmulatorProcessHelper.FindQemuPidFromPsOutput(output, 5554);
		Assert.Equal(1111, pid);
	}

	// ── GetDirectChildPids ───────────────────────────────────────────────────

	[Fact]
	public void GetDirectChildPids_ReturnsOnlyDirectChildren()
	{
		var pidToParent = new Dictionary<int, int>
		{
			[100] = 1,    // root-level process
			[200] = 100,  // direct child
			[201] = 100,  // direct child
			[300] = 200,  // grandchild — should NOT be included
		};
		var children = EmulatorProcessHelper.GetDirectChildPids(pidToParent, 100);
		Assert.Equal(2, children.Count);
		Assert.Contains(200, children);
		Assert.Contains(201, children);
		Assert.DoesNotContain(300, children);
	}

	[Fact]
	public void GetDirectChildPids_NoChildren_ReturnsEmpty()
	{
		var pidToParent = new Dictionary<int, int>
		{
			[100] = 1,
			[200] = 50,
		};
		var children = EmulatorProcessHelper.GetDirectChildPids(pidToParent, 999);
		Assert.Empty(children);
	}

	[Fact]
	public void GetDirectChildPids_EmptyMapping_ReturnsEmpty()
	{
		var children = EmulatorProcessHelper.GetDirectChildPids(
			new Dictionary<int, int>(), parentPid: 1234);
		Assert.Empty(children);
	}

	// ── Integration: ParseEmulatorPort + FindQemuPidFromPsOutput ─────────────

	[Fact]
	public void ParsePortThenFindPid_FullRoundTrip_FindsCorrectProcess()
	{
		const string serial = "emulator-5554";
		var psOutput = string.Join('\n',
			"  PID ARGS",
			" 9999 /sdk/emulator/qemu/darwin-aarch64/qemu-system-aarch64 -port 5554 -avd MAUI_Emulator",
			" 9998 /sdk/emulator/crashpad_handler --database=/tmp/db",
			"");

		var port = EmulatorProcessHelper.ParseEmulatorPort(serial);
		Assert.NotNull(port);

		var pid = EmulatorProcessHelper.FindQemuPidFromPsOutput(psOutput, port!.Value);
		Assert.Equal(9999, pid);
	}

	[Fact]
	public void GetDirectChildPids_AfterParsePidPpid_ReturnsOrphanCandidates()
	{
		// Simulate: emulator PID 9999 has two crashpad_handler children (9998, 9997)
		var psOutput = string.Join('\n',
			"  PID  PPID",
			" 9999  1000",
			" 9998  9999",
			" 9997  9999",
			" 9996   500",
			"");

		var pidToParent = EmulatorProcessHelper.ParsePidPpidOutput(psOutput);
		var children = EmulatorProcessHelper.GetDirectChildPids(pidToParent, 9999);

		Assert.Equal(2, children.Count);
		Assert.Contains(9998, children);
		Assert.Contains(9997, children);
	}
}
