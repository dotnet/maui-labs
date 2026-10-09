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

        var result = await client.SendCdpCommandAsync("Runtime.evaluate", contextId: "webview-2");

        Assert.Equal(2, result.GetProperty("result").GetProperty("result").GetProperty("value").GetInt32());
        Assert.Equal("contextId=webview-2", Assert.Single(agent.Requests).Query);
    }

    [Fact]
    public async Task GetWebViewScreenshotAsync_UsesNativeFirstEndpointAndContext()
    {
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
        using var agent = FakeAgent.Start(_ => FakeAgent.Response.Png(png));
        using var client = new AgentClient("localhost", agent.Port);

        var bytes = await client.GetWebViewScreenshotAsync("webview-3");

        Assert.Equal(png, bytes);
        var request = Assert.Single(agent.Requests);
        Assert.Equal("GET", request.Method);
        Assert.Equal("/api/v1/webview/screenshot", request.Path);
        Assert.Equal("contextId=webview-3", request.Query);
    }

    [Fact]
    public async Task GetWebViewScreenshotAsync_NonImageSuccess_Throws()
    {
        using var agent = FakeAgent.StartJson("""{"error":"Not an image"}""");
        using var client = new AgentClient("localhost", agent.Port);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetWebViewScreenshotAsync());
    }

    [Fact]
    public async Task GetCdpSourceAsync_UsesCanonicalContextQuery()
    {
        using var agent = FakeAgent.StartJson("<html>source</html>");
        using var client = new AgentClient("localhost", agent.Port);

        Assert.Equal("<html>source</html>", await client.GetCdpSourceAsync("webview-4"));
        Assert.Equal("contextId=webview-4", Assert.Single(agent.Requests).Query);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task WebViewRequests_ExplicitBlankContext_DoesNotFallBackToActiveHost(string contextId)
    {
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
        using var agent = FakeAgent.Start(request => request.Path.EndsWith("/screenshot", StringComparison.Ordinal)
            ? FakeAgent.Response.Png(png)
            : FakeAgent.Response.Json("""{"success":true,"result":{}}"""));
        using var client = new AgentClient("localhost", agent.Port) { AutoAcquireMutationLease = false };

        await client.SendCdpCommandAsync("Runtime.evaluate", contextId: contextId);
        await client.GetCdpSourceAsync(contextId);
        await client.GetWebViewScreenshotAsync(contextId);
        await client.NavigateWebViewAsync("/next", contextId);
        await client.ClickWebViewAsync("button", contextId);
        await client.FillWebViewAsync("input", "filled", contextId);
        await client.InsertWebViewTextAsync("typed", contextId);

        Assert.Equal(7, agent.Requests.Count);
        foreach (var request in agent.Requests)
        {
            if (request.Path.EndsWith("/evaluate", StringComparison.Ordinal) || request.Method == "GET")
                Assert.Equal($"contextId={Uri.EscapeDataString(contextId)}", request.Query);
            else
            {
                using var body = System.Text.Json.JsonDocument.Parse(request.Body);
                Assert.Equal(contextId, body.RootElement.GetProperty("contextId").GetString());
            }
        }
    }

    [Fact]
    public void LayoutScope_EmitsOnlyCanonicalWebViewOption()
    {
        using var document = System.Text.Json.JsonDocument.Parse(
            System.Text.Json.JsonSerializer.Serialize(new LayoutInspectionScope { IncludeWebViewElements = false }));

        Assert.False(document.RootElement.GetProperty("includeWebViewElements").GetBoolean());
        Assert.False(document.RootElement.TryGetProperty("includeBlazorElements", out _));
    }
}
