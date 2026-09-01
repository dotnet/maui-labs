using System.Diagnostics;
using System.Globalization;
using System.Xml.Linq;
using Xamarin.Android.Tools;

namespace Microsoft.Maui.DevFlow.Driver;

/// <summary>
/// Driver for Android MAUI apps via emulator/device.
/// Handles adb reverse port forwarding, adb shell commands,
/// and dialog detection/dismissal via UIAutomator dump.
/// </summary>
public class AndroidAppDriver : AppDriverBase, IAlertDriver
{
    private readonly Func<string, AdbRunner> _createAdbRunner;
    private readonly Func<string?> _getDefaultSerial;
    private AdbRunner? _adbRunner;
    private string? _adbRunnerPath;

    public AndroidAppDriver()
        : this(
            static adbPath => new AdbRunner(adbPath),
            static () => Environment.GetEnvironmentVariable("ANDROID_SERIAL"))
    {
    }

    internal AndroidAppDriver(Func<string, AdbRunner> createAdbRunner, Func<string?>? getDefaultSerial = null)
    {
        _createAdbRunner = createAdbRunner;
        _getDefaultSerial = getDefaultSerial ?? (static () => null);
    }

    /// <summary>
    /// Optional serial number for targeting a specific device/emulator (adb -s).
    /// </summary>
    public string? Serial { get; set; }

    /// <summary>ADB executable path. Defaults to resolving <c>adb</c> from PATH.</summary>
    public string AdbPath { get; set; } = "adb";

    public override string Platform => "Android";

    protected override async Task SetupPlatformAsync(string host, int port)
    {
        var serial = await ResolveSerialAsync().ConfigureAwait(false);
        var portSpec = new AdbPortSpec(AdbProtocol.Tcp, port);
        await GetAdbRunner().ReversePortAsync(serial, portSpec, portSpec).ConfigureAwait(false);
    }

    public override async Task BackAsync()
    {
        await RunAdbAsync("shell", "input", "keyevent", "KEYCODE_BACK").ConfigureAwait(false);
    }

    public override async Task PressKeyAsync(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var normalizedKey = key.ToUpperInvariant();
        if (normalizedKey.Any(character => !IsKeyCharacter(character)))
            throw new ArgumentException("Android key names may contain only ASCII letters, digits, and underscores.", nameof(key));

        var keycode = normalizedKey switch
        {
            "ENTER" or "RETURN" => "KEYCODE_ENTER",
            "BACK" => "KEYCODE_BACK",
            "HOME" => "KEYCODE_HOME",
            "TAB" => "KEYCODE_TAB",
            "ESCAPE" or "ESC" => "KEYCODE_ESCAPE",
            "DELETE" or "BACKSPACE" => "KEYCODE_DEL",
            _ => $"KEYCODE_{normalizedKey}"
        };

        await RunAdbAsync("shell", "input", "keyevent", keycode).ConfigureAwait(false);
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
            var qemu = await RunAdbWithOutputAsync("shell", "getprop", "ro.kernel.qemu").ConfigureAwait(false);
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

        await RunAdbAsync("shell", "cmd", "uimode", "night", mode).ConfigureAwait(false);

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
        await TapAsync(btn).ConfigureAwait(false);
    }

    /// <summary>
    /// Detects and dismisses an alert if one is present. Returns the alert info, or null if none found.
    /// </summary>
    public async Task<AlertInfo?> HandleAlertIfPresentAsync(string? buttonLabel = null)
    {
        var alert = await DetectAlertAsync().ConfigureAwait(false);
        if (alert is null) return null;

        var btn = FindButtonToTap(alert, buttonLabel);
        await TapAsync(btn).ConfigureAwait(false);
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
        await RunAdbAsync("shell", "uiautomator", "dump", devicePath).ConfigureAwait(false);
        var content = await RunAdbWithOutputAsync("shell", "cat", devicePath).ConfigureAwait(false);
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

        var arguments = BuildAdbArguments(
            Serial,
            "shell",
            "screenrecord",
            "--time-limit",
            effectiveTimeout.ToString(CultureInfo.InvariantCulture),
            DeviceRecordingPath);
        var processStartInfo = CreateAdbProcessStartInfo(AdbPath, arguments);
        int? recordingPid = null;
        var recordingTask = ProcessUtils.StartProcess(
            processStartInfo,
            TextWriter.Null,
            TextWriter.Null,
            CancellationToken.None,
            process => recordingPid = process.Id);

        if (recordingPid is null)
        {
            await recordingTask.ConfigureAwait(false);
            throw new InvalidOperationException("Failed to start adb screenrecord");
        }

        var watchdogPid = SpawnWatchdog(recordingPid.Value, effectiveTimeout);

        RecordingStateManager.Save(new RecordingState
        {
            RecordingPid = recordingPid.Value,
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
        await RunAdbAsync("pull", DeviceRecordingPath, state.OutputFile).ConfigureAwait(false);
        try { await RunAdbAsync("shell", "rm", DeviceRecordingPath).ConfigureAwait(false); } catch { }

        RecordingStateManager.Delete();
        return state.OutputFile;
    }

    // ──────────────────────────────────────────────
    // adb helpers
    // ──────────────────────────────────────────────

    private AdbRunner GetAdbRunner()
    {
        if (_adbRunner is null || !string.Equals(_adbRunnerPath, AdbPath, StringComparison.Ordinal))
        {
            _adbRunner = _createAdbRunner(AdbPath);
            _adbRunnerPath = AdbPath;
        }

        return _adbRunner;
    }

    private async Task<string> ResolveSerialAsync()
    {
        if (Serial is not null)
            return ValidateSerial(Serial, nameof(Serial));

        var defaultSerial = _getDefaultSerial();
        if (!string.IsNullOrWhiteSpace(defaultSerial))
            return ValidateSerial(defaultSerial, "ANDROID_SERIAL");

        var devices = await GetAdbRunner().ListDevicesAsync().ConfigureAwait(false);
        var connected = devices.Where(device => device.Status == AdbDeviceStatus.Online).ToArray();
        return connected.Length switch
        {
            1 => ValidateSerial(connected[0].Serial, "deviceSerial"),
            0 => throw new InvalidOperationException("No connected Android devices found."),
            _ => throw new InvalidOperationException("More than one Android device is connected. Set Serial to select a device."),
        };
    }

    private Task TapAsync(AlertButton button)
        => RunAdbAsync(
            "shell",
            "input",
            "tap",
            button.CenterX.ToString(CultureInfo.InvariantCulture),
            button.CenterY.ToString(CultureInfo.InvariantCulture));

    private async Task RunAdbAsync(params string[] arguments)
        => _ = await RunAdbWithOutputAsync(arguments).ConfigureAwait(false);

    private async Task<string> RunAdbWithOutputAsync(params string[] arguments)
    {
        var adbArguments = BuildAdbArguments(Serial, arguments);
        var processStartInfo = CreateAdbProcessStartInfo(AdbPath, adbArguments);
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);

        var exitCode = await ProcessUtils.StartProcess(
            processStartInfo,
            output,
            error,
            CancellationToken.None).ConfigureAwait(false);

        if (exitCode != 0)
            throw new InvalidOperationException($"adb {string.Join(" ", arguments)} failed: {error}");

        return output.ToString();
    }

    internal static ProcessStartInfo CreateAdbProcessStartInfo(string adbPath, params string[] arguments)
    {
        var processStartInfo = ProcessUtils.CreateProcessStartInfo(adbPath);
        foreach (var argument in arguments)
            processStartInfo.ArgumentList.Add(argument);
        return processStartInfo;
    }

    internal static string[] BuildAdbArguments(string? serial, params string[] arguments)
    {
        if (serial is null)
            return arguments;

        var result = new string[arguments.Length + 2];
        result[0] = "-s";
        result[1] = serial;
        Array.Copy(arguments, 0, result, 2, arguments.Length);
        return result;
    }

    private static string ValidateSerial(string serial, string paramName)
    {
        if (string.IsNullOrWhiteSpace(serial) || serial.Any(character => !IsSerialCharacter(character)))
        {
            throw new ArgumentException(
                "Android device serials may contain only ASCII letters, digits, periods, hyphens, underscores, colons, brackets, and percent signs.",
                paramName);
        }

        return serial;
    }

    private static bool IsKeyCharacter(char character)
        => character is >= 'A' and <= 'Z'
            or >= '0' and <= '9'
            or '_';

    private static bool IsSerialCharacter(char character)
        => character is >= 'A' and <= 'Z'
            or >= 'a' and <= 'z'
            or >= '0' and <= '9'
            or '.' or '-' or '_' or ':' or '[' or ']' or '%';
}
