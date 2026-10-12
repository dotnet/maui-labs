// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Microsoft.Maui.Cli.Utils;

/// <summary>
/// Result of a process execution.
/// </summary>
public record ProcessResult
{
	public int ExitCode { get; init; }
	public string StandardOutput { get; init; } = string.Empty;
	public string StandardError { get; init; } = string.Empty;
	public bool Success => ExitCode == 0;
	public TimeSpan Duration { get; init; }
}

/// <summary>
/// Utility for running external processes with output capture.
/// </summary>
public static class ProcessRunner
{
	static readonly TimeSpan DefaultProcessTimeout = TimeSpan.FromMinutes(5);
	const int ContinuousInputDelayMs = 250;
	static readonly TimeSpan InputTaskCleanupTimeout = TimeSpan.FromSeconds(2);
	static readonly TimeSpan OutputStreamCompletionTimeout = TimeSpan.FromSeconds(5);
	/// <summary>
	/// Validates a process argument value to prevent command injection.
	/// Rejects values containing shell metacharacters that could escape argument boundaries.
	/// </summary>
	internal static string SanitizeArg(string value)
	{
		ArgumentNullException.ThrowIfNull(value);

		// Reject values containing shell metacharacters that could enable injection
		char[] forbidden = [';', '&', '|', '`', '$', '\n', '\r', '\0'];
		if (value.IndexOfAny(forbidden) >= 0)
			throw new ArgumentException($"Argument contains forbidden characters: {value}", nameof(value));

		return value;
	}
	/// <summary>
	/// Runs a process synchronously and captures output.
	/// Uses ProcessStartInfo.ArgumentList to avoid shell quoting issues.
	/// </summary>
	public static ProcessResult RunSync(
		string fileName,
		string[] args,
		string? workingDirectory = null,
		Dictionary<string, string>? environmentVariables = null,
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default)
	{
		var stopwatch = Stopwatch.StartNew();
		var stdoutBuilder = new StringBuilder();
		var stderrBuilder = new StringBuilder();

		using var process = new Process();
		process.StartInfo = new ProcessStartInfo
		{
			FileName = fileName,
			WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true
		};

		foreach (var arg in args)
			process.StartInfo.ArgumentList.Add(arg);

		if (environmentVariables != null)
		{
			foreach (var kvp in environmentVariables)
			{
				process.StartInfo.EnvironmentVariables[kvp.Key] = kvp.Value;
			}
		}

		process.OutputDataReceived += (_, e) =>
		{
			if (e.Data != null)
				stdoutBuilder.AppendLine(e.Data);
		};

		process.ErrorDataReceived += (_, e) =>
		{
			if (e.Data != null)
				stderrBuilder.AppendLine(e.Data);
		};

		try
		{
			process.Start();
			process.BeginOutputReadLine();
			process.BeginErrorReadLine();

			var effectiveTimeout = timeout ?? DefaultProcessTimeout;
			var completed = process.WaitForExit((int)effectiveTimeout.TotalMilliseconds);

			if (!completed)
			{
				try
				{ process.Kill(entireProcessTree: true); }
				catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"Kill after sync timeout failed: {ex.Message}"); }
				throw new TimeoutException($"Process '{fileName}' timed out after {effectiveTimeout.TotalSeconds}s");
			}

			// Ensure all output is flushed
			process.WaitForExit();

			stopwatch.Stop();

			return new ProcessResult
			{
				ExitCode = process.ExitCode,
				StandardOutput = stdoutBuilder.ToString(),
				StandardError = stderrBuilder.ToString(),
				Duration = stopwatch.Elapsed
			};
		}
		catch (Exception ex) when (ex is not TimeoutException)
		{
			stopwatch.Stop();
			return new ProcessResult
			{
				ExitCode = -1,
				StandardOutput = stdoutBuilder.ToString(),
				StandardError = ex.Message,
				Duration = stopwatch.Elapsed
			};
		}
	}

	/// <summary>
	/// Runs a process asynchronously and captures output.
	/// Uses ProcessStartInfo.ArgumentList to avoid shell quoting issues.
	/// </summary>
	public static async Task<ProcessResult> RunAsync(
		string fileName,
		string[] args,
		string? workingDirectory = null,
		Dictionary<string, string>? environmentVariables = null,
		TimeSpan? timeout = null,
		string? continuousInput = null,
		IEnumerable<string>? environmentVariablesToRemove = null,
		Action<string>? onOutputData = null,
		Action<string>? onErrorData = null,
		CancellationToken cancellationToken = default)
	{
		var stopwatch = Stopwatch.StartNew();
		var stdoutBuilder = new StringBuilder();
		var stderrBuilder = new StringBuilder();

		using var process = new Process();
		process.StartInfo = new ProcessStartInfo
		{
			FileName = fileName,
			WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			RedirectStandardInput = continuousInput != null,
			CreateNoWindow = true
		};

		foreach (var arg in args)
			process.StartInfo.ArgumentList.Add(arg);

		// Remove inherited env vars first (e.g. MSBuild SDK path vars set by a parent dotnet run)
		if (environmentVariablesToRemove != null)
		{
			foreach (var key in environmentVariablesToRemove)
				process.StartInfo.EnvironmentVariables.Remove(key);
		}

		if (environmentVariables != null)
		{
			foreach (var kvp in environmentVariables)
			{
				process.StartInfo.EnvironmentVariables[kvp.Key] = kvp.Value;
			}
		}

		var outputTcs = new TaskCompletionSource<bool>();
		var errorTcs = new TaskCompletionSource<bool>();

		process.OutputDataReceived += (_, e) =>
		{
			if (e.Data != null)
			{
				stdoutBuilder.AppendLine(e.Data);
				InvokeCallbackSafely(onOutputData, e.Data, "stdout");
			}
			else
				outputTcs.TrySetResult(true);
		};

		process.ErrorDataReceived += (_, e) =>
		{
			if (e.Data != null)
			{
				stderrBuilder.AppendLine(e.Data);
				InvokeCallbackSafely(onErrorData, e.Data, "stderr");
			}
			else
				errorTcs.TrySetResult(true);
		};

		try
		{
			process.Start();
			process.BeginOutputReadLine();
			process.BeginErrorReadLine();

			var effectiveTimeout = timeout ?? DefaultProcessTimeout;

			using var timeoutCts = new CancellationTokenSource(effectiveTimeout);
			using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
				cancellationToken, timeoutCts.Token);

			// If continuous input is requested, start a background task to write it
			Task? inputTask = null;
			if (continuousInput != null)
			{
				inputTask = Task.Run(async () =>
				{
					while (!process.HasExited && !linkedCts.Token.IsCancellationRequested)
					{
						try
						{
							await process.StandardInput.WriteLineAsync(continuousInput);
							await process.StandardInput.FlushAsync();
						}
						catch (Exception ex)
						{
							System.Diagnostics.Trace.WriteLine($"Continuous input write failed: {ex.Message}");
							break;
						}
						await Task.Delay(ContinuousInputDelayMs, linkedCts.Token);
					}
				}, linkedCts.Token);
			}

			try
			{
				await process.WaitForExitAsync(linkedCts.Token);
			}
			catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
			{
				await KillAndWaitForExitAsync(process, "async timeout");
				throw new TimeoutException($"Process '{fileName}' timed out after {effectiveTimeout.TotalSeconds}s");
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				await KillAndWaitForExitAsync(process, "caller cancellation");
				throw;
			}

			// Wait for input task to complete if present
			if (inputTask != null)
			{
				try
				{ await inputTask.WaitAsync(InputTaskCleanupTimeout); }
				catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"Input task cleanup failed: {ex.Message}"); }
			}

			// Wait for output streams to complete
			await Task.WhenAll(outputTcs.Task, errorTcs.Task).WaitAsync(OutputStreamCompletionTimeout);

			stopwatch.Stop();

			return new ProcessResult
			{
				ExitCode = process.ExitCode,
				StandardOutput = stdoutBuilder.ToString(),
				StandardError = stderrBuilder.ToString(),
				Duration = stopwatch.Elapsed
			};
		}
		catch (Exception ex) when (ex is not TimeoutException && ex is not OperationCanceledException)
		{
			stopwatch.Stop();
			return new ProcessResult
			{
				ExitCode = -1,
				StandardOutput = stdoutBuilder.ToString(),
				StandardError = ex.Message,
				Duration = stopwatch.Elapsed
			};
		}
	}

	static async Task KillAndWaitForExitAsync(Process process, string reason)
	{
		var descendants = CaptureDescendantProcesses(process.Id);
		if (!process.HasExited)
		{
			try
			{
				process.Kill(entireProcessTree: true);
			}
			catch (Exception ex)
			{
				System.Diagnostics.Trace.WriteLine($"Kill after {reason} failed: {ex.Message}");
			}
		}

		await WaitForExitAsync(process, reason);
		foreach (var descendant in descendants)
		{
			try
			{
				if (!descendant.HasExited)
					descendant.Kill(entireProcessTree: false);
			}
			catch (Exception ex)
			{
				System.Diagnostics.Trace.WriteLine($"Kill descendant PID {descendant.Id} after {reason} failed: {ex.Message}");
			}

			await WaitForExitAsync(descendant, reason);
			descendant.Dispose();
		}
	}

	static async Task WaitForExitAsync(Process process, string reason)
	{
		try
		{
			await process.WaitForExitAsync();
		}
		catch (InvalidOperationException)
		{
			// The process exited before a wait handle could be created.
		}
		catch (Exception ex)
		{
			System.Diagnostics.Trace.WriteLine($"Wait for PID {process.Id} after {reason} failed: {ex.Message}");
		}
	}

	static IReadOnlyList<Process> CaptureDescendantProcesses(int rootProcessId)
	{
		try
		{
			var parentsByProcess = OperatingSystem.IsWindows()
				? GetWindowsProcessParents()
				: GetUnixProcessParents();
			var descendants = new List<Process>();
			var queue = new Queue<int>();
			queue.Enqueue(rootProcessId);

			while (queue.Count > 0)
			{
				var parentProcessId = queue.Dequeue();
				foreach (var (processId, candidateParentProcessId) in parentsByProcess)
				{
					if (candidateParentProcessId != parentProcessId)
						continue;

					try
					{
						descendants.Add(Process.GetProcessById(processId));
						queue.Enqueue(processId);
					}
					catch (ArgumentException)
					{
						// Process exited after the snapshot.
					}
				}
			}

			return descendants;
		}
		catch (Exception ex)
		{
			System.Diagnostics.Trace.WriteLine($"Capture descendant processes failed: {ex.Message}");
			return [];
		}
	}

	static IReadOnlyList<(int ProcessId, int ParentProcessId)> GetUnixProcessParents()
	{
		var result = RunSync("ps", ["-eo", "pid=,ppid="], timeout: TimeSpan.FromSeconds(5));
		if (!result.Success)
			return [];

		var processParents = new List<(int, int)>();
		foreach (var line in result.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
			if (parts.Length == 2
				&& int.TryParse(parts[0], out var processId)
				&& int.TryParse(parts[1], out var parentProcessId))
			{
				processParents.Add((processId, parentProcessId));
			}
		}

		return processParents;
	}

	static IReadOnlyList<(int ProcessId, int ParentProcessId)> GetWindowsProcessParents()
	{
		var snapshot = CreateToolhelp32Snapshot(0x00000002, 0);
		if (snapshot == new IntPtr(-1))
			return [];

		try
		{
			var entry = new ProcessEntry32
			{
				Size = (uint)Marshal.SizeOf<ProcessEntry32>()
			};
			var processParents = new List<(int, int)>();
			if (!Process32First(snapshot, ref entry))
				return processParents;

			do
			{
				processParents.Add(((int)entry.ProcessId, (int)entry.ParentProcessId));
				entry.Size = (uint)Marshal.SizeOf<ProcessEntry32>();
			}
			while (Process32Next(snapshot, ref entry));

			return processParents;
		}
		finally
		{
			CloseHandle(snapshot);
		}
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	struct ProcessEntry32
	{
		internal uint Size;
		internal uint Usage;
		internal uint ProcessId;
		internal IntPtr DefaultHeapId;
		internal uint ModuleId;
		internal uint Threads;
		internal uint ParentProcessId;
		internal int PriorityClassBase;
		internal uint Flags;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
		internal string ExecutableFile;
	}

	[DllImport("kernel32.dll", SetLastError = true)]
	static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	static extern bool Process32First(IntPtr snapshot, ref ProcessEntry32 entry);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry32 entry);

	[DllImport("kernel32.dll", SetLastError = true)]
	static extern bool CloseHandle(IntPtr handle);

	static void InvokeCallbackSafely(Action<string>? callback, string data, string streamName)
	{
		if (callback is null)
			return;

		try
		{
			callback(data);
		}
		catch (Exception ex)
		{
			Trace.WriteLine($"Process {streamName} callback failed: {ex.Message}");
		}
	}

	/// <summary>
	/// Re-launches the current process with administrator elevation (Windows UAC).
	/// Returns true if the elevated process completed successfully, false if the user
	/// cancelled the UAC prompt or the process failed.
	/// </summary>
	public static bool RelaunchElevated()
	{
		if (!PlatformDetector.IsWindows)
			return false;

		// Don't try to elevate if already running as admin — prevents infinite loop
		if (IsRunningElevated())
			return false;

		var processPath = Environment.ProcessPath;
		if (string.IsNullOrEmpty(processPath))
			return false;

		// Reconstruct command line args (skip the first which is the exe path)
		var args = Environment.GetCommandLineArgs().Skip(1);

		var psi = new ProcessStartInfo
		{
			FileName = processPath,
			Verb = "runas",
			UseShellExecute = true,
		};

		foreach (var arg in args)
			psi.ArgumentList.Add(arg);

		try
		{
			using var process = Process.Start(psi);
			process?.WaitForExit();
			return process?.ExitCode == 0;
		}
		catch (System.ComponentModel.Win32Exception ex)
		{
			System.Diagnostics.Trace.WriteLine($"UAC elevation cancelled or failed: {ex.Message}");
			return false;
		}
	}

	/// <summary>
	/// Checks whether the current process is running with administrator privileges.
	/// </summary>
	public static bool IsRunningElevated()
	{
		if (!OperatingSystem.IsWindows())
			return false;

		try
		{
			using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
			var principal = new System.Security.Principal.WindowsPrincipal(identity);
			return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
		}
		catch (Exception ex)
		{
			System.Diagnostics.Trace.WriteLine($"Elevation check failed: {ex.Message}");
			return false;
		}
	}

	/// <summary>
	/// Checks if a command exists in PATH.
	/// </summary>
	public static bool CommandExists(string command)
	{
		try
		{
			var whichCommand = PlatformDetector.IsWindows ? "where" : "which";
			var result = RunSync(whichCommand, [command], timeout: TimeSpan.FromSeconds(5));
			return result.ExitCode == 0;
		}
		catch (Exception ex)
		{
			System.Diagnostics.Trace.WriteLine($"CommandExists check for '{command}' failed: {ex.Message}");
			return false;
		}
	}

	/// <summary>
	/// Gets the full path of a command.
	/// </summary>
	public static string? GetCommandPath(string command)
	{
		try
		{
			var whichCommand = PlatformDetector.IsWindows ? "where" : "which";
			var result = RunSync(whichCommand, [command], timeout: TimeSpan.FromSeconds(5));
			if (result.ExitCode == 0)
			{
				var path = result.StandardOutput.Trim().Split('\n').FirstOrDefault()?.Trim();
				if (!string.IsNullOrEmpty(path) && File.Exists(path))
					return path;
			}
		}
		catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"Command path lookup for '{command}' failed: {ex.Message}"); }
		return null;
	}
}
