// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui.Cli.Utils;
using Xamarin.Android.Tools;

namespace Microsoft.Maui.Cli.Providers.Android;

/// <summary>
/// <see cref="AdbRunner"/> with reliable reverse-port discovery and captured mapping commands.
/// </summary>
internal sealed class MauiAdbRunner : AdbRunner
{
	static readonly char[] ColumnSeparators = [' ', '\t', '\r'];

	readonly string _adbPath;
	readonly Dictionary<string, string>? _environmentVariables;

	public MauiAdbRunner(string adbPath, IDictionary<string, string>? environmentVariables = null)
		: base(adbPath, environmentVariables)
	{
		_adbPath = adbPath;
		_environmentVariables = environmentVariables is null
			? null
			: new Dictionary<string, string>(environmentVariables);
	}

	public override async Task<IReadOnlyList<AdbPortRule>> ListReversePortsAsync(
		string serial,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(serial);

		var result = await ProcessRunner.RunAsync(
			_adbPath,
			["-s", serial, "reverse", "--list"],
			environmentVariables: _environmentVariables,
			cancellationToken: cancellationToken);

		if (!result.Success)
		{
			var detail = FirstLine(result.StandardError) ?? FirstLine(result.StandardOutput) ?? "no output";
			throw new InvalidOperationException(
				$"`adb reverse --list` exited with code {result.ExitCode}: {detail}");
		}

		return ParseReverseList(result.StandardOutput);
	}

	public override Task ReversePortAsync(
		string serial,
		AdbPortSpec remote,
		AdbPortSpec local,
		CancellationToken cancellationToken = default)
		=> RunPortCommandAsync("reverse", serial, remote, local, cancellationToken);

	public override Task ForwardPortAsync(
		string serial,
		AdbPortSpec local,
		AdbPortSpec remote,
		CancellationToken cancellationToken = default)
		=> RunPortCommandAsync("forward", serial, local, remote, cancellationToken);

	public override Task RemoveReversePortAsync(
		string serial,
		AdbPortSpec remote,
		CancellationToken cancellationToken = default)
		=> RunPortRemovalAsync("reverse", serial, remote, cancellationToken);

	public override Task RemoveForwardPortAsync(
		string serial,
		AdbPortSpec local,
		CancellationToken cancellationToken = default)
		=> RunPortRemovalAsync("forward", serial, local, cancellationToken);

	async Task RunPortCommandAsync(
		string direction,
		string serial,
		AdbPortSpec first,
		AdbPortSpec second,
		CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(serial);
		ArgumentNullException.ThrowIfNull(first);
		ArgumentNullException.ThrowIfNull(second);

		await RunCapturedAsync(
			["-s", serial, direction, first.ToSocketSpec(), second.ToSocketSpec()],
			$"`adb {direction} {first.ToSocketSpec()} {second.ToSocketSpec()}`",
			cancellationToken).ConfigureAwait(false);
	}

	async Task RunPortRemovalAsync(
		string direction,
		string serial,
		AdbPortSpec spec,
		CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(serial);
		ArgumentNullException.ThrowIfNull(spec);

		await RunCapturedAsync(
			["-s", serial, direction, "--remove", spec.ToSocketSpec()],
			$"`adb {direction} --remove {spec.ToSocketSpec()}`",
			cancellationToken).ConfigureAwait(false);
	}

	async Task RunCapturedAsync(string[] arguments, string description, CancellationToken cancellationToken)
	{
		var result = await ProcessRunner.RunAsync(
			_adbPath,
			arguments,
			environmentVariables: _environmentVariables,
			cancellationToken: cancellationToken).ConfigureAwait(false);

		if (!result.Success)
		{
			var detail = FirstLine(result.StandardError) ?? FirstLine(result.StandardOutput) ?? "no output";
			throw new InvalidOperationException($"{description} exited with code {result.ExitCode}: {detail}");
		}
	}

	/// <summary>
	/// Parses modern <c>adb reverse --list</c> rows as
	/// <c>&lt;transport&gt; &lt;device-side&gt; &lt;host-side&gt;</c>.
	/// </summary>
	/// <remarks>
	/// The role names follow adb's <c>reverse REMOTE LOCAL</c> syntax: <see cref="AdbPortRule.Remote"/>
	/// is the device-side spec and <see cref="AdbPortRule.Local"/> is the host-side spec.
	/// </remarks>
	internal static IReadOnlyList<AdbPortRule> ParseReverseList(string? output)
	{
		if (string.IsNullOrWhiteSpace(output))
			return [];

		var rules = new List<AdbPortRule>();
		foreach (var line in output.Split('\n'))
		{
			var columns = line.Split(ColumnSeparators, StringSplitOptions.RemoveEmptyEntries);
			if (columns.Length < 3)
				continue;

			var deviceSide = AdbPortSpec.TryParse(columns[^2]);
			var hostSide = AdbPortSpec.TryParse(columns[^1]);
			if (deviceSide is null || hostSide is null)
				continue;

			rules.Add(new AdbPortRule(Remote: deviceSide, Local: hostSide));
		}

		return rules;
	}

	static string? FirstLine(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
			return null;

		var newline = value.IndexOfAny(['\r', '\n']);
		var line = (newline < 0 ? value : value[..newline]).Trim();
		return line.Length == 0 ? null : line;
	}
}
