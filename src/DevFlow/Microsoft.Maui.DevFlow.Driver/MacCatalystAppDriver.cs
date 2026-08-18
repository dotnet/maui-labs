using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
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
///   2. Explicit AX dialog and sheet containers.
///   3. Generic "dialog cluster" scan — recursively walks the AX tree looking for any subtree that
///      contains ≥1 AXButton plus either AXStaticText or AXTextField, without relying on specific
///      nesting depths or container subroles. This catches inline prompt dialogs and any future
///      layout changes Apple may introduce.
///
/// Button label matching:
///   - Tries Title, Description, and Value on every AXButton (not just one attribute).
///   - Normalizes smart/curly quotes before comparison.
///   - Case-insensitive.
/// </summary>
public class MacCatalystAppDriver : AppDriverBase
{
    private static readonly HashSet<string> SystemDialogProcessNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "CoreServicesUIAgent",
            "NetAuthAgent",
            "SecurityAgent",
            "authorizationhost"
        };
    private static readonly ConcurrentDictionary<string, DialogLease> DialogLeases = new();
    private static readonly TimeSpan DialogLeaseLifetime = TimeSpan.FromMinutes(2);

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
    /// The target app is checked first, followed by known macOS system prompt hosts.
    /// </summary>
    public Task<AlertInfo?> DetectAlertAsync()
    {
        EnsureMacOS();
        EnsureAccessibilityAuthorized();

        var match = FindDialogMatch();
        if (match is null)
            return Task.FromResult<AlertInfo?>(null);

        if (!match.Value.info.CanRespond)
        {
            DisposeAll(match.Value.buttons);
            return Task.FromResult<AlertInfo?>(match.Value.info);
        }

        return Task.FromResult<AlertInfo?>(CreateDialogLease(match.Value));
    }

    public bool IsAccessibilityAuthorized()
    {
        EnsureMacOS();
        return MacAccessibility.AXIsProcessTrusted();
    }

    public static void CancelAlertResponse(string promptId)
    {
        if (string.IsNullOrWhiteSpace(promptId))
            return;
        if (DialogLeases.TryRemove(promptId, out var lease))
            lease.Dispose();
    }

    /// <summary>
    /// Presses an exact button on the exact prompt previously returned by
    /// <see cref="DetectAlertAsync"/>. If the prompt changed, no action is performed.
    /// </summary>
    public Task<AlertActionResult> PressAlertButtonAsync(string promptId, string buttonLabel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(promptId);
        ArgumentException.ThrowIfNullOrWhiteSpace(buttonLabel);
        EnsureMacOS();
        EnsureAccessibilityAuthorized();

        PurgeExpiredDialogLeases();
        if (!DialogLeases.TryRemove(promptId, out var lease))
        {
            return Task.FromResult(new AlertActionResult(
                false,
                true,
                "The reviewed prompt expired or was already used. Detect the visible prompt again and ask the user what to do."));
        }

        using (lease)
        {
            if (lease.TargetProcessId != ResolveProcessId())
            {
                return Task.FromResult(new AlertActionResult(
                    false,
                    true,
                    "The connected app changed after the prompt was reviewed. Detect the prompt again.",
                    lease.Info));
            }

            var match = FindDialogMatch();
            if (match is null)
            {
                return Task.FromResult(new AlertActionResult(
                    false,
                    true,
                    "The reviewed prompt is no longer visible. Ask the user before acting on any new prompt."));
            }

            var (info, currentButtons) = match.Value;
            try
            {
                var currentFingerprint = NativeDialogIdentity.CreateFingerprint(
                    Platform,
                    $"{info.SourceProcessId ?? 0}:{info.SourceProcessName}",
                    info);
                if (!string.Equals(lease.Fingerprint, currentFingerprint, StringComparison.Ordinal))
                {
                    return Task.FromResult(new AlertActionResult(
                        false,
                        true,
                        "The visible prompt changed after it was reviewed. Detect it again and ask the user what to do.",
                        info));
                }

                if (!info.CanRespond)
                {
                    return Task.FromResult(new AlertActionResult(
                        false,
                        true,
                        "The visible system dialog cannot be safely attributed to the target app. Ask the user to respond manually.",
                        info));
                }

                AXElement target;
                try
                {
                    target = PickExactButton(currentButtons, buttonLabel);
                }
                catch (InvalidOperationException ex)
                {
                    return Task.FromResult(new AlertActionResult(
                        false,
                        true,
                        $"{ex.Message} Ask the user to choose one of the currently visible buttons.",
                        info));
                }

                if (!target.Press())
                {
                    return Task.FromResult(new AlertActionResult(
                        false,
                        true,
                        $"macOS did not allow the '{buttonLabel}' AXPress action. Ask the user to respond manually.",
                        info));
                }

                return Task.FromResult(new AlertActionResult(
                    true,
                    false,
                    $"Pressed '{buttonLabel}' using AXPress without synthesizing mouse or keyboard input.",
                    info));
            }
            finally
            {
                DisposeAll(currentButtons);
            }
        }
    }

    /// <summary>
    /// Dismiss the current alert by pressing an exact button label via AXPress.
    /// </summary>
    /// <param name="buttonLabel">The exact visible button label. A label is always required on macOS.</param>
    public Task DismissAlertAsync(string buttonLabel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(buttonLabel);
        EnsureMacOS();
        EnsureAccessibilityAuthorized();

        var match = FindDialogMatch();
        if (match is null)
            throw new InvalidOperationException("No alert detected to dismiss.");

        var (info, buttonEls) = match.Value;
        try
        {
            if (!info.CanRespond)
                throw new InvalidOperationException("The visible system dialog cannot be safely attributed to the target app. Ask the user to respond manually.");

            var target = PickExactButton(buttonEls, buttonLabel);
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
    /// <param name="buttonLabel">The exact visible button label. A label is always required on macOS.</param>
    public Task<AlertInfo?> HandleAlertIfPresentAsync(string buttonLabel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(buttonLabel);
        EnsureMacOS();
        EnsureAccessibilityAuthorized();

        var match = FindDialogMatch();
        if (match is null)
            return Task.FromResult<AlertInfo?>(null);

        var (info, buttonEls) = match.Value;
        try
        {
            if (!info.CanRespond)
                throw new InvalidOperationException("The visible system dialog cannot be safely attributed to the target app. Ask the user to respond manually.");

            var target = PickExactButton(buttonEls, buttonLabel);
            if (!target.Press())
                throw new InvalidOperationException("AXPress action failed.");
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
        EnsureAccessibilityAuthorized();
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

        var windowId = TryGetWindowId()
            ?? throw new InvalidOperationException(
                "No visible app window was found. Refusing to record the entire desktop.");
        var args = $"-v -l {windowId} \"{fullPath}\"";

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
    /// Resolves the CGWindowID for the app's frontmost visible normal window via
    /// CoreGraphics without activating the application.
    /// Returns null if the window cannot be found.
    /// </summary>
    private int? TryGetWindowId()
    {
        try
        {
            return MacWindowServer.TryGetWindowId(ResolveProcessId());
        }
        catch
        {
            return null;
        }
    }

    // ──────────────────────────────────────────────
    // Detection
    // ──────────────────────────────────────────────

    private (AlertInfo info, List<AXElement> buttons)? FindDialogMatch()
    {
        var targetPid = ResolveProcessId();
        var targetProcessName = GetProcessName(targetPid) ?? AppName ?? "Target App";
        (AlertInfo info, List<AXElement> buttons)? unattributedSystemDialog = null;

        foreach (var candidate in GetDialogProcesses(targetPid, targetProcessName))
        {
            try
            {
                using var app = AXElement.CreateForApplication(candidate.ProcessId);
                var match = FindDialogButtons(app);
                var isRecognizedDialog = match.isRecognizedDialog;
                if ((match.info is null || match.buttons is null || match.buttons.Count == 0) &&
                    candidate.IsSystemDialog)
                {
                    DisposeAll(match.buttons);
                    match = FindSystemDialogButtons(app);
                    isRecognizedDialog = false;
                }

                if (match.info is null || match.buttons is null || match.buttons.Count == 0)
                {
                    DisposeAll(match.buttons);
                    continue;
                }

                var canRespond = !candidate.IsSystemDialog ||
                    (isRecognizedDialog &&
                    NativeDialogSafety.IsSystemDialogForTarget(
                        match.info,
                        AppName,
                        targetProcessName));
                var info = match.info with
                {
                    SourceProcessId = candidate.ProcessId,
                    SourceProcessName = candidate.ProcessName,
                    IsSystemDialog = candidate.IsSystemDialog,
                    RequiresUserConfirmation = true,
                    CanRespond = canRespond
                };

                if (canRespond)
                {
                    DisposeAll(unattributedSystemDialog?.buttons);
                    return (info, match.buttons);
                }

                if (unattributedSystemDialog is null)
                    unattributedSystemDialog = (info, match.buttons);
                else
                    DisposeAll(match.buttons);
            }
            catch when (candidate.IsSystemDialog)
            {
                // System prompt hosts are short-lived; continue if one exits during the scan.
            }
        }

        return unattributedSystemDialog;
    }

    private AlertInfo CreateDialogLease(
        (AlertInfo info, List<AXElement> buttons) match)
    {
        PurgeExpiredDialogLeases();

        var promptId = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var leasedInfo = match.info with { PromptId = promptId };
        var fingerprint = NativeDialogIdentity.CreateFingerprint(
            Platform,
            $"{match.info.SourceProcessId ?? 0}:{match.info.SourceProcessName}",
            match.info);
        var lease = new DialogLease(
            ResolveProcessId(),
            fingerprint,
            leasedInfo,
            match.buttons,
            DateTimeOffset.UtcNow.Add(DialogLeaseLifetime));

        if (!DialogLeases.TryAdd(promptId, lease))
        {
            lease.Dispose();
            throw new InvalidOperationException("Unable to create a unique native-dialog response token.");
        }

        _ = ExpireDialogLeaseAsync(promptId);
        return leasedInfo;
    }

    private static async Task ExpireDialogLeaseAsync(string promptId)
    {
        await Task.Delay(DialogLeaseLifetime).ConfigureAwait(false);
        if (DialogLeases.TryRemove(promptId, out var expired))
            expired.Dispose();
    }

    private static void PurgeExpiredDialogLeases()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in DialogLeases)
        {
            if (entry.Value.ExpiresAt <= now &&
                DialogLeases.TryRemove(entry.Key, out var expired))
            {
                expired.Dispose();
            }
        }
    }

    // ──────────────────────────────────────────────
    // Core: find dialog info AND live button elements in one pass
    // ──────────────────────────────────────────────

    /// <summary>
    /// Walks the AX tree and returns (AlertInfo, list of live AXButton elements for pressing).
    /// Caller MUST dispose the button elements.
    ///
    /// Strategy 1: Find an AXModalAlert subrole node — collect its direct-child text and buttons.
    /// Strategy 2: Find an explicit dialog or sheet container.
    /// Strategy 3: Generic dialog cluster scan via <see cref="FindDialogCluster"/>.
    /// </summary>
    private static (AlertInfo? info, List<AXElement>? buttons, bool isRecognizedDialog) FindDialogButtons(AXElement app)
    {
        // Strategy 1: AXModalAlert — the standard, most reliable signal
        using var modalAlert = app.FindFirst(el => el.Subrole == "AXModalAlert");
        if (modalAlert is not null)
        {
            var result = CollectButtonsAndText(modalAlert);
            if (result.buttons.Count > 0)
                return (CreateAlertInfo(result.texts, result.buttons), result.buttons, true);
            DisposeAll(result.buttons);
        }

        // Strategy 2: Explicit AX dialog/sheet containers.
        using var dialog = app.FindFirst(el =>
            el.Role == "AXSheet" ||
            el.Subrole is "AXDialog" or "AXSystemDialog");
        if (dialog is not null)
        {
            var result = CollectButtonsAndText(dialog);
            if (result.buttons.Count > 0)
                return (CreateAlertInfo(result.texts, result.buttons), result.buttons, true);
            DisposeAll(result.buttons);
        }

        // Strategy 3: Generic dialog cluster — handles inline prompts and any other dialog shape
        var cluster = FindDialogCluster(app);
        if (cluster is not null)
            return (cluster.Value.info, cluster.Value.buttons, false);

        return (null, null, false);
    }

    private static (AlertInfo? info, List<AXElement>? buttons, bool isRecognizedDialog) FindSystemDialogButtons(AXElement app)
    {
        var windows = app.GetChildren();
        try
        {
            foreach (var window in windows)
            {
                if (window.Role != "AXWindow")
                    continue;

                var result = CollectButtonsAndText(window);
                if (result.buttons.Count > 0)
                    return (CreateAlertInfo(result.texts, result.buttons), result.buttons, false);
                DisposeAll(result.buttons);
            }
        }
        finally
        {
            DisposeAll(windows);
        }

        return (null, null, false);
    }

    /// <summary>
    /// Collects all AXButton elements and AXStaticText from a container (any depth).
    /// Returns retained AXButton elements — caller must dispose.
    /// Reads label from ALL attributes (Title, Description, Value) for maximum resilience.
    /// </summary>
    private static (List<string> texts, List<AXElement> buttons) CollectButtonsAndText(AXElement container)
    {
        var texts = new List<string>();
        var buttonEls = new List<AXElement>();

        CollectRecursive(container, texts, buttonEls, depth: 0, maxDepth: 6);

        return (texts, buttonEls);
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
                    var info = CreateAlertInfo(texts, buttonEls);
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
    /// Picks one button by exact visible label and rejects ambiguous matches.
    /// </summary>
    private static AXElement PickExactButton(List<AXElement> buttons, string buttonLabel)
    {
        if (buttons.Count == 0)
            throw new InvalidOperationException("No buttons found in dialog.");

        var matches = buttons
            .Where(button => ButtonLabelsMatch(GetBestLabel(button), buttonLabel))
            .ToList();
        if (matches.Count == 1)
            return matches[0];

        var available = string.Join(", ", buttons.Select(GetBestLabel));
        if (matches.Count == 0)
        {
            throw new InvalidOperationException(
                $"Button '{buttonLabel}' was not found by exact label. Available: {available}");
        }

        throw new InvalidOperationException(
            $"More than one button has the exact label '{buttonLabel}'. No action was performed.");
    }

    internal static bool ButtonLabelsMatch(string visibleLabel, string requestedLabel)
        => NormalizeQuotes(visibleLabel).Equals(NormalizeQuotes(requestedLabel), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeQuotes(string value)
        => value.Replace('\u2018', '\'').Replace('\u2019', '\'')
            .Replace('\u201C', '"').Replace('\u201D', '"');

    private static List<AlertButton> ToAlertButtons(List<AXElement> elements)
        => elements.Select(button => new AlertButton(GetBestLabel(button), 0, 0, 0, 0)
        {
            Identifier = button.Identifier
        }).ToList();

    private static AlertInfo CreateAlertInfo(List<string> texts, List<AXElement> buttons)
        => new(texts.FirstOrDefault(), ToAlertButtons(buttons))
        {
            Text = texts
        };

    // ──────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────

    private int ResolveProcessId()
    {
        if (ProcessId.HasValue)
            return ProcessId.Value;

        if (!string.IsNullOrEmpty(AppName))
        {
            var processId = ProcessNameResolver.FindUniqueProcessId(AppName);
            if (processId.HasValue)
            {
                ProcessId = processId.Value;
                return processId.Value;
            }
        }

        throw new InvalidOperationException(
            "Unable to uniquely resolve the Mac Catalyst process. Set ProcessId explicitly.");
    }

    private static IReadOnlyList<DialogProcess> GetDialogProcesses(
        int targetPid,
        string targetProcessName)
    {
        var processes = new List<DialogProcess>
        {
            new(targetPid, targetProcessName, false)
        };

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id != targetPid && SystemDialogProcessNames.Contains(process.ProcessName))
                        processes.Add(new DialogProcess(process.Id, process.ProcessName, true));
                }
                catch
                {
                    // A process may exit or deny metadata access while enumerating.
                }
            }
        }

        return processes;
    }

    private static string? GetProcessName(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    private static void EnsureAccessibilityAuthorized()
    {
        if (!MacAccessibility.AXIsProcessTrusted())
        {
            throw new InvalidOperationException(
                "macOS Accessibility permission is required for native dialog interaction. " +
                "Pause and ask the user to grant Accessibility access to the terminal or host running maui, then retry.");
        }
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

    private readonly record struct DialogProcess(
        int ProcessId,
        string ProcessName,
        bool IsSystemDialog);

    private sealed class DialogLease(
        int targetProcessId,
        string fingerprint,
        AlertInfo info,
        List<AXElement> buttons,
        DateTimeOffset expiresAt) : IDisposable
    {
        public int TargetProcessId { get; } = targetProcessId;
        public string Fingerprint { get; } = fingerprint;
        public AlertInfo Info { get; } = info;
        public List<AXElement> Buttons { get; } = buttons;
        public DateTimeOffset ExpiresAt { get; } = expiresAt;

        public void Dispose() => DisposeAll(Buttons);
    }
}
