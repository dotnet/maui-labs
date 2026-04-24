using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;

/// <summary>
/// xUnit collection fixture wrapper that selects a platform-specific
/// fixture based on DEVFLOW_TEST_PLATFORM.
/// </summary>
public sealed class AppFixture : IAppFixture, IAsyncLifetime
{
    readonly IAppFixture _inner;
    readonly SemaphoreSlim _blazorReadyGate = new(1, 1);
    bool _blazorReady;

    public AppFixture()
    {
        _inner = AppFixtureFactory.Create();
    }

    public AgentClient Client => _inner.Client;
    public HttpClient Http => _inner.Http;
    public int AgentPort => _inner.AgentPort;
    public string AgentBaseUrl => _inner.AgentBaseUrl;
    public string Platform => _inner.Platform;

    public Task InitializeAsync() => _inner.InitializeAsync();
    public Task DisposeAsync() => _inner.DisposeAsync();

    /// <summary>
    /// Navigates to the //blazor Shell route (if not already there), waits for the
    /// BlazorWebView element to appear, and probes the CDP bridge with Runtime.evaluate
    /// until it answers cleanly. Always probes: Android's OnPageFinished can reset the
    /// bridge mid-test when the Blazor router performs internal navigation, so we never
    /// trust a cached "ready" flag. The first call pays the full warm-up cost; subsequent
    /// calls short-circuit in ~50ms when the bridge is already live.
    /// </summary>
    public async Task EnsureBlazorReadyAsync()
    {
        await _blazorReadyGate.WaitAsync();
        try
        {
            if (!await IsOnBlazorPageAsync())
            {
                _blazorReady = false;
                await Client.NavigateAsync("//blazor");
                await WaitForAutomationIdAsync("BlazorWebView", timeoutMs: 15000);
            }

            // Cold-start timeout (first time the bridge is coming up) is generous because
            // chobitsu + WebView2 / Android WebView can take a while on hosted runners.
            // Warm-path timeout (bridge previously responded) only needs to absorb a brief
            // re-injection window triggered by e.g. Blazor internal navigation.
            var timeoutMs = _blazorReady
                ? 15000
                : Platform switch
                {
                    "ios" => 60000,
                    "android" or "windows" => 90000,
                    _ => 45000,
                };

            var contextTimeoutMs = Platform == "windows" ? 20000 : 10000;

            if (await WaitForCdpResponsiveAsync(timeoutMs))
            {
                // CDP bridge is live, but Blazor may not have finished rendering yet.
                // Wait for the Blazor component tree to produce .todo-container.
                // Windows WebView2 on hosted runners can take 30s+ to render.
                var renderTimeout = Platform == "windows" ? 60000 : 30000;
                var rendered = await WaitForBlazorRenderedAsync(renderTimeout);
                var contextsReady = await WaitForCdpContextsAsync(contextTimeoutMs, requireReady: true);
                if (rendered && contextsReady)
                {
                    _blazorReady = true;
                    return;
                }
            }

            // A prior test may have left the existing bridge in a stale state after an
            // in-WebView route change. Force a fresh Shell navigation and retry using the
            // full cold-start budget instead of failing the next test on the warm-path cap.
            _blazorReady = false;
            await Client.NavigateAsync("//native");
            await Task.Delay(500);
            await Client.NavigateAsync("//blazor");
            await WaitForAutomationIdAsync("BlazorWebView", timeoutMs: 15000);

            var recoveryTimeoutMs = Platform switch
            {
                "ios" => 60000,
                "android" or "windows" => 90000,
                _ => 45000,
            };

            if (await WaitForCdpResponsiveAsync(recoveryTimeoutMs))
            {
                var renderTimeout = Platform == "windows" ? 60000 : 30000;
                var rendered = await WaitForBlazorRenderedAsync(renderTimeout);
                var contextsReady = await WaitForCdpContextsAsync(contextTimeoutMs, requireReady: true);
                if (rendered && contextsReady)
                {
                    _blazorReady = true;
                    return;
                }
            }

            throw new TimeoutException(
                $"Blazor readiness failed on {Platform}: bridge/contexts did not stabilize within {timeoutMs}ms (recovery {recoveryTimeoutMs}ms).");
        }
        finally
        {
            _blazorReadyGate.Release();
        }
    }

    /// <summary>
    /// Public retry helper: probes the CDP bridge until Runtime.evaluate "1+1" returns
    /// cleanly (or timeout). Useful when a test calls a bridge-dependent endpoint and the
    /// bridge is transiently mid-reinject (e.g. right after a navigate).
    /// </summary>
    public async Task<bool> WaitForCdpResponsiveAsync(int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var probe = await Client.SendCdpCommandAsync(
                    "Runtime.evaluate",
                    JsonNode.Parse("""{"expression":"1 + 1"}"""));
                var text = probe.ToString();
                if (!text.Contains("\"error\"", StringComparison.OrdinalIgnoreCase) &&
                    text.Contains("\"value\":2", StringComparison.Ordinal))
                {
                    return true;
                }
            }
            catch
            {
                // Not ready yet.
            }

            await Task.Delay(250);
        }

        return false;
    }

    /// <summary>
    /// Waits for the Blazor Home page to finish rendering by checking that the
    /// <c>.todo-container</c> element exists inside the WebView DOM. The Blazor
    /// layout/nav shell renders first (with its own <c>&lt;a&gt;</c> and
    /// <c>&lt;input&gt;</c> elements), so generic element checks return too early.
    /// Checking for a page-specific marker ensures the actual component tree has
    /// rendered.
    /// </summary>
    public async Task<bool> WaitForBlazorRenderedAsync(int timeoutMs = 30000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var probe = await Client.SendCdpCommandAsync(
                    "Runtime.evaluate",
                    JsonNode.Parse("""{"expression":"!!document.querySelector('.todo-container') ? 'ready' : 'waiting'"}"""));
                var text = probe.ToString();
                if (text.Contains("ready", StringComparison.Ordinal) &&
                    !text.Contains("\"error\"", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch
            {
                // Not ready yet.
            }

            await Task.Delay(500);
        }

        return false;
    }

    static int CountCdpContexts(JsonElement json)
    {
        if (json.ValueKind == JsonValueKind.Array)
            return json.GetArrayLength();

        if (json.ValueKind == JsonValueKind.Object &&
            json.TryGetProperty("webviews", out var webviews) &&
            webviews.ValueKind == JsonValueKind.Array)
        {
            return webviews.GetArrayLength();
        }

        return 0;
    }

    static bool HasReadyCdpContext(JsonElement json)
    {
        IEnumerable<JsonElement> contexts = Enumerable.Empty<JsonElement>();

        if (json.ValueKind == JsonValueKind.Array)
            contexts = json.EnumerateArray();
        else if (json.ValueKind == JsonValueKind.Object &&
                 json.TryGetProperty("webviews", out var webviews) &&
                 webviews.ValueKind == JsonValueKind.Array)
            contexts = webviews.EnumerateArray();

        foreach (var ctx in contexts)
        {
            if (ctx.ValueKind != JsonValueKind.Object)
                continue;

            if (ctx.TryGetProperty("isReady", out var r1) && r1.ValueKind == JsonValueKind.True)
                return true;
            if (ctx.TryGetProperty("ready", out var r2) && r2.ValueKind == JsonValueKind.True)
                return true;
        }

        return false;
    }

    public async Task<bool> WaitForCdpContextsAsync(int timeoutMs = 15000, bool requireReady = false)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var contexts = await Client.GetCdpWebViewsAsync();
                if (CountCdpContexts(contexts) > 0 &&
                    (!requireReady || HasReadyCdpContext(contexts)))
                {
                    return true;
                }
            }
            catch
            {
                // Not ready yet.
            }

            await Task.Delay(250);
        }

        return false;
    }

    async Task<bool> IsOnBlazorPageAsync()
    {
        try
        {
            var results = await Client.QueryAsync(automationId: "BlazorWebView");
            return results.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    async Task WaitForAutomationIdAsync(string automationId, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var results = await Client.QueryAsync(automationId: automationId);
            if (results.Count > 0)
                return;
            await Task.Delay(250);
        }

        throw new TimeoutException(
            $"Element with AutomationId '{automationId}' did not appear within {timeoutMs}ms.");
    }

    /// <summary>
    /// Marks the fixture as no-longer-on-Blazor-page so the next EnsureBlazorReadyAsync
    /// call will re-navigate. Call this from tests that intentionally navigate away
    /// (e.g. MultiBlazorPage_HasMultipleContexts).
    /// </summary>
    public void InvalidateBlazorReady() => _blazorReady = false;
}

[CollectionDefinition("AgentIntegration")]
public class AgentIntegrationCollection : ICollectionFixture<AppFixture>;
