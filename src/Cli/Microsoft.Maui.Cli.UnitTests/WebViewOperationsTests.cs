using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow;
using Microsoft.Maui.Cli.UnitTests.Fixtures;
using Microsoft.Maui.DevFlow.Driver;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

[Collection("CLI")]
public class WebViewOperationsTests
{
    [Theory]
    [InlineData("""{"result":{"result":{"value":"hello\n'world'\\"}}}""", "hello\n'world'\\")]
    [InlineData("""{"result":{"result":{"value":2}}}""", "2")]
    [InlineData("""{"result":{"result":{"value":true}}}""", "True")]
    [InlineData("""{"result":{"result":{"value":null}}}""", "")]
    [InlineData("""{"result":{"result":{"value":{"answer":42}}}}""", "{\n  \"answer\": 42\n}")]
    [InlineData("""{"result":{"result":{"value":[1,2]}}}""", "[\n  1,\n  2\n]")]
    [InlineData("""{"result":{"exceptionDetails":{"text":"ReferenceError"}}}""", "Error: ReferenceError")]
    [InlineData("""{"result":{"exceptionDetails":{}}}""", "Error: Unknown error")]
    [InlineData("""{"result":{"value":"raw chobitsu"}}""", "raw chobitsu")]
    [InlineData("""{"result":{"value":null}}""", "")]
    [InlineData("""{"error":{"message":"CDP unavailable"}}""", """{"error":{"message":"CDP unavailable"}}""")]
    [InlineData("""{"result":{"result":{"type":"undefined"}}}""", """{"result":{"result":{"type":"undefined"}}}""")]
    public async Task EvaluateAsync_ResponseShapes_PreserveExistingDecoding(string response, string expected)
    {
        await using var server = new MockAgentServer { WebViewEvaluateResponse = response };
        await server.StartAsync();
        using var client = new AgentClient("localhost", server.Port);
        var operations = new WebViewOperations(client);

        var actual = await operations.EvaluateAsync("fixtureExpression", "Right & pane");

        Assert.Equal(expected, actual.Replace("\r\n", "\n"));
        var request = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/webview/evaluate");
        Assert.Equal("?webview=Right%20%26%20pane", request.QueryString);
        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal("Runtime.evaluate", body.RootElement.GetProperty("method").GetString());
        Assert.Equal("fixtureExpression", body.RootElement.GetProperty("params").GetProperty("expression").GetString());
        Assert.True(body.RootElement.GetProperty("params").GetProperty("returnByValue").GetBoolean());
    }

    [Fact]
    public async Task EvaluateAsync_RawNonStringValue_PreservesExistingFailure()
    {
        await using var server = new MockAgentServer { WebViewEvaluateResponse = """{"result":{"value":2}}""" };
        await server.StartAsync();
        using var client = new AgentClient("localhost", server.Port);

        await Assert.ThrowsAsync<InvalidOperationException>(() => new WebViewOperations(client).EvaluateAsync("2"));
    }

    [Fact]
    public async Task EvaluateAsync_InvalidJson_PropagatesToCaller()
    {
        await using var server = new MockAgentServer { WebViewEvaluateResponse = "not JSON" };
        await server.StartAsync();
        using var client = new AgentClient("localhost", server.Port);

        await Assert.ThrowsAnyAsync<JsonException>(() => new WebViewOperations(client).EvaluateAsync("2"));
    }

    [Theory]
    [InlineData("Browser.getVersion")]
    [InlineData("DOM.getDocument")]
    [InlineData("Page.navigate")]
    [InlineData("Page.reload")]
    [InlineData("Input.insertText")]
    public async Task RawOperations_PreserveMethodParametersAndTarget(string method)
    {
        await using var server = new MockAgentServer();
        await server.StartAsync();
        using var client = new AgentClient("localhost", server.Port);
        var operations = new WebViewOperations(client);
        const string value = "https://example.test/'quoted'?value=\"a\"\\b\n";

        switch (method)
        {
            case "Browser.getVersion":
                var version = await operations.BrowserGetVersionAsync("Right");
                Assert.Equal("Chrome/120.0", version!.Value.GetProperty("result").GetProperty("product").GetString());
                break;
            case "DOM.getDocument":
                var document = await operations.DomGetDocumentAsync("Right");
                Assert.Equal("#document", document!.Value.GetProperty("result").GetProperty("root").GetProperty("nodeName").GetString());
                break;
            case "Page.navigate":
                await operations.PageNavigateAsync(value, "Right");
                break;
            case "Page.reload":
                await operations.PageReloadAsync("Right");
                break;
            case "Input.insertText":
                await operations.InputInsertTextAsync(value, "Right");
                break;
        }

        var request = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/webview/evaluate");
        Assert.Equal("POST", request.Method);
        Assert.Equal("?webview=Right", request.QueryString);
        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal(method, body.RootElement.GetProperty("method").GetString());
        if (method is "Page.navigate" or "Input.insertText")
            Assert.Equal(value, body.RootElement.GetProperty("params").GetProperty(method == "Page.navigate" ? "url" : "text").GetString());
        else
            Assert.False(body.RootElement.TryGetProperty("params", out _));
    }

    [Fact]
    public async Task DomGetDocumentAsync_NoTarget_PreservesDefaultSelection()
    {
        await using var server = new MockAgentServer();
        await server.StartAsync();
        using var client = new AgentClient("localhost", server.Port);
        var result = await new WebViewOperations(client).DomGetDocumentAsync();

        Assert.True(result!.Value.GetProperty("result").TryGetProperty("root", out _));
        var request = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/webview/evaluate");
        Assert.Equal("", request.QueryString);
        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal("DOM.getDocument", body.RootElement.GetProperty("method").GetString());
        Assert.False(body.RootElement.TryGetProperty("params", out _));
    }

    [Theory]
    [InlineData("""{"result":{"data":"cG5n"}}""", "cG5n")]
    [InlineData("""{"result":{"data":null}}""", null)]
    [InlineData("""{"result":{}}""", "{\n  \"result\": {}\n}")]
    public async Task PageCaptureScreenshotAsync_DecodesDataOrPreservesFallback(string response, string? expected)
    {
        await using var server = new MockAgentServer { WebViewEvaluateResponse = response };
        await server.StartAsync();
        using var client = new AgentClient("localhost", server.Port);

        var actual = await new WebViewOperations(client).PageCaptureScreenshotAsync("1");

        Assert.Equal(expected, actual?.Replace("\r\n", "\n"));
        var request = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/webview/evaluate");
        Assert.Equal("?webview=1", request.QueryString);
        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal("Page.captureScreenshot", body.RootElement.GetProperty("method").GetString());
    }

    [Theory]
    [InlineData("""{"cdpReady":true,"cdpWebViewCount":2}""", true, 2)]
    [InlineData("{}", false, 0)]
    public async Task GetStatusAsync_ReadsLegacyFieldsWithoutCdpOrLeaseCalls(string response, bool ready, int count)
    {
        await using var server = new MockAgentServer { AgentStatusResponse = response };
        await server.StartAsync();
        using var client = new AgentClient("localhost", server.Port);

        var result = await new WebViewOperations(client).GetStatusAsync();

        Assert.Equal((ready, count), result);
        Assert.Equal("/api/v1/agent/status", Assert.Single(server.RecordedRequests).Path);
    }

    [Fact]
    public async Task StatusCommand_PreservesDirectConnectionWithoutBrokerPreparation()
    {
        await using var server = new MockAgentServer
        {
            AgentStatusResponse = """{"cdpReady":true,"cdpWebViewCount":2}"""
        };
        await server.StartAsync();
        var cli = new CliTestHarness(server.Port);
        var resolveBroker = DevFlowCommands.ResolveRunningBrokerPortAsync;
        DevFlowCommands.ResolveRunningBrokerPortAsync = () => throw new InvalidOperationException("Status must not discover broker labels or forwarding.");
        try
        {
            var result = await cli.InvokeAsync("devflow", "webview", "status", "--webview", "Right");

            Assert.Equal(0, result.ExitCode);
            Assert.Equal("Connected: CDP ready (2 WebViews)", result.StdOut);
            Assert.Equal("", result.StdErr);
            Assert.Equal("/api/v1/agent/status", Assert.Single(server.RecordedRequests).Path);
        }
        finally
        {
            DevFlowCommands.ResolveRunningBrokerPortAsync = resolveBroker;
        }
    }

    [Fact]
    public async Task ConvenienceOperations_ReturnDataAndPreserveSourceSelection()
    {
        await using var server = new MockAgentServer();
        await server.StartAsync();
        using var client = new AgentClient("localhost", server.Port);
        var operations = new WebViewOperations(client);

        var contexts = await operations.GetWebViewsAsync();
        var source = await operations.GetSourceAsync("el-right & pane");

        Assert.Equal("webview-1", Assert.Single(contexts.EnumerateArray()).GetProperty("id").GetString());
        Assert.Equal(MockAgentResponses.WebViewSource, source);
        var sourceRequest = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/webview/source");
        Assert.Equal("?webview=el-right%20%26%20pane", sourceRequest.QueryString);
        Assert.DoesNotContain(server.RecordedRequests, r => r.Path == "/api/v1/agent/lease");
    }

    [Theory]
    [InlineData("click")]
    [InlineData("fill")]
    public async Task InputOperations_ErrorText_RemainsAResult(string action)
    {
        await using var server = new MockAgentServer
        {
            WebViewEvaluateResponse = """{"result":{"result":{"value":"Error: Element not found"}}}"""
        };
        await server.StartAsync();
        using var client = new AgentClient("localhost", server.Port);
        var operations = new WebViewOperations(client);

        var result = action == "click"
            ? await operations.InputDispatchClickAsync("#missing")
            : await operations.InputFillAsync("#missing", "text");

        Assert.Equal("Error: Element not found", result);
        Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/webview/evaluate");
    }
}
