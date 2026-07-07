using System.Diagnostics;
using System.Text.Json;

namespace Microsoft.Maui.DevFlow.Driver;

/// <summary>
/// Driver for iOS Simulator MAUI apps.
/// iOS Simulator shares host network stack, so localhost works directly.
/// Includes simctl-based permission management and accessibility-based alert detection
/// using the apple CLI (appledev.tools) for HID tap and accessibility tree queries.
/// </summary>
public class iOSSimulatorAppDriver : AppDriverBase
{
    private static readonly string[] AcceptLabels =
    [
        "Allow", "OK", "Allow While Using App", "Allow Once", "Continue", "Yes", "Confirm",
        // Variants seen in newer iOS versions
        "Allow Access", "Grant Access", "Enable", "Turn On", "Give Access",
        // Action-sheet style confirmations
        "Done", "Open", "Install", "Update",
    ];

    private static readonly string[] AlertIndicators =
        ["Alert", "alert", "UIAlertController", "Sheet", "ActionSheet"];

    public override string Platform => "iOSSimulator";

    /// <summary>
    /// The simulator device UDID (required for simctl/idb operations).
    /// </summary>
    public string? DeviceUdid { get; set; }

    /// <summary>
    /// The app bundle identifier (required for permission operations).
    /// </summary>
    public string? BundleId { get; set; }

    public override async Task<ThemeResult> SetThemeAsync(DevFlowTheme theme, ThemeSetScope scope = ThemeSetScope.Auto)
    {
        if (scope == ThemeSetScope.App || (scope == ThemeSetScope.Auto && theme == DevFlowTheme.System))
            return await base.SetThemeAsync(theme, ThemeSetScope.App).ConfigureAwait(false);

        if (theme == DevFlowTheme.System)
        {
            return new ThemeResult
            {
                Theme = theme,
                RequestedTheme = theme,
                Source = "system",
                Success = false,
                Message = "iOS Simulator host appearance supports light and dark only. Use app scope for system theme.",
            };
        }

        EnsureDeviceUdid();
        var appearance = theme == DevFlowTheme.Dark ? "dark" : "light";
        await RunProcessAsync("xcrun", $"simctl ui {DeviceUdid} appearance {appearance}").ConfigureAwait(false);

        return new ThemeResult
        {
            Theme = theme,
            RequestedTheme = theme,
            EffectiveTheme = theme,
            Source = "system",
            Success = true,
            Message = $"iOS Simulator appearance set to {appearance}.",
        };
    }

    // --- Permission management via xcrun simctl privacy ---

    /// <summary>
    /// Grant a permission to the app without showing a dialog.
    /// </summary>
    public Task GrantPermissionAsync(PermissionService service)
        => RunSimctlPrivacyAsync("grant", service, BundleId);

    /// <summary>
    /// Revoke a permission from the app.
    /// </summary>
    public Task RevokePermissionAsync(PermissionService service)
        => RunSimctlPrivacyAsync("revoke", service, BundleId);

    /// <summary>
    /// Reset a permission so the app will be prompted again on next use.
    /// </summary>
    public Task ResetPermissionAsync(PermissionService service = PermissionService.All)
        => RunSimctlPrivacyAsync("reset", service, BundleId);

    // --- Alert detection via idb accessibility_info ---

    /// <summary>
    /// Query the iOS accessibility tree and look for an alert/dialog.
    /// Returns AlertInfo if one is found, null otherwise.
    /// </summary>
    public Task<AlertInfo?> DetectAlertAsync() => DetectAlertAsync(maxAttempts: 3, retryDelayMs: 300);

    /// <summary>
    /// Query the iOS accessibility tree and look for an alert/dialog.
    /// Retries up to <paramref name="maxAttempts"/> times with <paramref name="retryDelayMs"/> ms
    /// between each attempt to tolerate timing windows where the AX tree is queried before the
    /// dialog finishes committing.
    /// Returns AlertInfo if one is found, null otherwise.
    /// </summary>
    public async Task<AlertInfo?> DetectAlertAsync(int maxAttempts, int retryDelayMs)
    {
        if (maxAttempts < 1) throw new ArgumentOutOfRangeException(nameof(maxAttempts), "Must be at least 1.");
        if (retryDelayMs < 0) throw new ArgumentOutOfRangeException(nameof(retryDelayMs), "Must be non-negative.");

        EnsureDeviceUdid();

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var json = await RunIdbAccessibilityInfoAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
            {
                if (attempt < maxAttempts) await Task.Delay(retryDelayMs).ConfigureAwait(false);
                continue;
            }

            try
            {
                // The accessibility output may contain embedded newlines in string values;
                // replace bare control characters so JsonDocument can parse it.
                json = SanitizeJson(json);
                using var doc = JsonDocument.Parse(json);
                var elements = ParseElements(doc.RootElement);
                var alert = FindAlert(elements);
                if (alert is not null)
                    return alert;
            }
            catch
            {
                // JSON parse failure — fall through to retry
            }

            if (attempt < maxAttempts)
                await Task.Delay(retryDelayMs).ConfigureAwait(false);
        }

        return null;
    }

    /// <summary>
    /// Dismiss the current alert by tapping a button.
    /// If buttonLabel is null, taps the first "Accept"-style button (Allow, OK, etc.).
    /// </summary>
    public async Task DismissAlertAsync(string? buttonLabel = null)
    {
        var alert = await DetectAlertAsync().ConfigureAwait(false);
        if (alert is null || alert.Buttons.Count == 0)
            throw new InvalidOperationException("No alert detected to dismiss.");

        var button = buttonLabel is not null
            ? alert.Buttons.FirstOrDefault(b => b.Label.Equals(buttonLabel, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"No button '{buttonLabel}' found. Available: {string.Join(", ", alert.Buttons.Select(b => b.Label))}")
            : alert.Buttons.FirstOrDefault(b => AcceptLabels.Contains(b.Label, StringComparer.OrdinalIgnoreCase))
                ?? alert.Buttons[0];

        await TapCoordinateAsync(button.CenterX, button.CenterY).ConfigureAwait(false);
    }

    /// <summary>
    /// Convenience: detect and dismiss an alert if present, no-op if not.
    /// Returns the alert info if one was found and dismissed, null otherwise.
    /// </summary>
    public async Task<AlertInfo?> HandleAlertIfPresentAsync(string? buttonLabel = null)
    {
        var alert = await DetectAlertAsync().ConfigureAwait(false);
        if (alert is null || alert.Buttons.Count == 0)
            return null;

        var button = buttonLabel is not null
            ? alert.Buttons.FirstOrDefault(b => b.Label.Equals(buttonLabel, StringComparison.OrdinalIgnoreCase))
            : alert.Buttons.FirstOrDefault(b => AcceptLabels.Contains(b.Label, StringComparer.OrdinalIgnoreCase))
                ?? alert.Buttons[0];

        if (button is not null)
            await TapCoordinateAsync(button.CenterX, button.CenterY).ConfigureAwait(false);

        return alert;
    }

    /// <summary>
    /// Returns the sanitized accessibility tree JSON from the simulator.
    /// If the JSON cannot be parsed, the raw (unsanitized) output is returned instead
    /// so that callers can diagnose what the apple CLI actually produced.
    /// </summary>
    public async Task<string> GetAccessibilityTreeAsync()
    {
        EnsureDeviceUdid();
        var raw = await RunIdbAccessibilityInfoAsync().ConfigureAwait(false);
        var sanitized = SanitizeJson(raw);
        // Validate the sanitized output parses; if not, return the raw string so the
        // caller can see exactly what came out of the apple CLI.
        try
        {
            using var _ = JsonDocument.Parse(sanitized);
            return sanitized;
        }
        catch
        {
            return raw;
        }
    }

    /// <summary>
    /// Tap at raw screen coordinates via HID events injected into the simulator.
    /// Uses the apple CLI (appledev.tools) which wraps idb_companion directly.
    /// </summary>
    public async Task TapCoordinateAsync(int x, int y)
    {
        EnsureDeviceUdid();
        await RunProcessAsync("apple", $"simulator idb tap {DeviceUdid} {x} {y}").ConfigureAwait(false);
    }

    // --- Screenshot via xcrun simctl io screenshot ---

    /// <summary>
    /// Captures a full-screen screenshot of the simulator using <c>xcrun simctl io screenshot</c>.
    /// This captures everything visible on the simulator display including SpringBoard overlays,
    /// permission dialogs, system view controllers (e.g., contact picker), and other out-of-process
    /// UI that the in-app agent screenshot cannot see.
    /// Falls back to the agent-based screenshot if simctl is unavailable.
    /// </summary>
    public override async Task<byte[]?> ScreenshotAsync()
    {
        if (!string.IsNullOrEmpty(DeviceUdid))
        {
            try
            {
                var tempFile = Path.Combine(Path.GetTempPath(), $"devflow-simctl-{Guid.NewGuid():N}.png");
                try
                {
                    await RunProcessAsync("xcrun", $"simctl io {DeviceUdid} screenshot --type png \"{tempFile}\"")
                        .ConfigureAwait(false);

                    if (File.Exists(tempFile))
                    {
                        var bytes = await File.ReadAllBytesAsync(tempFile).ConfigureAwait(false);
                        if (bytes.Length > 0)
                            return ResizeSimctlPng(bytes);
                    }
                }
                finally
                {
                    try { File.Delete(tempFile); } catch { }
                }
            }
            catch
            {
                // simctl failed — fall through to agent-based screenshot
            }
        }

        return await base.ScreenshotAsync().ConfigureAwait(false);
    }

    // --- Screen Recording via xcrun simctl io recordVideo ---

    public override async Task StartRecordingAsync(string outputFile, int timeoutSeconds = 30)
    {
        EnsureNotRecording();
        EnsureDeviceUdid();

        var fullPath = Path.GetFullPath(outputFile);
        var psi = new ProcessStartInfo("xcrun",
            $"simctl io {DeviceUdid} recordVideo --codec h264 \"{fullPath}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start xcrun simctl io recordVideo");

        // Give simctl a moment to initialize recording
        await Task.Delay(500);

        var watchdogPid = SpawnWatchdog(process.Id, timeoutSeconds);

        RecordingStateManager.Save(new RecordingState
        {
            RecordingPid = process.Id,
            WatchdogPid = watchdogPid,
            OutputFile = fullPath,
            Platform = "ios",
            StartedAt = DateTimeOffset.UtcNow,
            TimeoutSeconds = timeoutSeconds
        });
    }

    public override async Task<string> StopRecordingAsync()
    {
        var state = RecordingStateManager.Load()
            ?? throw new InvalidOperationException("No active recording found.");

        if (state.Platform != "ios")
            throw new InvalidOperationException($"Active recording is on {state.Platform}, not iOS.");

        KillWatchdog(state.WatchdogPid);
        SendInterrupt(state.RecordingPid);

        // Wait for simctl to finalize the video file
        try
        {
            var proc = Process.GetProcessById(state.RecordingPid);
            await proc.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        }
        catch { }

        RecordingStateManager.Delete();
        return state.OutputFile;
    }

    // --- Private helpers ---

    private void EnsureDeviceUdid()
    {
        if (string.IsNullOrEmpty(DeviceUdid))
            throw new InvalidOperationException("DeviceUdid must be set for simulator operations.");
    }

    /// <summary>
    /// Downscales a native-resolution simctl screenshot to 1x logical pixels.
    /// Infers the display density from common iOS simulator pixel widths.
    /// </summary>
    private static byte[] ResizeSimctlPng(byte[] pngData)
    {
        try
        {
            using var original = SkiaSharp.SKBitmap.Decode(pngData);
            if (original == null) return pngData;

            double density = original.Width switch
            {
                1290 or 1320 or 1206 => 3.0,
                1170 => 3.0,
                1125 => 3.0,
                1242 => 3.0,
                828 => 2.0,
                750 => 2.0,
                2048 or 2388 or 2360 => 2.0,
                _ => original.Width > 1000 ? 3.0 : 2.0
            };

            var targetWidth = (int)(original.Width / density);
            if (targetWidth <= 0 || targetWidth >= original.Width)
                return pngData;

            var scale = (float)targetWidth / original.Width;
            var newHeight = (int)(original.Height * scale);

            using var resized = original.Resize(
                new SkiaSharp.SKImageInfo(targetWidth, newHeight),
                SkiaSharp.SKSamplingOptions.Default);
            if (resized == null) return pngData;

            using var image = SkiaSharp.SKImage.FromBitmap(resized);
            using var encoded = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            return encoded.ToArray();
        }
        catch
        {
            return pngData;
        }
    }

    /// <summary>
    /// Remove bare newlines and carriage returns from the accessibility JSON output.
    /// The apple CLI output wraps long lines, inserting newlines inside string values
    /// and even number literals. Stripping all bare newlines is safe because the JSON
    /// is a single logical line.
    /// </summary>
    private static string SanitizeJson(string json)
        => json.Replace("\n", "").Replace("\r", "");

    private Task RunSimctlPrivacyAsync(string action, PermissionService service, string? bundleId)
    {
        EnsureDeviceUdid();

        var serviceStr = service switch
        {
            PermissionService.All => "all",
            PermissionService.Calendar => "calendar",
            PermissionService.Contacts => "contacts",
            PermissionService.ContactsLimited => "contacts-limited",
            PermissionService.Location => "location",
            PermissionService.LocationAlways => "location-always",
            PermissionService.Photos => "photos",
            PermissionService.PhotosAdd => "photos-add",
            PermissionService.MediaLibrary => "media-library",
            PermissionService.Microphone => "microphone",
            PermissionService.Motion => "motion",
            PermissionService.Reminders => "reminders",
            PermissionService.Siri => "siri",
            PermissionService.Camera => "camera",
            _ => throw new ArgumentOutOfRangeException(nameof(service))
        };

        var args = bundleId is not null
            ? $"simctl privacy {DeviceUdid} {action} {serviceStr} {bundleId}"
            : $"simctl privacy {DeviceUdid} {action} {serviceStr}";

        return RunProcessAsync("xcrun", args);
    }

    private async Task<string> RunIdbAccessibilityInfoAsync()
    {
        try
        {
            return await RunProcessWithOutputAsync("apple", $"simulator idb accessibility {DeviceUdid}").ConfigureAwait(false);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static async Task RunProcessAsync(string command, string args)
    {
        var psi = new ProcessStartInfo(command, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {command}");
        await proc.WaitForExitAsync().ConfigureAwait(false);

        if (proc.ExitCode != 0)
        {
            var stderr = await proc.StandardError.ReadToEndAsync().ConfigureAwait(false);
            throw new InvalidOperationException($"{command} failed (exit {proc.ExitCode}): {stderr}");
        }
    }

    private static async Task<string> RunProcessWithOutputAsync(string command, string args)
    {
        var psi = new ProcessStartInfo(command, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {command}");
        var output = await proc.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
        await proc.WaitForExitAsync().ConfigureAwait(false);
        return output;
    }

    // --- Accessibility tree parsing ---

    private record AccessibilityElement(string? Label, string? Value, string? Type, string? Description,
        double X, double Y, double Width, double Height, IReadOnlyList<AccessibilityElement> Children);

    private static IReadOnlyList<AccessibilityElement> ParseElements(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
            return element.EnumerateArray().Select(ParseElement).ToList();
        if (element.ValueKind == JsonValueKind.Object)
            return [ParseElement(element)];
        return [];
    }

    private static AccessibilityElement ParseElement(JsonElement el)
    {
        var label = el.TryGetProperty("AXLabel", out var l) ? l.GetString() : null;
        var value = el.TryGetProperty("AXValue", out var v) ? v.GetString() : null;
        var type = el.TryGetProperty("type", out var t) ? t.GetString() : null;
        var description = el.TryGetProperty("AXDescription", out var d) ? d.GetString() : null;

        double x = 0, y = 0, w = 0, h = 0;
        if (el.TryGetProperty("frame", out var f) && f.ValueKind == JsonValueKind.Object)
        {
            x = f.TryGetProperty("x", out var fx) ? fx.GetDouble() : 0;
            y = f.TryGetProperty("y", out var fy) ? fy.GetDouble() : 0;
            w = f.TryGetProperty("width", out var fw) ? fw.GetDouble() : 0;
            h = f.TryGetProperty("height", out var fh) ? fh.GetDouble() : 0;
        }

        IReadOnlyList<AccessibilityElement> children = [];
        if (el.TryGetProperty("children", out var c) && c.ValueKind == JsonValueKind.Array)
            children = c.EnumerateArray().Select(ParseElement).ToList();

        return new AccessibilityElement(label, value, type, description, x, y, w, h, children);
    }

    private static AlertInfo? FindAlert(IReadOnlyList<AccessibilityElement> elements)
    {
        // Strategy 1: Look for an element explicitly marked as alert
        foreach (var el in elements)
        {
            if (IsAlertElement(el))
            {
                var buttons = new List<AlertButton>();
                CollectButtons(el, buttons);
                if (buttons.Count > 0)
                {
                    var title = FindFirstLabelOfType(el, "StaticText");
                    return new AlertInfo(title, buttons);
                }
            }

            if (el.Children.Count > 0)
            {
                var found = FindAlert(el.Children);
                if (found is not null) return found;
            }
        }

        // Strategy 2: iOS 26+ heuristic — when a system alert or action sheet is showing,
        // the Application element's direct children flatten to only simple element types
        // (the normal app hierarchy collapses). Detect this pattern.
        // We check for the presence of Buttons and the absence of real app container types
        // (Window, NavigationBar, TabBar, ScrollView/ScrollArea, etc.) rather than
        // allowing only a specific set of types, so new element types in future iOS/Xcode
        // versions don't break detection.
        var app = elements.FirstOrDefault(e =>
            string.Equals(e.Type, "Application", StringComparison.OrdinalIgnoreCase));
        if (app is not null && app.Children.Count >= 2)
        {
            var childTypes = app.Children.Select(c => c.Type).ToList();
            bool hasButtons = childTypes.Any(t => string.Equals(t, "Button", StringComparison.OrdinalIgnoreCase));
            bool hasAppContainer = childTypes.Any(t =>
                string.Equals(t, "Window", StringComparison.OrdinalIgnoreCase)
                || string.Equals(t, "NavigationBar", StringComparison.OrdinalIgnoreCase)
                || string.Equals(t, "TabBar", StringComparison.OrdinalIgnoreCase)
                || string.Equals(t, "ToolBar", StringComparison.OrdinalIgnoreCase)
                || string.Equals(t, "ScrollView", StringComparison.OrdinalIgnoreCase)
                || string.Equals(t, "ScrollArea", StringComparison.OrdinalIgnoreCase));

            if (hasButtons && !hasAppContainer)
            {
                var buttons = new List<AlertButton>();
                CollectButtons(app, buttons);
                if (buttons.Count > 0)
                {
                    var title = FindFirstLabelOfType(app, "StaticText");
                    return new AlertInfo(title, buttons);
                }
            }
        }

        return null;
    }

    private static bool IsAlertElement(AccessibilityElement el)
        => (el.Type is not null && AlertIndicators.Any(a => el.Type.Contains(a, StringComparison.OrdinalIgnoreCase)))
        || (el.Description is not null && AlertIndicators.Any(a => el.Description.Contains(a, StringComparison.OrdinalIgnoreCase)));

    private static void CollectButtons(AccessibilityElement el, List<AlertButton> buttons)
    {
        if (el.Type is not null && el.Type.Contains("Button", StringComparison.OrdinalIgnoreCase)
            && el.Label is not null && el.Width > 0)
        {
            buttons.Add(new AlertButton(el.Label, el.X, el.Y, el.Width, el.Height));
        }

        foreach (var child in el.Children)
            CollectButtons(child, buttons);
    }

    private static string? FindFirstLabelOfType(AccessibilityElement el, string typeName)
    {
        if (el.Type is not null && el.Type.Contains(typeName, StringComparison.OrdinalIgnoreCase) && el.Label is not null)
            return el.Label;
        foreach (var child in el.Children)
        {
            var found = FindFirstLabelOfType(child, typeName);
            if (found is not null) return found;
        }
        return null;
    }
}
