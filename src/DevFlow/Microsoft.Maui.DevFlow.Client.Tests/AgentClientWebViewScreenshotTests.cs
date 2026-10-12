using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Client.Tests;

public class AgentClientWebViewScreenshotTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("Left View/#", "webview=Left%20View%2F%23")]
    public async Task GetWebViewScreenshotAsync_UsesNativeFirstEndpointAndTarget(string? target, string query)
    {
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
        using var agent = FakeAgent.Start(_ => FakeAgent.Response.Png(png));
        using var client = new AgentClient("localhost", agent.Port);

        Assert.Equal(png, await client.GetWebViewScreenshotAsync(target));
        var request = Assert.Single(agent.Requests);
        Assert.Equal("GET", request.Method);
        Assert.Equal("/api/v1/webview/screenshot", request.Path);
        Assert.Equal(query, request.Query);
    }

    [Theory]
    [InlineData("")]
    [InlineData("""{"error":"Not an image"}""")]
    public async Task GetWebViewScreenshotAsync_NonImageSuccess_Throws(string response)
    {
        using var agent = FakeAgent.StartJson(response);
        using var client = new AgentClient("localhost", agent.Port);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetWebViewScreenshotAsync());

        Assert.Contains("PNG", error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public async Task GetWebViewScreenshotAsync_InvalidSignature_Throws(int corruptedByte)
    {
        byte[] bytes = [137, 80, 78, 71, 13, 10, 26, 10];
        bytes[corruptedByte] = 0;
        using var agent = FakeAgent.Start(_ => FakeAgent.Response.Png(bytes));
        using var client = new AgentClient("localhost", agent.Port);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetWebViewScreenshotAsync());
    }

    [Fact]
    public async Task GetWebViewScreenshotAsync_TruncatedSignature_Throws()
    {
        using var agent = FakeAgent.Start(_ => FakeAgent.Response.Png([137, 80, 78, 71, 13, 10, 26]));
        using var client = new AgentClient("localhost", agent.Port);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetWebViewScreenshotAsync());
    }

    [Fact]
    public async Task GetWebViewScreenshotAsync_HttpFailure_ThrowsWithDetails()
    {
        const string body = """{"success":false,"error":"Failed to capture WebView screenshot"}""";
        using var agent = FakeAgent.Start(_ => FakeAgent.Response.Json(body, statusCode: 400));
        using var client = new AgentClient("localhost", agent.Port);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetWebViewScreenshotAsync());

        Assert.Contains("HTTP 400", error.Message);
        Assert.Contains(body, error.Message);
        Assert.Single(agent.Requests);
    }
}
