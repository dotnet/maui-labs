using System.Text.Json;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace Microsoft.Maui.DevFlow.Tests;

public class WebViewErrorTests
{
    [Theory]
    [InlineData("""{"result":{"exceptionDetails":{"text":"Uncaught","exception":{"description":"ReferenceError: missing"}}}}""", "ReferenceError: missing")]
    [InlineData("""{"result":{"exceptionDetails":{"text":"SyntaxError: invalid selector"}}}""", "invalid selector")]
    [InlineData("""{"result":{"exceptionDetails":null}}""", "JavaScript exception")]
    [InlineData("""{"error":{"message":"Method unimplemented"}}""", "Method unimplemented")]
    [InlineData("""{"error":""}""", "CDP error")]
    [InlineData("""{"success":false}""", "failed")]
    [InlineData("[]", "Invalid CDP response")]
    public async Task Evaluate_Failure_ThrowsWithoutReplay(string response, string expected)
    {
        using var service = new TestService();
        var commands = new List<string>();
        var webView = new CdpWebViewInfo
        {
            CommandHandler = command => { commands.Add(command); return Task.FromResult(response); }
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.EvaluateAsync(webView, "missing.click()"));

        Assert.Contains(expected, error.Message);
        Assert.Single(commands);
    }

    [Theory]
    [InlineData("evaluate")]
    [InlineData("click")]
    [InlineData("fill")]
    [InlineData("query")]
    [InlineData("source")]
    public async Task Handler_StackOnlyDescription_ReadsOriginalErrorMessage(string action)
    {
        using var service = new TestService();
        var methods = new List<string>();
        service.RegisterCdpWebView(command =>
        {
            using var document = JsonDocument.Parse(command);
            var method = document.RootElement.GetProperty("method").GetString()!;
            methods.Add(method);
            if (method == "Runtime.getProperties")
            {
                Assert.Equal("error-1", document.RootElement.GetProperty("params").GetProperty("objectId").GetString());
                return Task.FromResult("""{"result":{"result":[{"name":"message","value":{"value":"Can't find variable: missing"}}]}}""");
            }
            return Task.FromResult("""{"result":{"exceptionDetails":{"text":"Uncaught","exception":{"description":"eval code@\napp://stack","className":"ReferenceError","objectId":"error-1"}}}}""");
        }, () => true);

        var response = await service.ActionAsync(action);

        Assert.Equal(action == "evaluate" ? 200 : 400, response.StatusCode);
        using var body = JsonDocument.Parse(Assert.IsType<string>(response.Body));
        var message = action == "evaluate"
            ? body.RootElement.GetProperty("result").GetProperty("exceptionDetails").GetProperty("exception").GetProperty("description").GetString()
            : body.RootElement.GetProperty("error").GetString();
        Assert.Contains("ReferenceError: Can't find variable: missing", message);
        Assert.Equal(new[] { "Runtime.evaluate", "Runtime.getProperties" }, methods);
    }

    [Theory]
    [InlineData("""{"error":{"message":"Properties unavailable"}}""", "Properties unavailable")]
    [InlineData("[]", "Invalid CDP response")]
    [InlineData("{}", "Invalid error-properties response")]
    [InlineData("""{"result":{"result":[]}}""", "no message")]
    [InlineData("<html>bridge failed</html>", "invalid start")]
    public async Task ErrorDetails_Failure_PreservesOriginalErrorWithoutReplay(string propertiesResponse, string expected)
    {
        using var service = new TestService();
        var methods = new List<string>();
        var webView = new CdpWebViewInfo
        {
            CommandHandler = command =>
            {
                using var document = JsonDocument.Parse(command);
                var method = document.RootElement.GetProperty("method").GetString()!;
                methods.Add(method);
                return Task.FromResult(method == "Runtime.getProperties" ? propertiesResponse
                    : """{"result":{"exceptionDetails":{"text":"Uncaught","exception":{"description":"original-stack","objectId":"error-1"}}}}""");
            }
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.EvaluateAsync(webView, "missing.click()"));

        Assert.Contains("original-stack", error.Message);
        Assert.Contains("error details failed", error.Message);
        Assert.Contains(expected, error.Message);
        Assert.Equal(new[] { "Runtime.evaluate", "Runtime.getProperties" }, methods);
    }

    [Fact]
    public async Task ErrorDetails_TransportFailure_PreservesOriginalErrorWithoutReplay()
    {
        using var service = new TestService();
        var calls = 0;
        var webView = new CdpWebViewInfo
        {
            CommandHandler = _ => ++calls == 1
                ? Task.FromResult("""{"result":{"exceptionDetails":{"text":"Uncaught","exception":{"description":"original-stack","objectId":"error-1"}}}}""")
                : throw new HttpRequestException("Bridge disconnected")
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.EvaluateAsync(webView, "missing.click()"));

        Assert.Contains("original-stack", error.Message);
        Assert.Contains("Bridge disconnected", error.Message);
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData("click")]
    [InlineData("fill")]
    [InlineData("query")]
    [InlineData("source")]
    public async Task Handler_JavaScriptFailure_ReturnsUsefulFailure(string action)
    {
        using var service = new TestService();
        service.RegisterCdpWebView(_ => Task.FromResult(
            """{"result":{"exceptionDetails":{"text":"Uncaught","exception":{"description":"SyntaxError: invalid selector"}}}}"""), () => true);

        var response = await service.ActionAsync(action);

        Assert.Equal(400, response.StatusCode);
        Assert.Contains("SyntaxError: invalid selector", response.Body);
    }

    [Fact]
    public async Task RawCdp_Success_PreservesOriginalExpressionAndValue()
    {
        using var service = new TestService();
        service.RegisterCdpWebView(command =>
        {
            using var document = JsonDocument.Parse(command);
            Assert.Equal("1+1", document.RootElement.GetProperty("params").GetProperty("expression").GetString());
            return Task.FromResult("""{"result":{"result":{"value":2}}}""");
        }, () => true);

        var response = await service.RawCdpAsync(new HttpRequest
        {
            Body = """{"method":"Runtime.evaluate","params":{"expression":"1+1"}}"""
        });

        Assert.Equal(200, response.StatusCode);
        using var body = JsonDocument.Parse(Assert.IsType<string>(response.Body));
        Assert.Equal(2, body.RootElement.GetProperty("result").GetProperty("result").GetProperty("value").GetInt32());
    }

    private sealed class TestService : DevFlowAgentService
    {
        public Task<JsonElement?> EvaluateAsync(CdpWebViewInfo webView, string expression)
            => EvaluateWebViewExpressionAsync(webView, expression);

        public Task<HttpResponse> RawCdpAsync(HttpRequest request) => HandleCdp(request);

        public Task<HttpResponse> ActionAsync(string action)
        {
            var request = new HttpRequest
            {
                Body = action == "evaluate" ? """{"method":"Runtime.evaluate","params":{"expression":"missing.click()"}}"""
                    : """{"selector":"[","text":"hello"}"""
            };
            return action switch
            {
                "evaluate" => HandleCdp(request),
                "click" => HandleWebViewInputClick(request),
                "fill" => HandleWebViewInputFill(request),
                "query" => HandleWebViewDomQuery(request),
                "source" => HandleCdpSource(request),
                _ => throw new ArgumentOutOfRangeException(nameof(action))
            };
        }
    }
}
