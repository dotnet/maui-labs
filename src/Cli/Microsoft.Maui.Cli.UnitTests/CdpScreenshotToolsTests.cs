using System.Text;
using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow.Mcp;
using Microsoft.Maui.Cli.DevFlow.Mcp.Tools;
using Microsoft.Maui.Cli.UnitTests.Fixtures;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class CdpScreenshotToolsTests
{
    [Fact]
    public async Task Screenshot_UsesOnlyDirectCdpAndReturnsPng()
    {
        var response = JsonSerializer.Serialize(new { result = new { data = Convert.ToBase64String(MockAgentResponses.ScreenshotPng) } });
        await using var server = new MockAgentServer { WebViewEvaluateResponse = response };
        await server.StartAsync();
        var session = new McpAgentSession { DefaultAgentHost = "127.0.0.1", DefaultAgentPort = server.Port };

        var content = await CdpTools.CdpScreenshot(session, contextId: "webview-1");

        var image = Assert.IsType<ImageContentBlock>(content[1]);
        Assert.Equal("image/png", image.MimeType);
        Assert.Equal(MockAgentResponses.ScreenshotPng, Convert.FromBase64String(Encoding.UTF8.GetString(image.Data.Span)));
        var request = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/webview/evaluate");
        Assert.Equal("POST", request.Method);
        Assert.Equal("?contextId=webview-1", request.QueryString);
        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal("Page.captureScreenshot", body.RootElement.GetProperty("method").GetString());
        Assert.Equal("png", body.RootElement.GetProperty("params").GetProperty("format").GetString());
        Assert.DoesNotContain(server.RecordedRequests, r => r.Path == "/api/v1/webview/screenshot");
    }

    [Theory]
    [InlineData("{}", "result.data")]
    [InlineData("""{"result":null}""", "result.data")]
    [InlineData("""{"result":{}}""", "result.data")]
    [InlineData("""{"result":{"data":null}}""", "result.data")]
    [InlineData("""{"result":{"data":42}}""", "result.data")]
    [InlineData("""{"result":{"data":""}}""", "result.data")]
    [InlineData("""{"result":{"data":"  "}}""", "result.data")]
    [InlineData("""{"result":{"data":"not base64!"}}""", "base64")]
    [InlineData("""{"result":{"data":"cG5n"}}""", "PNG")]
    [InlineData("""{"result":{"data":"iVBORw0KGg=="}}""", "PNG")]
    [InlineData("""{"error":{"message":"Capture unsupported"}}""", "Capture unsupported")]
    [InlineData("""{"result":{"exceptionDetails":{"exception":{"description":"Screenshot JS failure"}}}}""", "Screenshot JS failure")]
    [InlineData("not JSON", "invalid JSON")]
    public async Task Screenshot_InvalidCdpResponse_ThrowsWithoutNativeFallback(string response, string expected)
    {
        await using var server = new MockAgentServer { WebViewEvaluateResponse = response };
        await server.StartAsync();
        var session = new McpAgentSession { DefaultAgentHost = "127.0.0.1", DefaultAgentPort = server.Port };

        var error = await Assert.ThrowsAsync<McpException>(() => CdpTools.CdpScreenshot(session, contextId: "webview-1"));

        Assert.Contains(expected, error.Message);
        Assert.Equal("?contextId=webview-1",
            Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/webview/evaluate").QueryString);
        Assert.DoesNotContain(server.RecordedRequests, r => r.Path == "/api/v1/webview/screenshot");
    }

    [Fact]
    public async Task Screenshot_HttpFailure_PreservesAgentDetails()
    {
        await using var server = new MockAgentServer(
            webViewEvaluateResponse: """{"error":"CDP target unavailable"}""", webViewResponseStatusCode: 400);
        await server.StartAsync();
        var session = new McpAgentSession { DefaultAgentHost = "127.0.0.1", DefaultAgentPort = server.Port };

        var error = await Assert.ThrowsAsync<McpException>(() => CdpTools.CdpScreenshot(session, contextId: "webview-1"));

        Assert.Contains("HTTP 400", error.Message);
        Assert.Contains("CDP target unavailable", error.Message);
        Assert.Equal("?contextId=webview-1",
            Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/webview/evaluate").QueryString);
        Assert.DoesNotContain(server.RecordedRequests, r => r.Path == "/api/v1/webview/screenshot");
    }

    [Fact]
    public async Task Source_ForwardsCanonicalContext()
    {
        await using var server = new MockAgentServer();
        await server.StartAsync();
        var session = new McpAgentSession { DefaultAgentHost = "127.0.0.1", DefaultAgentPort = server.Port };
        Assert.Contains("Hello Blazor", await CdpTools.CdpSource(session, contextId: "webview-1"));
        Assert.Equal("?contextId=webview-1",
            Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/webview/source").QueryString);
    }
}
