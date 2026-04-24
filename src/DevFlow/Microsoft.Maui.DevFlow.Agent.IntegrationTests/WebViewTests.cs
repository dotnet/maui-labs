using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;
using Xunit.Abstractions;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests;

[Collection("AgentIntegration")]
[Trait("Category", "WebView")]
public class WebViewTests : IntegrationTestBase
{
    public WebViewTests(AppFixture app, ITestOutputHelper output)
        : base(app, output) { }

    static bool HasWebViewContexts(JsonElement json)
    {
        if (json.ValueKind == JsonValueKind.Array)
            return json.GetArrayLength() > 0;

        if (json.ValueKind == JsonValueKind.Object &&
            json.TryGetProperty("webviews", out var webviews) &&
            webviews.ValueKind == JsonValueKind.Array)
        {
            return webviews.GetArrayLength() > 0;
        }

        return false;
    }

    static int CountWebViewContexts(JsonElement json)
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

    static bool AnyReadyContext(JsonElement json)
    {
        JsonElement array;
        if (json.ValueKind == JsonValueKind.Array)
            array = json;
        else if (json.ValueKind == JsonValueKind.Object &&
                 json.TryGetProperty("webviews", out var webviews) &&
                 webviews.ValueKind == JsonValueKind.Array)
            array = webviews;
        else
            return false;

        foreach (var ctx in array.EnumerateArray())
        {
            if (ctx.ValueKind != JsonValueKind.Object) continue;
            if (ctx.TryGetProperty("isReady", out var r1) && r1.ValueKind == JsonValueKind.True) return true;
            if (ctx.TryGetProperty("ready", out var r2) && r2.ValueKind == JsonValueKind.True) return true;
        }

        return false;
    }

    Task EnsureOnBlazorPageAsync() => App.EnsureBlazorReadyAsync();

    static IEnumerable<JsonElement> EnumerateContexts(JsonElement json)
    {
        if (json.ValueKind == JsonValueKind.Array)
        {
            foreach (var ctx in json.EnumerateArray())
                yield return ctx;
            yield break;
        }

        if (json.ValueKind == JsonValueKind.Object &&
            json.TryGetProperty("webviews", out var webviews) &&
            webviews.ValueKind == JsonValueKind.Array)
        {
            foreach (var ctx in webviews.EnumerateArray())
                yield return ctx;
        }
    }

    static bool IsReadyContext(JsonElement ctx)
        => (ctx.TryGetProperty("isReady", out var r1) && r1.ValueKind == JsonValueKind.True)
           || (ctx.TryGetProperty("ready", out var r2) && r2.ValueKind == JsonValueKind.True);

    async Task<string> GetActiveContextIdAsync(int timeoutMs = 15000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        JsonElement? lastJson = null;

        while (DateTime.UtcNow < deadline)
        {
            var json = await Client.GetCdpWebViewsAsync();
            lastJson = json;
            var contexts = EnumerateContexts(json).ToList();
            if (contexts.Count == 0)
            {
                await Task.Delay(250);
                continue;
            }

            JsonElement? pick =
                contexts.LastOrDefault(c =>
                    c.TryGetProperty("automationId", out var automationId) &&
                    automationId.ValueKind == JsonValueKind.String &&
                    automationId.GetString() == "BlazorWebView" &&
                    IsReadyContext(c));

            if (pick == null || pick.Value.ValueKind == JsonValueKind.Undefined)
                pick = contexts.LastOrDefault(IsReadyContext);

            if (pick == null || pick.Value.ValueKind == JsonValueKind.Undefined)
                pick = contexts.LastOrDefault(c =>
                    c.TryGetProperty("automationId", out var automationId) &&
                    automationId.ValueKind == JsonValueKind.String &&
                    automationId.GetString() == "BlazorWebView");

            var selected = pick ?? contexts[^1];
            if (selected.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                return id.GetString()!;
            if (selected.TryGetProperty("index", out var index) && index.ValueKind == JsonValueKind.Number)
                return index.GetInt32().ToString();

            return "0";
        }

        Assert.True(false, $"Expected at least one WebView context within {timeoutMs}ms. Last payload: {lastJson}");
        return "0";
    }

    /// <summary>
    /// Returns true if the response body indicates the CDP bridge is transiently
    /// not-ready (i.e. chobitsu is re-injecting after a page navigation). Android's
    /// OnPageFinished can reset the bridge mid-test on internal Blazor router navigations.
    /// </summary>
    static bool IsBridgeNotReady(string body)
        => body.Contains("CDP not ready", StringComparison.OrdinalIgnoreCase)
            || body.Contains("WebView not ready", StringComparison.OrdinalIgnoreCase)
            || body.Contains("WebView not initialized", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Executes an HTTP call and retries up to <paramref name="timeoutMs"/> when the
    /// response body signals the CDP bridge is transiently re-injecting. Any other
    /// error path (non-bridge 4xx/5xx, exception) returns immediately.
    /// </summary>
    async Task<HttpResponseMessage> WithBridgeRetryAsync(
        Func<Task<HttpResponseMessage>> call,
        int timeoutMs = 15000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        HttpResponseMessage? last = null;
        while (DateTime.UtcNow < deadline)
        {
            last?.Dispose();
            last = await call();
            if (last.IsSuccessStatusCode) return last;

            var body = await last.Content.ReadAsStringAsync();
            if (!IsBridgeNotReady(body))
                return last;

            await Task.Delay(250);
            // Re-create the response wrapper with the body we already read so callers
            // can still inspect it if we hit the deadline.
            if (DateTime.UtcNow >= deadline)
            {
                var replay = new HttpResponseMessage(last.StatusCode)
                {
                    Content = new StringContent(body),
                    RequestMessage = last.RequestMessage,
                };
                last.Dispose();
                return replay;
            }
        }
        return last!;
    }

    Task<HttpResponseMessage> GetWithBridgeRetryAsync(string path, int timeoutMs = 15000)
        => WithBridgeRetryAsync(() => GetRawAsync(path), timeoutMs);

    Task<HttpResponseMessage> PostWithBridgeRetryAsync(string path, object? body, int timeoutMs = 15000)
        => WithBridgeRetryAsync(() => PostRawAsync(path, body), timeoutMs);

    /// <summary>
    /// Asserts the CDP bridge is actually live by sending a trivial Runtime.evaluate probe.
    /// Retries briefly when the bridge is mid-reinject (Android OnPageFinished racing with
    /// a test call). Protects against false-positive test passes when chobitsu failed to
    /// load and the bridge is returning error payloads.
    /// </summary>
    async Task AssertCdpResponsiveAsync(int timeoutMs = 15000)
    {
        var effectiveTimeoutMs = Platform == "windows"
            ? Math.Max(timeoutMs, 30000)
            : timeoutMs;
        var ok = await App.WaitForCdpResponsiveAsync(effectiveTimeoutMs);
        if (!ok)
        {
            App.InvalidateBlazorReady();
            await App.EnsureBlazorReadyAsync();
            ok = await App.WaitForCdpResponsiveAsync(effectiveTimeoutMs);
        }
        Assert.True(ok,
            $"CDP bridge did not become responsive within {effectiveTimeoutMs}ms (expected a live bridge answering Runtime.evaluate).");
    }

    [Fact]
    public async Task Contexts_ReturnsAtLeastOneWebView()
    {
        await EnsureOnBlazorPageAsync();
        var json = await Client.GetCdpWebViewsAsync();

        Assert.True(HasWebViewContexts(json), "Expected at least one WebView context.");
    }

    [Fact]
    public async Task Contexts_WebViewIsReady()
    {
        await EnsureOnBlazorPageAsync();
        var json = await Client.GetCdpWebViewsAsync();
        Assert.True(HasWebViewContexts(json), "Expected at least one WebView context.");
        Assert.True(AnyReadyContext(json),
            $"Expected at least one WebView context with isReady=true. Got: {json}");
    }

    [Fact]
    public async Task Evaluate_DocumentTitle_ReturnsResult()
    {
        await EnsureOnBlazorPageAsync();

        var paramsJson = JsonNode.Parse("""{"expression": "document.title"}""");
        var result = await Client.SendCdpCommandAsync("Runtime.evaluate", paramsJson);

        var text = result.ToString();
        Assert.False(text.Contains("\"error\"", StringComparison.OrdinalIgnoreCase),
            $"Runtime.evaluate returned an error payload: {text}");
        // Response should be a Runtime.evaluate result object with a result.value (string title).
        Assert.Contains("\"result\"", text);
        Assert.Contains("\"value\"", text);
    }

    [Fact]
    public async Task Evaluate_SimpleExpression_ReturnsResult()
    {
        await EnsureOnBlazorPageAsync();

        var paramsJson = JsonNode.Parse("""{"expression": "1 + 1"}""");
        var result = await Client.SendCdpCommandAsync("Runtime.evaluate", paramsJson);

        var text = result.ToString();
        Assert.False(text.Contains("\"error\"", StringComparison.OrdinalIgnoreCase),
            $"Runtime.evaluate returned an error payload: {text}");
        Assert.Contains("\"value\":2", text);
    }

    [Fact]
    public async Task Source_ReturnsHtmlContent()
    {
        await EnsureOnBlazorPageAsync();

        var source = await Client.GetCdpSourceAsync();
        Assert.NotNull(source);
        Assert.NotEmpty(source);
        Assert.Contains("<", source);
    }

    [Fact]
    public async Task Dom_ReturnsOuterHtml()
    {
        await EnsureOnBlazorPageAsync();
        await AssertCdpResponsiveAsync();

        var response = await GetWithBridgeRetryAsync("/api/v1/webview/dom");
        Assert.True(response.IsSuccessStatusCode,
            $"/api/v1/webview/dom returned {(int)response.StatusCode}");

        var html = await response.Content.ReadAsStringAsync();
        Assert.NotEmpty(html);
        Assert.Contains("<", html);
    }

    [Fact]
    public async Task DomQuery_ReturnsElements()
    {
        await EnsureOnBlazorPageAsync();
        await AssertCdpResponsiveAsync();
        var contextId = await GetActiveContextIdAsync();

        var response = await PostWithBridgeRetryAsync("/api/v1/webview/dom/query", new
        {
            selector = ".add-btn",
            contextId,
        });

        Assert.True(response.IsSuccessStatusCode,
            $"/api/v1/webview/dom/query returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

        var body = await response.Content.ReadAsStringAsync();
        Assert.NotEmpty(body);
    }

    [Fact]
    public async Task WebViewNavigate_Succeeds()
    {
        await EnsureOnBlazorPageAsync();
        await AssertCdpResponsiveAsync();
        var contextId = await GetActiveContextIdAsync();

        var response = await PostWithBridgeRetryAsync("/api/v1/webview/navigate", new
        {
            url = "/counter",
            contextId,
        });

        Assert.True(response.IsSuccessStatusCode,
            $"WebView navigate failed with status {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        // Wait for chobitsu to re-inject after the nav before we navigate back.
        await App.WaitForCdpResponsiveAsync(15000);
        contextId = await GetActiveContextIdAsync();

        var backResponse = await PostWithBridgeRetryAsync("/api/v1/webview/navigate", new
        {
            url = "/",
            contextId,
        });
        Assert.True(backResponse.IsSuccessStatusCode,
            $"WebView navigate back failed with status {(int)backResponse.StatusCode}: {await backResponse.Content.ReadAsStringAsync()}");
        await App.WaitForCdpResponsiveAsync(15000);
        App.InvalidateBlazorReady();
    }

    [Fact]
    public async Task InputClick_Button_Succeeds()
    {
        await EnsureOnBlazorPageAsync();
        await AssertCdpResponsiveAsync();
        var contextId = await GetActiveContextIdAsync();

        // Use the always-present Add button. On slow renderers (Windows WebView2 on
        // hosted runners) the component DOM can lag behind the CDP "ready" state, so
        // poll until the element appears or a generous timeout expires.
        HttpResponseMessage response = null!;
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            response = await PostWithBridgeRetryAsync("/api/v1/webview/input/click", new
            {
                selector = ".add-btn",
                contextId,
            });

            if (response.IsSuccessStatusCode)
                break;

            var body = await response.Content.ReadAsStringAsync();
            if (!body.Contains("No element matches", StringComparison.OrdinalIgnoreCase))
                break; // Non-selector error, don't retry

            await Task.Delay(1000);
        }

        Assert.True(response.IsSuccessStatusCode,
            $"/api/v1/webview/input/click returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

    }

    [Fact]
    public async Task InputFill_TextInput_Succeeds()
    {
        await EnsureOnBlazorPageAsync();
        await AssertCdpResponsiveAsync();
        var contextId = await GetActiveContextIdAsync();

        var response = await PostWithBridgeRetryAsync("/api/v1/webview/input/fill", new
        {
            selector = "input",
            text = "Blazor fill test",
            contextId,
        });

        // Some platforms may not ship an input element on /counter; a 404 from the selector
        // is acceptable, but anything above 500 or a bridge error is not.
        Assert.True(
            response.IsSuccessStatusCode || (int)response.StatusCode == 404,
            $"/api/v1/webview/input/fill returned unexpected status {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

    }

    [Fact]
    public async Task InputText_InsertsText()
    {
        await EnsureOnBlazorPageAsync();
        await AssertCdpResponsiveAsync();
        var contextId = await GetActiveContextIdAsync();

        var response = await PostWithBridgeRetryAsync("/api/v1/webview/input/text", new
        {
            text = "Hello from test",
            contextId,
        });

        Assert.True(response.IsSuccessStatusCode,
            $"/api/v1/webview/input/text returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    [Fact]
    public async Task Screenshot_WebView_ReturnsImage()
    {
        await EnsureOnBlazorPageAsync();
        await AssertCdpResponsiveAsync();
        var contextId = await GetActiveContextIdAsync();

        var response = await GetWithBridgeRetryAsync($"/api/v1/webview/screenshot?contextId={Uri.EscapeDataString(contextId)}");

        // WebView screenshots rely on either CDP Page.captureScreenshot or native
        // element capture. iOS simulators on CI runners sometimes lack the graphics
        // surface needed for either path, so accept a 400 there as a known limitation.
        if (!response.IsSuccessStatusCode && Platform == "ios")
        {
            Output.WriteLine("WebView screenshot not supported on this iOS runner (known limitation).");
            return;
        }

        Assert.True(response.IsSuccessStatusCode,
            $"/api/v1/webview/screenshot returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.NotEmpty(bytes);
    }

    [Fact]
    public async Task Evaluate_WithDomAccess_Works()
    {
        await EnsureOnBlazorPageAsync();

        var paramsJson = JsonNode.Parse("""{"expression": "document.querySelectorAll('*').length"}""");
        var result = await Client.SendCdpCommandAsync("Runtime.evaluate", paramsJson);

        var text = result.ToString();
        Assert.False(text.Contains("\"error\"", StringComparison.OrdinalIgnoreCase),
            $"Runtime.evaluate returned an error payload: {text}");
        Assert.Contains("\"result\"", text);
        Assert.Contains("\"value\"", text);
    }

    [Fact]
    public async Task MultiBlazorPage_HasMultipleContexts()
    {
        // This test intentionally leaves the Blazor single-WebView page.
        App.InvalidateBlazorReady();
        try
        {
            await NavigateToPageAsync("//multiblazor", "BlazorLeft");

            var timeoutMs = Platform switch
            {
                "ios" => 60000,
                "android" or "windows" => 90000,
                _ => 45000,
            };

            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            var contextCount = 0;
            var anyReady = false;
            JsonElement lastJson = default;

            while (DateTime.UtcNow < deadline)
            {
                lastJson = await Client.GetCdpWebViewsAsync();
                contextCount = CountWebViewContexts(lastJson);
                anyReady = AnyReadyContext(lastJson);
                if (contextCount >= 2 && anyReady)
                    break;
                await Task.Delay(500);
            }

            Assert.True(contextCount >= 2,
                $"Expected at least 2 WebView contexts on the multi-Blazor page, got {contextCount}. Last response: {lastJson}");
            Assert.True(anyReady,
                $"Expected at least one ready WebView context on the multi-Blazor page. Last response: {lastJson}");
        }
        finally
        {
            await NavigateToMainPageAsync();
            App.InvalidateBlazorReady();
        }
    }
}
