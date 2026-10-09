using System.Text.Json;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace Microsoft.Maui.DevFlow.Tests;

public class CdpWebViewActionTests
{
    [Fact]
    public async Task EvaluateWebViewExpressionAsync_ObjectResult_EvaluatesMutationOnce()
    {
        using var service = new TestService();
        var evaluations = 0;
        var webView = new CdpWebViewInfo
        {
            CommandHandler = command =>
            {
                evaluations++;
                using var document = JsonDocument.Parse(command);
                var expression = document.RootElement.GetProperty("params").GetProperty("expression").GetString()!;
                return Task.FromResult(expression.StartsWith("JSON.stringify(", StringComparison.Ordinal)
                    ? """{"result":{"result":{"type":"string","value":"{\"success\":true}"}}}"""
                    : """{"result":{"result":{"type":"object","objectId":"object-1"}}}""");
            }
        };

        var result = await service.EvaluateAsync(webView, "(() => { button.click(); return { success: true }; })()");

        Assert.Equal(1, evaluations);
        Assert.True(result!.Value.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task EvaluateWebViewExpressionAsync_JavaScriptException_IsNotSuccess()
    {
        using var service = new TestService();
        var webView = new CdpWebViewInfo
        {
            CommandHandler = _ => Task.FromResult(
                """{"result":{"exceptionDetails":{"text":"Uncaught","exception":{"description":"ReferenceError: missing"}}}}""")
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.EvaluateAsync(webView, "missing.click()"));

        Assert.Contains("ReferenceError", error.Message);
    }

    [Fact]
    public void ResolveCdpWebView_NoActiveHost_DoesNotSelectHiddenReadyBridge()
    {
        using var service = new TestService();
        service.RegisterCdpWebView(_ => Task.FromResult("{}"), () => true, "hidden");

        Assert.Null(service.Resolve(new HashSet<string>()));
        Assert.NotNull(service.Resolve(new HashSet<string> { "hidden" }));
    }

    [Fact]
    public void ResolveCdpWebView_DuplicateAutomationIds_UsesOnlyCanonicalContextIds()
    {
        using var service = new TestService();
        var first = service.RegisterCdpWebView(
            _ => Task.FromResult("{}"), () => true, "MainWebView", "MainWebView", null, "webview", new object());
        var second = service.RegisterCdpWebView(
            _ => Task.FromResult("{}"), () => true, "MainWebView", "MainWebView:window:1", null, "webview", new object());

        Assert.Null(service.Resolve("MainWebView"));
        Assert.Null(service.Resolve("MainWebView:window:1"));
        Assert.Null(service.Resolve(first.ToString()));
        Assert.Null(service.Resolve($"webview-0{first}"));
        Assert.Null(service.Resolve($"WebView-{first}"));
        Assert.Equal(first, service.Resolve($"webview-{first}")!.Index);
        Assert.Equal(second, service.Resolve($"webview-{second}")!.Index);
    }

    [Fact]
    public void ResolveCdpWebView_OldOwnerWithSameElementId_DoesNotBecomeActive()
    {
        using var service = new TestService();
        var oldOwner = new object();
        var currentOwner = new object();
        var oldIndex = service.RegisterCdpWebView(
            _ => Task.FromResult("{}"), () => true, "BlazorWebView", "BlazorWebView", null, "blazor", oldOwner);
        var currentIndex = service.RegisterCdpWebView(
            _ => Task.FromResult("{}"), () => true, "BlazorWebView", "BlazorWebView", null, "blazor", currentOwner);

        Assert.Equal(currentIndex, service.Resolve(
            new HashSet<string> { "BlazorWebView", $"webview-{currentIndex}" })!.Index);
        Assert.NotEqual(oldIndex, currentIndex);
    }

    private sealed class TestService : DevFlowAgentService
    {
        protected override bool SupportsActiveCdpWebViewResolution => true;

        public Task<JsonElement?> EvaluateAsync(CdpWebViewInfo webView, string expression)
            => EvaluateWebViewExpressionAsync(webView, expression);

        public CdpWebViewInfo? Resolve(HashSet<string> active)
            => ResolveCdpWebView(null, active);

        public CdpWebViewInfo? Resolve(string id) => ResolveCdpWebView(id);
    }
}
