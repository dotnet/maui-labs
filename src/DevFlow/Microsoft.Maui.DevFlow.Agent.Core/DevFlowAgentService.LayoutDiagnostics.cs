using Microsoft.Maui.DevFlow.Agent.Core.LayoutDiagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Microsoft.Maui.DevFlow.Agent.Core;

/// <summary>
/// On-demand layout diagnostics: <c>GET|POST /api/v1/ui/diagnostics/layout</c>.
///
/// This is an explicit, one-shot, read-only scan. There is no watcher, no delta stream, and no
/// background sampling, so the observer effect is bounded to the single UI-thread tree walk the
/// caller asked for. The walk also populates the runtime element map, so the whole report comes
/// from one pass instead of a per-element <c>GetElementById</c> re-walk.
/// </summary>
public partial class DevFlowAgentService
{
    private readonly SemaphoreSlim _layoutDiagnosticsGate = new(1, 1);

    private sealed record LayoutCapture(
        IReadOnlyList<LayoutElementSnapshot> Snapshots,
        LayoutDiagnosticsScope Scope,
        DateTime CapturedUtc,
        bool RootFound,
        string? Error = null);

    private Task<HttpResponse> HandleLayoutDiagnosticsGet(HttpRequest request)
    {
        var layoutRequest = new LayoutInspectionRequest
        {
            Scope = new LayoutInspectionScope { Mode = LayoutScopeModes.AllWindows },
            ElementId = request.QueryParams.TryGetValue("elementId", out var elementId) && !string.IsNullOrWhiteSpace(elementId)
                ? elementId
                : null,
            Window = ParseWindowIndex(request),
            MaxElements = request.QueryParams.TryGetValue("maxElements", out var max) && int.TryParse(max, out var parsed)
                ? parsed
                : null,
            Rules = [.. LayoutDiagnosticRules.Managed],
            MinimumSeverity = LayoutSeverity.Info,
        };
        return RunLayoutDiagnosticsAsync(layoutRequest);
    }

    private Task<HttpResponse> HandleLayoutDiagnosticsPost(HttpRequest request)
    {
        var layoutRequest = request.BodyAs<LayoutInspectionRequest>() ?? new LayoutInspectionRequest();
        layoutRequest.Scope ??= new LayoutInspectionScope();
        layoutRequest.Stability ??= new LayoutStabilityOptions();
        layoutRequest.Occlusion ??= new LayoutOcclusionOptions();
        layoutRequest.Privacy ??= new LayoutPrivacyOptions();
        layoutRequest.Suppressions ??= [];
        layoutRequest.Window ??= ParseWindowIndex(request);
        return RunLayoutDiagnosticsAsync(layoutRequest);
    }

    private Task<HttpResponse> HandleLayoutDiagnosticRules(HttpRequest request)
        => Task.FromResult(HttpResponse.Json(LayoutDiagnosticsAnalyzer.CreateRuleCatalog()));

    private async Task<HttpResponse> RunLayoutDiagnosticsAsync(LayoutInspectionRequest request)
    {
        if (_app == null)
            return HttpResponse.Error("Agent not bound to app");

        ApplyLayoutProfile(request);
        var validation = ValidateLayoutRequest(request);
        if (validation is not null)
        {
            return HttpResponse.Error(
                validation,
                statusCode: 400,
                reason: "layout-diagnostics-validation");
        }

        static void ApplyLayoutProfile(LayoutInspectionRequest request)
        {
            request.Rules ??= [.. LayoutDiagnosticRules.All];
            switch (request.Profile.ToLowerInvariant())
            {
                case "strict":
                    request.Scope.Mode = LayoutScopeModes.ActivePage;
                    request.Suppressions ??= [];
                    request.SuppressionMode = LayoutSuppressionModes.Ignore;
                    request.IncludePasses = true;
                    request.Stability.StableFrames = Math.Max(3, request.Stability.StableFrames);
                    request.Occlusion.Mode = "all";
                    break;
                case "exhaustive":
                    request.Scope.Mode = LayoutScopeModes.AllWindows;
                    request.MaxElements ??= LayoutDiagnosticsFormat.MaxElements;
                    request.IncludePasses = true;
                    request.Stability.StableFrames = Math.Max(3, request.Stability.StableFrames);
                    request.Occlusion.Mode = "all";
                    break;
                case "ci":
                    request.Scope.Mode = LayoutScopeModes.AllWindows;
                    request.MaxElements ??= LayoutDiagnosticsFormat.MaxElements;
                    request.IncludePasses = true;
                    request.Stability.StableFrames = Math.Max(3, request.Stability.StableFrames);
                    request.Occlusion.Mode = "interactiveTargets";
                    break;
            }
        }

        var maxElements = Math.Clamp(
            request.MaxElements ?? LayoutDiagnosticsFormat.DefaultMaxElements,
            1,
            LayoutDiagnosticsFormat.MaxElements);
        var requestedRoot = request.Scope.RootElementId ?? request.ElementId;
        var rootElementId = string.IsNullOrWhiteSpace(requestedRoot) ? null : requestedRoot.Trim();
        var window = request.Scope.Window ?? request.Window;
        if (request.Profile is "exhaustive" or "ci")
            request.Scope.Mode = LayoutScopeModes.AllWindows;

        // Serialize scans so two concurrent callers cannot interleave walker state.
        using var timeout = new CancellationTokenSource(request.Stability.TimeoutMs);
        try
        {
            await _layoutDiagnosticsGate.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return HttpResponse.Error(
                "Timed out waiting to start layout diagnostics.",
                statusCode: 408,
                reason: "layout-diagnostics-timeout");
        }

        try
        {
            LayoutCapture? latest = null;
            string? previousRevision = null;
            var consecutiveStableFrames = 0;
            var requiredStableFrames = request.Stability.Mode.Equals(
                "immediate",
                StringComparison.OrdinalIgnoreCase)
                ? 1
                : Math.Max(2, request.Stability.StableFrames);
            var stable = false;
            var stabilityReason = "stability-timeout";

            while (!timeout.IsCancellationRequested)
            {
                latest = await DispatchAsync(() =>
                    CaptureLayoutSnapshot(request, rootElementId, window, maxElements));
                if (latest.Error is not null)
                {
                    return HttpResponse.Error(
                        latest.Error,
                        statusCode: 409,
                        reason: "layout-diagnostics-scope");
                }
                if (!latest.RootFound)
                {
                    return HttpResponse.Error(
                    $"Element '{rootElementId}' not found",
                    statusCode: 404,
                    reason: "layout-diagnostics-element-not-found");
                }

                if (request.Scope.IncludeBlazorElements)
                {
                    var augmented = latest.Snapshots.ToList();
                    bool blazorTruncated;
                    try
                    {
                        blazorTruncated = await EnrichBlazorLayoutSnapshotsAsync(
                            augmented,
                            request,
                            maxElements,
                            timeout.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        stabilityReason = "stability-timeout";
                        break;
                    }
                    latest = latest with
                    {
                        Snapshots = augmented,
                        Scope = new LayoutDiagnosticsScope
                        {
                            RootElementId = latest.Scope.RootElementId,
                            Window = latest.Scope.Window,
                            MaxElements = latest.Scope.MaxElements,
                            ElementsExamined = augmented.Count,
                            Truncated = latest.Scope.Truncated || blazorTruncated,
                            WindowBounds = latest.Scope.WindowBounds,
                        },
                    };
                }

                var revision = ComputeLayoutSnapshotRevision(latest.Snapshots);
                consecutiveStableFrames = string.Equals(
                    revision,
                    previousRevision,
                    StringComparison.Ordinal)
                    ? consecutiveStableFrames + 1
                    : 1;
                previousRevision = revision;

                if (request.Stability.Mode.Equals("immediate", StringComparison.OrdinalIgnoreCase))
                {
                    stabilityReason = "immediate-snapshot-requested";
                    break;
                }
                if (consecutiveStableFrames >= requiredStableFrames)
                {
                    stable = true;
                    stabilityReason = "consecutive-layout-snapshots-matched";
                    break;
                }

                try
                {
                    await Task.Delay(request.Stability.QuietPeriodMs, timeout.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            if (latest is null)
            {
                return HttpResponse.Error(
                    "Layout diagnostics timed out before a snapshot was captured.",
                    statusCode: 408,
                    reason: "layout-diagnostics-timeout");
            }

            var report = await Task.Run(() => LayoutDiagnosticsAnalyzer.Analyze(
                latest.Snapshots,
                latest.Scope,
                PlatformName,
                latest.CapturedUtc,
                request,
                stable,
                stabilityReason)).ConfigureAwait(false);
            return HttpResponse.Json(report);
        }
        finally
        {
            _layoutDiagnosticsGate.Release();
        }
    }

    /// <summary>Runs entirely on the UI thread and returns detached, geometry-only state.</summary>
    private LayoutCapture CaptureLayoutSnapshot(
        LayoutInspectionRequest request,
        string? rootElementId,
        int? window,
        int maxElements)
    {
        var app = _app;
        if (app == null)
        {
            return new LayoutCapture(
                [],
                new LayoutDiagnosticsScope(),
                DateTime.UtcNow,
                RootFound: false,
                Error: "Agent is not bound to an application.");
        }

        _treeWalker.CaptureWalkElements = true;
        _treeWalker.CaptureLayoutOnly = true;
        try
        {
            var maxDepth = request.Scope.IncludeDescendants
                ? request.Scope.MaxDepth
                : 1;
            List<ElementInfo> roots;
            string? effectiveRootElementId = rootElementId;
            var effectiveWindow = window;

            if (rootElementId is not null)
            {
                roots = _treeWalker.WalkSubtree(
                    app,
                    rootElementId,
                    maxDepth,
                    maxElements: maxElements,
                    windowIndex: window);
            }
            else if (request.Scope.Mode.Equals(
                LayoutScopeModes.ActivePage,
                StringComparison.OrdinalIgnoreCase))
            {
                if (effectiveWindow is null && app.Windows.Count != 1)
                {
                    return new LayoutCapture(
                        [],
                        new LayoutDiagnosticsScope(),
                        DateTime.UtcNow,
                        RootFound: false,
                        Error: "Active-page scope is ambiguous because multiple windows are open; specify scope.window or use allWindows.");
                }

                effectiveWindow ??= 0;
                var targetWindow = GetWindow(effectiveWindow);
                var activePage = ResolveActiveLayoutPage(targetWindow);
                if (activePage is null)
                {
                    return new LayoutCapture(
                        [],
                        new LayoutDiagnosticsScope(),
                        DateTime.UtcNow,
                        RootFound: false,
                        Error: "The active page could not be resolved for the requested window.");
                }

                roots = _treeWalker.WalkRoot(activePage, maxDepth, maxElements);
                effectiveRootElementId = roots.FirstOrDefault()?.Id;
            }
            else
            {
                roots = _treeWalker.WalkTree(
                    app,
                    maxDepth,
                    windowIndex: window,
                    maxElements: maxElements);
            }

            if (rootElementId is not null &&
                effectiveWindow is null &&
                roots.FirstOrDefault() is { } scopedRoot &&
                _treeWalker.WalkElements.TryGetValue(scopedRoot.Id, out var scopedRuntime) &&
                scopedRuntime is IVisualTreeElement scopedVisual)
            {
                effectiveWindow = ResolveContainingWindowIndex(app, scopedVisual);
                if (effectiveWindow is null && app.Windows.Count > 1)
                {
                    return new LayoutCapture(
                        [],
                        new LayoutDiagnosticsScope(),
                        DateTime.UtcNow,
                        RootFound: false,
                        Error: "The scoped element's containing window could not be resolved; specify scope.window.");
                }
            }

            var collected = LayoutSnapshotCollector.Collect(
                roots,
                _treeWalker.WalkElements,
                rootElementId: null,
                maxElements,
                _treeWalker,
                request);
            if (effectiveWindow is { } windowIndex)
            {
                foreach (var snapshot in collected.Snapshots)
                    snapshot.WindowId = $"window-{windowIndex}";
            }

            var scope = new LayoutDiagnosticsScope
            {
                RootElementId = effectiveRootElementId,
                Window = effectiveWindow,
                MaxElements = maxElements,
                ElementsExamined = collected.Snapshots.Count,
                Truncated = collected.Truncated || _treeWalker.WalkWasTruncated,
                WindowBounds = ResolveWindowRect(effectiveWindow),
            };

            return new LayoutCapture(
                collected.Snapshots,
                scope,
                DateTime.UtcNow,
                RootFound: rootElementId is null || roots.Count > 0);
        }
        finally
        {
            // Never retain strong references to live elements past the scan.
            _treeWalker.CaptureLayoutOnly = false;
            _treeWalker.CaptureWalkElements = false;
            _treeWalker.ClearWalkElements();
        }
    }

    internal static Page? ResolveActiveLayoutPage(Window? window)
    {
        var page = window?.Navigation?.ModalStack.LastOrDefault() ?? window?.Page;
        while (page is not null)
        {
            var next = page switch
            {
                Shell shell => shell.CurrentPage,
                NavigationPage navigationPage => navigationPage.CurrentPage,
                TabbedPage tabbedPage => tabbedPage.CurrentPage,
                FlyoutPage flyoutPage => flyoutPage.Detail,
                _ => page,
            };
            if (ReferenceEquals(next, page))
                return page;
            page = next;
        }
        return null;
    }

    private static int? ResolveContainingWindowIndex(
        Application app,
        IVisualTreeElement element)
    {
        IVisualTreeElement? current = element;
        while (current is not null)
        {
            if (current is Window window)
            {
                for (var index = 0; index < app.Windows.Count; index++)
                {
                    if (ReferenceEquals(app.Windows[index], window))
                        return index;
                }
                return null;
            }
            current = current.GetVisualParent();
        }
        return app.Windows.Count == 1 ? 0 : null;
    }

    private static string ComputeLayoutSnapshotRevision(
        IReadOnlyList<LayoutElementSnapshot> snapshots)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var snapshot in snapshots)
        {
            Append(snapshot.Id);
            Append(snapshot.ParentId);
            Append(snapshot.IsVisible ? "1" : "0");
            Append(snapshot.IsRealized ? "1" : "0");
            AppendRect(snapshot.Frame);
            AppendRect(snapshot.WindowBounds);
            AppendSize(snapshot.DesiredSize);
            AppendRegion(snapshot.FullRegion);
            AppendRegion(snapshot.VisibleRegion);
            AppendRegion(snapshot.ContentRegion);
            Append(snapshot.TextEvidence?.IsTruncated?.ToString());
            Append(snapshot.TextEvidence?.EllipsisCount?.ToString(CultureInfo.InvariantCulture));
            Append(snapshot.HitTestSampleCount.ToString(CultureInfo.InvariantCulture));
            Append(snapshot.BlockedHitTestSampleCount.ToString(CultureInfo.InvariantCulture));
            Append(snapshot.ExplicitWidth?.ToString("R", CultureInfo.InvariantCulture));
            Append(snapshot.ExplicitHeight?.ToString("R", CultureInfo.InvariantCulture));
            Append(snapshot.MinimumWidth?.ToString("R", CultureInfo.InvariantCulture));
            Append(snapshot.MinimumHeight?.ToString("R", CultureInfo.InvariantCulture));
            Append(snapshot.MaximumWidth?.ToString("R", CultureInfo.InvariantCulture));
            Append(snapshot.MaximumHeight?.ToString("R", CultureInfo.InvariantCulture));
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();

        void Append(string? value)
        {
            var bytes = Encoding.UTF8.GetBytes(value ?? "");
            hash.AppendData(bytes);
            hash.AppendData([0]);
        }

        void AppendNumber(double value)
            => Append(Math.Round(value, 3).ToString("R", CultureInfo.InvariantCulture));

        void AppendRect(LayoutRect? rect)
        {
            if (rect is null)
            {
                Append(null);
                return;
            }
            AppendNumber(rect.X);
            AppendNumber(rect.Y);
            AppendNumber(rect.Width);
            AppendNumber(rect.Height);
        }

        void AppendSize(LayoutSize? size)
        {
            if (size is null)
            {
                Append(null);
                return;
            }
            AppendNumber(size.Width);
            AppendNumber(size.Height);
        }

        void AppendRegion(LayoutRegionInfo? region)
        {
            AppendRect(region?.Bounds);
            Append(region?.Precision);
            Append(region?.Area.ToString("R", CultureInfo.InvariantCulture));
        }
    }

    private async Task<bool> EnrichBlazorLayoutSnapshotsAsync(
            List<LayoutElementSnapshot> snapshots,
            LayoutInspectionRequest request,
            int maxElements,
            CancellationToken cancellationToken)
        {
            var readyWebViews = GetCdpWebViewsSnapshot().Where(item => item.ReadyCheck()).ToArray();
            var remaining = maxElements - snapshots.Count;
            if (remaining <= 0)
                return readyWebViews.Length > 0;
            var truncated = false;

            foreach (var webView in readyWebViews)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (remaining <= 0)
                {
                    truncated = true;
                    break;
                }

                var host = snapshots.FirstOrDefault(snapshot =>
                    (!string.IsNullOrWhiteSpace(webView.ElementId) &&
                     string.Equals(snapshot.Id, webView.ElementId, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(webView.AutomationId) &&
                     string.Equals(snapshot.AutomationId, webView.AutomationId, StringComparison.OrdinalIgnoreCase)));
                if (host?.WindowBounds is null)
                    continue;

                JsonElement? value;
                try
                {
                    value = await EvaluateWebViewExpressionAsync(
                        webView,
                        BuildBlazorLayoutExpression(Math.Min(remaining, 500))).WaitAsync(cancellationToken);
                }
                catch
                {
                    host.PlatformEvidenceLimitations.Add(
                        "The Blazor DOM layout probe was unavailable or timed out.");
                    continue;
                }

                if (value is not { ValueKind: JsonValueKind.Object } result ||
                    !result.TryGetProperty("nodes", out var nodes) ||
                    nodes.ValueKind != JsonValueKind.Array)
                    continue;
                if (result.TryGetProperty("total", out var totalValue) &&
                    totalValue.TryGetInt32(out var total) &&
                    total > nodes.GetArrayLength())
                {
                    truncated = true;
                }

                var ordinal = 0;
                var domIds = new List<string>();
                foreach (var node in nodes.EnumerateArray())
                {
                    if (remaining-- <= 0)
                    {
                        truncated = true;
                        break;
                    }
                    if (!TryReadRect(node, "full", host.WindowBounds, out var full))
                        continue;

                    var id = node.TryGetProperty("automationId", out var automationIdValue)
                        ? automationIdValue.GetString()
                        : null;
                    var tag = node.TryGetProperty("tag", out var tagValue)
                        ? tagValue.GetString() ?? "element"
                        : "element";
                    var domId =
                        $"blazor-{webView.Index}-{ordinal}-{SanitizeDomId(id) ?? "node"}";
                    var parentId = host.Id;
                    if (node.TryGetProperty("parentIndex", out var parentIndexValue) &&
                        parentIndexValue.TryGetInt32(out var parentIndex) &&
                        parentIndex >= 0 &&
                        parentIndex < domIds.Count)
                    {
                        parentId = domIds[parentIndex];
                    }
                    var snapshot = new LayoutElementSnapshot
                    {
                        Id = domId,
                        ParentId = parentId,
                        WindowId = host.WindowId,
                        Type = $"Blazor:{tag}",
                        AutomationId = id,
                        Role = node.TryGetProperty("role", out var roleValue) ? roleValue.GetString() : null,
                        Interactive = node.TryGetProperty("interactive", out var interactiveValue) &&
                            interactiveValue.ValueKind == JsonValueKind.True,
                        IsVisible = true,
                        IsEnabled = true,
                        IsRealized = true,
                        HasLayoutState = true,
                        Frame = new LayoutRect
                        {
                            X = full.X - host.WindowBounds.X,
                            Y = full.Y - host.WindowBounds.Y,
                            Width = full.Width,
                            Height = full.Height,
                        },
                        WindowBounds = full,
                        FullRegion = LayoutPlatformEvidence.Region(full, "conservativeBounds"),
                    };

                    if (TryReadRect(node, "visible", host.WindowBounds, out var visible))
                        snapshot.VisibleRegion = LayoutPlatformEvidence.Region(visible, "conservativeBounds");
                    if (TryReadContentRegion(node, host.WindowBounds, out var content))
                        snapshot.ContentRegion = LayoutPlatformEvidence.Region(content, "conservativeBounds");
                    if (node.TryGetProperty("textTruncated", out var textTruncatedValue) &&
                        textTruncatedValue.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    {
                        snapshot.TextEvidence = new LayoutTextEvidence
                        {
                            Kind = "blazor-direct-text-layout",
                            IsTruncated = textTruncatedValue.GetBoolean(),
                            MeasurementSource = "blazor-cssom",
                        };
                    }
                    if (snapshot.Interactive &&
                        !request.Occlusion.Mode.Equals("none", StringComparison.OrdinalIgnoreCase))
                    {
                        snapshot.HitTestSampleCount = 1;
                        snapshot.BlockedHitTestSampleCount =
                            node.TryGetProperty("centerReceivesInput", out var receives) &&
                            receives.ValueKind == JsonValueKind.True
                                ? 0
                                : 1;
                    }
                    snapshot.PlatformEvidenceLimitations.Add(
                        "Blazor geometry is limited to the same-origin DOM; cross-origin frames, closed shadow roots, canvas, and native overlays are opaque.");
                    snapshots.Add(snapshot);
                    domIds.Add(domId);
                    ordinal++;
                }
            }
            return truncated;
        }

    private static string BuildBlazorLayoutExpression(int limit)
            => $$"""
            (() => {
              const limit = {{limit}};
              const all = Array.from(document.querySelectorAll('*'));
              const eligible = all.filter(e => {
                const r = e.getBoundingClientRect();
                if (r.width <= 0 || r.height <= 0) return false;
                const s = getComputedStyle(e);
                return s.display !== 'none' && s.visibility !== 'hidden';
              });
              const candidates = eligible.slice(0, limit);
              const intersect = (a, b) => {
                const left = Math.max(a.left, b.left), top = Math.max(a.top, b.top);
                const right = Math.min(a.right, b.right), bottom = Math.min(a.bottom, b.bottom);
                return right > left && bottom > top
                  ? { left, top, right, bottom, width: right-left, height: bottom-top }
                  : { left, top, right: left, bottom: top, width: 0, height: 0 };
              };
              const indexByElement = new Map(candidates.map((e, index) => [e, index]));
              const nodes = candidates.map((e, index) => {
                const full = e.getBoundingClientRect();
                let visible = intersect(full, {
                  left: 0, top: 0, right: innerWidth, bottom: innerHeight,
                  width: innerWidth, height: innerHeight
                });
                for (let p = e.parentElement; p && visible.width > 0 && visible.height > 0; p = p.parentElement) {
                  const s = getComputedStyle(p);
                  if (['hidden','clip','scroll','auto'].includes(s.overflowX) ||
                      ['hidden','clip','scroll','auto'].includes(s.overflowY)) {
                    visible = intersect(visible, p.getBoundingClientRect());
                  }
                }
                const style = getComputedStyle(e);
                const directText = Array.from(e.childNodes).some(n => n.nodeType === Node.TEXT_NODE);
                const textTruncated = directText &&
                  (e.scrollWidth > e.clientWidth + 1 || e.scrollHeight > e.clientHeight + 1) &&
                  (style.overflowX === 'hidden' || style.overflowY === 'hidden' ||
                   style.textOverflow === 'ellipsis' || style.webkitLineClamp !== 'none');
                const centerX = full.left + full.width / 2, centerY = full.top + full.height / 2;
                const receiver = document.elementFromPoint(centerX, centerY);
                return {
                  tag: e.tagName.toLowerCase(),
                  parentIndex: (() => {
                    for (let p = e.parentElement; p; p = p.parentElement) {
                      if (indexByElement.has(p)) return indexByElement.get(p);
                    }
                    return null;
                  })(),
                  automationId: e.getAttribute('data-automation-id') || e.id || null,
                  role: e.getAttribute('role'),
                  interactive: e.matches('button,input,select,textarea,a[href],[role=button],[tabindex]'),
                  full: { x: full.x, y: full.y, width: full.width, height: full.height },
                  visible: { x: visible.left, y: visible.top, width: visible.width, height: visible.height },
                  content: { x: full.x, y: full.y, width: e.scrollWidth, height: e.scrollHeight },
                  textTruncated,
                  centerReceivesInput: !!receiver && (receiver === e || e.contains(receiver))
                };
              });
              return { total: eligible.length, nodes };
            })()
            """;

    private static bool TryReadRect(
            JsonElement node,
            string propertyName,
            LayoutRect hostBounds,
            out LayoutRect rect)
        {
            rect = new LayoutRect();
            if (!node.TryGetProperty(propertyName, out var value) ||
                !TryNumber(value, "x", out var x) ||
                !TryNumber(value, "y", out var y) ||
                !TryNumber(value, "width", out var width) ||
                !TryNumber(value, "height", out var height))
                return false;
            rect = new LayoutRect
            {
                X = hostBounds.X + x,
                Y = hostBounds.Y + y,
                Width = Math.Max(0, width),
                Height = Math.Max(0, height),
            };
            return true;
        }

    private static bool TryReadContentRegion(
            JsonElement node,
            LayoutRect hostBounds,
            out LayoutRect rect)
            => TryReadRect(node, "content", hostBounds, out rect);

    private static bool TryNumber(JsonElement value, string propertyName, out double number)
        {
            number = 0;
            return value.TryGetProperty(propertyName, out var property) &&
                property.TryGetDouble(out number) &&
                double.IsFinite(number);
        }

    private static string? SanitizeDomId(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            var chars = value.Where(character => char.IsLetterOrDigit(character) || character is '-' or '_')
                .Take(64)
                .ToArray();
            return chars.Length == 0 ? null : new string(chars);
    }

    private static string? ValidateLayoutRequest(LayoutInspectionRequest request)
    {
        if (!string.Equals(
                request.SchemaVersion,
                LayoutDiagnosticsFormat.SchemaVersion,
                StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(request.SchemaVersion, "1.0", StringComparison.OrdinalIgnoreCase) &&
            // 2.0 requests stay acceptable: the 2.1 change removed response fields and redefined
            // the suppression key, and nothing a 2.0 caller can send became ambiguous. A 2.0
            // request that asks for text capture is refused by the privacy rule below, with a
            // reason that says why, rather than by a version mismatch that does not.
            !string.Equals(request.SchemaVersion, "2.0", StringComparison.OrdinalIgnoreCase))
        {
            return $"schemaVersion must be '{LayoutDiagnosticsFormat.SchemaVersion}'.";
        }

        if (!new[] { "agent", "strict", "exhaustive", "ci" }
            .Contains(request.Profile, StringComparer.OrdinalIgnoreCase))
            return "profile must be agent, strict, exhaustive, or ci.";
        if (!LayoutSeverity.All.Contains(request.MinimumSeverity, StringComparer.OrdinalIgnoreCase))
            return "minimumSeverity must be info, minor, moderate, serious, or critical.";
        if (!new[]
            {
                LayoutSuppressionModes.Report,
                LayoutSuppressionModes.Ignore,
                LayoutSuppressionModes.Off,
            }
            .Contains(request.SuppressionMode, StringComparer.OrdinalIgnoreCase))
        {
            return "suppressionMode must be report, ignore, or off.";
        }
        if (request.Suppressions.Count > 500)
            return "suppressions cannot contain more than 500 entries.";
        if (request.Suppressions.Any(suppression =>
            suppression.RuleId is null &&
            suppression.ElementId is null &&
            suppression.AutomationId is null &&
            suppression.ElementType is null &&
            suppression.RelatedElementId is null &&
            suppression.RelatedAutomationId is null &&
            suppression.SourceFile is null &&
            suppression.SourceLineStart is null &&
            suppression.Fingerprint is null))
        {
            return "each suppression must contain at least one selector.";
        }
        if (request.Rules is { Count: > 0 })
        {
            var unknown = request.Rules.FirstOrDefault(rule =>
                !LayoutDiagnosticRules.All.Contains(rule, StringComparer.OrdinalIgnoreCase));
            if (unknown is not null)
                return $"Unknown layout diagnostic rule '{unknown}'.";
        }
        if (request.Scope.MaxDepth < 0)
            return "scope.maxDepth must be zero or greater.";
        if (!new[] { LayoutScopeModes.ActivePage, LayoutScopeModes.AllWindows }
            .Contains(request.Scope.Mode, StringComparer.OrdinalIgnoreCase))
            return "scope.mode must be activePage or allWindows.";
        if (request.Scope.Window is < 0 || request.Window is < 0)
            return "window must be zero or greater.";
        if (!new[] { "wait", "immediate" }
            .Contains(request.Stability.Mode, StringComparer.OrdinalIgnoreCase))
            return "stability.mode must be wait or immediate.";
        if (request.Stability.StableFrames is < 1 or > 10)
            return "stability.stableFrames must be between 1 and 10.";
        if (request.Stability.QuietPeriodMs is < 0 or > 10_000)
            return "stability.quietPeriodMs must be between 0 and 10000.";
        if (request.Stability.TimeoutMs is < 1 or > 60_000)
            return "stability.timeoutMs must be between 1 and 60000.";
        if (!new[] { "none", "interactiveTargets", "all" }
            .Contains(request.Occlusion.Mode, StringComparer.OrdinalIgnoreCase))
            return "occlusion.mode must be none, interactiveTargets, or all.";
        if (request.Occlusion.MaxSamplesPerElement is < 1 or > 1000)
            return "occlusion.maxSamplesPerElement must be between 1 and 1000.";
        if (!double.IsFinite(request.Occlusion.CoverageError) ||
            request.Occlusion.CoverageError <= 0 ||
            request.Occlusion.CoverageError >= 1)
        {
            return "occlusion.coverageError must be greater than 0 and less than 1.";
        }
        if (!double.IsFinite(request.Occlusion.MinimumOverlapRatio) ||
            request.Occlusion.MinimumOverlapRatio < 0 ||
            request.Occlusion.MinimumOverlapRatio > 1)
        {
            return "occlusion.minimumOverlapRatio must be between 0 and 1.";
        }
        // Text and value content is never read by this subsystem, so the only honest value is
        // "none". Accepting "length" or "full" would advertise a capture mode the layer cannot
        // perform and would let a report be read as evidence that text was captured.
        if (!LayoutPrivacyTextModes.All.Contains(request.Privacy.Text, StringComparer.OrdinalIgnoreCase))
            return "privacy.text must be none; this layer never captures element text or values.";
        return null;
    }

    /// <summary>
    /// Window rectangle used by the window-relative rule. Returns null when the platform cannot
    /// report a usable size, which downgrades that rule to <c>incomplete</c> rather than a pass.
    /// </summary>
    private LayoutRect? ResolveWindowRect(int? window)
    {
        // A null window scope walks every window. One rectangle cannot truthfully represent
        // multiple windows with different sizes, so disable window-relative rules in that case.
        if (window is null && _app?.Windows.Count != 1)
            return null;

        var target = GetWindow(window);
        if (target is null)
            return null;

        var width = target.Width;
        var height = target.Height;
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
        {
            var (nativeWidth, nativeHeight) = GetNativeWindowSize(target);
            if (nativeWidth > 0) width = nativeWidth;
            if (nativeHeight > 0) height = nativeHeight;
        }

        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
            return null;

        return new LayoutRect { X = 0, Y = 0, Width = width, Height = height };
    }

    /// <summary>Capability descriptor advertised under <c>diagnostics.layout</c>.</summary>
    private static object BuildLayoutDiagnosticsCapability() => new
    {
        version = 2,
        supported = true,
        schemaVersion = LayoutDiagnosticsFormat.SchemaVersion,
        ruleSetVersion = LayoutDiagnosticsFormat.RuleSetVersion,
        maxElements = LayoutDiagnosticsFormat.MaxElements,
        profiles = new[] { "agent", "strict", "exhaustive", "ci" },
        features = new[]
        {
            "on-demand", "scoped-root", "window-scope", "coverage", "source-location",
            "rule-filter", "severity-filter", "evidence-control", "pass-accounting",
            "suppressions", "suppression-modes", "rule-catalog", "active-page-scope",
            "stability-sampling", "native-layout-evidence", "blazor-layout-evidence",
            "interaction-hit-test-sampling", "geometric-overlap"
        },
        rules = LayoutDiagnosticRules.All,
        neverCaptured = LayoutDiagnosticsFormat.NeverCaptured,
        limitations = new[]
        {
            "Native evidence is platform- and control-specific; unsupported nodes remain incomplete.",
            "Blazor evidence is limited to the same-origin DOM; cross-origin and custom-drawn content is opaque.",
            "Visual occlusion and accessibility visibility are not inferred.",
            "Interaction occlusion is bounded point sampling, not full-area proof.",
            "Unavailable geometry is reported as incomplete, never as a pass.",
        },
    };
}
