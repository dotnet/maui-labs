namespace Microsoft.Maui.DevFlow.Driver;

/// <summary>
/// Base driver that delegates most operations to the AgentClient.
/// Platform-specific drivers override platform-dependent methods.
/// </summary>
public abstract class AppDriverBase : IAppDriver
{
    protected AgentClient? Client { get; private set; }

    public abstract string Platform { get; }

    public virtual async Task ConnectAsync(string host = "localhost", int port = 9223)
    {
        await SetupPlatformAsync(host, port);
        Client = new AgentClient(host, port);

        var status = await Client.GetStatusAsync();
        if (status == null)
            throw new InvalidOperationException($"Could not connect to Microsoft.Maui.DevFlow Agent at {host}:{port}");
    }

    /// <summary>
    /// Platform-specific setup (e.g., adb reverse for Android).
    /// </summary>
    protected virtual Task SetupPlatformAsync(string host, int port) => Task.CompletedTask;

    public Task<AgentStatus?> GetStatusAsync()
        => EnsureClient().GetStatusAsync();

    public Task<List<ElementInfo>> GetTreeAsync(int maxDepth = 0)
        => EnsureClient().GetTreeAsync(maxDepth);

    public Task<List<ElementInfo>> QueryAsync(string? type = null, string? automationId = null, string? text = null)
        => EnsureClient().QueryAsync(type, automationId, text);

    public Task<bool> TapAsync(string elementId)
        => EnsureClient().TapAsync(elementId);

    public Task<bool> FillAsync(string elementId, string text)
        => EnsureClient().FillAsync(elementId, text);

    public Task<bool> ClearAsync(string elementId)
        => EnsureClient().ClearAsync(elementId);

    public virtual Task<byte[]?> ScreenshotAsync()
        => EnsureClient().ScreenshotAsync();

    public virtual Task BackAsync()
        => Task.CompletedTask;

    public virtual Task PressKeyAsync(string key)
        => Task.CompletedTask;

    public virtual Task StartRecordingAsync(string outputFile, int timeoutSeconds = 30)
        => throw new NotSupportedException($"Screen recording is not supported on {Platform}.");

    public virtual Task<string> StopRecordingAsync()
        => throw new NotSupportedException($"Screen recording is not supported on {Platform}.");

    /// <summary>
    /// Spawns a watchdog process that kills the recording after the timeout.
    /// Returns the watchdog PID, or null if spawning failed.
    /// </summary>
    protected static int? SpawnWatchdog(int recordingPid, int timeoutSeconds)
    {
        if (!OperatingSystem.IsWindows())
        {
            var psi = new System.Diagnostics.ProcessStartInfo("bash",
                $"-c \"sleep {timeoutSeconds} && kill -INT {recordingPid} 2>/dev/null\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            var watchdog = System.Diagnostics.Process.Start(psi);
            return watchdog?.Id;
        }
        return null;
    }

    /// <summary>
    /// Kills the watchdog process if it's still running.
    /// </summary>
    protected static void KillWatchdog(int? watchdogPid)
    {
        if (watchdogPid == null) return;
        try
        {
            var proc = System.Diagnostics.Process.GetProcessById(watchdogPid.Value);
            if (!proc.HasExited)
                proc.Kill(entireProcessTree: true);
        }
        catch { }
    }

    /// <summary>
    /// Sends SIGINT to a process for graceful shutdown.
    /// On Windows, kills the process directly.
    /// </summary>
    protected static void SendInterrupt(int pid)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var proc = System.Diagnostics.Process.GetProcessById(pid);
                if (!proc.HasExited)
                    proc.Kill();
            }
            catch { }
        }
        else
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("kill", $"-INT {pid}")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                };
                using var proc = System.Diagnostics.Process.Start(psi);
                proc?.WaitForExit(5000);
            }
            catch { }
        }
    }

    /// <summary>
    /// Ensures no recording is currently in progress. Throws if one is active.
    /// </summary>
    protected static void EnsureNotRecording()
    {
        if (RecordingStateManager.IsRecording())
            throw new InvalidOperationException(
                "A recording is already in progress. Stop it first with 'maui recording stop'.");
    }

    protected AgentClient EnsureClient()
        => Client ?? throw new InvalidOperationException("Not connected. Call ConnectAsync first.");

    public virtual void Dispose()
    {
        Client?.Dispose();
    }
}
