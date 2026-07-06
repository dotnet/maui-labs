// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Xamarin.Android.Tools;

namespace Microsoft.Maui.Cli.UnitTests.Fakes;

/// <summary>
/// In-memory fake for <see cref="AdbRunner"/> used to test <see cref="Microsoft.Maui.Cli.DevFlow.Android.AndroidDevFlowPortForwarder"/>
/// without shelling out to a real <c>adb</c> binary. Only the members the forwarder actually calls
/// (<see cref="ListForwardPortsAsync"/>, <see cref="ListReversePortsAsync"/>, <see cref="ForwardPortAsync"/>,
/// <see cref="ReversePortAsync"/>) are overridden. Port state is intentionally not keyed by serial - the
/// forwarder always targets a single selected device per call, and per-serial fidelity is <see cref="AdbRunner"/>'s
/// own concern, not something this fake needs to reproduce.
/// </summary>
public sealed class FakeAdbRunner : AdbRunner
{
	readonly HashSet<int> _forwardPorts;
	readonly HashSet<int> _reversePorts;

	public List<string> Commands { get; } = [];

	public FakeAdbRunner(HashSet<int>? forwardPorts = null, HashSet<int>? reversePorts = null)
		: base("adb")
	{
		_forwardPorts = forwardPorts ?? [];
		_reversePorts = reversePorts ?? [];
	}

	public override Task<IReadOnlyList<AdbPortRule>> ListForwardPortsAsync(string serial, CancellationToken cancellationToken = default)
	{
		Commands.Add($"-s {serial} forward --list");
		return Task.FromResult(ToRules(_forwardPorts));
	}

	public override Task<IReadOnlyList<AdbPortRule>> ListReversePortsAsync(string serial, CancellationToken cancellationToken = default)
	{
		Commands.Add($"-s {serial} reverse --list");
		return Task.FromResult(ToRules(_reversePorts));
	}

	public override Task ForwardPortAsync(string serial, AdbPortSpec local, AdbPortSpec remote, CancellationToken cancellationToken = default)
	{
		Commands.Add($"-s {serial} forward {local.ToSocketSpec()} {remote.ToSocketSpec()}");
		if (local.Port == remote.Port)
			_forwardPorts.Add(local.Port);
		return Task.CompletedTask;
	}

	public override Task ReversePortAsync(string serial, AdbPortSpec remote, AdbPortSpec local, CancellationToken cancellationToken = default)
	{
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
