using Microsoft.Maui.Cli.UnitTests.Fixtures;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

[Collection("CLI")]
public class WebViewScreenshotCommandTests
{
    [Fact]
    public async Task WebViewScreenshot_UsesNativeFirstEndpoint()
    {
        await using var server = new MockAgentServer();
        await server.StartAsync();
        var cli = new CliTestHarness(server.Port);

        var result = await cli.InvokeAsync("devflow", "webview", "--context-id", "webview-1", "Page", "captureScreenshot");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(MockAgentResponses.ScreenshotPng, Convert.FromBase64String(result.StdOut.Trim()));
        var request = Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/webview/screenshot");
        Assert.Equal("GET", request.Method);
        Assert.Equal("?contextId=webview-1", request.QueryString);
        Assert.DoesNotContain(server.RecordedRequests, r => r.Path == "/api/v1/webview/evaluate");
    }

    [Theory]
    [InlineData(true, false, "PNG")]
    [InlineData(false, true, "Failed to capture WebView screenshot")]
    public async Task WebViewScreenshot_Failure_ReturnsNonzeroExitAndDetails(bool invalidPng, bool httpFailure, string expected)
    {
        await using var server = new MockAgentServer(
            invalidWebViewScreenshot: invalidPng, failWebViewScreenshot: httpFailure);
        await server.StartAsync();
        var cli = new CliTestHarness(server.Port);

        var result = await cli.InvokeAsync("devflow", "webview", "Page", "captureScreenshot");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(expected, result.StdErr);
        Assert.True(string.IsNullOrWhiteSpace(result.StdOut));
    }
}
