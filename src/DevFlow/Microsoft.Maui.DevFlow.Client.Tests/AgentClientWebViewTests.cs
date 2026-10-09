using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Client.Tests;

public class AgentClientWebViewTests
{
    [Fact]
    public async Task SendCdpCommandAsync_HttpFailure_ThrowsWithAgentError()
    {
        using var agent = FakeAgent.Start(_ => FakeAgent.Response.Json(
            """{"success":false,"error":"No WebViews registered"}""", statusCode: 400));
        using var client = new AgentClient("localhost", agent.Port) { AutoAcquireMutationLease = false };

        var error = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.SendCdpCommandAsync("Runtime.evaluate"));

        Assert.Contains("No WebViews registered", error.Message);
        Assert.Contains("HTTP 400", error.Message);
    }

    [Theory]
    [InlineData("""{"error":{"message":"Method unimplemented"}}""", "Method unimplemented")]
    [InlineData("""{"error":"WebView not ready"}""", "WebView not ready")]
    [InlineData("""{"success":false}""", "failed")]
    [InlineData("""{"result":{"exceptionDetails":{"text":"Uncaught","exception":{"description":"ReferenceError: missingVariable"}}}}""", "missingVariable")]
    [InlineData("""{"result":{"exceptionDetails":{"text":"SyntaxError: invalid selector"}}}""", "invalid selector")]
    [InlineData("[]", "invalid CDP response")]
    public async Task SendCdpCommandAsync_CdpFailure_Throws(string response, string expected)
    {
        using var agent = FakeAgent.StartJson(response);
        using var client = new AgentClient("localhost", agent.Port) { AutoAcquireMutationLease = false };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.SendCdpCommandAsync("Runtime.evaluate"));

        Assert.Contains(expected, error.Message);
    }

    [Fact]
    public async Task SendCdpCommandAsync_TargetWebView_IsEncodedAndForwarded()
    {
        using var agent = FakeAgent.StartJson("""{"result":{"result":{"value":2}}}""");
        using var client = new AgentClient("localhost", agent.Port) { AutoAcquireMutationLease = false };

        var result = await client.SendCdpCommandAsync("Runtime.evaluate", webviewId: "Left View/#");

        Assert.Equal(2, result.GetProperty("result").GetProperty("result").GetProperty("value").GetInt32());
        Assert.Equal("webview=Left%20View%2F%23", Assert.Single(agent.Requests).Query);
    }

    [Fact]
    public async Task GetCdpSourceAsync_HttpFailure_ThrowsWithAgentDetails()
    {
        using var agent = FakeAgent.Start(_ => FakeAgent.Response.Json(
            """{"success":false,"error":"ReferenceError: source failed"}""", statusCode: 400));
        using var client = new AgentClient("localhost", agent.Port);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetCdpSourceAsync("Left View/#"));

        Assert.Contains("ReferenceError: source failed", error.Message);
        Assert.Equal("webview=Left%20View%2F%23", Assert.Single(agent.Requests).Query);
    }

    [Theory]
    [InlineData("navigate")]
    [InlineData("click")]
    [InlineData("fill")]
    [InlineData("text")]
    public async Task WebViewAction_HttpFailure_ThrowsWithDetails(string action)
    {
        using var agent = FakeAgent.Start(_ => FakeAgent.Response.Json(
            """{"success":false,"error":"ReferenceError: missing"}""", statusCode: 400));
        using var client = new AgentClient("localhost", agent.Port) { AutoAcquireMutationLease = false };

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => PerformActionAsync(client, action));

        Assert.Contains("ReferenceError: missing", error.Message);
        Assert.Single(agent.Requests);
    }

    [Theory]
    [InlineData("""{"success":false,"error":"Element not found"}""")]
    [InlineData("{}")]
    public async Task WebViewAction_NonSuccessBody_Throws(string response)
    {
        using var agent = FakeAgent.StartJson(response);
        using var client = new AgentClient("localhost", agent.Port) { AutoAcquireMutationLease = false };

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ClickWebViewAsync("#missing"));
    }

    [Theory]
    [InlineData("navigate")]
    [InlineData("click")]
    [InlineData("fill")]
    [InlineData("text")]
    public async Task WebViewAction_Success_ForwardsContextAndReturnsTrue(string action)
    {
        using var agent = FakeAgent.StartJson("""{"success":true}""");
        using var client = new AgentClient("localhost", agent.Port) { AutoAcquireMutationLease = false };

        Assert.True(await PerformActionAsync(client, action));
        using var body = System.Text.Json.JsonDocument.Parse(Assert.Single(agent.Requests).Body);
        Assert.Equal("LeftWebView", body.RootElement.GetProperty("contextId").GetString());
    }

    private static Task<bool> PerformActionAsync(AgentClient client, string action) => action switch
    {
        "navigate" => client.NavigateWebViewAsync("/next", "LeftWebView"),
        "click" => client.ClickWebViewAsync("button", "LeftWebView"),
        "fill" => client.FillWebViewAsync("input", "hello", "LeftWebView"),
        "text" => client.InsertWebViewTextAsync("typed", "LeftWebView"),
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    [Fact]
    public async Task GetWebViewScreenshotAsync_UsesNativeFirstEndpointAndTarget()
    {
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
        using var agent = FakeAgent.Start(_ => FakeAgent.Response.Png(png));
        using var client = new AgentClient("localhost", agent.Port);

        Assert.Equal(png, await client.GetWebViewScreenshotAsync("Left View/#"));
        var request = Assert.Single(agent.Requests);
        Assert.Equal("GET", request.Method);
        Assert.Equal("/api/v1/webview/screenshot", request.Path);
        Assert.Equal("webview=Left%20View%2F%23", request.Query);
    }

    [Theory]
    [InlineData("")]
    [InlineData("""{"error":"Not an image"}""")]
    public async Task GetWebViewScreenshotAsync_NonImageSuccess_Throws(string response)
    {
        using var agent = FakeAgent.StartJson(response);
        using var client = new AgentClient("localhost", agent.Port);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetWebViewScreenshotAsync());
    }

    [Fact]
    public async Task GetWebViewScreenshotAsync_HttpFailure_ThrowsWithDetails()
    {
        using var agent = FakeAgent.Start(_ => FakeAgent.Response.Json(
            """{"success":false,"error":"Failed to capture WebView screenshot"}""", statusCode: 400));
        using var client = new AgentClient("localhost", agent.Port);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetWebViewScreenshotAsync());
        Assert.Contains("Failed to capture WebView screenshot", error.Message);
    }
}
