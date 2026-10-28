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

        await AdvanceLayoutPollingAsync();
        await Expect(overlay).ToBeVisibleAsync();

        // Withholds the verdict so the in-progress feedback can be asserted before the check settles.
        async Task ClickRetryAsync(bool reachable, bool duringPoll = false)
        {
            var verdict = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            appReachable = verdict.Task;
            try
            {
                if (duringPoll) await _page.Clock.RunForAsync(3100);
                await retry.ClickAsync();
                await Expect(retry).ToBeDisabledAsync();
                await Expect(status).ToContainTextAsync("Checking for the running app");
            }
            finally
            {
                verdict.SetResult(reachable);
            }
        }

        await ClickRetryAsync(reachable: false);
        await Expect(retry).ToBeEnabledAsync();
        await Expect(status).ToContainTextAsync("App disconnected");
        await Expect(overlay).ToBeVisibleAsync();

        // A poll already in flight must not be reused: the check asks the app again once it settles.
        var requestsBefore = stateRequests;
        await ClickRetryAsync(reachable: true, duringPoll: true);
        await Expect(retry).ToBeEnabledAsync();
        Assert.True(stateRequests - requestsBefore >= 2, "Retry reused the in-flight poll instead of checking again.");
        await Expect(overlay).ToBeHiddenAsync();
        await Expect(status).ToBeEmptyAsync();
    }
}
