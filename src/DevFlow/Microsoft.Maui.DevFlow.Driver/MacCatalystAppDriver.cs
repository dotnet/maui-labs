using System.Diagnostics;
using Microsoft.Maui.DevFlow.Driver.Mac;

namespace Microsoft.Maui.DevFlow.Driver;

/// <summary>
/// Driver for Mac Catalyst MAUI apps.
/// Direct localhost connection, no special setup needed.
/// Uses macOS Accessibility API (AXUIElement) via P/Invoke to detect and dismiss native dialogs.
/// No Swift/Xcode dependency — pure C# interop with ApplicationServices framework.
///
/// Detection strategy:
///   1. AXModalAlert subrole — standard macOS alert sheets (alerts, action sheets, confirm dialogs).
///   2. Generic "dialog cluster" scan — recursively walks the AX tree looking for any subtree that
///      contains ≥1 AXButton plus either AXStaticText or AXTextField, without relying on specific
///      nesting depths or container subroles. This catches inline prompt dialogs and any future
///      layout changes Apple may introduce.
///
/// Button label matching:
///   - Tries Title, Description, and Value on every AXButton (not just one attribute).
///   - Normalizes smart/curly quotes before comparison.
///   - Case-insensitive.
/// </summary>
public class MacCatalystAppDriver : AppDriverBase, IAlertDriver
{
    public override string Platform => "MacCatalyst";

    /// <summary>
    /// The PID of the Mac Catalyst app process (required for AX operations).
    /// </summary>
    public int? ProcessId { get; set; }

    /// <summary>
    /// The bundle name or app name to find the process automatically.
    /// </summary>
    public string? AppName { get; set; }

    // ──────────────────────────────────────────────
    // Public API
    // ──────────────────────────────────────────────

    /// <summary>
    /// Detect a native dialog (alert, action sheet, or prompt) using macOS Accessibility API.
    /// </summary>
    public Task<AlertInfo?> DetectAlertAsync()
    {
        EnsureMacOS();
        var pid = ResolveProcessId();
        using var app = AXElement.CreateForApplication(pid);
        return Task.FromResult(DetectDialog(app));
    }

    /// <summary>
    /// Dismiss the current alert by pressing a button via AXPress action.
    /// If buttonLabel is null, presses the first button found.
    /// </summary>
    public Task DismissAlertAsync(string? buttonLabel = null)
    {
        EnsureMacOS();
        var pid = ResolveProcessId();
        using var app = AXElement.CreateForApplication(pid);

        var (_, buttonEls) = FindDialogButtons(app);
        if (buttonEls is null || buttonEls.Count == 0)
        {
            DisposeAll(buttonEls);
            throw new InvalidOperationException("No alert detected to dismiss.");
        }

        try
        {
            var target = PickButton(buttonEls, buttonLabel);
            if (!target.Press())
                throw new InvalidOperationException("AXPress action failed.");
        }
        finally { DisposeAll(buttonEls); }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Convenience: detect and dismiss an alert if present, no-op if not.
    /// Single AX tree walk — detects and dismisses in one pass to avoid stale coordinates.
    /// </summary>
    public Task<AlertInfo?> HandleAlertIfPresentAsync(string? buttonLabel = null)
    {
        EnsureMacOS();
        var pid = ResolveProcessId();
        using var app = AXElement.CreateForApplication(pid);

        var (info, buttonEls) = FindDialogButtons(app);
        if (info is null || buttonEls is null || buttonEls.Count == 0)
        {
            DisposeAll(buttonEls);
            return Task.FromResult<AlertInfo?>(null);
        }

        try
        {
            var target = PickButton(buttonEls, buttonLabel);
            target.Press();
        }
        finally { DisposeAll(buttonEls); }

        return Task.FromResult<AlertInfo?>(info);
    }

    /// <summary>
    /// Returns the full macOS accessibility tree for the app as text.
    /// </summary>
    public Task<string> GetAccessibilityTreeAsync()
    {
        EnsureMacOS();
        var pid = ResolveProcessId();
        using var app = AXElement.CreateForApplication(pid);
        var children = app.GetChildren();
        try
        {
            var result = string.Empty;
            foreach (var child in children)
            {
                if (child.Role == "AXWindow")
                    result += child.DumpTree();
            }
            return Task.FromResult(result);
        }
        finally { DisposeAll(children); }
    }

    // ──────────────────────────────────────────────
    // Screen Recording via screencapture
    // ──────────────────────────────────────────────

    public override async Task StartRecordingAsync(string outputFile, int timeoutSeconds = 30)
    {
        EnsureNotRecording();
        EnsureMacOS();

        var fullPath = Path.GetFullPath(outputFile);
        // Ensure .mov extension for screencapture
        if (!fullPath.EndsWith(".mov", StringComparison.OrdinalIgnoreCase))
            fullPath = Path.ChangeExtension(fullPath, ".mov");

        // Try to capture just the app window using -l windowID
        var args = $"-v \"{fullPath}\"";
        var windowId = TryGetWindowId();
        if (windowId.HasValue)
            args = $"-v -l {windowId.Value} \"{fullPath}\"";

        var psi = new ProcessStartInfo("screencapture", args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start screencapture");

        await Task.Delay(500);

        var watchdogPid = SpawnWatchdog(process.Id, timeoutSeconds);

        RecordingStateManager.Save(new RecordingState
        {
            RecordingPid = process.Id,
            WatchdogPid = watchdogPid,
            OutputFile = fullPath,
            Platform = "maccatalyst",
            StartedAt = DateTimeOffset.UtcNow,
            TimeoutSeconds = timeoutSeconds
        });
    }

    public override async Task<string> StopRecordingAsync()
    {
        var state = RecordingStateManager.Load()
            ?? throw new InvalidOperationException("No active recording found.");

        if (state.Platform != "maccatalyst")
            throw new InvalidOperationException($"Active recording is on {state.Platform}, not Mac Catalyst.");

        KillWatchdog(state.WatchdogPid);
        SendInterrupt(state.RecordingPid);

        try
        {
            var proc = Process.GetProcessById(state.RecordingPid);
            await proc.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch { }

        RecordingStateManager.Delete();
        return state.OutputFile;
    }

    /// <summary>
    /// Resolves the CGWindowID for the app's main window via the macOS window list.
    /// Uses a small Python script to call CoreGraphics, avoiding native P/Invoke complexity.
    /// Returns null if the window cannot be found.
    /// </summary>
    private int? TryGetWindowId()
    {
        try
        {
            var pid = ResolveProcessId();
            var script = $"""
                import Quartz
                wl = Quartz.CGWindowListCopyWindowInfo(Quartz.kCGWindowListOptionOnScreenOnly, Quartz.kCGNullWindowID)
                for w in wl:
                    if w.get('kCGWindowOwnerPID') == {pid} and w.get('kCGWindowLayer', 99) == 0:
                        print(w['kCGWindowNumber'])
                        break
                """;
            var psi = new ProcessStartInfo("python3", $"-c \"{script.Replace("\"", "\\\"")}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var proc = Process.Start(psi);
            if (proc == null) return null;
            var output = proc.StandardOutput.ReadToEnd().Trim();
            proc.WaitForExit(5000);
            return int.TryParse(output, out var wid) ? wid : null;
        }
        catch
        {
            return null;
        }
    }

    // ──────────────────────────────────────────────
    // Detection: returns AlertInfo only (for detect command)
    // ──────────────────────────────────────────────

    private static AlertInfo? DetectDialog(AXElement app)
    {
        var (info, buttonEls) = FindDialogButtons(app);
        DisposeAll(buttonEls);
        return info;
    }

    // ──────────────────────────────────────────────
    // Core: find dialog info AND live button elements in one pass
    // ──────────────────────────────────────────────

    /// <summary>
    /// Walks the AX tree and returns (AlertInfo, list of live AXButton elements for pressing).
    /// Caller MUST dispose the button elements.
    ///
    /// Strategy 1: Find an AXModalAlert subrole node — collect its direct-child text and buttons.
    /// Strategy 2 (fallback): Generic dialog cluster scan via <see cref="FindDialogCluster"/>.
    /// </summary>
    private static (AlertInfo? info, List<AXElement>? buttons) FindDialogButtons(AXElement app)
    {
        // Strategy 1: AXModalAlert — the standard, most reliable signal
        using var modalAlert = app.FindFirst(el => el.Subrole == "AXModalAlert");
        if (modalAlert is not null)
        {
            var result = CollectButtonsAndText(modalAlert);
            if (result.buttons.Count > 0)
                return (new AlertInfo(result.title, ToAlertButtons(result.buttons)), result.buttons);
            DisposeAll(result.buttons);
        }

        // Strategy 2: Generic dialog cluster — handles inline prompts and any other dialog shape
        var cluster = FindDialogCluster(app);
        if (cluster is not null)
            return cluster.Value;

        return (null, null);
    }

    /// <summary>
    /// Collects all AXButton elements and AXStaticText from a container (any depth).
    /// Returns retained AXButton elements — caller must dispose.
    /// Reads label from ALL attributes (Title, Description, Value) for maximum resilience.
    /// </summary>
    private static (string? title, List<AXElement> buttons) CollectButtonsAndText(AXElement container)
    {
        var texts = new List<string>();
        var buttonEls = new List<AXElement>();

        CollectRecursive(container, texts, buttonEls, depth: 0, maxDepth: 6);

        return (texts.Count > 0 ? texts[0] : null, buttonEls);
    }

    private static void CollectRecursive(AXElement el, List<string> texts, List<AXElement> buttonEls, int depth, int maxDepth)
    {
        if (depth >= maxDepth) return;

        var role = el.Role;

        if (role == "AXStaticText")
        {
            var text = el.Value ?? el.Title ?? el.Description ?? "";
            if (text.Length > 0) texts.Add(text);
        }
        else if (role == "AXButton")
        {
            var label = GetBestLabel(el);
            if (label.Length > 0)
                buttonEls.Add(AXElement.FromNonOwned(el.Handle));
        }

        // Don't recurse into known non-dialog roles
        if (role is "AXMenuBar" or "AXMenu" or "AXMenuItem" or "AXMenuBarItem") return;

        var children = el.GetChildren();
        try
        {
            foreach (var child in children)
                CollectRecursive(child, texts, buttonEls, depth + 1, maxDepth);
        }
        finally { DisposeAll(children); }
    }

    // ──────────────────────────────────────────────
    // Strategy 2: Generic dialog cluster detection
    // ──────────────────────────────────────────────

    /// <summary>
    /// Walks all AXWindow children looking for any subtree that looks like a dialog:
    ///   - Contains ≥1 AXButton with a non-empty label
    ///   - Contains ≥1 AXStaticText OR ≥1 AXTextField
    ///   - Is NOT the main content area (heuristic: the cluster must be "small" relative to the window —
    ///     we look for groups with ≤20 total descendants to avoid matching the entire page)
    ///
    /// This replaces the old hard-coded "iOSContentGroup → exactly 1 child → ..." pattern
    /// with a flexible scan that works regardless of nesting depth or container naming.
    /// </summary>
    private static (AlertInfo info, List<AXElement> buttons)? FindDialogCluster(AXElement app)
    {
        var windows = app.GetChildren();
        try
        {
            foreach (var window in windows)
            {
                if (window.Role != "AXWindow") continue;

                // Walk the window's subtree looking for dialog-like groups
                var result = ScanForDialogCluster(window, depth: 0, maxDepth: 10);
                if (result is not null) return result;
            }
        }
        finally { DisposeAll(windows); }
        return null;
    }

    /// <summary>
    /// Recursively scans for a "dialog cluster" — a group containing both buttons and text/textfields.
    /// Prefers the deepest (most specific) match to avoid matching the entire window.
    /// </summary>
    private static (AlertInfo info, List<AXElement> buttons)? ScanForDialogCluster(AXElement el, int depth, int maxDepth)
    {
        if (depth >= maxDepth) return null;

        var children = el.GetChildren();
        try
        {
            // First, recurse into children to find a more specific (deeper) cluster
            foreach (var child in children)
            {
                var childResult = ScanForDialogCluster(child, depth + 1, maxDepth);
                if (childResult is not null) return childResult;
            }

            // If no child matched, check if THIS element is a dialog cluster
            if (IsDialogCluster(el, children))
            {
                var texts = new List<string>();
                var buttonEls = new List<AXElement>();
                CollectRecursive(el, texts, buttonEls, 0, 6);

                if (buttonEls.Count > 0)
                {
                    var info = new AlertInfo(texts.Count > 0 ? texts[0] : null, ToAlertButtons(buttonEls));
                    return (info, buttonEls);
                }
                DisposeAll(buttonEls);
            }
        }
        finally { DisposeAll(children); }
        return null;
    }

    /// <summary>
    /// Heuristic: a node looks like a dialog cluster if its subtree contains
    /// both buttons and a text field (AXTextField), and is small enough to not be the whole page.
    ///
    /// This is deliberately stricter than "buttons + text" because normal page content often has
    /// both buttons and static text. The AXTextField requirement targets prompt dialogs specifically.
    /// Standard alerts/action sheets are already caught by Strategy 1 (AXModalAlert).
    /// </summary>
    private static bool IsDialogCluster(AXElement el, List<AXElement> children)
    {
        if (children.Count == 0) return false;

        var role = el.Role;
        // Only consider groups as potential dialog containers
        if (role is not ("AXGroup" or "AXSheet")) return false;

        bool hasButton = false;
        bool hasTextField = false;
        int totalCount = 0;

        CountDialogSignals(el, ref hasButton, ref hasTextField, ref totalCount, depth: 0, maxDepth: 6);

        // Must have both buttons AND a text field, and be reasonably small
        return hasButton && hasTextField && totalCount <= 30;
    }

    private static void CountDialogSignals(AXElement el, ref bool hasButton, ref bool hasTextField, ref int totalCount, int depth, int maxDepth)
    {
        if (depth >= maxDepth || totalCount > 30) return;
        totalCount++;

        var role = el.Role;
        if (role == "AXButton" && GetBestLabel(el).Length > 0) hasButton = true;
        if (role == "AXTextField") hasTextField = true;

        if (hasButton && hasTextField) return; // Early exit

        var children = el.GetChildren();
        try
        {
            foreach (var child in children)
            {
                CountDialogSignals(child, ref hasButton, ref hasTextField, ref totalCount, depth + 1, maxDepth);
                if (hasButton && hasTextField) return;
            }
        }
        finally { DisposeAll(children); }
    }

    // ──────────────────────────────────────────────
    // Button matching
    // ──────────────────────────────────────────────

    /// <summary>
    /// Gets the best human-readable label from an AXButton by trying Title, Description, then Value.
    /// </summary>
    private static string GetBestLabel(AXElement button)
    {
        var title = button.Title;
        if (!string.IsNullOrEmpty(title)) return title;
        var desc = button.Description;
        if (!string.IsNullOrEmpty(desc)) return desc;
        var val = button.Value;
        if (!string.IsNullOrEmpty(val)) return val;
        return "";
    }

    /// <summary>
    /// Normalizes smart/curly quotes to ASCII for reliable matching across locales.
    /// </summary>
    private static string NormalizeQuotes(string s)
        => s.Replace('\u2018', '\'').Replace('\u2019', '\'')
            .Replace('\u201C', '"').Replace('\u201D', '"');

    /// <summary>
    /// Picks the button to press. If a label is specified, matches case-insensitively
    /// with quote normalization. Otherwise picks the first button.
    /// </summary>
    private static AXElement PickButton(List<AXElement> buttons, string? buttonLabel)
    {
        if (buttons.Count == 0)
            throw new InvalidOperationException("No buttons found in dialog.");

        if (buttonLabel is not null)
        {
            var normalized = NormalizeQuotes(buttonLabel);
            var match = buttons.FirstOrDefault(b =>
                NormalizeQuotes(GetBestLabel(b)).Equals(normalized, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                var available = string.Join(", ", buttons.Select(b => GetBestLabel(b)));
                throw new InvalidOperationException($"Button '{buttonLabel}' not found. Available: {available}");
            }
            return match;
        }

        // No label specified — return first button
        return buttons[0];
    }

    private static List<AlertButton> ToAlertButtons(List<AXElement> elements)
        => elements.Select(b => new AlertButton(GetBestLabel(b), 0, 0, 0, 0)).ToList();

    // ──────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────

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

        throw new InvalidOperationException("ProcessId or AppName must be set for Mac Catalyst operations.");
    }

    private static void EnsureMacOS()
    {
        if (!OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException("Mac Catalyst dialog handling requires macOS.");
    }

    private static void DisposeAll(List<AXElement>? elements)
    {
        if (elements is null) return;
        foreach (var el in elements) el.Dispose();
    }
}
