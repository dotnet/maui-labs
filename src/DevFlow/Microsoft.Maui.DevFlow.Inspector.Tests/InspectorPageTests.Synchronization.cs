using Microsoft.Playwright;

namespace Microsoft.Maui.DevFlow.Inspector.Tests;

public partial class InspectorLayoutWorkspaceTests
{
    [InspectorBrowserFact]
    public Task ReadOnlyPropertyFilterRemainsUsable()
        => VerifyReadOnlyPropertyFilterAsync(legacyAgent: false);

    [InspectorBrowserFact]
    public Task ReadOnlyPropertyFilterRemainsUsableWithLegacyAgent()
        => VerifyReadOnlyPropertyFilterAsync(legacyAgent: true);

    private async Task VerifyReadOnlyPropertyFilterAsync(bool legacyAgent)
    {
        var descriptorsStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDescriptors = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var propertiesStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseProperties = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = 0;
        await _page.RouteAsync("**/api/getProperties", async route =>
        {
            descriptorsStarted.TrySetResult();
            await releaseDescriptors.Task;
            await route.FulfillAsync(new()
            {
                ContentType = "application/json",
                Body = legacyAgent ? """{"ok":false,"supported":false}""" : """
                    {"ok":true,"supported":true,"properties":[
                      {"name":"FontSize","kind":"number","value":"28","writable":true,"persistable":true},
                      {"name":"FontAttributes","kind":"enum","value":"Bold","choices":["None","Bold","Italic"],"writable":true,"persistable":true},
                      {"name":"Opacity","kind":"number","value":"1","writable":true,"persistable":true}]}
                    """
            });
        });
        await _page.RouteAsync("**/api/getProperty", async route =>
        {
            propertiesStarted.TrySetResult();
            await releaseProperties.Task;
            await route.FulfillAsync(new() { ContentType = "application/json", Body = """{"value":"1"}""" });
        });
        await _page.RouteAsync("**/api/setProperty", route =>
        {
            Interlocked.Increment(ref writes);
            return route.FulfillAsync(new() { ContentType = "application/json", Body = """{"ok":true}""" });
        });
        try
        {
            await PrepareLayoutWorkspaceAsync(elements: """
                <div class="devflow-element" data-id="layout-root" data-type="Window" style="position:absolute;left:0px;top:0px;width:320px;height:600px;"></div>
                <div class="devflow-element" data-id="layout-target" data-parentId="layout-root" data-type="Label" data-automationId="HeaderLabel" data-hasSource="true" style="position:absolute;left:0px;top:20px;width:180px;height:40px;"></div>
                """);
            await _page.RouteAsync("**/api/control", route => route.FulfillAsync(new()
            {
                ContentType = "application/json",
                Body = """{"youAreWriter":false,"heldByOther":true,"label":"Other Inspector"}"""
            }));
            await _page.RunAndWaitForResponseAsync(() => _page.Clock.RunForAsync(4000),
                response => response.Url.EndsWith("/api/control", StringComparison.Ordinal));
            await Expect(_page.Locator("#df-presence")).ToContainTextAsync("Read-only");

            await _page.Locator(".df-tree-node[data-tree-id='layout-target']").ClickAsync();
            await descriptorsStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Expect(_page.Locator("#df-props-pane")).ToBeVisibleAsync();
            // Loading is deliberately longer than the old assertion's window, without sleeping.
            await _page.Clock.RunForAsync(6000);
            await Expect(_page.Locator("#df-prop-filter")).ToHaveCountAsync(0);
            await ReleaseResponseAsync(releaseDescriptors, "/api/getProperties");
            if (legacyAgent)
            {
                await propertiesStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
                await Expect(_page.Locator("#df-prop-filter")).ToHaveCountAsync(0);
                await ReleaseResponseAsync(releaseProperties, "/api/getProperty");
            }

            var filter = _page.Locator("#df-prop-filter");
            await Expect(filter).ToBeEnabledAsync();
            await Expect(_page.Locator(".df-prop-row[data-property-name='fontsize'] input")).ToBeDisabledAsync();
            await Expect(_page.Locator(".df-prop-row[data-property-name='fontattributes'] select")).ToBeDisabledAsync();
            await Expect(_page.Locator(".df-prop-row[data-property-name='fontsize'] .df-prop-source")).ToBeDisabledAsync();
            await filter.FillAsync("Font");
            await Expect(_page.Locator(".df-prop-row:visible")).ToHaveCountAsync(2);
            await _page.Clock.RunForAsync(4000);
            await Expect(filter).ToBeEnabledAsync();
            await filter.FillAsync("not-a-property");
            await Expect(_page.Locator(".df-prop-no-results")).ToBeVisibleAsync();
            Assert.Equal(0, writes);
            Assert.Equal(legacyAgent, propertiesStarted.Task.IsCompleted);
        }
        finally
        {
            releaseDescriptors.TrySetResult();
            releaseProperties.TrySetResult();
        }
    }

    [InspectorBrowserFact]
    public async Task NetworkTabRefreshesAutomaticallyAndKeepsDetailOpen()
    {
        var networkCalls = 0;
        var initialStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseInitial = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pollStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePoll = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var detailStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDetail = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await _page.RouteAsync("**/api/network", async route =>
        {
            var call = Interlocked.Increment(ref networkCalls);
            if (call == 1)
            {
                initialStarted.TrySetResult();
                await releaseInitial.Task;
            }
            else if (call == 3)
            {
                pollStarted.TrySetResult();
                await releasePoll.Task;
            }
            var marker = call == 1 ? "first" : call == 2 ? "second" : "third";
            await route.FulfillAsync(new()
            {
                ContentType = "application/json",
                Body = $$"""
                    {"ok":true,"requests":[{"id":"request-1","method":"GET","url":"https://example.test/{{marker}}","statusCode":200,"durationMs":12}]}
                    """
            });
        });
        await _page.RouteAsync("**/api/network/detail", async route =>
        {
            detailStarted.TrySetResult();
            await releaseDetail.Task;
            await route.FulfillAsync(new()
            {
                ContentType = "application/json",
                Body = """
                    {"ok":true,"request":{"id":"request-1","method":"GET","url":"https://example.test/second","statusCode":200,"statusText":"OK","durationMs":12}}
                    """
            });
        });
        try
        {
            await PrepareLayoutWorkspaceAsync();
            var data = _page.Locator("#df-toggle-dock");
            if (!await data.IsVisibleAsync())
                await _page.Locator("#df-more").ClickAsync();
            await data.ClickAsync();
            await _page.Locator("#df-tab-network").ClickAsync();
            await initialStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Expect(_page.Locator("#df-attach-data")).ToBeDisabledAsync();

            await _page.RunAndWaitForResponseAsync(() => _page.Clock.RunForAsync(2000),
                response => response.Url.EndsWith("/api/network", StringComparison.Ordinal));
            await Expect(_page.Locator("#df-dock-body")).ToContainTextAsync("https://example.test/second");
            await ReleaseResponseAsync(releaseInitial, "/api/network");
            await _page.Clock.RunForAsync(2000);
            await pollStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(3, networkCalls);
            await Expect(_page.Locator("#df-dock-body")).Not.ToContainTextAsync("https://example.test/first");
            await Expect(_page.Locator("#df-dock-meta")).ToContainTextAsync("live");
            await Expect(_page.Locator("#df-attach-data")).ToBeEnabledAsync();

            await _page.Locator("#df-dock-body tr.df-row-click").ClickAsync();
            await detailStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Expect(_page.Locator("#df-attach-data")).ToBeDisabledAsync();
            await Expect(_page.Locator("#df-dock-meta")).ToContainTextAsync("loading");
            // A list response already in flight must not restore its attachable snapshot.
            await ReleaseResponseAsync(releasePoll, "/api/network");
            await _page.Clock.RunForAsync(6000);
            await Expect(_page.Locator("#df-attach-data")).ToBeDisabledAsync();
            await Expect(_page.Locator("#df-dock-meta")).ToContainTextAsync("loading");
            await Expect(_page.Locator("#df-dock-body")).Not.ToContainTextAsync("https://example.test/third");
            Assert.Equal(3, networkCalls);

            await ReleaseResponseAsync(releaseDetail, "/api/network/detail");
            await Expect(_page.Locator("#df-dock-body")).ToContainTextAsync("Back to requests");
            await Expect(_page.Locator("#df-attach-data")).ToBeEnabledAsync();
            await _page.Clock.RunForAsync(6000);
            await Expect(_page.Locator("#df-dock-body")).ToContainTextAsync("Back to requests");
            await Expect(_page.Locator("#df-dock-meta")).ToContainTextAsync("live paused");
            Assert.Equal(3, networkCalls);
            await _page.Locator("#df-attach-data").ClickAsync();
            var snapshot = await _page.EvaluateAsync<string>("window.__copiedDevFlowData");
            Assert.Contains("\"view\": \"detail\"", snapshot);
            Assert.Contains("https://example.test/second", snapshot);
            Assert.DoesNotContain("https://example.test/third", snapshot);
        }
        finally
        {
            releaseInitial.TrySetResult();
            releasePoll.TrySetResult();
            releaseDetail.TrySetResult();
        }
    }

    private async Task ReleaseResponseAsync(TaskCompletionSource release, string path)
    {
        var response = await _page.RunAndWaitForResponseAsync(() =>
        {
            release.TrySetResult();
            return Task.CompletedTask;
        }, response => response.Url.EndsWith(path, StringComparison.Ordinal));
        await response.FinishedAsync();
    }
}
