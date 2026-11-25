// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Xamarin.Android.Tools;

namespace Microsoft.Maui.Cli.UnitTests.Fakes;

/// <summary>
/// In-memory fake for <see cref="AdbRunner"/> used to test <see cref="Microsoft.Maui.Cli.DevFlow.Android.AndroidDevFlowPortForwarder"/>
/// without shelling out to a real <c>adb</c> binary. Only the members the forwarder actually calls
/// (<see cref="ListForwardPortsAsync"/>, <see cref="ListReversePortsAsync"/>, <see cref="ForwardPortAsync"/>,
/// <see cref="ReversePortAsync"/>, <see cref="RunShellCommandAsync(string, string, CancellationToken)"/>) are overridden. Tests can use shared port state for single-device
/// operations or per-serial state when verifying exact multi-device ownership.
/// </summary>
public sealed class FakeAdbRunner : AdbRunner
{
	readonly HashSet<int> _forwardPorts;
	readonly HashSet<int> _reversePorts;
	readonly IReadOnlyDictionary<string, HashSet<int>>? _forwardPortsBySerial;
	readonly IReadOnlySet<string>? _forwardListFailures;

	public List<string> Commands { get; } = [];

	/// <summary>Read-only <c>adb shell</c> commands, kept apart from mapping commands.</summary>
	public List<string> ShellCommands { get; } = [];

	/// <summary>Device-side listening TCP ports reported by <c>/proc/net/tcp</c>, per serial.</summary>
	public Dictionary<string, HashSet<int>> ListeningPortsBySerial { get; } = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>Serials whose <c>adb shell</c> probe fails.</summary>
	public HashSet<string> ShellFailures { get; } = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// Optional hook invoked before <see cref="ReversePortAsync"/> performs its normal
	/// work. Throw from this delegate to simulate an <c>adb</c> failure. Throwing an
	/// <see cref="OperationCanceledException"/> here simulates an internal per-call
	/// timeout (as opposed to the caller's own <see cref="CancellationToken"/> firing).
	/// </summary>
	public Action? OnReversePort { get; set; }

	/// <summary>
	/// Optional hook invoked before <see cref="ForwardPortAsync"/> performs its normal
	/// work. Throw from this delegate to simulate an <c>adb</c> failure. Throwing an
	/// <see cref="OperationCanceledException"/> here simulates an internal per-call
	/// timeout (as opposed to the caller's own <see cref="CancellationToken"/> firing).
	/// </summary>
	public Action? OnForwardPort { get; set; }

	public FakeAdbRunner(
		HashSet<int>? forwardPorts = null,
		HashSet<int>? reversePorts = null,
		IReadOnlyDictionary<string, HashSet<int>>? forwardPortsBySerial = null,
		IReadOnlySet<string>? forwardListFailures = null)
		: base("adb")
	{
		_forwardPorts = forwardPorts ?? [];
		_reversePorts = reversePorts ?? [];
		_forwardPortsBySerial = forwardPortsBySerial;
		_forwardListFailures = forwardListFailures;
	}

	public override Task<IReadOnlyList<AdbPortRule>> ListForwardPortsAsync(string serial, CancellationToken cancellationToken = default)
	{
		Commands.Add($"-s {serial} forward --list");
		if (_forwardListFailures?.Contains(serial) == true)
			throw new InvalidOperationException($"Could not inspect {serial}");
		var ports = _forwardPortsBySerial is not null && _forwardPortsBySerial.TryGetValue(serial, out var serialPorts)
			? serialPorts
			: _forwardPorts;
		return Task.FromResult(ToRules(ports));
	}

	public override Task<IReadOnlyList<AdbPortRule>> ListReversePortsAsync(string serial, CancellationToken cancellationToken = default)
	{
		Commands.Add($"-s {serial} reverse --list");
		return Task.FromResult(ToRules(_reversePorts));
	}

	public override Task ForwardPortAsync(string serial, AdbPortSpec local, AdbPortSpec remote, CancellationToken cancellationToken = default)
	{
		// The hook runs first so a simulated failure (throwing from OnForwardPort) prevents the
		// command from being recorded as "attempted" - Commands should only ever reflect calls
		// that actually completed, matching what a real adb invocation would have logged.
		OnForwardPort?.Invoke();
		Commands.Add($"-s {serial} forward {local.ToSocketSpec()} {remote.ToSocketSpec()}");
		if (local.Port == remote.Port)
		{
			if (_forwardPortsBySerial is not null && _forwardPortsBySerial.TryGetValue(serial, out var serialPorts))
				serialPorts.Add(local.Port);
			else
				_forwardPorts.Add(local.Port);
		}
		return Task.CompletedTask;
	}

	public override Task ReversePortAsync(string serial, AdbPortSpec remote, AdbPortSpec local, CancellationToken cancellationToken = default)
	{
		// See the comment in ForwardPortAsync: the hook runs first so a simulated failure keeps
		// Commands limited to calls that actually completed.
		OnReversePort?.Invoke();
		Commands.Add($"-s {serial} reverse {remote.ToSocketSpec()} {local.ToSocketSpec()}");
		if (local.Port == remote.Port)
			_reversePorts.Add(local.Port);
		return Task.CompletedTask;
	}

	public override Task<string> RunShellCommandAsync(string serial, string command, CancellationToken cancellationToken = default)
	{
		ShellCommands.Add($"-s {serial} shell {command}");
		if (ShellFailures.Contains(serial))
			throw new InvalidOperationException($"adb shell failed on {serial}");

		var lines = new List<string> { "  sl  local_address rem_address   st tx_queue rx_queue tr tm->when retrnsmt   uid  timeout inode" };
		if (ListeningPortsBySerial.TryGetValue(serial, out var ports))
		{
			var i = 0;
			foreach (var port in ports)
				lines.Add($"   {i++}: 00000000:{port:X4} 00000000:0000 0A 00000000:00000000 00:00000000 00000000 10123        0 12345 1");
		}
		// An established (non-listening) connection on another port must be ignored.
		lines.Add("   9: 0100007F:9C40 0100007F:4B4F 01 00000000:00000000 00:00000000 00000000 10123        0 12346 1");
		return Task.FromResult(string.Join("\n", lines));
	}

	static IReadOnlyList<AdbPortRule> ToRules(HashSet<int> ports)
		=> ports
			.Select(port => new AdbPortRule(new AdbPortSpec(AdbProtocol.Tcp, port), new AdbPortSpec(AdbProtocol.Tcp, port)))
			.ToArray();
}
