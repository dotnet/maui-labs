using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.Cli.DevFlow;

internal sealed class RecordingOperations(
    Func<string, string?, bool, IAppDriver> createDriver,
    Func<RecordingState?> readState)
{
    internal static IAppDriver CreateDefaultDriver(string platform) =>
        platform.Equals("ios", StringComparison.OrdinalIgnoreCase)
            || platform.Equals("iossimulator", StringComparison.OrdinalIgnoreCase)
            ? new RecordingIOSSimulatorAppDriver()
            : AppDriverFactory.Create(platform);

    internal async Task<string> StartAsync(string platform, string? device, string filename, int timeout)
    {
        using var driver = createDriver(platform, device, true);
        await driver.StartRecordingAsync(filename, timeout);
        EnsureProcessStarted(readState());

        return Path.GetFullPath(filename);
    }

    internal async Task<(string Path, long Size)> StopAsync()
    {
        var state = readState() ?? throw new InvalidOperationException("No active recording found.");
        using var driver = createDriver(state.Platform, state.Serial, false);
        var outputFile = await driver.StopRecordingAsync();

        return (outputFile, File.Exists(outputFile) ? new FileInfo(outputFile).Length : 0);
    }

    internal RecordingState? GetStatus()
    {
        var state = RecordingStateManager.Load();

        return state is null || !RecordingStateManager.IsRecording() ? null : state;
    }

    internal static IAppDriver CreateDriver(Func<string, IAppDriver> factory, string platform, string? device, bool starting)
    {
        var isIos = platform.Equals("ios", StringComparison.OrdinalIgnoreCase)
            || platform.Equals("iossimulator", StringComparison.OrdinalIgnoreCase);
        if (starting
            && isIos
            && string.IsNullOrWhiteSpace(device))
        {
            throw new ArgumentException("Specify --device <simulator UDID> for iOS recording.", nameof(device));
        }

        var driver = factory(platform);
        if (driver is iOSSimulatorAppDriver simulator)
            simulator.DeviceUdid = device;
        else if (driver is AndroidAppDriver android)
            android.Serial = device;

        return driver;
    }

    internal static void EnsureProcessStarted(RecordingState? state,
        Func<int, bool>? isProcessRunning = null, Action<int?>? killWatchdog = null, Action? deleteState = null)
    {
        if (state is null)
            throw new InvalidOperationException("Recording process did not persist startup state.");

        isProcessRunning ??= static pid =>
        {
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById(pid);
                return !process.HasExited;
            }
            catch (ArgumentException)
            {
                return false;
            }
        };

        if (isProcessRunning(state.RecordingPid))
            return;

        try
        {
            (killWatchdog ?? KillWatchdog)(state.WatchdogPid);
        }
        finally
        {
            (deleteState ?? RecordingStateManager.Delete)();
        }

        throw new InvalidOperationException($"{state.Platform} recording process exited during startup.");
    }

    private static void KillWatchdog(int? pid)
    {
        if (pid is null)
            return;

        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid.Value);
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (ArgumentException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }
}

internal sealed class RecordingIOSSimulatorAppDriver : iOSSimulatorAppDriver
{
    public override async Task StartRecordingAsync(string outputFile, int timeoutSeconds = 30)
    {
        EnsureNotRecording();
        if (string.IsNullOrWhiteSpace(DeviceUdid))
            throw new InvalidOperationException("DeviceUdid must be set for simulator operations.");

        var fullPath = Path.GetFullPath(outputFile);
        var startInfo = new System.Diagnostics.ProcessStartInfo("xcrun")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("simctl");
        startInfo.ArgumentList.Add("io");
        startInfo.ArgumentList.Add(DeviceUdid);
        startInfo.ArgumentList.Add("recordVideo");
        startInfo.ArgumentList.Add("--codec");
        startInfo.ArgumentList.Add("h264");
        startInfo.ArgumentList.Add(fullPath);

        var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start xcrun simctl io recordVideo");

        await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
        process.Refresh();
        var hasExited = process.HasExited;
        var standardError = hasExited
            ? await process.StandardError.ReadToEndAsync().ConfigureAwait(false)
            : string.Empty;
        ThrowIfProcessExited(hasExited, hasExited ? process.ExitCode : 0, standardError);

        var watchdogPid = SpawnWatchdog(process.Id, timeoutSeconds);
        RecordingStateManager.Save(new RecordingState
        {
            RecordingPid = process.Id,
            WatchdogPid = watchdogPid,
            OutputFile = fullPath,
            Platform = "ios",
            StartedAt = DateTimeOffset.UtcNow,
            TimeoutSeconds = timeoutSeconds,
        });
    }

    internal static void ThrowIfProcessExited(bool hasExited, int exitCode, string standardError)
    {
        if (!hasExited)
            return;

        var detail = standardError.Trim();
        var suffix = detail.Length == 0 ? string.Empty : $": {detail}";
        throw new InvalidOperationException(
            $"xcrun simctl io recordVideo exited during startup with code {exitCode}{suffix}");
    }
}
