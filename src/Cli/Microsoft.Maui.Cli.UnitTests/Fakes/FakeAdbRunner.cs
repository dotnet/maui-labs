// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Xamarin.Android.Tools;

namespace Microsoft.Maui.Cli.UnitTests.Fakes;

/// <summary>
/// In-memory fake for <see cref="AdbRunner"/> used to test <see cref="Microsoft.Maui.Cli.DevFlow.Android.AndroidDevFlowPortForwarder"/>
/// without shelling out to a real <c>adb</c> binary. Only the members the forwarder actually calls
/// (<see cref="ListForwardPortsAsync"/>, <see cref="ListReversePortsAsync"/>, <see cref="ForwardPortAsync"/>,
/// <see cref="ReversePortAsync"/>) are overridden. Tests can use shared port state for single-device
/// operations or per-serial state when verifying exact multi-device ownership.
/// </summary>
public sealed class FakeAdbRunner : AdbRunner
{
	readonly HashSet<int> _forwardPorts;
	readonly HashSet<int> _reversePorts;
	readonly IReadOnlyDictionary<string, HashSet<int>>? _forwardPortsBySerial;
	readonly IReadOnlySet<string>? _forwardListFailures;
	readonly IReadOnlyList<AdbPortRule>? _forwardRules;
	readonly IReadOnlyList<AdbPortRule>? _reverseRules;

	public List<string> Commands { get; } = [];

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
		IReadOnlySet<string>? forwardListFailures = null,
		IReadOnlyList<AdbPortRule>? forwardRules = null,
		IReadOnlyList<AdbPortRule>? reverseRules = null)
		: base("adb")
	{
		_forwardPorts = forwardPorts ?? [];
		_reversePorts = reversePorts ?? [];
		_forwardPortsBySerial = forwardPortsBySerial;
		_forwardListFailures = forwardListFailures;
		_forwardRules = forwardRules;
		_reverseRules = reverseRules;
	}

	public override Task<IReadOnlyList<AdbPortRule>> ListForwardPortsAsync(string serial, CancellationToken cancellationToken = default)
	{
		Commands.Add($"-s {serial} forward --list");
		if (_forwardListFailures?.Contains(serial) == true)
			throw new InvalidOperationException($"Could not inspect {serial}");
		if (_forwardRules is not null)
			return Task.FromResult(_forwardRules);
		var ports = _forwardPortsBySerial is not null && _forwardPortsBySerial.TryGetValue(serial, out var serialPorts)
			? serialPorts
			: _forwardPorts;
		return Task.FromResult(ToRules(ports));
	}

	public override Task<IReadOnlyList<AdbPortRule>> ListReversePortsAsync(string serial, CancellationToken cancellationToken = default)
	{
		Commands.Add($"-s {serial} reverse --list");
		if (_reverseRules is not null)
			return Task.FromResult(_reverseRules);
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

	static IReadOnlyList<AdbPortRule> ToRules(HashSet<int> ports)
		=> ports
			.Select(port => new AdbPortRule(new AdbPortSpec(AdbProtocol.Tcp, port), new AdbPortSpec(AdbProtocol.Tcp, port)))
			.ToArray();
}
