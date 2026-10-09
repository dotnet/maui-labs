using System.Net;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Client.Tests;

public class AgentClientWebViewTests
{
    [Fact]
    public async Task SendCdpCommandAsync_HttpFailure_ThrowsWithAgentError()
    {
        using var agent = FakeAgent.Start(_ => FakeAgent.Response.Json(
            """{"success":false,"error":"No WebViews registered"}""",
            statusCode: 400));
        using var client = new AgentClient("localhost", agent.Port) { AutoAcquireMutationLease = false };

        var error = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.SendCdpCommandAsync("Runtime.evaluate"));

        Assert.Contains("No WebViews registered", error.Message);
        Assert.Contains("HTTP 400", error.Message);
    }

    [Theory]
    [InlineData("""{"error":{"message":"Method unimplemented"}}""", "Method unimplemented")]
    [InlineData("""{"error":"WebView not ready"}""", "WebView not ready")]
    [InlineData("""{"result":{"exceptionDetails":{"text":"Uncaught","exception":{"description":"ReferenceError: missingVariable"}}}}""", "missingVariable")]
    public async Task SendCdpCommandAsync_CdpFailure_Throws(string response, string expected)
    {
        using var agent = FakeAgent.StartJson(response);
        using var client = new AgentClient("localhost", agent.Port) { AutoAcquireMutationLease = false };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.SendCdpCommandAsync("Runtime.evaluate"));

        Assert.Contains(expected, error.Message);
    }

    [Fact]
    public async Task SendCdpCommandAsync_TargetContext_IsEncodedAndForwarded()
    {
        using var agent = FakeAgent.StartJson("""{"result":{"result":{"value":2}}}""");
        using var client = new AgentClient("localhost", agent.Port) { AutoAcquireMutationLease = false };

        var result = await client.SendCdpCommandAsync("Runtime.evaluate", webviewId: "Hybrid view");

        Assert.Equal(2, result.GetProperty("result").GetProperty("result").GetProperty("value").GetInt32());
        Assert.Equal("webview=Hybrid%20view", Assert.Single(agent.Requests).Query);
    }

    [Fact]
    public async Task GetWebViewScreenshotAsync_UsesNativeFirstEndpointAndContext()
    {
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
        using var agent = FakeAgent.Start(_ => FakeAgent.Response.Png(png));
        using var client = new AgentClient("localhost", agent.Port);

        var bytes = await client.GetWebViewScreenshotAsync("Plain view");

        Assert.Equal(png, bytes);
        var request = Assert.Single(agent.Requests);
        Assert.Equal("GET", request.Method);
        Assert.Equal("/api/v1/webview/screenshot", request.Path);
        Assert.Equal("contextId=Plain%20view", request.Query);
    }

    [Fact]
    public async Task GetWebViewScreenshotAsync_NonImageSuccess_Throws()
    {
        using var agent = FakeAgent.StartJson("""{"error":"Not an image"}""");
        using var client = new AgentClient("localhost", agent.Port);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetWebViewScreenshotAsync());
    }
}
