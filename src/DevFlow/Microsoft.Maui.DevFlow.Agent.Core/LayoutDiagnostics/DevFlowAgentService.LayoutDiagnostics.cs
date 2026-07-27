namespace Microsoft.Maui.DevFlow.Agent.Core;

public partial class DevFlowAgentService
{
    private const int MaxLayoutDiagnosticNodes = 5000;
    private const int MaxLayoutDiagnosticFindings = 1000;
    private readonly SemaphoreSlim _layoutDiagnosticsGate = new(1, 1);
    private readonly SemaphoreSlim _nativeLayoutProbeGate = new(1, 1);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, SemaphoreSlim>
        _blazorLayoutProbeGates = new();

    private Task<HttpResponse> HandleLayoutDiagnosticRules(HttpRequest request)
        => Task.FromResult(HttpResponse.Json(
            LayoutDiagnosticsEngine.BuildCatalog(_treeWalker.GetLayoutRuleSupport())));

    private async Task<HttpResponse> HandleLayoutDiagnostics(HttpRequest request)
    {
        if (_app is null)
        {
            return HttpResponse.Error(
                "Agent is not yet bound to an app.",
                503,
                "layout-diagnostics-not-ready");
        }

        var inspectionRequest = request.BodyAs<LayoutInspectionRequest>() ?? new LayoutInspectionRequest();
        var validationError = NormalizeLayoutInspectionRequest(inspectionRequest);
        if (validationError is not null)
        {
            return HttpResponse.Error(
                validationError,
                400,
                "layout-diagnostics-validation");
        }

        if (!await _layoutDiagnosticsGate.WaitAsync(0))
            return HttpResponse.Error("A layout diagnostics scan is already running", 429, "layout-diagnostics-busy");

        try
        {
        var cancellationToken = request.CancellationToken;
        var timeout = TimeSpan.FromMilliseconds(inspectionRequest.Stability.TimeoutMs);
        var deadline = DateTimeOffset.UtcNow + timeout;
        LayoutCaptureSnapshot? lastCapture = null;
        string? priorHash = null;
        var consecutiveStableFrames = 0;
        var stable = false;
        string? stabilityReason = null;
        Task<List<ElementInfo>>? pendingNativeCapture = null;
        var nativeCaptureUnavailable = false;
        var pendingBlazorCaptures = new Dictionary<int, Task<string>>();
        var unavailableBlazorCaptures = new HashSet<int>();

        do
        {
            using var captureCts =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
            captureCts.CancelAfter(Math.Max(
                1,
                (int)Math.Ceiling(
                    (deadline - DateTimeOffset.UtcNow)
                    .TotalMilliseconds)));
            try
            {
                lastCapture = await DispatchAsync(() =>
                {
                    var capture = _treeWalker.CaptureLayoutSnapshot(_app, inspectionRequest);
                    ApplyWindowScales(capture);
                    return capture;
                }, captureCts.Token);
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                lastCapture = new LayoutCaptureSnapshot();
                lastCapture.MarkIncomplete(
                    "UI-thread layout capture did not complete before the scan deadline.");
                stabilityReason =
                    "UI-thread layout capture exceeded the requested stability timeout.";
                break;
            }
            (pendingNativeCapture, nativeCaptureUnavailable) =
                await EnrichLayoutCaptureAsync(
                lastCapture,
                inspectionRequest,
                pendingNativeCapture,
                nativeCaptureUnavailable,
                pendingBlazorCaptures,
                unavailableBlazorCaptures,
                deadline,
                cancellationToken);

            if (inspectionRequest.Stability.Mode.Equals("immediate", StringComparison.OrdinalIgnoreCase))
            {
                stable = !lastCapture.HasActiveAnimations || inspectionRequest.Stability.AllowActiveAnimations;
                stabilityReason = stable ? null : "An active platform animation was detected.";
                break;
            }

            if (lastCapture.HasActiveAnimations && !inspectionRequest.Stability.AllowActiveAnimations)
            {
                consecutiveStableFrames = 0;
                priorHash = null;
                stabilityReason = "An active platform animation was detected.";
            }
            else if (string.Equals(priorHash, lastCapture.StabilityHash, StringComparison.Ordinal))
            {
                consecutiveStableFrames++;
                if (consecutiveStableFrames >= inspectionRequest.Stability.StableFrames)
                {
                    stable = true;
                    stabilityReason = null;
                    break;
                }
            }
            else
            {
                priorHash = lastCapture.StabilityHash;
                consecutiveStableFrames = 1;
            }

            if (DateTimeOffset.UtcNow >= deadline)
                break;

            var remainingDelayMs = Math.Max(
                0,
                (int)(deadline - DateTimeOffset.UtcNow).TotalMilliseconds);
            if (remainingDelayMs == 0)
                break;
            await Task.Delay(Math.Min(
                inspectionRequest.Stability.QuietPeriodMs,
                remainingDelayMs),
                cancellationToken);
        }
        while (DateTimeOffset.UtcNow < deadline);

        if (pendingNativeCapture is { IsCompleted: false })
        {
            _ = pendingNativeCapture.ContinueWith(
                task => _ = task.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
        foreach (var pendingBlazorCapture in pendingBlazorCaptures.Values)
        {
            if (!pendingBlazorCapture.IsCompleted)
            {
                _ = pendingBlazorCapture.ContinueWith(
                    task => _ = task.Exception,
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }

        if (lastCapture is null)
        {
            return HttpResponse.Error(
                "Unable to capture layout diagnostics.",
                500,
                "layout-diagnostics-server-error");
        }

        if (!stable && stabilityReason is null)
            stabilityReason = "The analyzed geometry did not remain unchanged for the requested stable frame count.";

        var result = LayoutDiagnosticsEngine.Analyze(
            lastCapture,
            inspectionRequest,
            PlatformName,
            stable,
            stabilityReason,
            _treeWalker.GetLayoutRuleSupport());
        if (result.Findings.Count > MaxLayoutDiagnosticFindings)
        {
            result.Coverage.Limitations.Add(
                $"Findings were truncated to {MaxLayoutDiagnosticFindings} entries.");
            result.Findings = result.Findings.Take(MaxLayoutDiagnosticFindings).ToList();
            result.Summary = LayoutDiagnosticsEngine.Summarize(
                result.Findings,
                result.Summary.Passes,
                result.Summary.NotApplicable);
            if (!stable)
                result.Summary.Incomplete++;
            result.Summary.Incomplete += lastCapture.IncompleteReasons
                .Distinct(StringComparer.Ordinal)
                .Count();
            result.Summary.Incomplete += result.Coverage.Rules.Count(rule =>
                rule.Support.Equals(
                    "unsupported",
                    StringComparison.OrdinalIgnoreCase)
                && (inspectionRequest.Rules is not { Count: > 0 }
                    || inspectionRequest.Rules.Contains(
                        rule.RuleId,
                        StringComparer.OrdinalIgnoreCase)));
            if (result.Coverage.OpaqueSubtrees.Count > 0)
                result.Summary.Incomplete++;
            result.Summary.Incomplete++;
        }
        return HttpResponse.Json(result);
        }

        catch (OperationCanceledException) when (request.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ArgumentException ex)
        {
            return HttpResponse.Error(
                ex.Message,
                400,
                "layout-diagnostics-validation");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Microsoft.Maui.DevFlow.Agent] Layout diagnostics failed: {ex}");
            return HttpResponse.Error(
                "Layout diagnostics failed.",
                500,
                "layout-diagnostics-server-error");
        }
        finally
        {
            _layoutDiagnosticsGate.Release();
        }
    }

    private async Task<(Task<List<ElementInfo>>? PendingNativeCapture, bool NativeCaptureUnavailable)> EnrichLayoutCaptureAsync(
        LayoutCaptureSnapshot capture,
        LayoutInspectionRequest request,
        Task<List<ElementInfo>>? pendingNativeCapture,
        bool nativeCaptureUnavailable,
        Dictionary<int, Task<string>> pendingBlazorCaptures,
        HashSet<int> unavailableBlazorCaptures,
        DateTimeOffset deadline,
        CancellationToken cancellationToken)
    {
        if (request.Scope.IncludeNativeElements && _treeWalker.SupportsNativeElements)
        {
            if (nativeCaptureUnavailable)
            {
                capture.MarkIncomplete(
                    "Native automation layout capture was unavailable after an earlier timeout in this scan.");
            }
            else
            {
            var appendedCompletedCapture = false;
            if (pendingNativeCapture is { IsCompleted: true })
            {
                try
                {
                    _treeWalker.AppendNativeLayoutNodes(
                        capture,
                        await pendingNativeCapture);
                }
                catch (Exception ex)
                {
                    capture.MarkIncomplete(
                        $"Native automation layout capture failed: {ex.GetType().Name}");
                }
                pendingNativeCapture = null;
                appendedCompletedCapture = true;
            }

            if (!appendedCompletedCapture && pendingNativeCapture is null)
            {
                IReadOnlyList<IntPtr> handles = [];
                using var handleCts =
                    CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken);
                handleCts.CancelAfter(
                    GetRemainingProbeTimeoutMs(deadline));
                try
                {
                    handles = await DispatchAsync(() =>
                        _treeWalker.GetKnownNativeWindowHandles(
                            _app!,
                            request.Scope.Window),
                        handleCts.Token);
                }
                catch (OperationCanceledException)
                    when (!cancellationToken.IsCancellationRequested)
                {
                    nativeCaptureUnavailable = true;
                    capture.MarkIncomplete(
                        "Native window discovery did not complete before the current scan deadline.");
                }
                if (!nativeCaptureUnavailable)
                {
                    if (!await _nativeLayoutProbeGate.WaitAsync(
                        0,
                        cancellationToken))
                    {
                        nativeCaptureUnavailable = true;
                        capture.MarkIncomplete(
                            "A previous native automation layout probe is still running.");
                    }
                    else
                    {
                        pendingNativeCapture = Task.Run(() =>
                        {
                            try
                            {
                                return _treeWalker.WalkNativeTree(
                                    handles,
                                    request.Scope.MaxDepth);
                            }
                            finally
                            {
                                _nativeLayoutProbeGate.Release();
                            }
                        });
                        _ = pendingNativeCapture.ContinueWith(
                            task => _ = task.Exception,
                            CancellationToken.None,
                            TaskContinuationOptions.OnlyOnFaulted
                                | TaskContinuationOptions.ExecuteSynchronously,
                            TaskScheduler.Default);
                    }
                }
            }

            if (!appendedCompletedCapture && pendingNativeCapture is not null)
            {
                var nativeWinner = await Task.WhenAny(
                    pendingNativeCapture,
                    Task.Delay(
                        GetRemainingProbeTimeoutMs(deadline),
                        cancellationToken));
                cancellationToken.ThrowIfCancellationRequested();
                if (nativeWinner == pendingNativeCapture)
                {
                    try
                    {
                        _treeWalker.AppendNativeLayoutNodes(
                            capture,
                            await pendingNativeCapture);
                    }
                    catch (Exception ex)
                    {
                        capture.MarkIncomplete(
                            $"Native automation layout capture failed: {ex.GetType().Name}");
                    }
                    pendingNativeCapture = null;
                }
                else
                {
                    capture.MarkIncomplete(
                        $"Native automation layout capture exceeded {NativeUiProbeTimeoutMs} ms.");
                    _ = pendingNativeCapture.ContinueWith(
                        task => _ = task.Exception,
                        CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted
                            | TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                    pendingNativeCapture = null;
                    nativeCaptureUnavailable = true;
                }
            }
            }
        }

        await EnrichLayoutCaptureWithBlazorAsync(
            capture,
            request,
            pendingBlazorCaptures,
            unavailableBlazorCaptures,
            deadline,
            cancellationToken);
        _treeWalker.ApplyLayoutScope(capture, request.Scope);
        ApplyLayoutCaptureLimits(capture);
        capture.HasActiveAnimations = capture.Nodes.Any(node => node.HasActiveAnimation);
        return (pendingNativeCapture, nativeCaptureUnavailable);
    }

    private static int GetRemainingProbeTimeoutMs(DateTimeOffset deadline)
        => Math.Clamp(
            (int)Math.Ceiling((deadline - DateTimeOffset.UtcNow).TotalMilliseconds),
            1,
            NativeUiProbeTimeoutMs);

    private void ApplyWindowScales(LayoutCaptureSnapshot capture)
    {
        if (_app is null)
            return;

        foreach (var windowInfo in capture.Windows)
        {
            if (!windowInfo.Id.StartsWith("window-", StringComparison.Ordinal)
                || !int.TryParse(windowInfo.Id.AsSpan("window-".Length), out var windowIndex)
                || windowIndex < 0
                || windowIndex >= _app.Windows.Count)
            {
                continue;
            }

            var scale = GetWindowDisplayDensity(_app.Windows[windowIndex]);
            if (!double.IsFinite(scale) || scale <= 0)
                scale = 1;
            windowInfo.Scale = scale;
            foreach (var node in capture.Nodes.Where(node => node.WindowId == windowInfo.Id))
                node.WindowScale = scale;
        }
    }

    private static void ApplyLayoutCaptureLimits(LayoutCaptureSnapshot capture)
    {
        if (capture.Nodes.Count <= MaxLayoutDiagnosticNodes)
            return;

        capture.Nodes.RemoveRange(
            MaxLayoutDiagnosticNodes,
            capture.Nodes.Count - MaxLayoutDiagnosticNodes);
        capture.MarkIncomplete(
            $"The realized tree exceeded {MaxLayoutDiagnosticNodes} nodes and was truncated.");
    }

    private static string? NormalizeLayoutInspectionRequest(LayoutInspectionRequest request)
    {
        if (!request.SchemaVersion.Equals("1.0", StringComparison.Ordinal))
            return $"Unsupported layout diagnostics schema version '{request.SchemaVersion}'. Expected '1.0'.";

        request.Scope ??= new LayoutInspectionScope();
        request.Stability ??= new LayoutStabilityOptions();
        request.Occlusion ??= new LayoutOcclusionOptions();
        request.Privacy ??= new LayoutPrivacyOptions();
        request.Suppressions ??= [];

        request.Profile = (request.Profile ?? string.Empty).Trim().ToLowerInvariant();
        if (request.Profile is not ("agent" or "strict" or "exhaustive" or "ci"))
            return "profile must be one of: agent, strict, exhaustive, ci";

        request.MinimumSeverity = (request.MinimumSeverity ?? string.Empty).Trim().ToLowerInvariant();
        if (request.MinimumSeverity is not ("info" or "minor" or "moderate" or "serious" or "critical"))
            return "minimumSeverity must be one of: info, minor, moderate, serious, critical";

        request.Stability.Mode = (request.Stability.Mode ?? string.Empty).Trim().ToLowerInvariant();
        if (request.Stability.Mode is not ("wait" or "immediate"))
            return "stability.mode must be 'wait' or 'immediate'";

        var occlusionMode = (request.Occlusion.Mode ?? string.Empty).Trim();
        if (occlusionMode.Equals("interactiveTargets", StringComparison.OrdinalIgnoreCase))
            request.Occlusion.Mode = "interactiveTargets";
        else if (occlusionMode.Equals("none", StringComparison.OrdinalIgnoreCase))
            request.Occlusion.Mode = "none";
        else if (occlusionMode.Equals("all", StringComparison.OrdinalIgnoreCase))
            request.Occlusion.Mode = "all";
        else
            return "occlusion.mode must be one of: none, interactiveTargets, all";

        request.Privacy.Text = (request.Privacy.Text ?? string.Empty).Trim().ToLowerInvariant();
        if (request.Privacy.Text is not ("none" or "length" or "raw"))
            return "privacy.text must be one of: none, length, raw";

        if (request.Scope.Window < 0)
            return "scope.window must be zero or greater";

        if (request.Rules is { Count: > 0 })
        {
            request.Rules = request.Rules
                .Where(rule => !string.IsNullOrWhiteSpace(rule))
                .Select(rule => rule.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (request.Rules.Count == 0)
                return "rules must contain at least one layout diagnostic rule ID";

            var unknownRules = request.Rules
                .Where(rule => !LayoutDiagnosticRules.All.Contains(
                    rule,
                    StringComparer.OrdinalIgnoreCase))
                .OrderBy(rule => rule, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (unknownRules.Length > 0)
            {
                return $"Unknown layout diagnostic rule ID(s): {string.Join(", ", unknownRules)}. "
                    + $"Supported rules: {string.Join(", ", LayoutDiagnosticRules.All)}";
            }
        }

        request.Stability.StableFrames = Math.Clamp(request.Stability.StableFrames, 1, 10);
        request.Stability.QuietPeriodMs = Math.Clamp(request.Stability.QuietPeriodMs, 16, 1000);
        request.Stability.TimeoutMs = Math.Clamp(request.Stability.TimeoutMs, 50, 10000);
        request.Occlusion.MaxSamplesPerElement = Math.Clamp(request.Occlusion.MaxSamplesPerElement, 1, 1024);
        request.Occlusion.CoverageError = Math.Clamp(request.Occlusion.CoverageError, 0.001, 0.5);
        request.Occlusion.MinimumOverlapRatio = Math.Clamp(request.Occlusion.MinimumOverlapRatio, 0, 1);
        request.Scope.MaxDepth = Math.Clamp(request.Scope.MaxDepth, 0, 200);
        return null;
    }
}
