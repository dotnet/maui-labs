using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;
using Microsoft.Maui.DevFlow.Driver;
using Xunit.Abstractions;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests;

[Collection("AgentIntegration")]
[Trait("Category", "WebView")]
[Trait(TestFramework.Trait, TestFramework.Maui)]
public class GenericWebViewTests(AppFixture app, ITestOutputHelper output) : IntegrationTestBase(app, output)
{
    [Theory]
    [InlineData("//webview", "StandardWebView", "webview", "Standard WebView fixture")]
    [InlineData("//hybrid", "HybridWebView", "hybrid", "Hybrid WebView fixture")]
    public async Task Context_GenericHost_IsReadyActiveAndCorrelated(
        string route, string id, string kind, string title)
    {
        await EnsureHostAsync(route, id, title);
        var contexts = await Client.GetCdpWebViewsAsync();
        var context = Assert.Single(contexts.GetProperty("webviews").EnumerateArray(),
            c => c.GetProperty("automationId").GetString() == id);

        Assert.Equal(kind, context.GetProperty("hostKind").GetString());
        Assert.True(context.GetProperty("ready").GetBoolean());
        Assert.False(context.TryGetProperty("isReady", out _));
        Assert.Equal($"webview-{context.GetProperty("index").GetInt32()}", context.GetProperty("id").GetString());
        Assert.True(context.GetProperty("active").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(context.GetProperty("elementId").GetString()));
        Assert.Equal(title, (await EvaluateAsync("document.title")).GetString());
    }

    [Theory]
    [InlineData("//webview", "StandardWebView", "Standard WebView fixture")]
    [InlineData("//hybrid", "HybridWebView", "Hybrid WebView fixture")]
    public async Task Input_GenericHost_FillsAndClicksExactlyOnce(string route, string id, string title)
    {
        await EnsureHostAsync(route, id, title);
        await EvaluateAsync("count = 0; document.querySelector('#fixture-count').textContent = '0'; true", id);

        Assert.True(await Client.FillWebViewAsync("#fixture-input", "generic input", await GetContextIdAsync(id)));
        Assert.Equal("generic input", (await EvaluateAsync("document.querySelector('#fixture-input').value", id)).GetString());
        Assert.True(await Client.ClickWebViewAsync("#fixture-button", await GetContextIdAsync(id)));
        Assert.Equal(1, (await EvaluateAsync("Number(document.querySelector('#fixture-count').textContent)", id)).GetInt32());
    }

    [Theory]
    [InlineData("//webview", "StandardWebView", "Standard WebView fixture")]
    [InlineData("//hybrid", "HybridWebView", "Hybrid WebView fixture")]
    public async Task Screenshot_GenericHost_ReturnsNativeBackedPng(string route, string id, string title)
    {
        await EnsureHostAsync(route, id, title);
        var bytes = await Client.GetWebViewScreenshotAsync(await GetContextIdAsync(id));

        Assert.True(bytes.Length > 32);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, bytes.Take(8).ToArray());
    }

    [Theory]
    [InlineData("//webview", "StandardWebView", "Standard WebView fixture")]
    [InlineData("//hybrid", "HybridWebView", "Hybrid WebView fixture")]
    public async Task Reload_GenericHost_ReinjectsWithoutBlazorRoot(string route, string id, string title)
    {
        await EnsureHostAsync(route, id, title);
        await Client.SendCdpCommandAsync("Page.reload", contextId: await GetContextIdAsync(id));
        await WaitForDocumentAsync(id, title);

        Assert.Equal(2, (await EvaluateAsync("1 + 1", id)).GetInt32());
    }

    [Theory]
    [InlineData("//webview", "StandardWebView", "Standard WebView fixture")]
    [InlineData("//hybrid", "HybridWebView", "Hybrid WebView fixture")]
    public async Task Layout_GenericHost_IncludesDomOverflow(string route, string id, string title)
    {
        await EnsureHostAsync(route, id, title);
        var capabilities = (await Client.GetCapabilitiesAsync())
            .GetProperty("capabilities").GetProperty("ui.layoutDiagnostics");
        Assert.True(capabilities.GetProperty("webview").GetProperty("supported").GetBoolean());
        Assert.False(capabilities.TryGetProperty("blazor", out _));
        Assert.Contains(capabilities.GetProperty("features").EnumerateArray(),
            feature => feature.GetString() == "webview-dom");
        Assert.DoesNotContain(capabilities.GetProperty("features").EnumerateArray(),
            feature => feature.GetString() == "blazor-dom");
        var result = await Client.AnalyzeLayoutAsync(new LayoutInspectionRequest
        {
            Profile = "strict",
            MinimumSeverity = "info",
            Scope = new LayoutInspectionScope { IncludeWebViewElements = true, IncludeNativeElements = false },
            Stability = new LayoutStabilityOptions { Mode = "immediate" }
        });

        Assert.NotNull(result);
        Assert.Contains(result!.Findings, f => f.Element.AutomationId == "fixture-overflow"
            && f.RuleId == "layout.text-not-fully-rendered");
        var excluded = await Client.AnalyzeLayoutAsync(new LayoutInspectionRequest
        {
            Profile = "strict",
            MinimumSeverity = "info",
            Scope = new LayoutInspectionScope { IncludeWebViewElements = false, IncludeNativeElements = false },
            Stability = new LayoutStabilityOptions { Mode = "immediate" }
        });
        Assert.NotNull(excluded);
        Assert.DoesNotContain(excluded!.Findings, f => f.Element.AutomationId == "fixture-overflow");
    }

    [Fact]
    public async Task HybridMessaging_RemainsWorkingAfterCdpInjection()
    {
        await EnsureHostAsync("//hybrid", "HybridWebView", "Hybrid WebView fixture");
        await EvaluateAsync("count = 0; true", "HybridWebView");
        Assert.True(await Client.ClickWebViewAsync("#fixture-button", await GetContextIdAsync("HybridWebView")));
        await WaitForAsync(async () => (await FindElementAsync("HybridMessageStatus")).Text == "js-click: 1");

        var button = await FindElementAsync("HybridSendMessageButton");
        Assert.True(await Client.TapAsync(button.Id));
        await WaitForAsync(async () => (await FindElementAsync("HybridMessageStatus")).Text == "js-ack: native-to-js-ok");
        Assert.Equal("native-to-js-ok",
            (await EvaluateAsync("document.querySelector('#native-message').textContent", "HybridWebView")).GetString());
    }

    [Fact]
    public async Task MixedHosts_CorrelatesStandardViewWithoutAutomationId()
    {
        Assert.True(await Client.NavigateAsync("//mixedwebviews"));
        await WaitForAsync(async () =>
        {
            var contexts = await Client.GetCdpWebViewsAsync();
            return contexts.GetProperty("webviews").EnumerateArray()
                .Count(c => c.GetProperty("active").GetBoolean() && c.GetProperty("ready").GetBoolean()) == 3;
        }, timeoutMs: 60000);
        var registered = await Client.GetCdpWebViewsAsync();
        var active = registered.GetProperty("webviews").EnumerateArray()
            .Where(c => c.GetProperty("active").GetBoolean()).ToArray();
        var standard = Assert.Single(active, c => c.GetProperty("hostKind").GetString() == "webview");
        Assert.Equal(JsonValueKind.Null, standard.GetProperty("automationId").ValueKind);
        var elementId = standard.GetProperty("elementId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(elementId));
        var contextId = standard.GetProperty("id").GetString()!;
        Assert.Equal("Mixed standard WebView fixture", (await EvaluateAsync("document.title", contextId)).GetString());
        Assert.Contains(active, c => c.GetProperty("hostKind").GetString() == "hybrid");
        Assert.Contains(active, c => c.GetProperty("hostKind").GetString() == "blazor");
        var bytes = await Client.GetWebViewScreenshotAsync(contextId);
        Assert.True(bytes.Length > 32);
    }

    [Fact]
    public async Task Console_GenericHost_CapturesBrowserMessages()
    {
        await EnsureHostAsync("//webview", "StandardWebView", "Standard WebView fixture");
        await EvaluateAsync("console.log('generic-webview-integration-marker'); true", "StandardWebView");

        await WaitForAsync(async () =>
        {
            using var response = await GetRawAsync("/api/v1/webview/console");
            return (await response.Content.ReadAsStringAsync()).Contains("generic-webview-integration-marker", StringComparison.Ordinal);
        }, timeoutMs: 10000);
    }

    [Theory]
    [InlineData("//hybrid", "HybridWebView", "Hybrid WebView fixture")]
    public async Task Navigate_GenericHost_LoadsRealRelativeDocument(string route, string id, string title)
    {
        await EnsureHostAsync(route, id, title);
        Assert.True(await Client.NavigateWebViewAsync("second.html", await GetContextIdAsync(id)));
        await WaitForDocumentAsync(id, "Second WebView document");
        Assert.Equal("Second document loaded",
            (await EvaluateAsync("document.querySelector('#second-document').textContent", id)).GetString());
        Assert.True(await Client.NavigateWebViewAsync("index.html", await GetContextIdAsync(id)));
        await WaitForDocumentAsync(id, title);
    }

    [Fact]
    public async Task Navigate_StandardWebView_ResolvesRelativeHttpDocument()
    {
        await EnsureHostAsync("//webview", "StandardWebView", "Standard WebView fixture");
        await using var fixture = new WebViewHttpFixture();
        try
        {
            Assert.True(await Client.NavigateWebViewAsync(fixture.GetUrl(Platform), await GetContextIdAsync("StandardWebView")));
            await WaitForDocumentAsync("StandardWebView", "HTTP WebView fixture");
            Assert.True(await Client.NavigateWebViewAsync("second.html", await GetContextIdAsync("StandardWebView")));
            await WaitForDocumentAsync("StandardWebView", "Second WebView document");
            Assert.Equal("Second document loaded",
                (await EvaluateAsync("document.querySelector('#second-document').textContent", "StandardWebView")).GetString());
        }
        finally
        {
            var reset = await FindElementAsync("ResetStandardWebViewButton");
            Assert.True(await Client.TapAsync(reset.Id));
            await WaitForDocumentAsync("StandardWebView", "Standard WebView fixture");
        }
    }

    async Task EnsureHostAsync(string route, string id, string title)
    {
        await NavigateToPageAsync(route, id);
        await WaitForDocumentAsync(id, title);
    }

    async Task WaitForDocumentAsync(string id, string title)
    {
        await WaitForAsync(async () =>
        {
            var registered = await Client.GetCdpWebViewsAsync();
            if (!registered.GetProperty("webviews").EnumerateArray().Any(c =>
                    c.GetProperty("automationId").GetString() == id && c.GetProperty("ready").GetBoolean()))
                return false;
            try
            {
                return (await EvaluateAsync("document.title", id)).GetString() == title;
            }
            catch (InvalidOperationException ex) when (
                ex.Message.Contains("not ready", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("initialization", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }, timeoutMs: 60000);
    }

    async Task<JsonElement> EvaluateAsync(string expression, string? context = null)
    {
        if (context is not null && !context.StartsWith("webview-", StringComparison.Ordinal))
            context = await GetContextIdAsync(context);
        var result = await Client.SendCdpCommandAsync("Runtime.evaluate",
            new JsonObject { ["expression"] = expression, ["returnByValue"] = true }, context);
        return result.GetProperty("result").GetProperty("result").GetProperty("value").Clone();
    }

    async Task<string> GetContextIdAsync(string automationId)
    {
        var registered = await Client.GetCdpWebViewsAsync();
        var context = Assert.Single(registered.GetProperty("webviews").EnumerateArray(),
            c => c.GetProperty("active").GetBoolean() && c.GetProperty("automationId").GetString() == automationId);
        return context.GetProperty("id").GetString()!;
    }
}
