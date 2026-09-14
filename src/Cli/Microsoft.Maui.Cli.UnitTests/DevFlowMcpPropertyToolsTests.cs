using Microsoft.Maui.Cli.DevFlow.Mcp;
using Microsoft.Maui.Cli.DevFlow.Mcp.Tools;
using Microsoft.Maui.Cli.UnitTests.Fixtures;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

/// <summary>
/// Exercises the maui_get_property and maui_assert MCP tools against a mock agent to confirm
/// that a genuine "property not found" (HTTP 404, no reason) preserves the documented
/// null/not-found/FAIL fallback contract, while other explicit server failures (native
/// rejections, failures without a reason, malformed responses) still surface as hard errors.
/// </summary>
public class DevFlowMcpPropertyToolsTests
{
    [Fact]
    public async Task GetProperty_MissingProperty_ReturnsNotFoundMessageInsteadOfThrowing()
    {
        await using var server = new MockAgentServer(propertyNotFound: true);
        await server.StartAsync();
        var session = CreateSession(server.Port);

        var result = await PropertyTools.GetProperty(session, "el-1", "Missing", server.Port);

        Assert.Equal("Property 'Missing' not found on element 'el-1'.", result);
    }

    [Fact]
    public async Task GetProperty_NativeElementRejection_ThrowsWithReason()
    {
        await using var server = new MockAgentServer(rejectNativeProperty: true);
        await server.StartAsync();
        var session = CreateSession(server.Port);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => PropertyTools.GetProperty(session, "native:registered:x", "Opacity", server.Port));

        Assert.Contains("native-property-not-supported", ex.Message);
    }

    [Fact]
    public async Task GetProperty_FailureWithoutReason_StillThrows()
    {
        await using var server = new MockAgentServer(propertyFailureWithoutReason: true);
        await server.StartAsync();
        var session = CreateSession(server.Port);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => PropertyTools.GetProperty(session, "el-1", "Opacity", server.Port));

        Assert.Contains("Agent not bound to app", ex.Message);
    }

    [Fact]
    public async Task Assert_MissingProperty_ReturnsFailInsteadOfThrowing()
    {
        await using var server = new MockAgentServer(propertyNotFound: true);
        await server.StartAsync();
        var session = CreateSession(server.Port);

        var result = await AssertTool.Assert(
            session,
            propertyName: "Missing",
            expectedValue: "expected",
            elementId: "el-1",
            automationId: null,
            agentPort: server.Port);

        Assert.StartsWith("FAIL:", result);
        Assert.Contains("(null)", result);
    }

    [Fact]
    public async Task Assert_NativeElementRejection_ThrowsWithReason()
    {
        await using var server = new MockAgentServer(rejectNativeProperty: true);
        await server.StartAsync();
        var session = CreateSession(server.Port);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => AssertTool.Assert(
                session,
                propertyName: "Opacity",
                expectedValue: "1",
                elementId: "native:registered:x",
                automationId: null,
                agentPort: server.Port));

        Assert.Contains("native-property-not-supported", ex.Message);
    }

    private static McpAgentSession CreateSession(int port)
        => new()
        {
            DefaultAgentHost = "127.0.0.1",
            DefaultAgentPort = port
        };
}
