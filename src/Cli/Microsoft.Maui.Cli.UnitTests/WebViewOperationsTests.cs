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
    [InlineData("evaluate")]
    [InlineData("version")]
    [InlineData("document")]
    [InlineData("query")]
    [InlineData("queryAll")]
    [InlineData("html")]
    [InlineData("navigate")]
    [InlineData("reload")]
    [InlineData("screenshot")]
    [InlineData("click")]
    [InlineData("text")]
    [InlineData("fill")]
    [InlineData("status")]
    [InlineData("contexts")]
    [InlineData("source")]
    [InlineData("snapshot")]
    public async Task AllOperations_UseCanonicalContextOutsideCommandPayload(string operation)
    {
        await using var server = new MockAgentServer();
        await server.StartAsync();
        using var client = new AgentClient("localhost", server.Port) { AutoAcquireMutationLease = false };
        var operations = new WebViewOperations(client);
        using var listed = JsonDocument.Parse(MockAgentResponses.WebViews);
        var contextId = Assert.Single(listed.RootElement.GetProperty("webviews").EnumerateArray())
            .GetProperty("id").GetString()!;

        switch (operation)
        {
            case "evaluate": await operations.EvaluateAsync("1+1", contextId: contextId); break;
            case "version": await operations.BrowserGetVersionAsync(contextId: contextId); break;
            case "document": await operations.DomGetDocumentAsync(contextId: contextId); break;
            case "query": await operations.DomQuerySelectorAsync("#target", contextId: contextId); break;
            case "queryAll": await operations.DomQuerySelectorAllAsync("#target", contextId: contextId); break;
            case "html": await operations.DomGetOuterHtmlAsync("#target", contextId: contextId); break;
            case "navigate": await operations.PageNavigateAsync("/next", contextId: contextId); break;
            case "reload": await operations.PageReloadAsync(contextId: contextId); break;
            case "screenshot": await operations.PageCaptureScreenshotAsync(contextId: contextId); break;
            case "click": await operations.InputDispatchClickAsync("#target", contextId: contextId); break;
            case "text": await operations.InputInsertTextAsync("text", contextId: contextId); break;
            case "fill": await operations.InputFillAsync("#target", "text", contextId: contextId); break;
            case "status": Assert.Equal((true, 1), await operations.GetStatusAsync(contextId: contextId)); break;
            case "contexts": await operations.GetWebViewsAsync(); break;
            case "source": await operations.GetSourceAsync(contextId: contextId); break;
            case "snapshot": await operations.SnapshotAsync(contextId: contextId); break;
            default: throw new ArgumentOutOfRangeException(nameof(operation));
        }

        var request = Assert.Single(server.RecordedRequests);
        Assert.Equal(operation switch
        {
            "screenshot" => "/api/v1/webview/screenshot",
            "source" => "/api/v1/webview/source",
            "status" or "contexts" => "/api/v1/webview/contexts",
            _ => "/api/v1/webview/evaluate"
        }, request.Path);
        Assert.Equal(operation is "status" or "contexts" ? "" : $"?contextId={contextId}", request.QueryString);
        if (request.Body is not null)
        {
            using var body = JsonDocument.Parse(request.Body);
            Assert.False(body.RootElement.TryGetProperty("contextId", out _));
            Assert.False(body.RootElement.TryGetProperty("webviewId", out _));
        }
        Assert.DoesNotContain(server.RecordedRequests, request => request.Path.StartsWith("/api/v1/webview/input/", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("1")]
    [InlineData("Main")]
    [InlineData("main-element")]
    [InlineData("webview-01")]
    public async Task GetStatusAsync_InvalidExplicitTarget_DoesNotUseDefaultOrDispatchCdp(string contextId)
    {
        await using var server = new MockAgentServer();
        await server.StartAsync();
        using var client = new AgentClient("localhost", server.Port);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new WebViewOperations(client).GetStatusAsync(contextId));

        Assert.Contains("not found", error.Message);
        Assert.Equal("/api/v1/webview/contexts", Assert.Single(server.RecordedRequests).Path);
    }

    [Theory]
    [InlineData("""{"result":{"result":{"value":"hello\n'world'\\"}}}""", "hello\n'world'\\")]
    [InlineData("""{"result":{"result":{"value":2}}}""", "2")]
    [InlineData("""{"result":{"result":{"value":true}}}""", "True")]
    [InlineData("""{"result":{"result":{"value":null}}}""", "")]
    [InlineData("""{"result":{"result":{"value":{"answer":42}}}}""", "{\n  \"answer\": 42\n}")]
    [InlineData("""{"result":{"result":{"value":[1,2]}}}""", "[\n  1,\n  2\n]")]
    [InlineData("""{"result":{"value":"raw chobitsu"}}""", "raw chobitsu")]
    [InlineData("""{"result":{"value":null}}""", "")]
    [InlineData("""{"result":{"result":{"type":"undefined"}}}""", """{"result":{"result":{"type":"undefined"}}}""")]
    public async Task EvaluateAsync_ResponseShapes_PreserveExistingDecoding(string response, string expected)
    {
        await using var server = new MockAgentServer { WebViewEvaluateResponse = response };
        await server.StartAsync();
        using var client = new AgentClient("localhost", server.Port);
        var operations = new WebViewOperations(client);

        var actual = await operations.EvaluateAsync("fixtureExpression", contextId: "webview-1");

        Assert.Equal(expected, actual.Replace("\r\n", "\n"));
        var request = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/webview/evaluate");
        Assert.Equal("?contextId=webview-1", request.QueryString);
        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal("Runtime.evaluate", body.RootElement.GetProperty("method").GetString());
        Assert.Equal("fixtureExpression", body.RootElement.GetProperty("params").GetProperty("expression").GetString());
        Assert.True(body.RootElement.GetProperty("params").GetProperty("returnByValue").GetBoolean());
    }

    [Theory]
    [InlineData("""{"result":{"exceptionDetails":{"text":"ReferenceError"}}}""", "ReferenceError")]
    [InlineData("""{"result":{"exceptionDetails":{}}}""", "Runtime.evaluate' threw: {}")]
    [InlineData("""{"error":{"message":"CDP unavailable"}}""", "CDP unavailable")]
    public async Task EvaluateAsync_FailedResponse_ThrowsWithoutReplayingExpression(string response, string expected)
    {
        await using var server = new MockAgentServer { WebViewEvaluateResponse = response };
        await server.StartAsync();
        using var client = new AgentClient("localhost", server.Port);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new WebViewOperations(client).EvaluateAsync("fixtureExpression", contextId: "webview-1"));

        Assert.Contains(expected, error.Message);
        Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/webview/evaluate");
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
                var version = await operations.BrowserGetVersionAsync(contextId: "webview-1");
                Assert.Equal("Chrome/120.0", version!.Value.GetProperty("result").GetProperty("product").GetString());
                break;
            case "DOM.getDocument":
                var document = await operations.DomGetDocumentAsync(contextId: "webview-1");
                Assert.Equal("#document", document!.Value.GetProperty("result").GetProperty("root").GetProperty("nodeName").GetString());
                break;
            case "Page.navigate":
                await operations.PageNavigateAsync(value, contextId: "webview-1");
                break;
            case "Page.reload":
                await operations.PageReloadAsync(contextId: "webview-1");
                break;
            case "Input.insertText":
                await operations.InputInsertTextAsync(value, contextId: "webview-1");
                break;
        }

        var request = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/webview/evaluate");
        Assert.Equal("POST", request.Method);
        Assert.Equal("?contextId=webview-1", request.QueryString);
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

    [Fact]
    public async Task PageCaptureScreenshotAsync_ReturnsNativePngAsBase64()
    {
        await using var server = new MockAgentServer();
        await server.StartAsync();
        using var client = new AgentClient("localhost", server.Port);

        var actual = await new WebViewOperations(client).PageCaptureScreenshotAsync(contextId: "webview-1");

        Assert.Equal(MockAgentResponses.ScreenshotPng, Convert.FromBase64String(actual!));
        var request = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/webview/screenshot");
        Assert.Equal("GET", request.Method);
        Assert.Equal("?contextId=webview-1", request.QueryString);
        Assert.DoesNotContain(server.RecordedRequests, r => r.Path == "/api/v1/webview/evaluate");
    }

    [Fact]
    public async Task PageCaptureScreenshotAsync_InvalidPng_ThrowsInsteadOfReturningFallback()
    {
        await using var server = new MockAgentServer(invalidWebViewScreenshot: true);
        await server.StartAsync();
        using var client = new AgentClient("localhost", server.Port);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new WebViewOperations(client).PageCaptureScreenshotAsync());

        Assert.Contains("PNG", error.Message);
    }

    [Fact]
    public async Task PageCaptureScreenshotAsync_HttpFailure_PreservesDetails()
    {
        await using var server = new MockAgentServer(failWebViewScreenshot: true);
        await server.StartAsync();
        using var client = new AgentClient("localhost", server.Port);

        var error = await Assert.ThrowsAsync<HttpRequestException>(
            () => new WebViewOperations(client).PageCaptureScreenshotAsync());

        Assert.Contains("HTTP 400", error.Message);
        Assert.Contains("Failed to capture WebView screenshot", error.Message);
    }

    [Theory]
    [InlineData("""{"webviews":[{"id":"webview-0","ready":false},{"id":"webview-1","ready":true}]}""", true, 2)]
    [InlineData("""{"webviews":[]}""", false, 0)]
    public async Task GetStatusAsync_ReadsCanonicalReadinessWithoutCdpOrLeaseCalls(string response, bool ready, int count)
    {
        await using var server = new MockAgentServer { WebViewContextsResponse = response };
        await server.StartAsync();
        using var client = new AgentClient("localhost", server.Port);

        var result = await new WebViewOperations(client).GetStatusAsync();

        Assert.Equal((ready, count), result);
        Assert.Equal("/api/v1/webview/contexts", Assert.Single(server.RecordedRequests).Path);
    }

    [Fact]
    public async Task StatusCommand_PreservesDirectConnectionWithoutBrokerPreparation()
    {
        await using var server = new MockAgentServer
        {
            WebViewContextsResponse = """{"webviews":[{"id":"webview-0","ready":false},{"id":"webview-1","ready":true}]}"""
        };
        await server.StartAsync();
        var cli = new CliTestHarness(server.Port);
        var resolveBroker = DevFlowCommands.ResolveRunningBrokerPortAsync;
        DevFlowCommands.ResolveRunningBrokerPortAsync = () => throw new InvalidOperationException("Status must not discover broker labels or forwarding.");
        try
        {
            var result = await cli.InvokeAsync("devflow", "webview", "status", "--context-id", "webview-1");

            Assert.Equal(0, result.ExitCode);
            Assert.Equal("Connected: webview-1 ready", result.StdOut);
            Assert.Equal("", result.StdErr);
            Assert.Equal("/api/v1/webview/contexts", Assert.Single(server.RecordedRequests).Path);
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
        var source = await operations.GetSourceAsync(contextId: "webview-1");

        Assert.Equal("webview-1", Assert.Single(contexts.GetProperty("webviews").EnumerateArray()).GetProperty("id").GetString());
        Assert.Equal(MockAgentResponses.WebViewSource, source);
        var sourceRequest = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/webview/source");
        Assert.Equal("?contextId=webview-1", sourceRequest.QueryString);
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
