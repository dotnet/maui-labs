using Microsoft.Maui.Cli.DevFlow.Mcp;
using Microsoft.Maui.Cli.DevFlow.Mcp.Tools;
using Microsoft.Maui.Cli.UnitTests.Fixtures;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class WebViewScreenshotToolsTests
{
    [Fact]
    public async Task Screenshot_UsesNativeEndpointAndReturnsPng()
    {
        await using var server = new MockAgentServer();
        await server.StartAsync();
        var session = new McpAgentSession { DefaultAgentHost = "127.0.0.1", DefaultAgentPort = server.Port };

        var content = await CdpTools.WebViewScreenshot(session, contextId: "webview-1");

        Assert.Contains("screenshot captured", Assert.IsType<TextContentBlock>(content[0]).Text);
        var image = Assert.IsType<ImageContentBlock>(content[1]);
        Assert.Equal("image/png", image.MimeType);
        Assert.Equal(MockAgentResponses.ScreenshotPng,
            Convert.FromBase64String(System.Text.Encoding.UTF8.GetString(image.Data.Span)));
        var request = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/webview/screenshot");
        Assert.Equal("GET", request.Method);
        Assert.Equal("?contextId=webview-1", request.QueryString);
        Assert.DoesNotContain(server.RecordedRequests, r => r.Path == "/api/v1/webview/evaluate");
    }

    [Theory]
    [InlineData(true, false, "PNG")]
    [InlineData(false, true, "Failed to capture WebView screenshot")]
    public async Task Screenshot_Failure_ThrowsUsefulMcpError(bool invalidPng, bool httpFailure, string expected)
    {
        await using var server = new MockAgentServer(
            invalidWebViewScreenshot: invalidPng, failWebViewScreenshot: httpFailure);
        await server.StartAsync();
        var session = new McpAgentSession { DefaultAgentHost = "127.0.0.1", DefaultAgentPort = server.Port };

        var error = await Assert.ThrowsAsync<McpException>(() => CdpTools.WebViewScreenshot(session, contextId: "webview-1"));

        Assert.Contains(expected, error.Message);
        Assert.Equal("?contextId=webview-1",
            Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/webview/screenshot").QueryString);
        Assert.DoesNotContain(server.RecordedRequests, r => r.Path == "/api/v1/webview/evaluate");
    }
}
