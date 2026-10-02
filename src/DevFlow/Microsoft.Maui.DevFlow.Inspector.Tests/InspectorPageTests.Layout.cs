using System.Text.Json;
using Microsoft.Playwright;
using Xunit;

namespace Microsoft.Maui.DevFlow.Inspector.Tests;

public partial class InspectorPageTests
{
    [LiveInspectorFact]
    public async Task LayoutWorkspace_DefaultScopeIncludesFlyoutAndDetailPanes()
    {
        string? requestedRoot = null;
        await _page.RouteAsync("**/api/diagnostics/layout", route =>
        {
            using var request = JsonDocument.Parse(route.Request.PostData!);
            requestedRoot = request.RootElement.GetProperty("rootElementId").GetString();
            return route.FulfillAsync(new() { ContentType = "application/json", Body = LayoutWorkspaceResponse() });
        });
        await PrepareLayoutWorkspaceAsync(elements: """
            <div class="devflow-element" data-id="layout-root" data-type="MainShell" data-isVisible="true" style="position:absolute;left:0px;top:0px;width:320px;height:600px;"></div>
            <div class="devflow-element" data-id="menu" data-parentId="layout-root" data-type="ContentPage" data-isVisible="true" style="position:absolute;left:0px;top:0px;width:80px;height:600px;"></div>
            <div class="devflow-element" data-id="navigation" data-parentId="layout-root" data-type="NavigationPage" data-isVisible="true" style="position:absolute;left:80px;top:0px;width:240px;height:600px;"></div>
            <div class="devflow-element" data-id="layout-target" data-parentId="navigation" data-type="BoxView" data-isVisible="true" style="position:absolute;left:100px;top:20px;width:180px;height:40px;"></div>
            """);
        await OpenLayoutWorkspaceAsync();
        await Expect(_page.Locator("#diagnostics-list .diagnostic-item")).ToHaveCountAsync(1);
        Assert.Equal("layout-root", requestedRoot);
    }

    [LiveInspectorFact]
    public Task LayoutWorkspace_FullTreeRevisionInvalidatesUnchangedOverlaysWithPolling()
        => VerifyFullTreeRevisionInvalidatesAsync(eventsSupported: false);

    [LiveInspectorFact]
    public Task LayoutWorkspace_FullTreeRevisionInvalidatesUnchangedOverlaysWithEventStream()
        => VerifyFullTreeRevisionInvalidatesAsync(eventsSupported: true);

    [LiveInspectorFact]
    public async Task LayoutWorkspace_RenderedStateInvalidatesUnchangedGeometryRevision()
    {
        var position = 0;
        await _page.RouteAsync("**/api/diagnostics/layout", route => route.FulfillAsync(new()
        {
            ContentType = "application/json", Body = LayoutWorkspaceResponse()
        }));
        await PrepareLayoutWorkspaceAsync(() => position, treeRevision: () => "unchanged-geometry");
        await OpenLayoutWorkspaceAsync();
        await Expect(_page.Locator("#layout-coverage")).ToHaveTextAsync("Partial coverage");

        position = 10;
        await AdvanceLayoutPollingAsync();

        await Expect(_page.Locator("#layout-coverage")).ToHaveTextAsync("Stale snapshot");
        await Expect(_page.Locator("#df-attach-data")).ToBeDisabledAsync();
    }

    private async Task VerifyFullTreeRevisionInvalidatesAsync(bool eventsSupported)
    {
        var revision = "before-scroll";
        var scans = 0;
        var socketOpened = new TaskCompletionSource<IWebSocketRoute>(TaskCreationOptions.RunContinuationsAsynchronously);
        await _page.RouteWebSocketAsync("**/ws/events", socket => socketOpened.TrySetResult(socket));
        await _page.RouteAsync("**/api/diagnostics/layout", route =>
        {
            scans++;
            return route.FulfillAsync(new() { ContentType = "application/json", Body = LayoutWorkspaceResponse() });
        });
        await PrepareLayoutWorkspaceAsync(eventsSupported: eventsSupported, treeRevision: () => revision);
        if (eventsSupported)
            SendLayoutConnectionSeed(await socketOpened.Task.WaitAsync(TimeSpan.FromSeconds(10)), "seed");
        await OpenLayoutWorkspaceAsync();
        await _page.Locator("#diagnostics-list .diagnostic-item").ClickAsync();
        await Expect(_page.Locator("#layout-coverage")).ToHaveTextAsync("Partial coverage");
        Assert.Equal(1, scans);

        revision = "after-scroll";
        await _page.Clock.RunForAsync(3100);
        await Expect(_page.Locator("#layout-coverage")).ToHaveTextAsync("Stale snapshot");
        await Expect(_page.Locator("#df-attach-data")).ToBeDisabledAsync();
        await Expect(_page.GetByRole(AriaRole.Button, new() { Name = "parent: LayoutRoot", Exact = true }))
            .ToBeDisabledAsync();
        Assert.Equal(1, scans);
    }

    [LiveInspectorFact]
    public async Task LayoutWorkspace_ToolbarFollowsDockCloseCollapseAndTabChanges()
    {
        var scans = 0;
        await _page.RouteAsync("**/api/diagnostics/layout", route =>
        {
            scans++;
            return route.FulfillAsync(new() { ContentType = "application/json", Body = LayoutWorkspaceResponse() });
        });
        await PrepareLayoutWorkspaceAsync();
        await OpenLayoutWorkspaceAsync();
        await Expect(_page.Locator("#diagnostics-list .diagnostic-item")).ToHaveCountAsync(1);
        var toggle = _page.Locator("#df-toggle-diagnostics");
        await Expect(toggle).ToHaveAttributeAsync("aria-expanded", "true");

        await _page.Locator("#df-dock-close").ClickAsync();
        await Expect(toggle).ToHaveAttributeAsync("aria-expanded", "false");
        await Expect(toggle).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"\bdf-active\b"));
        await toggle.ClickAsync();
        await Expect(_page.Locator("#df-diagnostics-pane")).ToBeVisibleAsync();
        await Expect(toggle).ToHaveAttributeAsync("aria-expanded", "true");

        await _page.Locator("#df-tab-logs").ClickAsync();
        await Expect(toggle).ToHaveAttributeAsync("aria-expanded", "false");
        await _page.Locator("#df-tab-layout").ClickAsync();
        await Expect(toggle).ToHaveAttributeAsync("aria-expanded", "true");
        await _page.Locator("#df-dock-collapse").ClickAsync();
        await Expect(_page.Locator("#df-dock-body")).Not.ToBeVisibleAsync();
        await Expect(toggle).ToHaveAttributeAsync("aria-expanded", "false");
        await toggle.ClickAsync();
        await Expect(_page.Locator("#df-diagnostics-pane")).ToBeVisibleAsync();
        await Expect(toggle).ToHaveAttributeAsync("aria-expanded", "true");
        Assert.Equal(1, scans);
    }

    [LiveInspectorFact]
    public async Task LayoutWorkspace_SelectingCollapsedTabDefersItsFirstScanUntilExpanded()
    {
        var scans = 0;
        await _page.RouteAsync("**/api/diagnostics/layout", route =>
        {
            scans++;
            return route.FulfillAsync(new() { ContentType = "application/json", Body = LayoutWorkspaceResponse() });
        });
        await PrepareLayoutWorkspaceAsync();
        var data = _page.Locator("#df-toggle-dock");
        if (!await data.IsVisibleAsync())
            await _page.Locator("#df-more").ClickAsync();
        await data.ClickAsync();
        await Expect(_page.Locator("#df-tab-logs")).ToHaveAttributeAsync("aria-selected", "true");
        await _page.Locator("#df-dock-collapse").ClickAsync();
        await _page.Locator("#df-tab-layout").ClickAsync();
        await AdvanceLayoutPollingAsync();
        await Expect(_page.Locator("#df-dock-body")).Not.ToBeVisibleAsync();
        await Expect(_page.Locator("#df-toggle-diagnostics")).ToHaveAttributeAsync("aria-expanded", "false");
        Assert.Equal(0, scans);

        await _page.Locator("#df-toggle-diagnostics").ClickAsync();
        await Expect(_page.Locator("#diagnostics-list .diagnostic-item")).ToHaveCountAsync(1);
        await Expect(_page.Locator("#df-toggle-diagnostics")).ToHaveAttributeAsync("aria-expanded", "true");
        Assert.Equal(1, scans);
    }

    [LiveInspectorFact]
    public async Task LayoutWorkspace_SameRouteNavigationInvalidatesButReconnectSeedsDoNot()
    {
        var initial = new TaskCompletionSource<IWebSocketRoute>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reconnected = new TaskCompletionSource<IWebSocketRoute>(TaskCreationOptions.RunContinuationsAsynchronously);
        var connections = 0;
        await _page.RouteWebSocketAsync("**/ws/events", socket =>
        {
            (Interlocked.Increment(ref connections) == 1 ? initial : reconnected).TrySetResult(socket);
        });
        await _page.RouteAsync("**/api/diagnostics/layout", route => route.FulfillAsync(new()
        {
            ContentType = "application/json", Body = LayoutWorkspaceResponse()
        }));
        await PrepareLayoutWorkspaceAsync(eventsSupported: true);
        var socket = await initial.Task.WaitAsync(TimeSpan.FromSeconds(10));
        SendLayoutConnectionSeed(socket, "initial");
        await _page.Clock.RunForAsync(200);
        await OpenLayoutWorkspaceAsync();
        await _page.Locator("#diagnostics-list .diagnostic-item").ClickAsync();
        var related = _page.GetByRole(AriaRole.Button, new() { Name = "parent: LayoutRoot", Exact = true });
        await Expect(related).ToBeEnabledAsync();

        socket.Send("""{"type":"navigation","timestamp":"reload","data":{"from":null,"to":"//layout","route":"//layout"}}""");
        await Expect(_page.Locator("#layout-coverage")).ToHaveTextAsync("Stale snapshot");
        await Expect(related).ToBeDisabledAsync();
        await Expect(_page.Locator("#df-attach-data")).ToBeDisabledAsync();
        await _page.Locator("#layout-rescan").ClickAsync();
        await Expect(_page.Locator("#layout-coverage")).ToHaveTextAsync("Partial coverage");

        await socket.CloseAsync(new() { Code = 1000, Reason = "Layout reconnect fixture" });
        await _page.Clock.RunForAsync(3500);
        var nextSocket = await reconnected.Task.WaitAsync(TimeSpan.FromSeconds(10));
        SendLayoutConnectionSeed(nextSocket, "reconnected");
        await _page.Clock.RunForAsync(200);
        await Expect(_page.Locator("#layout-coverage")).ToHaveTextAsync("Partial coverage");
        await Expect(related).ToBeEnabledAsync();

        nextSocket.Send("""{"type":"navigation","timestamp":"second reload","data":{"from":null,"to":"//layout","route":"//layout"}}""");
        await Expect(_page.Locator("#layout-coverage")).ToHaveTextAsync("Stale snapshot");
        await Expect(related).ToBeDisabledAsync();
    }

    private static void SendLayoutConnectionSeed(IWebSocketRoute socket, string timestamp)
    {
        socket.Send(JsonSerializer.Serialize(new { type = "lifecycle", timestamp, data = new { state = "started" } }));
        socket.Send(JsonSerializer.Serialize(new
        {
            type = "navigation", timestamp, data = new { from = (string?)null, to = "//layout", route = "//layout" }
        }));
    }

    [LiveInspectorFact]
    public async Task LayoutWorkspace_ManualScansAndFiltersStayBoundedToTheVisibleRoot()
    {
        var requests = new List<string>();
        await _page.RouteAsync("**/api/diagnostics/layout", route =>
        {
            requests.Add(route.Request.PostData!);
            return route.FulfillAsync(new() { ContentType = "application/json", Body = LayoutWorkspaceResponse() });
        });
        await PrepareLayoutWorkspaceAsync();
        await AdvanceLayoutPollingAsync();
        Assert.Empty(requests);
        await OpenLayoutWorkspaceAsync();
        await Expect(_page.Locator("#diagnostics-list .diagnostic-item")).ToHaveCountAsync(1);
        using (var request = JsonDocument.Parse(Assert.Single(requests)))
            Assert.Equal("layout-root", request.RootElement.GetProperty("rootElementId").GetString());

        await AdvanceLayoutPollingAsync();
        Assert.Single(requests);
        await _page.Locator(".df-layout-filter-menu > summary").ClickAsync();
        await AssertLayoutFiltersFitAsync();
        await _page.GetByLabel("Selected subtree only", new() { Exact = true }).CheckAsync();
        await _page.Locator("#layout-rescan").ClickAsync();
        await Expect(_page.Locator("#df-diagnostics-pane [role='alert']")).ToContainTextAsync("Select an element");
        Assert.Single(requests);

        await _page.GetByLabel("Selected subtree only", new() { Exact = true }).UncheckAsync();
        await _page.Locator("#layout-profile").SelectOptionAsync("strict");
        await _page.Locator(".df-layout-filter-menu > summary").ClickAsync();
        await _page.Locator("#layout-rescan").ClickAsync();
        await Expect(_page.Locator("#layout-coverage")).ToHaveTextAsync("Partial coverage");
        Assert.Equal(2, requests.Count);
        using (var request = JsonDocument.Parse(requests[1]))
            Assert.Equal("strict", request.RootElement.GetProperty("profile").GetString());

        await _page.SetViewportSizeAsync(480, 500);
        await _page.Locator(".df-layout-filter-menu > summary").ClickAsync();
        await AssertLayoutFiltersFitAsync();
        await _page.GetByLabel("Selected subtree only", new() { Exact = true }).CheckAsync();
        await Expect(_page.GetByLabel("Selected subtree only", new() { Exact = true })).ToBeInViewportAsync();
    }

    [LiveInspectorFact]
    public async Task LayoutWorkspace_TransportErrorsAndSuppressionSuccessRemainVisible()
    {
        var scans = 0;
        var writes = 0;
        var suppressed = false;
        var sourcePath = Path.Combine(Path.GetTempPath(), "layout-ui-fixture", "Page.xaml");
        await _page.RouteAsync("**/api/source", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(new { ok = true, file = sourcePath, line = 21 })
        }));
        await _page.RouteAsync("**/api/diagnostics/layout", route =>
        {
            var first = ++scans == 1;
            return route.FulfillAsync(new()
            {
                Status = first ? 429 : 200,
                ContentType = "application/json",
                Body = first ? """{"ok":false,"error":"Another Layout scan is still running."}"""
                    : LayoutWorkspaceResponse(suppressed)
            });
        });
        await _page.RouteAsync("**/api/diagnostics/suppress", route =>
        {
            writes++;
            suppressed = true;
            using var request = JsonDocument.Parse(route.Request.PostData!);
            Assert.Equal(LayoutWorkspacePolicyPath, request.RootElement.GetProperty("policyFilePath").GetString());
            return route.FulfillAsync(new() { ContentType = "application/json", Body = """{"success":true}""" });
        });
        await _page.RouteAsync("**/api/diagnostics/unsuppress", route => route.FulfillAsync(new()
        {
            Status = 409,
            ContentType = "application/json",
            Body = """{"success":false,"message":"A user policy also suppresses this finding."}"""
        }));
        await PrepareLayoutWorkspaceAsync();
        await OpenLayoutWorkspaceAsync();
        await Expect(_page.Locator("#df-diagnostics-pane [role='alert']")).ToContainTextAsync("Another Layout scan is still running.");
        await _page.Locator("#layout-rescan").ClickAsync();
        await _page.Locator("#diagnostics-list .diagnostic-item").ClickAsync();
        await _page.GetByRole(AriaRole.Button, new() { Name = "Copy payload", Exact = true }).ClickAsync();
        var copied = await _page.EvaluateAsync<string>("window.__copiedDevFlowData");
        Assert.Contains("\"layout\"", copied);
        Assert.Contains("layout.child-outside-parent", copied);
        await _page.GetByRole(AriaRole.Button, new() { Name = "Open source", Exact = true }).ClickAsync();
        await Expect(_page.Locator("#df-status")).ToContainTextAsync(sourcePath + ":21");
        Assert.Equal(sourcePath + ":21", await _page.EvaluateAsync<string>("window.__copiedDevFlowData"));

        await _page.GetByRole(AriaRole.Button, new() { Name = "Suppress...", Exact = true }).ClickAsync();
        await Expect(_page.GetByRole(AriaRole.Dialog)).ToContainTextAsync(LayoutWorkspacePolicyPath);
        await _page.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
        Assert.Equal(0, writes);
        await _page.GetByRole(AriaRole.Button, new() { Name = "Suppress...", Exact = true }).ClickAsync();
        await _page.GetByRole(AriaRole.Button, new() { Name = "Save suppression", Exact = true }).ClickAsync();
        await Expect(_page.Locator("#diagnostics-list .diagnostic-item")).ToHaveCountAsync(0);
        await Expect(_page.Locator("#df-status")).ToContainTextAsync("Layout check complete");
        Assert.Equal(1, writes);
        Assert.Equal(3, scans);

        await _page.Locator(".df-layout-filter-menu > summary").ClickAsync();
        await _page.GetByLabel("Include suppressed", new() { Exact = true }).CheckAsync();
        await _page.Locator(".df-layout-filter-menu > summary").ClickAsync();
        await _page.Locator("#diagnostics-list .diagnostic-item").ClickAsync();
        await _page.GetByRole(AriaRole.Button, new() { Name = "Unsuppress...", Exact = true }).ClickAsync();
        await _page.GetByRole(AriaRole.Button, new() { Name = "Remove suppression", Exact = true }).ClickAsync();
        await Expect(_page.Locator("#df-status")).ToContainTextAsync("A user policy also suppresses this finding.");
    }

    [LiveInspectorFact]
    public async Task LayoutWorkspace_QueuedRefreshDoesNotSurviveDeactivation()
    {
        var scans = 0;
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await _page.RouteAsync("**/api/diagnostics/layout", async route =>
        {
            if (++scans == 1)
            {
                firstStarted.SetResult();
                await release.Task;
            }
            await route.FulfillAsync(new() { ContentType = "application/json", Body = LayoutWorkspaceResponse() });
        });
        await PrepareLayoutWorkspaceAsync();
        await OpenLayoutWorkspaceAsync();
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await _page.Locator("#df-dock-refresh").ClickAsync();
        await _page.Locator("#df-tab-logs").ClickAsync();
        var completed = _page.WaitForResponseAsync(response => response.Url.EndsWith("/api/diagnostics/layout", StringComparison.Ordinal));
        release.SetResult();
        await completed;
        await _page.Locator("#df-tab-layout").ClickAsync();
        await Expect(_page.Locator("#layout-rescan")).ToBeEnabledAsync();
        Assert.Equal(1, scans);
        await _page.Locator("#layout-rescan").ClickAsync();
        await Expect(_page.Locator("#layout-rescan")).ToBeEnabledAsync();
        await AdvanceLayoutPollingAsync();
        Assert.Equal(2, scans);
    }

    [LiveInspectorFact]
    public async Task LayoutWorkspace_StaleActionsAndLiveVisibilityRespectTheSnapshot()
    {
        var scans = 0;
        var position = 0;
        var message = "Original finding evidence.";
        await _page.RouteAsync("**/api/diagnostics/layout", route =>
        {
            scans++;
            return route.FulfillAsync(new()
            {
                ContentType = "application/json",
                Body = LayoutWorkspaceResponse(message: message)
            });
        });
        await PrepareLayoutWorkspaceAsync(() => position);
        await OpenLayoutWorkspaceAsync();
        await _page.Locator("#diagnostics-list .diagnostic-item").ClickAsync();
        var related = _page.GetByRole(AriaRole.Button, new() { Name = "parent: LayoutRoot", Exact = true });
        await Expect(related).ToBeEnabledAsync();
        position = 10;
        await AdvanceLayoutPollingAsync();
        await Expect(_page.Locator("#layout-coverage")).ToHaveTextAsync("Stale snapshot");
        await Expect(related).ToBeDisabledAsync();
        await Expect(_page.Locator("#df-attach-data")).ToBeDisabledAsync();
        var recheck = _page.GetByRole(AriaRole.Button, new() { Name = "Recheck", Exact = true });
        await Expect(recheck).ToBeEnabledAsync();
        message = "Updated finding evidence.";
        await recheck.ClickAsync();
        await Expect(_page.Locator("[data-layout-detail-id]")).ToContainTextAsync(message);
        await Expect(related).ToBeEnabledAsync();

        await _page.Locator("#layout-live").CheckAsync();
        await _page.RunAndWaitForResponseAsync(() => _page.Clock.RunForAsync(500),
            response => response.Url.EndsWith("/api/diagnostics/layout", StringComparison.Ordinal));
        var beforeHidden = scans;
        await _page.Locator("#df-dock-collapse").ClickAsync();
        position = 20;
        await AdvanceLayoutPollingAsync();
        Assert.Equal(beforeHidden, scans);
        await _page.Locator("#df-dock-collapse").ClickAsync();
        await _page.RunAndWaitForResponseAsync(() => _page.Clock.RunForAsync(500),
            response => response.Url.EndsWith("/api/diagnostics/layout", StringComparison.Ordinal));
        Assert.Equal(beforeHidden + 1, scans);

        await _page.EvaluateAsync("""
            window.__layoutHidden = true;
            Object.defineProperty(document, 'hidden', { configurable: true, get: () => window.__layoutHidden });
            document.dispatchEvent(new Event('visibilitychange'));
            const profile = document.getElementById('layout-profile');
            profile.value = 'exhaustive';
            profile.dispatchEvent(new Event('change'));
            """);
        await _page.Clock.RunForAsync(5000);
        Assert.Equal(beforeHidden + 1, scans);
        await _page.EvaluateAsync("""
            window.__layoutHidden = false;
            document.dispatchEvent(new Event('visibilitychange'));
            """);
        await _page.RunAndWaitForResponseAsync(() => _page.Clock.RunForAsync(500),
            response => response.Url.EndsWith("/api/diagnostics/layout", StringComparison.Ordinal));
        Assert.Equal(beforeHidden + 2, scans);
    }

    private static string LayoutWorkspacePolicyPath => Path.Combine(Path.GetTempPath(), "layout-ui-fixture", ".mauidevflow");

    private static string LayoutWorkspaceResponse(bool suppressed = false, string message = "Child outside parent.") =>
        JsonSerializer.Serialize(new
        {
            ok = true,
            policyFilePath = LayoutWorkspacePolicyPath,
            report = new
            {
                schemaVersion = "1.0",
                ruleSetVersion = "1.1",
                snapshot = new { id = "snapshot", stable = true, nodeCount = 2, capturedAt = "2026-09-09T00:00:00Z" },
                summary = new { violations = 0, observations = 1, incomplete = 0, passes = 1, notApplicable = 0, suppressed = suppressed ? 1 : 0, filtered = 0 },
                coverage = new { overall = "partial", rules = Array.Empty<object>(), limitations = new[] { "Fixture coverage is partial." } },
                findings = new[]
                {
                    new
                    {
                        id = "layout-finding", ruleId = "layout.child-outside-parent", outcome = "observation",
                        severity = "info", confidence = "high", message, suppressed,
                        element = new { id = "layout-target", type = "BoxView", automationId = "LayoutTarget" },
                        relatedElements = new[] { new { relation = "parent", element = new { id = "layout-root", type = "Window", automationId = "LayoutRoot" } } }
                    }
                }
            }
        });

    private async Task PrepareLayoutWorkspaceAsync(Func<int>? position = null, bool eventsSupported = false,
        Func<string>? treeRevision = null, string? elements = null)
    {
        await _page.Clock.InstallAsync();
        await CaptureClipboardWritesAsync();
        await _page.RouteAsync("**/api/eventSupport", route => route.FulfillAsync(new()
        {
            ContentType = "application/json", Body = JsonSerializer.Serialize(new { supported = eventsSupported })
        }));
        await _page.RouteAsync("**/api/control", route => route.FulfillAsync(new()
        {
            ContentType = "application/json", Body = """{"youAreWriter":true,"heldByOther":false}"""
        }));
        await _page.RouteAsync("**/api/state", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(new
            {
                viewportWidth = 320, viewportHeight = 600,
                treeRevision = treeRevision?.Invoke(),
                elements = elements ?? $"""
                    <div class="devflow-element" data-id="layout-root" data-type="Window" data-automationId="LayoutRoot" data-isVisible="true" data-isEnabled="true" style="position:absolute;left:0px;top:0px;width:320px;height:600px;"></div>
                    <div class="devflow-element" data-id="layout-target" data-parentId="layout-root" data-type="BoxView" data-automationId="LayoutTarget" data-hasSource="true" data-isVisible="true" data-isEnabled="true" style="position:absolute;left:{position?.Invoke() ?? 0}px;top:20px;width:180px;height:40px;"></div>
                    """
            })
        }));
        await _page.GotoAsync(BaseUrl);
        await AdvanceLayoutPollingAsync();
        await Expect(_page.Locator(".devflow-element[data-id='layout-target']")).ToBeAttachedAsync();
    }

    private Task AdvanceLayoutPollingAsync() =>
        _page.RunAndWaitForResponseAsync(() => _page.Clock.RunForAsync(3100),
            response => response.Url.EndsWith("/api/state", StringComparison.Ordinal));

    private async Task OpenLayoutWorkspaceAsync()
    {
        var toggle = _page.Locator("#df-toggle-diagnostics");
        if (!await toggle.IsVisibleAsync())
            await _page.Locator("#df-more").ClickAsync();
        await toggle.ClickAsync();
        await Expect(_page.Locator("#df-diagnostics-pane")).ToBeVisibleAsync();
    }

    private async Task AssertLayoutFiltersFitAsync()
    {
        await Expect(_page.Locator(".df-layout-filter-popover")).ToBeVisibleAsync();
        Assert.True(await _page.Locator(".df-layout-filter-popover").EvaluateAsync<bool>("""
            popover => {
              const bounds = popover.getBoundingClientRect();
              const panel = document.getElementById('df-diagnostics-pane').getBoundingClientRect();
              return bounds.top >= panel.top && bounds.bottom <= panel.bottom &&
                bounds.left >= panel.left && bounds.right <= panel.right && bounds.height > 30;
            }
            """), "The complete scrollable Filters popup must fit inside the dock, including narrow layouts.");
    }
}
