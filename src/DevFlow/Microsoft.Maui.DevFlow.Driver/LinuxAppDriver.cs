using System.Diagnostics;

namespace Microsoft.Maui.DevFlow.Driver;

/// <summary>
/// Driver for Linux (GTK) MAUI apps.
/// Direct localhost connection, no special setup needed (like Windows).
/// Dialog detection uses the agent's /api/tree endpoint initially.
/// </summary>
public class LinuxAppDriver : AppDriverBase, IAlertDriver
{
    public override string Platform => "Linux";

    /// <summary>
    /// The PID of the Linux app process.
    /// </summary>
    public int? ProcessId { get; set; }

    /// <summary>
    /// The app name or binary name to find the process automatically.
    /// </summary>
    public string? AppName { get; set; }

    /// <summary>
    /// Detect an alert/dialog by inspecting the MAUI visual tree via the agent.
    /// Looks for common dialog patterns (e.g., AlertDialog elements).
    /// </summary>
    public async Task<AlertInfo?> DetectAlertAsync()
    {
        var client = EnsureClient();
        try
        {
            var tree = await client.GetTreeAsync();
            return FindAlertCandidate(tree)?.Info;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Dismiss the current alert by tapping a button found in the visual tree.
    /// </summary>
    public async Task DismissAlertAsync(string? buttonLabel = null)
    {
        var client = EnsureClient();
        var tree = await client.GetTreeAsync();
        var candidate = FindAlertCandidate(tree);
        if (candidate == null)
            throw new InvalidOperationException("No alert detected to dismiss.");

        // Find and tap the target button
        var button = buttonLabel != null
            ? candidate.Buttons.FirstOrDefault(b => b.Button.Label.Equals(buttonLabel, StringComparison.OrdinalIgnoreCase))
            : candidate.Buttons.FirstOrDefault();

        if (button == null)
        {
            var available = string.Join(", ", candidate.Info.Buttons.Select(b => b.Label));
            throw new InvalidOperationException($"Button '{buttonLabel}' not found. Available: {available}");
        }

        await client.TapAsync(button.ElementId);
    }

    /// <summary>
    /// Convenience: detect and dismiss an alert if present.
    /// </summary>
    public async Task<AlertInfo?> HandleAlertIfPresentAsync(string? buttonLabel = null)
    {
        var alert = await DetectAlertAsync();
        if (alert == null) return null;

        await DismissAlertAsync(buttonLabel);
        return alert;
    }

    public async Task<AlertActionResult> HandleAlertAsync(
        string? buttonLabel = null,
        string? expectedRevision = null)
    {
        var client = EnsureClient();
        var tree = await client.GetTreeAsync();
        var candidate = FindAlertCandidate(tree);
        if (candidate is null)
            return new(null, MatchesExpected: true, Dismissed: false);
        if (!string.IsNullOrEmpty(expectedRevision) &&
            !string.Equals(expectedRevision, AlertRevision.Create(candidate.Info), StringComparison.Ordinal))
        {
            return new(candidate.Info, MatchesExpected: false, Dismissed: false);
        }

        var target = buttonLabel is not null
            ? candidate.Buttons.FirstOrDefault(item =>
                item.Button.Label.Equals(buttonLabel, StringComparison.OrdinalIgnoreCase))
            : candidate.Buttons.FirstOrDefault();
        if (target is null)
            return new(candidate.Info, MatchesExpected: true, Dismissed: false);

        await client.TapAsync(target.ElementId);
        return new(candidate.Info, MatchesExpected: true, Dismissed: true);
    }

    // ──────────────────────────────────────────────
    // Screen Recording via ffmpeg
    // ──────────────────────────────────────────────

    public override async Task StartRecordingAsync(string outputFile, int timeoutSeconds = 30)
    {
        EnsureNotRecording();
        EnsureFfmpegLinux();

        var fullPath = Path.GetFullPath(outputFile);
        if (!fullPath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
            fullPath = Path.ChangeExtension(fullPath, ".mp4");

        var captureFormat = LinuxDisplayServer.GetFfmpegCaptureFormat();
        var captureInput = LinuxDisplayServer.GetDefaultCaptureDisplay();
        var psi = new ProcessStartInfo("ffmpeg",
            $"-f {captureFormat} -framerate 30 -t {timeoutSeconds} -i {captureInput} -y \"{fullPath}\"")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start ffmpeg");

        await Task.Delay(500);

        var watchdogPid = SpawnWatchdog(process.Id, timeoutSeconds);

        RecordingStateManager.Save(new RecordingState
        {
            RecordingPid = process.Id,
            WatchdogPid = watchdogPid,
            OutputFile = fullPath,
            Platform = "linux",
            StartedAt = DateTimeOffset.UtcNow,
            TimeoutSeconds = timeoutSeconds
        });
    }

    public override async Task<string> StopRecordingAsync()
    {
        var state = RecordingStateManager.Load()
            ?? throw new InvalidOperationException("No active recording found.");

        if (state.Platform != "linux")
            throw new InvalidOperationException($"Active recording is on {state.Platform}, not Linux.");

        KillWatchdog(state.WatchdogPid);

        // Send 'q' to ffmpeg's stdin for graceful stop
        try
        {
            var proc = Process.GetProcessById(state.RecordingPid);
            if (!proc.HasExited)
            {
                SendInterrupt(state.RecordingPid);
                await proc.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
        }
        catch { }

        RecordingStateManager.Delete();
        return state.OutputFile;
    }

    private static void EnsureFfmpegLinux()
    {
        try
        {
            var psi = new ProcessStartInfo("ffmpeg", "-version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(5000);
            if (proc?.ExitCode != 0)
                throw new Exception();
        }
        catch
        {
            throw new InvalidOperationException(
                "ffmpeg is required for screen recording on Linux but was not found on PATH. " +
                "Install it via your package manager (e.g., 'apt install ffmpeg' or 'dnf install ffmpeg').");
        }

        // Warn if on Wayland and PipeWire may not be available in ffmpeg
        if (LinuxDisplayServer.IsWayland)
        {
            try
            {
                var psi = new ProcessStartInfo("ffmpeg", "-devices")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                };
                using var proc = Process.Start(psi);
                var stdout = proc?.StandardOutput.ReadToEnd() ?? "";
                var stderr = proc?.StandardError.ReadToEnd() ?? "";
                proc?.WaitForExit(5000);
                if (!stdout.Contains("pipewire", StringComparison.OrdinalIgnoreCase) &&
                    !stderr.Contains("pipewire", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "Screen recording on Wayland requires ffmpeg with PipeWire support, but your ffmpeg build " +
                        "does not include the 'pipewire' device. Install a PipeWire-enabled ffmpeg build, or run " +
                        "under X11 (GDK_BACKEND=x11) to use x11grab instead.");
                }
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch
            {
                // Could not verify PipeWire support — proceed and let ffmpeg fail if needed
            }
        }
    }

    public override Task BackAsync()
    {
        // Linux GTK apps don't have a system back button
        return Task.CompletedTask;
    }

    public override Task PressKeyAsync(string key)
    {
        if (!OperatingSystem.IsLinux())
            return Task.CompletedTask;

        var tool = LinuxDisplayServer.GetPreferredInputTool();
        switch (tool)
        {
            case InputTool.Xdotool:
                return PressKeyWithXdotool(key);
            case InputTool.Ydotool:
                return PressKeyWithYdotool(key);
            default:
                // No input tool available — silent no-op (matches previous behavior)
                return Task.CompletedTask;
        }
    }

    private static Task PressKeyWithXdotool(string key)
    {
        var xdotoolKey = MapToXdotoolKey(key);
        if (xdotoolKey == null) return Task.CompletedTask;

        try
        {
            var psi = new ProcessStartInfo("xdotool", $"key {xdotoolKey}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var proc = Process.Start(psi);
            if (proc != null && !proc.WaitForExit(5000))
            {
                try { proc.Kill(); } catch { }
            }
        }
        catch
        {
            // xdotool may not be installed
        }

        return Task.CompletedTask;
    }

    private static Task PressKeyWithYdotool(string key)
    {
        var keyCode = MapToYdotoolKeyCode(key);
        if (keyCode == null) return Task.CompletedTask;

        try
        {
            // ydotool key syntax: <keycode>:1 <keycode>:0 (press then release)
            var psi = new ProcessStartInfo("ydotool", $"key {keyCode}:1 {keyCode}:0")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var proc = Process.Start(psi);
            if (proc != null && !proc.WaitForExit(5000))
            {
                try { proc.Kill(); } catch { }
            }
        }
        catch
        {
            // ydotool may not be installed or ydotoold not running
        }

        return Task.CompletedTask;
    }

    private int ResolveProcessId()
    {
        if (ProcessId.HasValue)
            return ProcessId.Value;

        if (!string.IsNullOrEmpty(AppName))
        {
            var psi = new ProcessStartInfo("pgrep", $"-f {AppName}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var proc = Process.Start(psi);
            if (proc is not null)
            {
                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();
                var lines = output.Trim().Split('\n', StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length > 0 && int.TryParse(lines[0].Trim(), out var pid))
                {
                    ProcessId = pid;
                    return pid;
                }
            }
        }

        throw new InvalidOperationException("ProcessId or AppName must be set for Linux operations.");
    }

    /// <summary>
    /// Searches the MAUI visual tree for dialog/alert patterns.
    /// GTK dialogs rendered by Maui.Gtk's GtkAlertManager appear as overlay elements.
    /// </summary>
    private sealed record LinuxAlertButton(AlertButton Button, string ElementId);
    private sealed record LinuxAlertCandidate(AlertInfo Info, IReadOnlyList<LinuxAlertButton> Buttons);

    private static LinuxAlertCandidate? FindAlertCandidate(List<ElementInfo> tree)
    {
        foreach (var element in tree)
        {
            var alert = FindAlertCandidateRecursive(element);
            if (alert != null) return alert;
        }
        return null;
    }

    private static LinuxAlertCandidate? FindAlertCandidateRecursive(ElementInfo element)
    {
        // Look for common alert dialog patterns in the MAUI tree
        // AlertDialog, DisplayAlert results, etc.
        if (element.Type is "AlertDialog" or "DialogOverlay" or "ModalPage")
        {
            var buttons = new List<LinuxAlertButton>();
            var title = element.Text;
            CollectAlertButtons(element, buttons);
            if (buttons.Count > 0)
            {
                return new LinuxAlertCandidate(
                    new AlertInfo(title, buttons.Select(item => item.Button).ToList()),
                    buttons);
            }
        }

        if (element.Children != null)
        {
            foreach (var child in element.Children)
            {
                var alert = FindAlertCandidateRecursive(child);
                if (alert != null) return alert;
            }
        }

        return null;
    }

    private static void CollectAlertButtons(ElementInfo element, List<LinuxAlertButton> buttons)
    {
        if (element.Type == "Button" && element.Text != null && element.Id is not null)
        {
            buttons.Add(new LinuxAlertButton(
                new AlertButton(element.Text, 0, 0, 0, 0),
                element.Id));
        }

        if (element.Children != null)
        {
            foreach (var child in element.Children)
                CollectAlertButtons(child, buttons);
        }
    }

    private static string? MapToXdotoolKey(string key) => key.ToLowerInvariant() switch
    {
        "enter" or "return" => "Return",
        "escape" or "back" => "Escape",
        "tab" => "Tab",
        "space" => "space",
        "backspace" => "BackSpace",
        "delete" => "Delete",
        "up" => "Up",
        "down" => "Down",
        "left" => "Left",
        "right" => "Right",
        "home" => "Home",
        "end" => "End",
        _ => null
    };

    // Linux input event codes from linux/input-event-codes.h
    private static string? MapToYdotoolKeyCode(string key) => key.ToLowerInvariant() switch
    {
        "enter" or "return" => "28",
        "escape" or "back" => "1",
        "tab" => "15",
        "space" => "57",
        "backspace" => "14",
        "delete" => "111",
        "up" => "103",
        "down" => "108",
        "left" => "105",
        "right" => "106",
        "home" => "102",
        "end" => "107",
        _ => null
    };
}
