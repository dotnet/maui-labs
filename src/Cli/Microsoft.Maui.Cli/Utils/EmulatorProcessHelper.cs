// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Globalization;

namespace Microsoft.Maui.Cli.Utils;

/// <summary>
/// Helpers for identifying and cleaning up orphaned emulator child processes
/// (e.g. <c>crashpad_handler</c>) after the main emulator process is stopped.
/// </summary>
/// <remarks>
/// When the Android emulator is stopped via ADB (<c>adb emu kill</c>), the main
/// <c>qemu-system-aarch64</c> process is killed but its child processes (such as
/// <c>crashpad_handler</c>) may be left as orphans (reparented to PID 1).  Over
/// repeated start/stop cycles these orphans accumulate and consume memory.
/// This helper finds those children before the stop and terminates any survivors
/// after the stop completes.
/// </remarks>
internal static class EmulatorProcessHelper
{
	/// <summary>
	/// Extracts the console port number from an emulator device serial string
	/// (e.g. <c>"emulator-5554"</c> → <c>5554</c>).
	/// Returns <c>null</c> for any other format.
	/// </summary>
	internal static int? ParseEmulatorPort(string serial)
	{
		const string prefix = "emulator-";
		if (string.IsNullOrEmpty(serial) ||
			!serial.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			return null;

		return int.TryParse(serial[prefix.Length..], out var port) ? port : null;
	}

	/// <summary>
	/// Parses the output of <c>ps -eo pid,ppid</c> into a dictionary mapping
	/// each PID to its parent PID.  Header lines and malformed entries are skipped.
	/// </summary>
	internal static IReadOnlyDictionary<int, int> ParsePidPpidOutput(string psOutput)
	{
		var result = new Dictionary<int, int>();
		foreach (var line in psOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
		{
			var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length >= 2 &&
				int.TryParse(parts[0], out var pid) &&
				int.TryParse(parts[1], out var ppid))
			{
				result[pid] = ppid;
			}
		}
		return result;
	}

	/// <summary>
	/// Scans the output of <c>ps -eo pid,args</c> for a <c>qemu-system-*</c> process
	/// that owns the given console port and returns its PID.
	/// Returns <c>null</c> if no matching process is found.
	/// </summary>
	/// <param name="psOutput">Raw stdout from <c>ps -eo pid,args</c>.</param>
	/// <param name="port">The emulator console port (e.g. 5554).</param>
	internal static int? FindQemuPidFromPsOutput(string psOutput, int port)
	{
		var portStr = port.ToString(CultureInfo.InvariantCulture);
		foreach (var line in psOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
		{
			var trimmed = line.TrimStart();
			var spaceIdx = trimmed.IndexOf(' ');
			if (spaceIdx <= 0)
				continue;

			if (!int.TryParse(trimmed[..spaceIdx], out var pid))
				continue;

			var args = trimmed[spaceIdx..];

			// Only match qemu-system-* processes (the underlying emulator engine).
			if (!args.Contains("qemu-system-", StringComparison.Ordinal))
				continue;

			// Match the console port.  The emulator passes it as:
			//   -port 5554          (explicit port flag, possibly at end of line)
			//   @5554               (compact notation used in some versions)
			if (args.Contains($"-port {portStr} ", StringComparison.Ordinal) ||
				args.Contains($"-port {portStr}\t", StringComparison.Ordinal) ||
				args.EndsWith($"-port {portStr}", StringComparison.Ordinal) ||
				args.Contains($" @{portStr}", StringComparison.Ordinal))
			{
				return pid;
			}
		}
		return null;
	}

	/// <summary>
	/// Returns the PIDs that are <em>direct</em> children of <paramref name="parentPid"/>
	/// according to the supplied pid→ppid mapping.
	/// </summary>
	internal static IReadOnlyList<int> GetDirectChildPids(
		IReadOnlyDictionary<int, int> pidToParent, int parentPid)
	{
		var children = new List<int>();
		foreach (var (pid, ppid) in pidToParent)
		{
			if (ppid == parentPid)
				children.Add(pid);
		}
		return children;
	}

	// ── Platform operations ─────────────────────────────────────────────────

	/// <summary>
	/// Finds the PID of the <c>qemu-system-*</c> process for the given emulator serial
	/// on macOS or Linux.  Returns <c>null</c> on Windows or when the process cannot
	/// be identified.
	/// </summary>
	internal static int? FindEmulatorProcessId(string serial)
	{
		var port = ParseEmulatorPort(serial);
		if (port is null || (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux()))
			return null;

		try
		{
			var result = ProcessRunner.RunSync("ps", ["-eo", "pid,args"],
				timeout: TimeSpan.FromSeconds(5));
			if (!result.Success)
				return null;

			return FindQemuPidFromPsOutput(result.StandardOutput, port.Value);
		}
		catch (Exception ex)
		{
			Trace.WriteLine($"FindEmulatorProcessId failed for '{serial}': {ex.Message}");
			return null;
		}
	}

	/// <summary>
	/// Returns the PIDs of all direct child processes of <paramref name="parentPid"/>
	/// on macOS or Linux.  Returns an empty list on Windows or if an error occurs.
	/// </summary>
	internal static IReadOnlyList<int> GetChildProcessIds(int parentPid)
	{
		if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
			return [];

		try
		{
			var result = ProcessRunner.RunSync("ps", ["-eo", "pid,ppid"],
				timeout: TimeSpan.FromSeconds(5));
			if (!result.Success)
				return [];

			var pidToParent = ParsePidPpidOutput(result.StandardOutput);
			return GetDirectChildPids(pidToParent, parentPid);
		}
		catch (Exception ex)
		{
			Trace.WriteLine($"GetChildProcessIds failed for PID {parentPid}: {ex.Message}");
			return [];
		}
	}

	/// <summary>
	/// Terminates the specified processes.  Individual kill failures are traced
	/// but do not abort the overall operation.
	/// </summary>
	internal static void KillProcessIds(IReadOnlyList<int> pids)
	{
		foreach (var pid in pids)
		{
			try
			{
				using var process = Process.GetProcessById(pid);
				process.Kill(entireProcessTree: false);
			}
			catch (ArgumentException)
			{
				// Process already exited — nothing to do.
			}
			catch (Exception ex)
			{
				Trace.WriteLine($"Kill PID {pid} failed: {ex.Message}");
			}
		}
	}
}
