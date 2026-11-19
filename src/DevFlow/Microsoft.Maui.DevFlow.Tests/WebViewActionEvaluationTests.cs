using System.Text.Json;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace Microsoft.Maui.DevFlow.Tests;

public class WebViewActionEvaluationTests
{
    [Theory]
    [InlineData("{\"success\":true}")]
    [InlineData("[{\"index\":0,\"tagName\":\"button\"}]")]
    [InlineData("\"text\"")]
    [InlineData("null")]
    public async Task EvaluateWebViewExpressionAsync_StructuredResult_EvaluatesOnce(string serializedResult)
    {
        using var service = new TestService();
        var evaluations = 0;
        var webView = new CdpWebViewInfo
        {
            CommandHandler = command =>
            {
                evaluations++;
                using var document = JsonDocument.Parse(command);
                Assert.Equal("Runtime.evaluate", document.RootElement.GetProperty("method").GetString());
                var parameters = document.RootElement.GetProperty("params");
                Assert.True(parameters.GetProperty("returnByValue").GetBoolean());
                Assert.Equal("JSON.stringify(((() => { button.click(); return { success: true }; })()))",
                    parameters.GetProperty("expression").GetString());
                return Task.FromResult(JsonSerializer.Serialize(new
                {
                    result = new { result = new { type = "string", value = serializedResult } }
                }));
            }
        };

        var result = await service.EvaluateAsync(webView, "(() => { button.click(); return { success: true }; })()");

        Assert.Equal(1, evaluations);
        Assert.Equal(serializedResult, result!.Value.GetRawText());
    }

    [Theory]
    [InlineData("""{"error":{"message":"Method unimplemented"}}""", "Method unimplemented")]
    [InlineData("""{"result":{"result":{"type":"object","objectId":"object-1"}}}""", "serialized result")]
    public async Task EvaluateWebViewExpressionAsync_Failure_IsNotReplayed(string response, string expected)
    {
        using var service = new TestService();
        var evaluations = 0;
        var webView = new CdpWebViewInfo
        {
            CommandHandler = _ => { evaluations++; return Task.FromResult(response); }
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.EvaluateAsync(webView, "button.click()"));

        Assert.Contains(expected, error.Message);
        Assert.Equal(1, evaluations);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not JSON")]
    public async Task EvaluateWebViewExpressionAsync_InvalidSerialization_FailsWithoutReplay(string serializedResult)
    {
        using var service = new TestService();
        var evaluations = 0;
        var webView = new CdpWebViewInfo
        {
            CommandHandler = _ =>
            {
                evaluations++;
                return Task.FromResult(JsonSerializer.Serialize(new
                {
                    result = new { result = new { type = "string", value = serializedResult } }
                }));
            }
        };

        await Assert.ThrowsAnyAsync<JsonException>(() => service.EvaluateAsync(webView, "button.click()"));
        Assert.Equal(1, evaluations);
    }

    [Theory]
    [InlineData("click")]
    [InlineData("fill")]
    [InlineData("query")]
    public async Task Action_ObjectReferenceBridge_ExecutesOnceAndReturnsStructuredResult(string action)
    {
        using var service = new TestService();
        var evaluations = 0;
        var serializedResult = action == "query"
            ? """[{"index":0,"tagName":"button"}]"""
            : """{"success":true}""";
        service.RegisterCdpWebView(command =>
        {
            evaluations++;
            using var document = JsonDocument.Parse(command);
            Assert.Equal("Runtime.evaluate", document.RootElement.GetProperty("method").GetString());
            var expression = document.RootElement.GetProperty("params").GetProperty("expression").GetString()!;
            Assert.Contains(action switch
            {
                "click" => "el.click()",
                "fill" => "el.dispatchEvent(new Event('input'",
                _ => "document.querySelectorAll("
            }, expression);

            // Model a bridge that returns references for objects even with returnByValue.
            return Task.FromResult(expression.StartsWith("JSON.stringify(", StringComparison.Ordinal)
                ? JsonSerializer.Serialize(new { result = new { result = new { type = "string", value = serializedResult } } })
                : """{"result":{"result":{"type":"object","objectId":"object-1"}}}""");
        }, () => true);

        var response = await service.ActionAsync(action, new HttpRequest
        {
            Body = """{"selector":"#target","text":"hello"}"""
        });

        Assert.Equal(200, response.StatusCode);
        Assert.Equal(serializedResult, response.Body);
        Assert.Equal(1, evaluations);
    }

    private sealed class TestService : DevFlowAgentService
    {
        public Task<JsonElement?> EvaluateAsync(CdpWebViewInfo webView, string expression)
            => EvaluateWebViewExpressionAsync(webView, expression);

        public Task<HttpResponse> ActionAsync(string action, HttpRequest request) => action switch
        {
            "click" => HandleWebViewInputClick(request),
            "fill" => HandleWebViewInputFill(request),
            "query" => HandleWebViewDomQuery(request),
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
    }
}
