using Microsoft.Maui.Cli.DevFlow.Mcp;
using Microsoft.Maui.Cli.DevFlow.Mcp.Tools;
using Microsoft.Maui.Cli.UnitTests.Fixtures;
using ModelContextProtocol.Protocol;
using ModelContextProtocol;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class CdpToolsTests
{
    [Fact]
    public async Task Screenshot_UsesNativeEndpointAndReturnsPng()
    {
        await using var server = new MockAgentServer();
        await server.StartAsync();
        var session = new McpAgentSession { DefaultAgentHost = "127.0.0.1", DefaultAgentPort = server.Port };

        var content = await CdpTools.CdpScreenshot(session, contextId: "webview-1");

        var image = Assert.IsType<ImageContentBlock>(content[1]);
        Assert.Equal("image/png", image.MimeType);
        Assert.Equal(MockAgentResponses.ScreenshotPng,
            Convert.FromBase64String(System.Text.Encoding.UTF8.GetString(image.Data.Span)));
        Assert.Contains("contextId=webview-1", Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/webview/screenshot").QueryString);
        Assert.DoesNotContain(server.RecordedRequests, r => r.Path == "/api/v1/webview/evaluate");
    }

    [Fact]
    public async Task Screenshot_InvalidPng_Throws()
    {
        await using var server = new MockAgentServer(invalidWebViewScreenshot: true);
        await server.StartAsync();
        var session = new McpAgentSession { DefaultAgentHost = "127.0.0.1", DefaultAgentPort = server.Port };

        var error = await Assert.ThrowsAsync<McpException>(() => CdpTools.CdpScreenshot(session));
        Assert.Contains("PNG", error.Message);
    }

    [Fact]
    public async Task Evaluate_JavaScriptFailure_ThrowsWithDescription()
    {
        await using var server = new MockAgentServer(
            webViewEvaluateResponse: """{"result":{"exceptionDetails":{"text":"Uncaught","exception":{"description":"ReferenceError: missingVariable"}}}}""");
        await server.StartAsync();
        var session = new McpAgentSession { DefaultAgentHost = "127.0.0.1", DefaultAgentPort = server.Port };

        var error = await Assert.ThrowsAsync<McpException>(
            () => CdpTools.CdpEvaluate(session, "missingVariable", contextId: "webview-1"));

        Assert.Contains("missingVariable", error.Message);
        Assert.Equal("?contextId=webview-1",
            Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/webview/evaluate").QueryString);
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
