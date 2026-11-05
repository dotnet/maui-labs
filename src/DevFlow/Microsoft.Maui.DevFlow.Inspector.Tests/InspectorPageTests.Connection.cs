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
                await Expect(retry).ToBeDisabledAsync();
                await Expect(status).ToContainTextAsync("Checking for the running app");
                await Expect(overlay).ToHaveAttributeAsync("aria-busy", "true");
                await Expect(retry).ToHaveTextAsync("Checking\u2026");
                await Expect(icon).ToHaveCSSAsync("animation-name", "df-connection-spin");
                await _page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
                await Expect(icon).ToHaveCSSAsync("animation-name", "none");
            }
            finally
            {
                verdict.SetResult(reachable);
            }
        }

        await ClickRetryAsync(reachable: false);
        await Expect(retry).ToBeEnabledAsync();
        await Expect(retry).ToHaveTextAsync("Retry now");
        await Expect(overlay).Not.ToHaveAttributeAsync("aria-busy", "true");
        await _page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });
        await Expect(icon).ToHaveCSSAsync("animation-name", "none");
        await Expect(status).ToContainTextAsync("App disconnected");
        await Expect(overlay).ToBeVisibleAsync();

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
        _page.RequestFailed += (_, request) =>
        {
            if (request.Url.EndsWith("/api/state", StringComparison.Ordinal)) aborted.TrySetResult();
        };
        await _page.RouteAsync("**/api/state", async route =>
        {
            started.TrySetResult();
            await release.Task;
            await route.FallbackAsync();
        }, new() { Times = 1 });
        try
        {
            await retry.ClickAsync();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await _page.Clock.RunForAsync(9000);
            await Expect(retry).ToBeDisabledAsync();
            await _page.Clock.RunForAsync(1100);
            output.WriteLine($"After 10.1s of browser time: Retry disabled={await retry.IsDisabledAsync()}.");
            await Expect(retry).ToBeEnabledAsync(new() { Timeout = 1000 });
            await aborted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Expect(retry).ToHaveTextAsync("Retry now");
            await Expect(overlay).Not.ToHaveAttributeAsync("aria-busy", "true");
            await Expect(overlay).ToBeVisibleAsync();
            await Expect(_page.Locator("#df-status")).ToContainTextAsync("App disconnected");
        }
        finally
        {
            release.TrySetResult();
        }

        await retry.ClickAsync();
        await Expect(overlay).ToBeHiddenAsync();
        await Expect(retry).ToBeEnabledAsync();
        await Expect(_page.Locator("#df-status")).ToBeEmptyAsync();
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
