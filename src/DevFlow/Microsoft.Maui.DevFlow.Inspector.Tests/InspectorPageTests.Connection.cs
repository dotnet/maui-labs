using Microsoft.Playwright;

namespace Microsoft.Maui.DevFlow.Inspector.Tests;

public partial class InspectorLayoutWorkspaceTests
{
    [InspectorBrowserFact]
    public async Task RetryNowChecksTheAppAndReconnects()
    {
        // Every /api/state call waits for this verdict: true reaches the app, false answers 503.
        var appReachable = Task.FromResult(false);
        var stateRequests = 0;
        await PrepareLayoutWorkspaceAsync();
        await _page.RouteAsync("**/api/state", async route =>
        {
            Interlocked.Increment(ref stateRequests);
            if (await appReachable) await route.FallbackAsync();
            else await route.FulfillAsync(new() { Status = 503 });
        });
        var overlay = _page.Locator("#df-disconnected-overlay");
        var retry = _page.Locator("#df-retry-connection");
        var status = _page.Locator("#df-status");
        var icon = overlay.Locator(".df-connection-icon");

        await AdvanceLayoutPollingAsync();
        await Expect(overlay).ToBeVisibleAsync();
        await Expect(_page.Locator("#df-preview-badges")).ToBeEmptyAsync();

        // Withholds the verdict so the in-progress feedback can be asserted before the check settles.
        async Task ClickRetryAsync(bool reachable, bool duringPoll = false)
        {
            var verdict = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            appReachable = verdict.Task;
            try
            {
                await _page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });
                if (duringPoll) await _page.Clock.RunForAsync(3100);
                await retry.ClickAsync();
                await Expect(retry).ToHaveJSPropertyAsync("disabled", false);
                await Expect(retry).ToHaveAttributeAsync("aria-disabled", "true");
                await Expect(status).ToContainTextAsync("Checking for the running app");
                await Expect(overlay).ToHaveAttributeAsync("aria-busy", "true");
                await Expect(retry).ToHaveTextAsync("Checking\u2026");
                await Expect(icon).ToHaveCSSAsync("animation-name", "df-connection-spin");
                await _page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
                await Expect(icon).ToHaveCSSAsync("animation-name", "none");
                await _page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });
                await _page.EvaluateAsync("() => document.documentElement.dataset.reducedMotion = 'true'");
                await Expect(icon).ToHaveCSSAsync("animation-name", "none");
                await _page.EvaluateAsync("() => delete document.documentElement.dataset.reducedMotion");
            }
            finally
            {
                verdict.SetResult(reachable);
            }
        }

        await ClickRetryAsync(reachable: false);
        await Expect(retry).ToBeEnabledAsync();
        await Expect(retry).ToHaveAttributeAsync("aria-disabled", "false");
        await Expect(retry).ToHaveTextAsync("Retry now");
        await Expect(overlay).Not.ToHaveAttributeAsync("aria-busy", "true");
        await _page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });
        await Expect(icon).ToHaveCSSAsync("animation-name", "none");
        await Expect(status).ToContainTextAsync("App disconnected");
        await Expect(status).ToHaveAttributeAsync("role", "status");
        await Expect(status).ToHaveAttributeAsync("aria-live", "polite");
        await Expect(_page.Locator("#df-health-live")).ToBeEmptyAsync();
        await Expect(overlay).ToBeVisibleAsync();
        await _page.EvaluateAsync("""
            () => document.querySelector(".devflow-element[data-id='layout-target']")
              .dispatchEvent(new MouseEvent('click', { bubbles: true }))
            """);
        await Expect(status).ToContainTextAsync("App disconnected");

        // A poll already in flight must not be reused: the check asks the app again once it settles.
        var requestsBefore = stateRequests;
        await ClickRetryAsync(reachable: true, duringPoll: true);
        await Expect(retry).ToBeEnabledAsync();
        await Expect(retry).ToHaveTextAsync("Retry now");
        await Expect(overlay).Not.ToHaveAttributeAsync("aria-busy", "true");
        Assert.True(stateRequests - requestsBefore >= 2, "Retry reused the in-flight poll instead of checking again.");
        await Expect(overlay).ToBeHiddenAsync();
        await Expect(status).ToBeEmptyAsync();
    }

    [InspectorBrowserFact]
    public async Task RetryNowTimesOutAHungStateRequestAndCanTryAgain()
    {
        await PrepareLayoutWorkspaceAsync();
        await _page.RouteAsync("**/api/state", route => route.FulfillAsync(new() { Status = 503 }),
            new() { Times = 1 });
        await AdvanceLayoutPollingAsync();
        var overlay = _page.Locator("#df-disconnected-overlay");
        var retry = _page.Locator("#df-retry-connection");
        await Expect(overlay).ToBeVisibleAsync();

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var aborted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stateRequest = 0;
        var recover = false;
        _page.RequestFailed += (_, request) =>
        {
            if (request.Url.EndsWith("/api/state", StringComparison.Ordinal)) aborted.TrySetResult();
        };
        await _page.RouteAsync("**/api/state", async route =>
        {
            if (Interlocked.Increment(ref stateRequest) == 1)
            {
                started.TrySetResult();
                await release.Task;
                await route.FallbackAsync();
            }
            else if (recover)
            {
                await route.FallbackAsync();
            }
            else
            {
                await route.FulfillAsync(new()
                {
                    Status = 503,
                    ContentType = "application/json",
                    Body = """{"ok":false,"reason":"agent-unavailable","retryable":true}"""
                });
            }
        });
        try
        {
            await retry.ClickAsync();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await _page.Clock.RunForAsync(9000);
            await Expect(retry).ToHaveAttributeAsync("aria-disabled", "true");
            await _page.Clock.RunForAsync(1100);
            output.WriteLine($"After 10.1s of browser time: Retry aria-disabled={await retry.GetAttributeAsync("aria-disabled")}.");
            await Expect(retry).ToHaveAttributeAsync("aria-disabled", "false", new() { Timeout = 1000 });
            await aborted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Expect(retry).ToHaveTextAsync("Retry now");
            await Expect(overlay).Not.ToHaveAttributeAsync("aria-busy", "true");
            await Expect(overlay).ToBeVisibleAsync();
            await Expect(_page.Locator("#df-health-banner")).ToBeHiddenAsync();
            await Expect(_page.Locator("#df-status")).ToContainTextAsync("App disconnected");
        }
        finally
        {
            release.TrySetResult();
        }

        recover = true;
        await retry.ClickAsync();
        await Expect(overlay).ToBeHiddenAsync();
        await Expect(_page.Locator("#df-health-banner")).ToBeHiddenAsync();
        await Expect(retry).ToBeEnabledAsync();
        await Expect(_page.Locator("#df-status")).ToBeEmptyAsync();
    }

    [InspectorBrowserFact]
    public async Task CaptureFailureKeepsStaleFrameInspectableWithoutOverwritingNewerStatus()
    {
        var socketOpened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await _page.RouteWebSocketAsync("**/ws/events", _ => socketOpened.TrySetResult());
        await PrepareLayoutWorkspaceAsync(eventsSupported: true);
        await socketOpened.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await _page.Locator("#df-mode-inspect").ClickAsync();
        var status = _page.Locator("#df-status");
        await Expect(status).ToContainTextAsync("Inspect mode");
        await _page.EvaluateAsync("""
            () => {
              window.healthAnnouncementCount = 0;
              new MutationObserver(() => window.healthAnnouncementCount++)
                .observe(document.getElementById('df-health-live'), { childList: true });
            }
            """);

        var recover = false;
        await _page.RouteAsync("**/api/state", route => recover
            ? route.FallbackAsync()
            : route.FulfillAsync(new()
        {
            Status = 503,
            ContentType = "application/json",
            Body = """{"ok":false,"reason":"capture-failed","retryable":true}"""
        }));

        async Task FailCaptureAsync()
        {
            await _page.RunAndWaitForResponseAsync(
                () => _page.EvaluateAsync("() => window.dispatchEvent(new Event('online'))"),
                response => response.Url.EndsWith("/api/state", StringComparison.Ordinal));
            await Expect(_page.Locator("#df-health-banner")).ToBeVisibleAsync();
            await Expect(_page.Locator("#df-disconnected-overlay")).ToBeHiddenAsync();
            await Expect(_page.Locator("#df-preview-health-label")).ToHaveTextAsync("Preview delayed");
            await Expect(_page.Locator("#df-preview-badges")).ToBeEmptyAsync();
            await Expect(status).ToContainTextAsync("Inspect mode");
            await Expect(status).ToHaveAttributeAsync("role", "status");
            await Expect(status).ToHaveAttributeAsync("aria-live", "polite");
        }

        await FailCaptureAsync();
        var announcementCount = await _page.EvaluateAsync<int>("() => window.healthAnnouncementCount");
        await FailCaptureAsync();
        Assert.Equal(announcementCount, await _page.EvaluateAsync<int>("() => window.healthAnnouncementCount"));

        await _page.Locator(".devflow-element[data-id='layout-target']").ClickAsync();
        await Expect(status).ToContainTextAsync("LayoutTarget");
        var retryCapture = _page.Locator("#df-retry-capture");
        recover = true;
        await retryCapture.FocusAsync();
        await Expect(retryCapture).ToHaveAttributeAsync("aria-disabled", "false");
        await retryCapture.ClickAsync();
        await Expect(_page.Locator("#df-health-banner")).ToBeHiddenAsync();
        await Expect(_page.Locator("#df-preview-health-label")).ToHaveTextAsync("Live");
        await Expect(status).ToContainTextAsync("LayoutTarget");
        Assert.True(await _page.Locator("#df-view-pane")
            .EvaluateAsync<bool>("element => document.activeElement === element"));
    }

    [InspectorBrowserFact]
    public async Task CommittedClampedFrameShowsASilentResolutionBadge()
    {
        await PrepareLayoutWorkspaceAsync();
        await _page.EvaluateAsync("""
            () => {
              window.healthAnnouncementCount = 0;
              new MutationObserver(() => window.healthAnnouncementCount++)
                .observe(document.getElementById('df-health-live'), { childList: true });
            }
            """);
        await _page.UnrouteAsync("**/api/state");
        await _page.RouteAsync("**/api/state", async route =>
        {
            var response = await route.FetchAsync();
            var body = System.Text.Json.Nodes.JsonNode.Parse(await response.TextAsync())!.AsObject();
            var frame = body["frame"]!.AsObject();
            frame["frameId"] = "clamped-frame";
            frame["frameRevision"] = frame["frameRevision"]!.GetValue<long>() + 1;
            frame["captureScale"] = 0.75;
            frame["requestedCaptureScale"] = 1.5;
            frame["nativeScale"] = 3;
            frame["resolutionClamped"] = true;
            await route.FulfillAsync(new()
            {
                Status = response.Status,
                ContentType = response.Headers.GetValueOrDefault("content-type"),
                Body = body.ToJsonString()
            });
        }, new() { Times = 1 });

        await _page.RunAndWaitForResponseAsync(
            () => _page.EvaluateAsync("() => window.dispatchEvent(new Event('online'))"),
            response => response.Url.EndsWith("/api/state", StringComparison.Ordinal));

        var badge = _page.Locator("#df-preview-badges [data-badge='resolution']");
        await Expect(badge).ToHaveTextAsync("Resolution limited");
        await Expect(badge).ToHaveAttributeAsync(
            "title",
            "Captured at 0.75×; requested 1.5×; native 3×");
        Assert.Equal(0, await _page.EvaluateAsync<int>("() => window.healthAnnouncementCount"));
    }

    [InspectorBrowserFact]
    public async Task TransientLeaseFailureKeepsTheLastWriterStateAndRetriesHeartbeat()
    {
        await PrepareLayoutWorkspaceAsync();
        await Expect(_page.Locator("#df-presence")).ToContainTextAsync("Driving");
        await _page.RouteAsync("**/api/control", route => route.FulfillAsync(new()
        {
            Status = 503,
            ContentType = "application/json",
            Body = """{"ok":false,"error":"temporarily unavailable"}"""
        }));

        await _page.RunAndWaitForResponseAsync(
            () => _page.Clock.RunForAsync(4000),
            response => response.Url.EndsWith("/api/control", StringComparison.Ordinal));

        await Expect(_page.Locator("#df-presence")).ToContainTextAsync("Driving");
        await Expect(_page.Locator("#df-preview-badges")).ToHaveTextAsync("Polling");
        await Expect(_page.Locator("#df-presence")).ToHaveAttributeAsync(
            "title",
            new System.Text.RegularExpressions.Regex("temporarily unavailable"));
        await Expect(_page.Locator("#df-toggle-record")).ToBeEnabledAsync();
    }

    [InspectorBrowserFact]
    public async Task CaptureFailurePreservesDirtyEditorAndRecordingControls()
    {
        await PrepareLayoutWorkspaceAsync(elements: """
            <div class="devflow-element" data-id="layout-root" data-type="Window" data-automationId="LayoutRoot" data-isVisible="true" data-isEnabled="true" style="position:absolute;left:0px;top:0px;width:320px;height:600px;"></div>
            <div class="devflow-element" data-id="layout-target" data-parentId="layout-root" data-type="Entry" data-text="Original" data-isVisible="true" data-isEnabled="true" style="position:absolute;left:0px;top:20px;width:180px;height:40px;"></div>
            """);
        await _page.RouteAsync("**/api/flows/record/start", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = """{"ok":true,"recordingId":"health-recording","name":"Health recording"}"""
        }));
        await _page.RouteAsync("**/api/tap", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = """{"ok":true,"elementId":"layout-target"}"""
        }));

        var record = _page.Locator("#df-toggle-record");
        await record.ClickAsync();
        await Expect(record).ToHaveAttributeAsync("aria-pressed", "true");

        await _page.Locator(".devflow-element[data-id='layout-target']").ClickAsync();
        var editor = _page.Locator("#app-viewport input[data-element-id='layout-target']");
        await Expect(editor).ToBeVisibleAsync();
        await editor.FillAsync("Pending text");

        await _page.RouteAsync("**/api/state", route => route.FulfillAsync(new()
        {
            Status = 503,
            ContentType = "application/json",
            Body = """{"ok":false,"reason":"capture-failed","retryable":true}"""
        }), new() { Times = 1 });
        await _page.RunAndWaitForResponseAsync(
            () => _page.EvaluateAsync("() => window.dispatchEvent(new Event('online'))"),
            response => response.Url.EndsWith("/api/state", StringComparison.Ordinal));

        await Expect(_page.Locator("#df-health-banner")).ToBeVisibleAsync();
        await Expect(record).ToBeEnabledAsync();
        await Expect(_page.Locator("#df-record-cancel")).ToBeEnabledAsync();
        await editor.PressAsync("Enter");
        await Expect(editor).ToBeVisibleAsync();
        await Expect(editor).ToHaveValueAsync("Pending text");
        await Expect(editor).ToHaveAttributeAsync("aria-invalid", "true");
    }

    [InspectorBrowserFact]
    public async Task DisconnectedStatePollsWhileEventStreamIsLive()
    {
        var socketOpened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await _page.RouteWebSocketAsync("**/ws/events", _ => socketOpened.TrySetResult());
        await PrepareLayoutWorkspaceAsync(eventsSupported: true);
        await socketOpened.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var stateRequests = 0;
        _page.Request += (_, request) =>
        {
            if (request.Url.EndsWith("/api/state", StringComparison.Ordinal)) Interlocked.Increment(ref stateRequests);
        };
        var overlay = _page.Locator("#df-disconnected-overlay");

        await _page.Clock.RunForAsync(6100);
        Assert.Equal(0, stateRequests);

        async Task FailAndRecoverAsync()
        {
            var requestsBefore = stateRequests;
            await _page.RouteAsync("**/api/state", route => route.FulfillAsync(new() { Status = 503 }),
                new() { Times = 1 });
            await _page.RunAndWaitForResponseAsync(
                () => _page.EvaluateAsync("() => window.dispatchEvent(new Event('online'))"),
                response => response.Url.EndsWith("/api/state", StringComparison.Ordinal));
            await Expect(overlay).ToBeVisibleAsync();
            await _page.Clock.RunForAsync(3100);
            await Expect(overlay).ToBeHiddenAsync(new() { Timeout = 1000 });
            Assert.Equal(requestsBefore + 2, stateRequests);
            output.WriteLine("Recovered on the next 3s safety poll with the event socket still open.");
        }

        await FailAndRecoverAsync();
        var connectedRequests = stateRequests;
        await _page.Clock.RunForAsync(6100);
        Assert.Equal(connectedRequests, stateRequests);

        // Exercise the interval recreated after a host hides and shows its Inspector webview.
        await _page.EvaluateAsync("() => window.dispatchEvent(new Event('offline'))");
        await Expect(overlay).ToBeVisibleAsync();
        await _page.EvaluateAsync("""
            () => {
              Object.defineProperty(document, 'hidden', { configurable: true, value: true });
              document.dispatchEvent(new Event('visibilitychange'));
            }
            """);
        await _page.Clock.RunForAsync(6100);
        Assert.Equal(connectedRequests, stateRequests);
        await _page.RunAndWaitForResponseAsync(() => _page.EvaluateAsync("""
            () => {
              Object.defineProperty(document, 'hidden', { configurable: true, value: false });
              document.dispatchEvent(new Event('visibilitychange'));
            }
            """), response => response.Url.EndsWith("/api/state", StringComparison.Ordinal));
        await FailAndRecoverAsync();
    }
}
