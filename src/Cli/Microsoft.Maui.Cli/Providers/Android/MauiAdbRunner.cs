// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui.Cli.Utils;
using Xamarin.Android.Tools;

namespace Microsoft.Maui.Cli.Providers.Android;

/// <summary>
/// <see cref="AdbRunner"/> with a corrected <c>adb reverse --list</c> reader.
/// </summary>
/// <remarks>
/// <para>
/// <c>adb forward --list</c> prints the device serial in its first column
/// (<c>emulator-5554 tcp:5000 tcp:5000</c>), but <c>adb reverse --list</c> prints the ADB
/// transport id instead (<c>host-17 tcp:5000 tcp:5000</c>). The upstream
/// <see cref="AdbRunner.ListReversePortsAsync"/> matches that first column against the requested
/// serial, so it silently returns an empty list even when reverse mappings exist.
/// </para>
/// <para>
/// An empty reverse list is indistinguishable from "no mapping present", which made every
/// reverse-mapping check fail closed (the DevFlow broker reverse could never be verified) and
/// made the foreign-mapping conflict guard unreachable. Reading the raw output here keeps both
/// checks real.
/// </para>
/// </remarks>
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

	/// <summary>
	/// Parses <c>adb reverse --list</c> output. Each mapping line is
	/// <c>&lt;transport&gt; &lt;device-side spec&gt; &lt;host-side spec&gt;</c>; the two port specs are
	/// read from the end of the line so an extra leading column never drops a mapping. Lines whose
	/// last two columns are not port specs are ignored rather than guessed at.
	/// </summary>
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

			// AdbPortRule mirrors `adb reverse <local-on-device> <remote-on-host>`, so the
			// second-to-last column is Local and the last column is Remote - the same column
			// order AdbRunner.ListForwardPortsAsync uses for `adb forward --list`.
			var local = AdbPortSpec.TryParse(columns[^2]);
			var remote = AdbPortSpec.TryParse(columns[^1]);
			if (local is null || remote is null)
				continue;

			rules.Add(new AdbPortRule(Remote: remote, Local: local));
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
