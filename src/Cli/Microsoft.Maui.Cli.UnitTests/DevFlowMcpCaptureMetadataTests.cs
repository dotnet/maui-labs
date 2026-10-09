using System.Text.Json;
using ModelContextProtocol;
using Microsoft.Maui.Cli.DevFlow.Mcp;
using Microsoft.Maui.Cli.DevFlow.Mcp.Tools;
using Microsoft.Maui.Cli.UnitTests.Fixtures;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class DevFlowMcpCaptureMetadataTests
{
    [Fact]
    public async Task Tap_ForwardsOptionalCaptureMetadata()
    {
        await using var server = new MockAgentServer();
        await server.StartAsync();
        var session = CreateSession(server.Port);

        await InteractionTools.Tap(
            session,
            "native:registered:tap",
            server.Port,
            captureEpoch: 42,
            registryGeneration: 7);

        var request = Assert.Single(
            server.RecordedRequests,
            recorded => recorded.Path == "/api/v1/ui/actions/tap");
        AssertCaptureMetadata(request, 42, 7);
    }

    [Fact]
    public async Task Tap_OmitsCaptureMetadataWhenNotProvided()
    {
        await using var server = new MockAgentServer();
        await server.StartAsync();
        var session = CreateSession(server.Port);

        await InteractionTools.Tap(session, "legacy-element", server.Port);

        var request = Assert.Single(
            server.RecordedRequests,
            recorded => recorded.Path == "/api/v1/ui/actions/tap");
        using var body = JsonDocument.Parse(request.Body!);
        Assert.False(body.RootElement.TryGetProperty("captureEpoch", out _));
        Assert.False(body.RootElement.TryGetProperty("registryGeneration", out _));
    }

    [Fact]
    public async Task FocusAndSetProperty_ForwardCaptureMetadata()
    {
        await using var server = new MockAgentServer();
        await server.StartAsync();
        var session = CreateSession(server.Port);

        await NavigationTools.Focus(
            session,
            "native:registered:focus",
            server.Port,
            captureEpoch: 43,
            registryGeneration: 8);
        await PropertyTools.SetProperty(
            session,
            "native:registered:property",
            "Text",
            "updated",
            server.Port,
            captureEpoch: 44,
            registryGeneration: 9);

        AssertCaptureMetadata(
            Assert.Single(
                server.RecordedRequests,
                recorded => recorded.Path == "/api/v1/ui/actions/focus"),
            43,
            8);
        AssertCaptureMetadata(
            Assert.Single(
                server.RecordedRequests,
                recorded => recorded.Path.Contains("/properties/Text", StringComparison.Ordinal)),
            44,
            9);
    }

    [Fact]
    public async Task Batch_ForwardsCaptureMetadataAtRequestLevel()
    {
        await using var server = new MockAgentServer();
        await server.StartAsync();
        var session = CreateSession(server.Port);

        await BatchTools.Batch(
            session,
            """[{"action":"tap","elementId":"native:registered:item"}]""",
            continueOnError: false,
            agentPort: server.Port,
            captureEpoch: 45,
            registryGeneration: 10);

        AssertCaptureMetadata(
            Assert.Single(
                server.RecordedRequests,
                recorded => recorded.Path == "/api/v1/ui/actions/batch"),
            45,
            10);
    }

    [Fact]
    public async Task Screenshot_ForwardsCaptureMetadataAsQueryParameters()
    {
        await using var server = new MockAgentServer();
        await server.StartAsync();
        var session = CreateSession(server.Port);

        await ScreenshotTool.Screenshot(
            session,
            agentPort: server.Port,
            captureEpoch: 46,
            registryGeneration: 11);

        var request = Assert.Single(
            server.RecordedRequests,
            recorded => recorded.Path == "/api/v1/ui/screenshot");
        Assert.Contains("captureEpoch=46", request.QueryString, StringComparison.Ordinal);
        Assert.Contains("registryGeneration=11", request.QueryString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tap_StaleCapture_ReportsReasonAndRecoveryAction()
    {
        await using var server = new MockAgentServer(staleFirstElementTap: true);
        await server.StartAsync();
        var session = CreateSession(server.Port);

        var exception = await Assert.ThrowsAsync<McpException>(() =>
            InteractionTools.Tap(
                session,
                "element-42",
                server.Port,
                captureEpoch: 47,
                registryGeneration: 12));

        Assert.Contains("stale-capture-epoch", exception.Message, StringComparison.Ordinal);
        Assert.Contains("fresh tree or hit-test", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static McpAgentSession CreateSession(int port)
        => new()
        {
            DefaultAgentHost = "127.0.0.1",
            DefaultAgentPort = port
        };

    private static void AssertCaptureMetadata(
        RecordedRequest request,
        long captureEpoch,
        long registryGeneration)
    {
        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal(captureEpoch, body.RootElement.GetProperty("captureEpoch").GetInt64());
        Assert.Equal(
            registryGeneration,
            body.RootElement.GetProperty("registryGeneration").GetInt64());
    }
}
