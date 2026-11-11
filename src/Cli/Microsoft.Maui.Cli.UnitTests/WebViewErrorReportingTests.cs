using Microsoft.Maui.Cli.DevFlow.Mcp;
using Microsoft.Maui.Cli.DevFlow.Mcp.Tools;
using Microsoft.Maui.Cli.UnitTests.Fixtures;
using ModelContextProtocol;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

[Collection("CLI")]
public class WebViewErrorReportingTests
{
    [Theory]
    [InlineData("""{"result":{"exceptionDetails":{"text":"Uncaught","exception":{"description":"ReferenceError: missingVariable"}}}}""", 200, "missingVariable")]
    [InlineData("""{"error":{"message":"Method unavailable"}}""", 200, "Method unavailable")]
    [InlineData("""{"success":false,"error":"No WebViews registered"}""", 400, "No WebViews registered")]
    [InlineData("<html>Bridge unavailable</html>", 400, "Bridge unavailable")]
    [InlineData("<html>Invalid CDP response</html>", 200, "Invalid CDP response")]
    public async Task Evaluate_Failure_ReportsUsefulErrorAndNonzeroExit(string response, int status, string expected)
    {
        await using var server = new MockAgentServer(
            webViewEvaluateResponse: response, webViewResponseStatusCode: status);
        await server.StartAsync();
        var cli = new CliTestHarness(server.Port);

        var result = await cli.InvokeAsync("devflow", "webview", "Runtime", "evaluate", "missingVariable");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(expected, result.StdErr);
        Assert.DoesNotContain("Error:", result.StdOut);
    }

    [Theory]
    [InlineData("""{"result":{"exceptionDetails":{"text":"Uncaught","exception":{"description":"ReferenceError: missingVariable"}}}}""", 200, "missingVariable")]
    [InlineData("""{"error":{"message":"Method unavailable"}}""", 200, "Method unavailable")]
    [InlineData("<html>Bridge unavailable</html>", 400, "Bridge unavailable")]
    [InlineData("<html>Invalid CDP response</html>", 200, "Invalid CDP response")]
    public async Task McpEvaluate_Failure_ThrowsUsefulMcpException(string response, int status, string expected)
    {
        await using var server = new MockAgentServer(
            webViewEvaluateResponse: response, webViewResponseStatusCode: status);
        await server.StartAsync();
        var session = new McpAgentSession { DefaultAgentHost = "127.0.0.1", DefaultAgentPort = server.Port };

        var error = await Assert.ThrowsAsync<McpException>(
            () => CdpTools.CdpEvaluate(session, "missingVariable"));

        Assert.Contains(expected, error.Message);
    }

    [Theory]
    [InlineData("dispatchClickEvent")]
    [InlineData("fill")]
    public async Task MissingSelector_ThrowsJavaScriptErrorAndReportsNonzeroExit(string action)
    {
        await using var server = new MockAgentServer(webViewEvaluateResponse:
            """{"result":{"exceptionDetails":{"text":"Uncaught","exception":{"description":"Error: Element not found"}}}}""");
        await server.StartAsync();
        var cli = new CliTestHarness(server.Port);
        var arguments = new List<string> { "devflow", "webview", "Input", action, "#missing" };
        if (action == "fill")
            arguments.Add("hello");

        var result = await cli.InvokeAsync(arguments.ToArray());

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Element not found", result.StdErr);
        using var body = System.Text.Json.JsonDocument.Parse(
            Assert.Single(server.RecordedRequests, r => r.Path == "/api/v1/webview/evaluate").Body!);
        Assert.Contains("throw new Error('Element not found')",
            body.RootElement.GetProperty("params").GetProperty("expression").GetString());
    }

    [Theory]
    [InlineData("""{"error":"ReferenceError: source failed"}""", 400, "source failed")]
    [InlineData("", 200, "no HTML source")]
    [InlineData("  ", 200, "no HTML source")]
    public async Task McpSource_Failure_ThrowsUsefulMcpException(string response, int status, string expected)
    {
        await using var server = new MockAgentServer(
            webViewSourceResponse: response, webViewResponseStatusCode: status);
        await server.StartAsync();
        var session = new McpAgentSession { DefaultAgentHost = "127.0.0.1", DefaultAgentPort = server.Port };

        var error = await Assert.ThrowsAsync<McpException>(() => CdpTools.CdpSource(session));

        Assert.Contains(expected, error.Message);
    }
}
