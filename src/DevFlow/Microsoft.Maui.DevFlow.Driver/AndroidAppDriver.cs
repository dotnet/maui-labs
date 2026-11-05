using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Xamarin.Android.Tools;
using System.Text.RegularExpressions;

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

    /// <summary>
    /// Taps a physical screen-pixel coordinate through Android's input pipeline.
    /// Use the current accessibility tree to locate native controls outside the MAUI tree.
    /// </summary>
    public async Task TapCoordinateAsync(int x, int y)
    {
        var arguments = BuildCoordinateTapArguments(x, y);
        var serial = await ResolveSerialAsync().ConfigureAwait(false);
        await RunAdbForSerialAsync(serial, arguments).ConfigureAwait(false);
    }

    internal static string[] BuildCoordinateTapArguments(int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        return
        [
            "shell", "input", "tap",
            x.ToString(CultureInfo.InvariantCulture),
            y.ToString(CultureInfo.InvariantCulture),
        ];
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
        AlertInfo? unstableAlert = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var before = await RunAdbWithOutputAsync("shell", "dumpsys", "window", "-a");
            var xml = await DumpUiHierarchyAsync().ConfigureAwait(false);
            if (xml is null) return null;
            var alert = ParseAlertFromHierarchy(xml);
            if (alert is null) return null;
            var after = await RunAdbWithOutputAsync("shell", "dumpsys", "window", "-a");
            var beforeId = AndroidDialogWindowSafety.GetInstanceId(before, alert.SourceProcessName);
            var afterId = AndroidDialogWindowSafety.GetInstanceId(after, alert.SourceProcessName);
            if (beforeId != null && beforeId == afterId)
                return alert with { InstanceId = afterId };

            unstableAlert = alert;
            if (attempt < 2)
                await Task.Delay(100).ConfigureAwait(false);
        }

        return unstableAlert;
    }

    public async Task<bool> IsDialogAttributedToTargetAsync(
        AlertInfo dialog, string expectedPackageId, string expectedAppName)
    {
        var ownerPackage = dialog.SourceProcessName;
        if (string.IsNullOrWhiteSpace(ownerPackage) || string.IsNullOrWhiteSpace(dialog.InstanceId))
            return false;
        if (!Regex.IsMatch(ownerPackage, @"^[A-Za-z][A-Za-z0-9_]*(?:\.[A-Za-z][A-Za-z0-9_]*)*$", RegexOptions.CultureInvariant))
            return false;
        if (dialog.IsSystemDialog)
        {
            if (ownerPackage is not ("com.google.android.permissioncontroller" or "com.android.permissioncontroller")
                || !PermissionPromptNamesTarget(dialog.Title, expectedAppName))
            {
                return false;
            }
        }
        else if (ownerPackage != expectedPackageId)
        {
            return false;
        }

        var packageInfo = await RunAdbWithOutputAsync("shell", "cmd", "package", "list", "packages", "-U", ownerPackage);
        var ownerUid = AndroidDialogWindowSafety.GetPackageUid(packageInfo, ownerPackage);
        if (!ownerUid.HasValue)
            return false;
        var windows = await RunAdbWithOutputAsync("shell", "dumpsys", "window", "-a");
        return AndroidDialogWindowSafety.IsUnobscuredDialog(windows, dialog, ownerPackage, ownerUid.Value);
    }

    public async Task<bool> IsTargetAppForegroundAsync(string packageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        var windowState = await RunAdbWithOutputAsync("shell", "dumpsys", "window");
        if (string.IsNullOrWhiteSpace(windowState))
            return false;

        return IsFocusedPackage(windowState, packageId);
    }

    internal static bool IsFocusedPackage(string windowState, string packageId)
    {
        var focusedAppLine = windowState
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(line => line.Contains("mFocusedApp", StringComparison.Ordinal));
        if (focusedAppLine is null)
            return false;
        var component = Regex.Match(
            focusedAppLine,
            @"(?<package>[A-Za-z0-9._]+)/[A-Za-z0-9._$]+",
            RegexOptions.CultureInvariant);
        return component.Success
            && component.Groups["package"].Value.Equals(packageId, StringComparison.Ordinal);
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

    public async Task<AlertActionResult> PressAlertButtonSafelyAsync(
        AlertInfo reviewedDialog,
        string buttonLabel,
        string expectedPackageId,
        string expectedAppName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(buttonLabel);
        cancellationToken.ThrowIfCancellationRequested();
        var currentDialog = await DetectAlertAsync();
        if (currentDialog == null || !NativeDialogIdentity.CreateFingerprint(Platform, Serial ?? string.Empty, currentDialog)
            .Equals(NativeDialogIdentity.CreateFingerprint(Platform, Serial ?? string.Empty, reviewedDialog), StringComparison.Ordinal))
        {
            return new AlertActionResult(false, true, "The reviewed Android dialog changed or disappeared. Detect it again before acting.", currentDialog);
        }

        var matches = currentDialog.Buttons
            .Where(button => button.Label.Equals(buttonLabel, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1)
        {
            return new AlertActionResult(
                false,
                true,
                matches.Length == 0
                    ? $"Button '{buttonLabel}' was not found by exact label."
                    : $"More than one button has the exact label '{buttonLabel}'.",
                reviewedDialog);
        }

        var attributedToTarget = await IsDialogAttributedToTargetAsync(currentDialog, expectedPackageId, expectedAppName);
        if (!attributedToTarget)
        {
            return new AlertActionResult(
                false,
                true,
                "The Android dialog window owner or unobscured button could not be verified. Respond manually or detect the prompt again.",
                reviewedDialog);
        }

        cancellationToken.ThrowIfCancellationRequested();
        await TapAsync(matches[0]);
        return new AlertActionResult(
            true,
            false,
            $"Pressed '{buttonLabel}' using Android device input without moving the host pointer.",
            reviewedDialog);
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
                    buttons.Add(new AlertButton(label, r.x, r.y, r.w, r.h)
                    {
                        Identifier = btn.Attribute("resource-id")?.Value
                    });
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
                    buttons.Add(new AlertButton(label, r.x, r.y, r.w, r.h)
                    {
                        Identifier = item.Attribute("resource-id")?.Value
                    });
            }
        }

        return new AlertInfo(title, buttons)
        {
            Text = CollectVisibleText(parentPanel),
            SourceProcessName = GetHierarchyPackage(parentPanel)
        };
    }

    /// <summary>
    /// Detects system permission dialogs from the permission controller package.
    /// These have buttons like "Allow", "Don't allow", "While using the app", etc.
    /// </summary>
    private static AlertInfo? FindPermissionDialog(XElement root)
    {
        // Permission dialogs come from com.google.android.permissioncontroller
        var permNodes = root.DescendantsAndSelf("node")
            .Where(n => n.Attribute("package")?.Value is "com.google.android.permissioncontroller" or "com.android.permissioncontroller")
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
                buttons.Add(new AlertButton(label, r.x, r.y, r.w, r.h)
                {
                    Identifier = btn.Attribute("resource-id")?.Value
                });
        }

        if (buttons.Count == 0) return null;
        return new AlertInfo(title ?? "Permission Request", buttons)
        {
            Text = CollectVisibleText(permNodes),
            IsSystemDialog = true,
            SourceProcessName = GetHierarchyPackage(permNodes.First())
        };
    }

    private static string? GetHierarchyPackage(XElement root)
    {
        var packages = root.DescendantsAndSelf("node")
            .Select(node => node.Attribute("package")?.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return packages.Length == 1 ? packages[0] : null;
    }

    internal static bool PermissionPromptNamesTarget(string? promptText, string targetName)
    {
        if (string.IsNullOrWhiteSpace(promptText) || string.IsNullOrWhiteSpace(targetName))
            return false;

        var match = Regex.Match(
            promptText,
            @"^Allow\s+(?<app>.+?)\s+to\b",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        return match.Success
            && match.Groups["app"].Value.Equals(targetName, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> CollectVisibleText(XElement root)
        => CollectVisibleText(root.DescendantsAndSelf("node"));

    private static IReadOnlyList<string> CollectVisibleText(IEnumerable<XElement> nodes)
        => nodes
            .SelectMany(node => new[]
            {
                node.Attribute("text")?.Value,
                node.Attribute("content-desc")?.Value
            })
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => text!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

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

        var serial = await ResolveSerialAsync().ConfigureAwait(false);
        var effectiveTimeout = timeoutSeconds;
        if (effectiveTimeout > AdbMaxTimeLimit)
        {
            Console.Error.WriteLine(
                $"Warning: Android adb screenrecord max is {AdbMaxTimeLimit}s. Capping timeout from {effectiveTimeout}s.");
            effectiveTimeout = AdbMaxTimeLimit;
        }

        var arguments = BuildAdbArguments(
            serial,
            "shell",
            "screenrecord",
            "--time-limit",
            effectiveTimeout.ToString(CultureInfo.InvariantCulture),
            DeviceRecordingPath);
        var processStartInfo = CreateAdbProcessStartInfo(AdbPath, arguments);
        int? recordingPid = null;
        var error = new StringWriter(CultureInfo.InvariantCulture);
        var recordingTask = ProcessUtils.StartProcess(
            processStartInfo,
            TextWriter.Null,
            error,
            CancellationToken.None,
            process => recordingPid = process.Id);

        await EnsureRecordingProcessStartedAsync(
            recordingTask,
            error,
            "adb screenrecord").ConfigureAwait(false);

        if (recordingPid is null)
            throw new InvalidOperationException("Failed to start adb screenrecord");

        var watchdogPid = SpawnWatchdog(recordingPid.Value, effectiveTimeout);

        RecordingStateManager.Save(new RecordingState
        {
            RecordingPid = recordingPid.Value,
            WatchdogPid = watchdogPid,
            OutputFile = Path.GetFullPath(outputFile),
            Platform = "android",
            DeviceOutputFile = DeviceRecordingPath,
            Serial = serial,
            StartedAt = DateTimeOffset.UtcNow,
            TimeoutSeconds = effectiveTimeout
        });
    }

    internal static async Task EnsureRecordingProcessStartedAsync(
        Task<int> recordingTask,
        TextWriter error,
        string command)
    {
        var completedTask = await Task.WhenAny(
            recordingTask,
            Task.Delay(TimeSpan.FromMilliseconds(500))).ConfigureAwait(false);
        if (completedTask != recordingTask)
            return;

        var exitCode = await recordingTask.ConfigureAwait(false);
        var detail = error.ToString()?.Trim() ?? string.Empty;
        var suffix = detail.Length == 0 ? string.Empty : $": {detail}";
        throw new InvalidOperationException(
            $"{command} exited during startup with code {exitCode}{suffix}");
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
    // ──────────────────────────────────────────────
    // Background jobs (WorkManager via JobScheduler)
    // ──────────────────────────────────────────────

    /// <summary>
    /// The JobScheduler namespace WorkManager schedules into. Job lookups and the run command
    /// both need it — without <c>-n</c> the job simply is not found.
    /// </summary>
    public const string WorkManagerNamespace = "androidx.work.systemjobscheduler";

    /// <summary>
    /// Lists the app's scheduled JobScheduler jobs by parsing <c>dumpsys jobscheduler</c>.
    ///
    /// <para>
    /// This is the host-side counterpart to the in-app WorkManager query. It sees what the
    /// system actually has scheduled rather than what the app's WorkManager database believes,
    /// and it needs no cooperation from the app — but it reports JobScheduler's view, so the
    /// identifier is a numeric job id rather than a WorkSpec UUID.
    /// </para>
    /// </summary>
    public virtual async Task<IReadOnlyList<AndroidScheduledJob>> ListScheduledJobsAsync(string packageName, CancellationToken cancellationToken = default)
    {
        var dump = await RunJobAdbAsync(["shell", "dumpsys", "jobscheduler"], cancellationToken);
        return ParseScheduledJobs(dump, packageName);
    }

    /// <summary>
    /// Reads jobs through a temporary forward on the selected adb device, pinning its serial
    /// for subsequent scheduler commands. Callers must compare the agent instance identity.
    /// </summary>
    public virtual async Task<JsonElement> ReadJobsOnDeviceAsync(int agentPort, CancellationToken cancellationToken = default)
    {
        if (agentPort is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(agentPort));
        Serial = await ResolveSerialAsync().WaitAsync(cancellationToken);
        var localPortText = await RunJobAdbAsync(
            ["forward", "tcp:0", $"tcp:{agentPort}"], cancellationToken);
        if (!int.TryParse(localPortText.Trim(), out var localPort) || localPort is < 1 or > 65535)
            throw new InvalidOperationException($"adb did not return an allocated forward port: {localPortText}");
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var response = await http.GetAsync(
                $"http://127.0.0.1:{localPort}/api/v1/device/jobs", cancellationToken);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return document.RootElement.Clone();
        }
        finally
        {
            await RunJobAdbAsync(["forward", "--remove", $"tcp:{localPort}"], CancellationToken.None);
        }
    }

    /// <summary>
    /// Parses <c>dumpsys jobscheduler</c> output. Entries look like:
    /// <code>
    /// JOB androidx.work.systemjobscheduler:u0a233/1: 258ed68 #SampleSyncWorker#@androidx.work.systemjobscheduler@com.pkg/androidx.work.impl.background.systemjob.SystemJobService
    /// JOB #1000/808: cb8ff9f android/com.android.server.MountServiceIdler
    /// </code>
    /// The namespaced form carries the WorkManager worker name in <c>#…#</c>.
    /// </summary>
    internal static IReadOnlyList<AndroidScheduledJob> ParseScheduledJobs(string dumpsysOutput, string packageName)
    {
        var jobs = new List<AndroidScheduledJob>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // Two shapes occur, and both must parse:
        //   JOB <namespace>:<uid>/<jobId>: <hash> #<worker>#@<namespace>@<package>/<service>
        //   JOB #<uid>/<jobId>: <hash> <package>/<service>          (no namespace)
        var pattern = new System.Text.RegularExpressions.Regex(
            @"JOB\s+(?:(?<ns>[A-Za-z0-9_.\-]+):)?#?u?(?<uid>[0-9a-z]+)/(?<jobId>-?\d+):\s+\S+\s+(?<detail>.*)",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        foreach (var rawLine in dumpsysOutput.Split('\n'))
        {
            var line = rawLine.Trim();
            if (!line.StartsWith("JOB ", StringComparison.Ordinal)) continue;
            var match = pattern.Match(line);
            if (!match.Success) continue;

            var detail = match.Groups["detail"].Value;
            var jobId = match.Groups["jobId"].Value;

            // A namespace appears as a "<ns>:" prefix, and again as "@<ns>@" inside the detail.
            // The plain "JOB #<uid>/<id>" form has neither.
            string? ns = match.Groups["ns"].Success && match.Groups["ns"].Value.Length > 0
                ? match.Groups["ns"].Value
                : null;
            if (ns == null)
            {
                var atMatch = System.Text.RegularExpressions.Regex.Match(detail, @"@(?<ns>[^@]+)@");
                if (atMatch.Success) ns = atMatch.Groups["ns"].Value;
            }

            var workerMatch = System.Text.RegularExpressions.Regex.Match(detail, @"#(?<worker>[^#]+)#");
            var worker = workerMatch.Success ? workerMatch.Groups["worker"].Value : null;

            var serviceMatch = System.Text.RegularExpressions.Regex.Match(
                detail, @"(?:^|@|\s)" + System.Text.RegularExpressions.Regex.Escape(packageName) + @"/(?<service>\S+)");
            if (!serviceMatch.Success) continue;
            var service = serviceMatch.Success ? serviceMatch.Groups["service"].Value : null;

            var key = $"{ns}|{jobId}";
            if (!seen.Add(key)) continue;

            jobs.Add(new AndroidScheduledJob
            {
                JobId = jobId,
                Namespace = ns,
                Worker = worker,
                Service = service,
                IsWorkManager = ns == WorkManagerNamespace
                    || service?.Contains("androidx.work", StringComparison.Ordinal) == true
            });
        }

        return jobs;
    }

    /// <summary>
    /// Returns JobScheduler's view of a job: pending / active / ready / waiting, etc.
    /// </summary>
    public async Task<string> GetJobStateAsync(string packageName, string jobId, string? jobNamespace = WorkManagerNamespace)
    {
        var output = await RunAdbWithOutputAsync(BuildJobSchedulerArguments("get-job-state", packageName, jobId, jobNamespace));
        return output.Trim();
    }

    /// <summary>
    /// Forces a scheduled job to run now.
    /// </summary>
    /// <param name="force">
    /// Ignore unmet constraints. Required for anything with a delay or a network/charging
    /// requirement, which is most real work; without it JobScheduler declines.
    /// </param>
    public virtual async Task<string> RunScheduledJobAsync(string packageName, string jobId, string? jobNamespace = WorkManagerNamespace, bool force = true, CancellationToken cancellationToken = default)
    {
        var output = await RunJobAdbAsync(BuildJobSchedulerArguments("run", packageName, jobId, jobNamespace, force), cancellationToken);
        return output.Trim();
    }

    /// <summary>
    /// Stops a running job, delivering the given stop reason so <c>onStopped</c> paths are testable.
    /// </summary>
    public async Task<string> StopScheduledJobAsync(string packageName, string jobId, string? jobNamespace = WorkManagerNamespace)
    {
        var output = await RunAdbWithOutputAsync(BuildJobSchedulerArguments("stop", packageName, jobId, jobNamespace));
        return output.Trim();
    }

    internal static string[] BuildJobSchedulerArguments(
        string action, string packageName, string jobId, string? jobNamespace, bool force = false)
    {
        var arguments = new List<string> { "shell", "cmd", "jobscheduler", action };
        if (force) arguments.Add("-f");
        if (!string.IsNullOrWhiteSpace(jobNamespace))
            arguments.AddRange(["-n", jobNamespace]);
        arguments.AddRange([packageName, jobId]);
        return arguments.ToArray();
    }

    private async Task<string> RunJobAdbAsync(string[] arguments, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        return await RunAdbForSerialAsync(Serial, arguments, timeout.Token);
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
        => RunAdbAsync(BuildCoordinateTapArguments(button.CenterX, button.CenterY));

    private async Task RunAdbAsync(params string[] arguments)
        => _ = await RunAdbWithOutputAsync(arguments).ConfigureAwait(false);

    internal virtual Task<string> RunAdbWithOutputAsync(params string[] arguments)
        => RunAdbForSerialAsync(Serial, arguments);

    private async Task<string> RunAdbForSerialAsync(string? serial, string[] arguments, CancellationToken cancellationToken = default)
    {
        var adbArguments = BuildAdbArguments(serial, arguments);
        var processStartInfo = CreateAdbProcessStartInfo(AdbPath, adbArguments);
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);

        var exitCode = await ProcessUtils.StartProcess(
            processStartInfo,
            output,
            error,
            cancellationToken).ConfigureAwait(false);

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
