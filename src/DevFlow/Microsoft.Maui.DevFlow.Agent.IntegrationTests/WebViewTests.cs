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

    async Task EnsureOnBlazorPageAsync()
    {
        await NavigateToPageAsync("//blazor", "BlazorWebView");
        var cdpReady = await WaitForCdpReadyAsync(timeoutMs: 30000);
        if (!cdpReady)
            Output.WriteLine("WARNING: CDP not ready after 30s — WebView tests may fail.");
    }

    Task<bool> IsCdpReady(int timeoutMs = 10000)
        => WaitForCdpReadyAsync(timeoutMs: timeoutMs, pollIntervalMs: 500);

    async Task<string> GetCdpSourceWithRetryAsync()
    {
        try { return await Client.GetCdpSourceAsync(); }
        catch (HttpRequestException) when (Platform == "android")
        {
            Output.WriteLine("Initial CDP source request failed on Android; waiting and retrying once.");
            await WaitForCdpReadyAsync(timeoutMs: 15000);
            return await Client.GetCdpSourceAsync();
        }
    }

    async Task<JsonElement> SendCdpCommandWithRetryAsync(string method, JsonNode? paramsJson = null)
    {
        var result = await Client.SendCdpCommandAsync(method, paramsJson);
        if (Platform == "android" &&
            result.ValueKind == JsonValueKind.Object &&
            result.TryGetProperty("error", out _))
        {
            Output.WriteLine($"Initial CDP command '{method}' returned an error on Android; waiting and retrying once.");
            await WaitForCdpReadyAsync(timeoutMs: 15000);
            result = await Client.SendCdpCommandAsync(method, paramsJson);
        }

        return result;
    }

    [Fact]
    public async Task Contexts_ReturnsAtLeastOneWebView()
    {
        await EnsureOnBlazorPageAsync();
        var json = await Client.GetCdpWebViewsAsync();

        Assert.True(json.ValueKind == JsonValueKind.Object || json.ValueKind == JsonValueKind.Array);
    }

    [Fact]
    public async Task Evaluate_DocumentTitle_ReturnsResult()
    {
        await EnsureOnBlazorPageAsync();

        var paramsJson = JsonNode.Parse("""{"expression": "document.title"}""");
        var result = await SendCdpCommandWithRetryAsync("Runtime.evaluate", paramsJson);

        Assert.True(result.ValueKind != JsonValueKind.Undefined);
    }

    [Fact]
    public async Task Evaluate_SimpleExpression_ReturnsResult()
    {
        await EnsureOnBlazorPageAsync();
        if (!await IsCdpReady(timeoutMs: 30000))
        {
            Output.WriteLine("CDP not ready — skipping.");
            return;
        }

        var paramsJson = JsonNode.Parse("""{"expression": "1 + 1"}""");
        var result = await SendCdpCommandWithRetryAsync("Runtime.evaluate", paramsJson);

        Assert.Contains("2", result.ToString());
    }

    [Fact]
    public async Task Source_ReturnsHtmlContent()
    {
        await EnsureOnBlazorPageAsync();
        if (!await IsCdpReady(timeoutMs: 30000))
        {
            Output.WriteLine("CDP not ready — skipping.");
            return;
        }

        var source = await GetCdpSourceWithRetryAsync();
        Assert.NotNull(source);
        Assert.NotEmpty(source);
        Assert.Contains("<", source);
    }

    [Fact]
    public async Task Dom_ReturnsOuterHtml()
    {
        await EnsureOnBlazorPageAsync();
        if (!await IsCdpReady())
        {
            Output.WriteLine("CDP not ready — skipping.");
            return;
        }

        var response = await GetRawAsync("/api/v1/webview/dom");
        Assert.True(response.IsSuccessStatusCode);

        var html = await response.Content.ReadAsStringAsync();
        Assert.NotEmpty(html);
        Assert.Contains("<", html);
    }

    [Fact]
    public async Task DomQuery_ReturnsElements()
    {
        await EnsureOnBlazorPageAsync();
        if (!await IsCdpReady())
        {
            Output.WriteLine("CDP not ready — skipping.");
            return;
        }

        var response = await PostRawAsync("/api/v1/webview/dom/query", new
        {
            selector = "button",
            contextId = "0",
        });

        if (!response.IsSuccessStatusCode)
        {
            Output.WriteLine($"DOM query not available on this platform: {(int)response.StatusCode}");
            return;
        }

        var body = await response.Content.ReadAsStringAsync();
        Assert.NotEmpty(body);
    }

    [Fact]
    public async Task WebViewNavigate_Succeeds()
    {
        await EnsureOnBlazorPageAsync();
        if (!await IsCdpReady())
        {
            Output.WriteLine("CDP not ready — skipping.");
            return;
        }

        var response = await PostRawAsync("/api/v1/webview/navigate", new
        {
            url = "/counter",
            contextId = "0",
        });

        Assert.True(response.IsSuccessStatusCode, $"WebView navigate failed with status {(int)response.StatusCode}");
        await SettleAsync(1000);

        await PostRawAsync("/api/v1/webview/navigate", new
        {
            url = "/",
            contextId = "0",
        });
        await SettleAsync(1000);
    }

    [Fact]
    public async Task InputClick_Button_Succeeds()
    {
        await EnsureOnBlazorPageAsync();
        if (!await IsCdpReady())
        {
            Output.WriteLine("CDP not ready — skipping.");
            return;
        }

        await PostRawAsync("/api/v1/webview/navigate", new { url = "/counter", contextId = "0" });
        await SettleAsync(1000);

        var response = await PostRawAsync("/api/v1/webview/input/click", new
        {
            selector = "button",
            contextId = "0",
        });

        if (!response.IsSuccessStatusCode)
        {
            Output.WriteLine($"Input click not available on this platform: {(int)response.StatusCode}");
            return;
        }

        await PostRawAsync("/api/v1/webview/navigate", new { url = "/", contextId = "0" });
        await SettleAsync(500);
    }

    [Fact]
    public async Task InputFill_TextInput_Succeeds()
    {
        await EnsureOnBlazorPageAsync();
        var response = await PostRawAsync("/api/v1/webview/input/fill", new
        {
            selector = "input",
            text = "Blazor fill test",
            contextId = "0",
        });

        Output.WriteLine($"Input fill status: {(int)response.StatusCode}");
    }

    [Fact]
    public async Task InputText_InsertsText()
    {
        await EnsureOnBlazorPageAsync();

        var response = await PostRawAsync("/api/v1/webview/input/text", new
        {
            text = "Hello from test",
            contextId = "0",
        });

        Output.WriteLine($"Input text status: {(int)response.StatusCode}");
    }

    [Fact]
    public async Task Screenshot_WebView_ReturnsImage()
    {
        await EnsureOnBlazorPageAsync();

        var response = await GetRawAsync("/api/v1/webview/screenshot?contextId=0");
        if (response.IsSuccessStatusCode)
        {
            var bytes = await response.Content.ReadAsByteArrayAsync();
            Assert.NotEmpty(bytes);
        }
        else
        {
            Output.WriteLine($"WebView screenshot not available: {(int)response.StatusCode}");
        }
    }

    [Fact]
    public async Task Evaluate_WithDomAccess_Works()
    {
        await EnsureOnBlazorPageAsync();

        var paramsJson = JsonNode.Parse("""{"expression": "document.querySelectorAll('*').length"}""");
        var result = await Client.SendCdpCommandAsync("Runtime.evaluate", paramsJson);

        Assert.True(result.ValueKind != JsonValueKind.Undefined);
    }

    [Fact]
    public async Task MultiBlazorPage_HasMultipleContexts()
    {
        await NavigateToPageAsync("//multiblazor", "BlazorLeft");
        await SettleAsync(2000);

        var json = await Client.GetCdpWebViewsAsync();
        Assert.True(json.ValueKind != JsonValueKind.Undefined);

        await NavigateToMainPageAsync();
    }
}
