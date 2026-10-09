using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace Microsoft.Maui.DevFlow.Tests;

public class CdpWebViewActionTests
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
                var expression = document.RootElement.GetProperty("params").GetProperty("expression").GetString()!;
                Assert.StartsWith("JSON.stringify(", expression);
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
    [InlineData("""{"result":{"exceptionDetails":{"text":"Uncaught","exception":{"description":"ReferenceError: missing"}}}}""", "ReferenceError: missing")]
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
            () => service.EvaluateAsync(webView, "missing.click()"));

        Assert.Contains(expected, error.Message);
        Assert.Equal(1, evaluations);
    }

    [Fact]
    public async Task JavaScriptError_StackOnlyDescription_ReadsOriginalMessageWithoutReplay()
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
                if (method == "Runtime.getProperties")
                {
                    Assert.Equal("error-1", document.RootElement.GetProperty("params").GetProperty("objectId").GetString());
                    return Task.FromResult("""{"result":{"result":[{"name":"message","value":{"value":"Can't find variable: missing"}}]}}""");
                }
                return Task.FromResult("""{"result":{"exceptionDetails":{"text":"Uncaught","exception":{"description":"eval code@\napp://stack","className":"ReferenceError","objectId":"error-1"}}}}""");
            }
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.EvaluateAsync(webView, "missing.click()"));

        Assert.Contains("ReferenceError: Can't find variable: missing", error.Message);
        Assert.Equal(new[] { "Runtime.evaluate", "Runtime.getProperties" }, methods);
    }

    [Theory]
    [InlineData("click")]
    [InlineData("fill")]
    [InlineData("query")]
    public async Task InputAndQuery_UsesSelectedBridgeAndSingleEvaluation(string action)
    {
        using var service = new TestService();
        service.RegisterCdpWebView(_ => throw new InvalidOperationException("Wrong bridge"), () => true, "Left");
        var evaluations = 0;
        service.RegisterCdpWebView(command =>
        {
            evaluations++;
            using var document = JsonDocument.Parse(command);
            Assert.StartsWith("JSON.stringify(", document.RootElement.GetProperty("params").GetProperty("expression").GetString()!);
            return Task.FromResult(action == "query"
                ? """{"result":{"result":{"value":"[{\"index\":0,\"tagName\":\"button\"}]"}}}"""
                : """{"result":{"result":{"value":"{\"success\":true}"}}}""");
        }, () => true, "Right");

        var response = await service.ActionAsync(action, new HttpRequest
        {
            Body = """{"selector":"#target","text":"hello","contextId":"Right"}"""
        });

        Assert.Equal(200, response.StatusCode);
        Assert.Equal(1, evaluations);
    }

    [Theory]
    [InlineData("click")]
    [InlineData("fill")]
    [InlineData("query")]
    [InlineData("source")]
    public async Task Action_JavaScriptException_ReturnsFailureWithDescription(string action)
    {
        using var service = new TestService();
        service.RegisterCdpWebView(_ => Task.FromResult(
            """{"result":{"exceptionDetails":{"text":"Uncaught","exception":{"description":"SyntaxError: invalid selector"}}}}"""), () => true);

        var response = await service.ActionAsync(action, new HttpRequest { Body = """{"selector":"[","text":"hello"}""" });

        Assert.Equal(400, response.StatusCode);
        Assert.Contains("SyntaxError: invalid selector", response.Body);
    }

    [Fact]
    public async Task WebViewNetwork_IsUnsupportedAndNotAdvertised()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        using var service = new TestService(new AgentOptions { Port = port });
        service.RegisterCdpWebView(_ => Task.FromResult("{}"), () => true);

        var response = await service.NetworkAsync();
        Assert.Equal(501, response.StatusCode);
        using var body = JsonDocument.Parse(Assert.IsType<string>(response.Body));
        Assert.Equal("not_supported", body.RootElement.GetProperty("error").GetString());
        Assert.Equal("webview.network", body.RootElement.GetProperty("capability").GetString());
        service.StartServerOnly(dispatcher: null);
        using var http = new HttpClient();
        using var document = JsonDocument.Parse(await http.GetStringAsync($"http://localhost:{port}/api/v1/agent/capabilities"));
        Assert.DoesNotContain("network", document.RootElement.GetProperty("capabilities")
            .GetProperty("webview").GetProperty("features").EnumerateArray().Select(feature => feature.GetString()));
    }

    private sealed class TestService : DevFlowAgentService
    {
        public TestService(AgentOptions? options = null) : base(options) { }

        public Task<JsonElement?> EvaluateAsync(CdpWebViewInfo webView, string expression)
            => EvaluateWebViewExpressionAsync(webView, expression);

        public Task<HttpResponse> ActionAsync(string action, HttpRequest request) => action switch
        {
            "click" => HandleWebViewInputClick(request),
            "fill" => HandleWebViewInputFill(request),
            "query" => HandleWebViewDomQuery(request),
            "source" => HandleCdpSource(request),
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
        public Task<HttpResponse> NetworkAsync() => HandleWebViewNetwork(new HttpRequest());
    }
}
