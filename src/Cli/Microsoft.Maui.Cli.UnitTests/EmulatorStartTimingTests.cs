// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using Microsoft.Maui.Cli.Providers.Android;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Maui.Cli.UnitTests;

/// <summary>
/// Live integration test that reproduces the 60-second delay observed on
/// <c>maui android emulator start &lt;name&gt;</c> when the target AVD is already booted.
///
/// The test is opt-in: it only runs when the environment variable
/// <c>MAUI_TEST_AVD_NAME</c> is set to the name of an AVD that is already running
/// and has finished booting (sys.boot_completed = 1). When not set, the test
/// exits silently so regular CI runs are not affected.
///
/// Methodology:
///   1. Resolve adb from ANDROID_HOME / ANDROID_SDK_ROOT.
///   2. Confirm via a direct <c>adb shell getprop sys.boot_completed</c> that
///      the emulator with the given AVD name is fully booted — record the
///      wall-clock time of that confirmation (T_adbBooted).
///   3. Invoke the exact same code path the CLI uses
///      (<see cref="AvdManager.StartAvdAsync"/>) and record its completion
///      time (T_toolReturned).
///   4. Compute the gap T_toolReturned - T_adbBooted.
///
/// Today this gap is approximately 60 seconds (caused by
/// <c>AdbRunner.ListDevicesAsync</c> looping through emulator devices and
/// running <c>adb -s emulator-XXXX emu avd name</c>, which blocks on adb's
/// internal socket/auth timeout). After the fix the gap should be &lt; 5s.
/// </summary>
public class EmulatorStartTimingTests
{
	readonly ITestOutputHelper _output;

	public EmulatorStartTimingTests(ITestOutputHelper output)
	{
		_output = output;
	}

	// Max acceptable gap between "adb confirms booted" and "StartAvdAsync returns"
	// when the emulator is already running. The buggy code path easily exceeds 60s;
	// a fix should bring this well under a few seconds.
	static readonly TimeSpan AcceptableGap = TimeSpan.FromSeconds(5);

	[Fact]
	public async Task StartAvdAsync_WhenAlreadyBooted_ReturnsSoonAfterAdbConfirmsBoot()
	{
		if (!TryResolveEnvironment(out var avdName, out var sdkPath, out var adbExe)) return;

		// 1. Locate the serial of the running emulator that matches the AVD name.
		var serial = await FindSerialForAvdAsync(adbExe, avdName, TimeSpan.FromSeconds(10));
		if (serial == null)
		{
			_output.WriteLine($"SKIP: no running emulator with AVD name '{avdName}' found. Please boot it first.");
			return;
		}
		_output.WriteLine($"Resolved AVD '{avdName}' to serial '{serial}'.");

		// 2. Confirm sys.boot_completed == 1 via adb directly. Record T0.
		var bootedAt = Stopwatch.StartNew();
		var booted = await IsBootCompletedAsync(adbExe, serial, TimeSpan.FromSeconds(10));
		var adbConfirmAtMs = bootedAt.ElapsedMilliseconds;
		if (!booted)
		{
			_output.WriteLine($"SKIP: sys.boot_completed != 1 on {serial}. Wait for boot to finish and re-run.");
			return;
		}
		_output.WriteLine($"adb confirmed sys.boot_completed=1 in {adbConfirmAtMs} ms.");

		// 3. Invoke the exact CLI code path: AvdManager.StartAvdAsync.
		var adb = new Adb(() => sdkPath);
		Assert.True(adb.IsAvailable, "Adb wrapper failed to resolve adb executable");

		var avdManager = new AvdManager(() => sdkPath, () => null, adb);

		var toolTimer = Stopwatch.StartNew();
		await avdManager.StartAvdAsync(avdName, coldBoot: false, wait: false, CancellationToken.None);
		toolTimer.Stop();

		var gap = toolTimer.Elapsed;
		_output.WriteLine($"AvdManager.StartAvdAsync returned in {gap.TotalMilliseconds:F0} ms.");
		_output.WriteLine($"Gap between adb-confirmed-boot and tool-returned: {gap.TotalMilliseconds:F0} ms " +
			$"(threshold {AcceptableGap.TotalMilliseconds:F0} ms).");

		Assert.True(gap < AcceptableGap,
			$"StartAvdAsync took {gap.TotalMilliseconds:F0} ms for an already-booted emulator. " +
			$"adb confirmed boot in {adbConfirmAtMs} ms. Expected the tool to return within " +
			$"{AcceptableGap.TotalMilliseconds:F0} ms. This reproduces the ~60s hang caused by " +
			$"AdbRunner.ListDevicesAsync looping `adb emu avd name` during the 'already running' probe.");
	}

	/// <summary>
	/// Cold-start scenario: the emulator is NOT running. We invoke
	/// <see cref="AvdManager.StartAvdAsync"/> with <c>wait=true</c> (same as
	/// <c>maui android emulator start --wait</c>) and concurrently poll
	/// <c>adb shell getprop sys.boot_completed</c>. We record:
	///   * T_adbBooted   — first time adb reports sys.boot_completed=1
	///   * T_toolReturned — when StartAvdAsync returns
	/// and report the gap between them. A well-behaved implementation should
	/// return within a few seconds of adb confirming the boot. A significant
	/// positive gap indicates a post-boot hang (e.g. the same probe-timeout
	/// bug manifesting inside BootEmulatorAsync).
	///
	/// Skips unless MAUI_TEST_AVD_NAME is set AND the AVD is not already running.
	/// </summary>
	[Fact]
	public async Task StartAvdAsync_FromCold_ReturnsSoonAfterAdbConfirmsBoot()
	{
		if (!TryResolveEnvironment(out var avdName, out var sdkPath, out var adbExe)) return;

		var preExisting = await FindSerialForAvdAsync(adbExe, avdName, TimeSpan.FromSeconds(10));
		if (preExisting != null)
		{
			_output.WriteLine($"SKIP: AVD '{avdName}' is already running on {preExisting}. " +
				"Stop the emulator (adb emu kill) and re-run to exercise the cold-start path.");
			return;
		}

		var adb = new Adb(() => sdkPath);
		Assert.True(adb.IsAvailable, "Adb wrapper failed to resolve adb executable");
		var avdManager = new AvdManager(() => sdkPath, () => null, adb);

		using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));

		// Start a parallel poller that watches for sys.boot_completed=1 on any
		// emulator that reports our AVD name. Record the first moment it sees boot.
		long? adbBootedAtMs = null;
		var overall = Stopwatch.StartNew();
		var pollerTask = Task.Run(async () =>
		{
			while (!cts.IsCancellationRequested && adbBootedAtMs == null)
			{
				try
				{
					var s = await FindSerialForAvdAsync(adbExe, avdName, TimeSpan.FromSeconds(5));
					if (s != null && await IsBootCompletedAsync(adbExe, s, TimeSpan.FromSeconds(5)))
					{
						adbBootedAtMs = overall.ElapsedMilliseconds;
						_output.WriteLine($"[poller] adb confirmed sys.boot_completed=1 at +{adbBootedAtMs} ms on {s}.");
						return;
					}
				}
				catch { /* transient adb errors while the emulator starts — retry */ }
				await Task.Delay(500, cts.Token);
			}
		}, cts.Token);

		long toolReturnedAtMs;
		try
		{
			_output.WriteLine($"[tool] Calling AvdManager.StartAvdAsync(name='{avdName}', wait=true)…");
			await avdManager.StartAvdAsync(avdName, coldBoot: false, wait: true, cts.Token);
			toolReturnedAtMs = overall.ElapsedMilliseconds;
			_output.WriteLine($"[tool] StartAvdAsync returned at +{toolReturnedAtMs} ms.");
		}
		finally
		{
			// Give the poller a moment to record, then clean up regardless of outcome.
			try { await Task.WhenAny(pollerTask, Task.Delay(1000)); } catch { }
			cts.Cancel();
		}

		if (adbBootedAtMs == null)
		{
			_output.WriteLine("WARN: poller never observed sys.boot_completed=1. " +
				"Either StartAvdAsync failed before boot, or the poller could not resolve the AVD.");
			return;
		}

		var gap = toolReturnedAtMs - adbBootedAtMs.Value;
		_output.WriteLine($"Gap between adb-confirmed-boot and tool-returned: {gap} ms " +
			$"(threshold {AcceptableGap.TotalMilliseconds:F0} ms).");

		Assert.True(gap < AcceptableGap.TotalMilliseconds,
			$"StartAvdAsync(wait:true) returned {gap} ms AFTER adb first reported sys.boot_completed=1. " +
			$"The tool is hanging past the actual boot (likely the same `adb emu avd name` probe " +
			$"timing out inside BootEmulatorAsync's post-boot device list).");
	}

	bool TryResolveEnvironment(out string avdName, out string sdkPath, out string adbExe)
	{
		avdName = Environment.GetEnvironmentVariable("MAUI_TEST_AVD_NAME") ?? "";
		sdkPath = Environment.GetEnvironmentVariable("ANDROID_HOME")
			?? Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT")
			?? "";
		adbExe = "";

		if (string.IsNullOrWhiteSpace(avdName))
		{
			_output.WriteLine("SKIP: set MAUI_TEST_AVD_NAME to the name of the AVD under test.");
			return false;
		}
		if (string.IsNullOrWhiteSpace(sdkPath) || !Directory.Exists(sdkPath))
		{
			_output.WriteLine($"SKIP: ANDROID_HOME / ANDROID_SDK_ROOT not set or does not exist (got '{sdkPath}').");
			return false;
		}
		adbExe = Path.Combine(sdkPath, "platform-tools",
			OperatingSystem.IsWindows() ? "adb.exe" : "adb");
		if (!File.Exists(adbExe))
		{
			_output.WriteLine($"SKIP: adb not found at '{adbExe}'.");
			return false;
		}
		return true;
	}

	static async Task<string?> FindSerialForAvdAsync(string adbExe, string avdName, TimeSpan timeout)
	{
		// `adb devices` → list serials. For each emulator serial, call `adb emu avd name` with a short
		// timeout; if it matches, return. We do this ourselves (not via AdbRunner) so the test isn't
		// subject to the very bug we're measuring.
		var (code, stdout, _) = await RunAdbAsync(adbExe, new[] { "devices" }, timeout);
		if (code != 0) return null;

		foreach (var line in stdout.Split('\n'))
		{
			var trimmed = line.Trim();
			if (trimmed.Length == 0 || trimmed.StartsWith("List of devices")) continue;
			var parts = trimmed.Split('\t', ' ');
			if (parts.Length < 2) continue;
			var serial = parts[0];
			if (!serial.StartsWith("emulator-", StringComparison.OrdinalIgnoreCase)) continue;
			if (!string.Equals(parts[1], "device", StringComparison.OrdinalIgnoreCase)) continue;

			var (c, so, _) = await RunAdbAsync(adbExe, new[] { "-s", serial, "emu", "avd", "name" }, timeout);
			if (c != 0) continue;

			foreach (var nl in so.Split('\n'))
			{
				var t = nl.Trim();
				if (t.Length == 0 || t.Equals("OK", StringComparison.OrdinalIgnoreCase)) continue;
				if (string.Equals(t, avdName, StringComparison.OrdinalIgnoreCase))
					return serial;
				break;
			}
		}
		return null;
	}

	static async Task<bool> IsBootCompletedAsync(string adbExe, string serial, TimeSpan timeout)
	{
		var (code, stdout, _) = await RunAdbAsync(adbExe,
			new[] { "-s", serial, "shell", "getprop", "sys.boot_completed" }, timeout);
		return code == 0 && stdout.Trim() == "1";
	}

	static async Task<(int ExitCode, string Stdout, string Stderr)> RunAdbAsync(
		string adbExe, string[] args, TimeSpan timeout)
	{
		var psi = new ProcessStartInfo(adbExe)
		{
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true,
		};
		foreach (var a in args) psi.ArgumentList.Add(a);

		using var p = Process.Start(psi)!;
		using var cts = new CancellationTokenSource(timeout);
		try
		{
			await p.WaitForExitAsync(cts.Token);
		}
		catch (OperationCanceledException)
		{
			try { p.Kill(entireProcessTree: true); } catch { }
			return (-1, "", "timeout");
		}
		var so = await p.StandardOutput.ReadToEndAsync();
		var se = await p.StandardError.ReadToEndAsync();
		return (p.ExitCode, so, se);
	}
}
