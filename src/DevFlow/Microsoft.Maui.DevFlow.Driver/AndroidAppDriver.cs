using System.Diagnostics;
using System.Xml.Linq;

namespace Microsoft.Maui.DevFlow.Driver;

/// <summary>
/// Driver for Android MAUI apps via emulator/device.
/// Handles adb reverse port forwarding, adb shell commands,
/// and dialog detection/dismissal via UIAutomator dump.
/// </summary>
public class AndroidAppDriver : AppDriverBase
{
    /// <summary>
    /// Optional serial number for targeting a specific device/emulator (adb -s).
    /// </summary>
    public string? Serial { get; set; }

    public override string Platform => "Android";

    protected override async Task SetupPlatformAsync(string host, int port)
    {
        await RunAdbAsync($"reverse tcp:{port} tcp:{port}");
    }

    public override async Task BackAsync()
    {
        await RunAdbAsync("shell input keyevent KEYCODE_BACK");
    }

    public override async Task PressKeyAsync(string key)
    {
        var keycode = key.ToUpperInvariant() switch
        {
            "ENTER" or "RETURN" => "KEYCODE_ENTER",
            "BACK" => "KEYCODE_BACK",
            "HOME" => "KEYCODE_HOME",
            "TAB" => "KEYCODE_TAB",
            "ESCAPE" or "ESC" => "KEYCODE_ESCAPE",
            "DELETE" or "BACKSPACE" => "KEYCODE_DEL",
            _ => $"KEYCODE_{key.ToUpperInvariant()}"
        };

        await RunAdbAsync($"shell input keyevent {keycode}");
    }

    public override async Task<ThemeResult> SetThemeAsync(DevFlowTheme theme, ThemeSetScope scope = ThemeSetScope.Auto)
    {
        if (scope == ThemeSetScope.App)
            return await base.SetThemeAsync(theme, scope).ConfigureAwait(false);

        if (scope == ThemeSetScope.System || await ShouldUseHostThemeAsync().ConfigureAwait(false))
            return await SetHostThemeAsync(theme).ConfigureAwait(false);

        return await base.SetThemeAsync(theme, ThemeSetScope.App).ConfigureAwait(false);
    }

    private async Task<bool> ShouldUseHostThemeAsync()
    {
        try
        {
            var status = await GetStatusAsync().ConfigureAwait(false);
            if (status?.DeviceType?.Equals("Virtual", StringComparison.OrdinalIgnoreCase) == true)
                return true;
            if (status?.DeviceType?.Equals("Physical", StringComparison.OrdinalIgnoreCase) == true)
                return false;
        }
        catch
        {
        }

        if (Serial?.StartsWith("emulator-", StringComparison.OrdinalIgnoreCase) == true)
            return true;

        try
        {
            var qemu = await RunAdbWithOutputAsync("shell getprop ro.kernel.qemu").ConfigureAwait(false);
            return qemu.Trim().Equals("1", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private async Task<ThemeResult> SetHostThemeAsync(DevFlowTheme theme)
    {
        var mode = theme switch
        {
            DevFlowTheme.Light => "no",
            DevFlowTheme.Dark => "yes",
            _ => "auto",
        };

        await RunAdbAsync($"shell cmd uimode night {mode}").ConfigureAwait(false);

        return new ThemeResult
        {
            Theme = theme,
            RequestedTheme = theme,
            EffectiveTheme = theme,
            Source = "system",
            Success = true,
            Message = $"Android system theme set to {theme.ToProtocolString()}.",
        };
    }

    // ──────────────────────────────────────────────
    // Dialog Detection & Dismissal via UIAutomator
    // ──────────────────────────────────────────────

    /// <summary>
    /// Dumps the UI hierarchy via `uiautomator dump` and detects alert dialogs.
    /// Recognizes Android AlertDialog (parentPanel pattern) and system permission dialogs.
    /// </summary>
    public async Task<AlertInfo?> DetectAlertAsync()
    {
        var xml = await DumpUiHierarchyAsync().ConfigureAwait(false);
        if (xml is null) return null;
        return ParseAlertFromHierarchy(xml);
    }

    /// <summary>
    /// Dismisses the current alert by tapping the button matching the label.
    /// If no label is provided, taps the last button (typically default/accept).
    /// </summary>
    public async Task DismissAlertAsync(string? buttonLabel = null)
    {
        var alert = await DetectAlertAsync().ConfigureAwait(false);
        if (alert is null) throw new InvalidOperationException("No alert detected to dismiss");

        var btn = FindButtonToTap(alert, buttonLabel);
        await RunAdbAsync($"shell input tap {btn.CenterX} {btn.CenterY}");
    }

    /// <summary>
    /// Detects and dismisses an alert if one is present. Returns the alert info, or null if none found.
    /// </summary>
    public async Task<AlertInfo?> HandleAlertIfPresentAsync(string? buttonLabel = null)
    {
        var alert = await DetectAlertAsync().ConfigureAwait(false);
        if (alert is null) return null;

        var btn = FindButtonToTap(alert, buttonLabel);
        await RunAdbAsync($"shell input tap {btn.CenterX} {btn.CenterY}");
        return alert;
    }

    /// <summary>
    /// Returns the full UIAutomator hierarchy XML as a string for debugging.
    /// </summary>
    public async Task<string> GetAccessibilityTreeAsync()
    {
        var xml = await DumpUiHierarchyAsync().ConfigureAwait(false);
        return xml?.ToString() ?? "<empty />";
    }

    // ──────────────────────────────────────────────
    // Implementation
    // ──────────────────────────────────────────────

    private async Task<XElement?> DumpUiHierarchyAsync()
    {
        const string devicePath = "/sdcard/window_dump.xml";
        await RunAdbAsync($"shell uiautomator dump {devicePath}");
        var content = await RunAdbWithOutputAsync($"shell cat {devicePath}");
        if (string.IsNullOrWhiteSpace(content)) return null;

        try { return XElement.Parse(content); }
        catch { return null; }
    }

    /// <summary>
    /// Parses the UIAutomator hierarchy to find Android AlertDialog or permission dialogs.
    /// </summary>
    private static AlertInfo? ParseAlertFromHierarchy(XElement root)
    {
        // Strategy 1: Standard AlertDialog with parentPanel resource-id
        var parentPanel = FindByResourceId(root, "parentPanel");
        if (parentPanel is not null)
            return ParseAlertDialog(parentPanel);

        // Strategy 2: System permission dialog (com.google.android.permissioncontroller)
        var permDialog = FindPermissionDialog(root);
        if (permDialog is not null)
            return permDialog;

        return null;
    }

    /// <summary>
    /// Parses a standard Android AlertDialog from its parentPanel node.
    /// Structure: parentPanel → topPanel(alertTitle) + contentPanel(message) + buttonPanel(buttons)
    /// Action sheets use: contentPanel → select_dialog_listview → text1 items
    /// </summary>
    private static AlertInfo ParseAlertDialog(XElement parentPanel)
    {
        string? title = FindByResourceId(parentPanel, "alertTitle")?.Attribute("text")?.Value;
        var buttons = new List<AlertButton>();

        // Collect buttons from buttonPanel
        var buttonPanel = FindByResourceId(parentPanel, "buttonPanel");
        if (buttonPanel is not null)
        {
            foreach (var btn in buttonPanel.Descendants("node")
                .Where(n => n.Attribute("class")?.Value?.Contains("Button") == true))
            {
                var label = btn.Attribute("text")?.Value;
                if (string.IsNullOrEmpty(label)) continue;
                if (TryParseBounds(btn.Attribute("bounds")?.Value, out var r))
                    buttons.Add(new AlertButton(label, r.x, r.y, r.w, r.h));
            }
        }

        // Also collect action sheet list items (select_dialog_listview)
        var listView = FindByResourceId(parentPanel, "select_dialog_listview");
        if (listView is not null)
        {
            foreach (var item in listView.Descendants("node")
                .Where(n => n.Attribute("class")?.Value?.Contains("TextView") == true))
            {
                var label = item.Attribute("text")?.Value;
                if (string.IsNullOrEmpty(label)) continue;
                if (TryParseBounds(item.Attribute("bounds")?.Value, out var r))
                    buttons.Add(new AlertButton(label, r.x, r.y, r.w, r.h));
            }
        }

        return new AlertInfo(title, buttons);
    }

    /// <summary>
    /// Detects system permission dialogs from the permission controller package.
    /// These have buttons like "Allow", "Don't allow", "While using the app", etc.
    /// </summary>
    private static AlertInfo? FindPermissionDialog(XElement root)
    {
        // Permission dialogs come from com.google.android.permissioncontroller
        var permNodes = root.DescendantsAndSelf("node")
            .Where(n => n.Attribute("package")?.Value == "com.google.android.permissioncontroller")
            .ToList();

        if (permNodes.Count == 0) return null;

        // Find the title/message text
        string? title = null;
        var textNodes = permNodes
            .Where(n => n.Attribute("class")?.Value?.Contains("TextView") == true)
            .ToList();
        if (textNodes.Count > 0)
            title = textNodes[0].Attribute("text")?.Value;

        // Find clickable buttons
        var buttons = new List<AlertButton>();
        var clickables = permNodes
            .Where(n => n.Attribute("clickable")?.Value == "true"
                && !string.IsNullOrEmpty(n.Attribute("text")?.Value ?? n.Attribute("content-desc")?.Value));

        foreach (var btn in clickables)
        {
            var label = btn.Attribute("text")?.Value ?? btn.Attribute("content-desc")?.Value ?? "";
            if (string.IsNullOrEmpty(label)) continue;
            if (TryParseBounds(btn.Attribute("bounds")?.Value, out var r))
                buttons.Add(new AlertButton(label, r.x, r.y, r.w, r.h));
        }

        if (buttons.Count == 0) return null;
        return new AlertInfo(title ?? "Permission Request", buttons);
    }

    private static XElement? FindByResourceId(XElement root, string shortId)
    {
        return root.DescendantsAndSelf("node")
            .FirstOrDefault(n =>
            {
                var res = n.Attribute("resource-id")?.Value;
                if (res is null) return false;
                // Match "android:id/alertTitle" or "com.xxx:id/alertTitle" or just "alertTitle"
                return res == shortId || res.EndsWith($"/{shortId}", StringComparison.Ordinal);
            });
    }

    /// <summary>
    /// Parses Android bounds format "[left,top][right,bottom]" into position and size.
    /// </summary>
    private static bool TryParseBounds(string? bounds, out (double x, double y, double w, double h) result)
    {
        result = default;
        if (bounds is null) return false;

        // Format: [left,top][right,bottom]
        var parts = bounds.Replace("][", ",").Trim('[', ']').Split(',');
        if (parts.Length != 4) return false;

        if (int.TryParse(parts[0], out var left) && int.TryParse(parts[1], out var top)
            && int.TryParse(parts[2], out var right) && int.TryParse(parts[3], out var bottom))
        {
            result = (left, top, right - left, bottom - top);
            return true;
        }
        return false;
    }

    private static AlertButton FindButtonToTap(AlertInfo alert, string? buttonLabel)
    {
        if (alert.Buttons.Count == 0)
            throw new InvalidOperationException("Alert has no buttons");

        if (buttonLabel is not null)
        {
            var normalized = NormalizeQuotes(buttonLabel);
            var match = alert.Buttons.FirstOrDefault(b =>
                NormalizeQuotes(b.Label).Equals(normalized, StringComparison.OrdinalIgnoreCase));
            if (match is null)
                throw new InvalidOperationException(
                    $"No button labeled \"{buttonLabel}\". Available: {string.Join(", ", alert.Buttons.Select(b => b.Label))}");
            return match;
        }

        return alert.Buttons[^1]; // Last button is typically the default/accept
    }

    /// <summary>
    /// Normalizes smart/curly quotes to ASCII equivalents for reliable matching.
    /// Android system dialogs use Unicode right single quotation mark (U+2019) in text like "Don't allow".
    /// </summary>
    private static string NormalizeQuotes(string s)
        => s.Replace('\u2018', '\'').Replace('\u2019', '\'')
            .Replace('\u201C', '"').Replace('\u201D', '"');

    // ──────────────────────────────────────────────
    // Screen Recording via adb screenrecord
    // ──────────────────────────────────────────────

    private const string DeviceRecordingPath = "/sdcard/mauidevflow_recording.mp4";
    private const int AdbMaxTimeLimit = 180;

    public override async Task StartRecordingAsync(string outputFile, int timeoutSeconds = 30)
    {
        EnsureNotRecording();

        var effectiveTimeout = timeoutSeconds;
        if (effectiveTimeout > AdbMaxTimeLimit)
        {
            Console.Error.WriteLine(
                $"Warning: Android adb screenrecord max is {AdbMaxTimeLimit}s. Capping timeout from {effectiveTimeout}s.");
            effectiveTimeout = AdbMaxTimeLimit;
        }

        var args = Serial is not null ? $"-s {Serial} " : "";
        args += $"shell screenrecord --time-limit {effectiveTimeout} {DeviceRecordingPath}";

        var psi = new ProcessStartInfo("adb", args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start adb screenrecord");

        var watchdogPid = SpawnWatchdog(process.Id, effectiveTimeout);

        RecordingStateManager.Save(new RecordingState
        {
            RecordingPid = process.Id,
            WatchdogPid = watchdogPid,
            OutputFile = Path.GetFullPath(outputFile),
            Platform = "android",
            DeviceOutputFile = DeviceRecordingPath,
            Serial = Serial,
            StartedAt = DateTimeOffset.UtcNow,
            TimeoutSeconds = effectiveTimeout
        });
    }

    public override async Task<string> StopRecordingAsync()
    {
        var state = RecordingStateManager.Load()
            ?? throw new InvalidOperationException("No active recording found.");

        if (state.Platform != "android")
            throw new InvalidOperationException($"Active recording is on {state.Platform}, not Android.");

        KillWatchdog(state.WatchdogPid);
        SendInterrupt(state.RecordingPid);

        // Wait for adb to finish writing
        try
        {
            var proc = Process.GetProcessById(state.RecordingPid);
            await proc.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch { }

        // Pull the file from device
        await RunAdbAsync($"pull {DeviceRecordingPath} \"{state.OutputFile}\"");
        try { await RunAdbAsync($"shell rm {DeviceRecordingPath}"); } catch { }

        RecordingStateManager.Delete();
        return state.OutputFile;
    }

    // ──────────────────────────────────────────────
    // adb helpers
    // ──────────────────────────────────────────────

    private async Task RunAdbAsync(string arguments)
    {
        var args = Serial is not null ? $"-s {Serial} {arguments}" : arguments;
        var psi = new ProcessStartInfo("adb", args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null) throw new InvalidOperationException("Failed to start adb");
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync();
            throw new InvalidOperationException($"adb {arguments} failed: {error}");
        }
    }

    private async Task<string> RunAdbWithOutputAsync(string arguments)
    {
        var args = Serial is not null ? $"-s {Serial} {arguments}" : arguments;
        var psi = new ProcessStartInfo("adb", args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null) throw new InvalidOperationException("Failed to start adb");
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync();
            throw new InvalidOperationException($"adb {arguments} failed: {error}");
        }

        return output;
    }
}
