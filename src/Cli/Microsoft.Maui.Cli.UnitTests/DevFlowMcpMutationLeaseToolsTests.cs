using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow.Mcp;
using Microsoft.Maui.Cli.DevFlow.Mcp.Tools;
using Microsoft.Maui.Cli.UnitTests.Fixtures;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class DevFlowMcpMutationLeaseToolsTests
{
    [Fact]
    public async Task TakeControl_ForwardsExplicitForceForTheMcpSession()
    {
        await using var server = new MockAgentServer(supportMutationLease: true);
        await server.StartAsync();
        var session = new McpAgentSession
        {
            DefaultAgentHost = "127.0.0.1",
            DefaultAgentPort = server.Port
        };

        var result = await MutationLeaseTools.TakeControl(
            session,
            agentPort: server.Port,
            force: true);

        var request = Assert.Single(
            server.RecordedRequests,
            recorded => recorded.Path == "/api/v1/agent/lease");
        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal("claim", body.RootElement.GetProperty("action").GetString());
        Assert.True(body.RootElement.GetProperty("force").GetBoolean());
        Assert.Equal("mcp", body.RootElement.GetProperty("holderKind").GetString());
        Assert.Contains("\"youHold\":true", result, StringComparison.Ordinal);
        Assert.Contains("\"authority\":\"mock\"", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TakeControl_WhenAnotherHostOwnsLease_ReturnsActionableNextStep()
    {
        await using var server = new MockAgentServer(
            supportMutationLease: true,
            mutationLeaseHeldByOther: true);
        await server.StartAsync();
        var session = new McpAgentSession
        {
            DefaultAgentHost = "127.0.0.1",
            DefaultAgentPort = server.Port
        };

        var result = await MutationLeaseTools.TakeControl(
            session,
            agentPort: server.Port,
            force: false);

        Assert.Contains("\"heldByOther\":true", result, StringComparison.Ordinal);
        Assert.Contains("VS Code Inspector is currently driving the app", result, StringComparison.Ordinal);
        Assert.Contains("maui_take_control with force=true", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ControlTools_TrackClaimStatusAndReleaseForOneMcpSession()
    {
        await using var server = new MockAgentServer(supportMutationLease: true);
        await server.StartAsync();
        var session = new McpAgentSession
        {
            DefaultAgentHost = "127.0.0.1",
            DefaultAgentPort = server.Port
        };

        var initial = await MutationLeaseTools.Status(session, server.Port);
        var claimed = await MutationLeaseTools.TakeControl(session, server.Port);
        var held = await MutationLeaseTools.Status(session, server.Port);
        var released = await MutationLeaseTools.ReleaseControl(session, server.Port);
        var final = await MutationLeaseTools.Status(session, server.Port);

        Assert.Contains("\"youHold\":false", initial, StringComparison.Ordinal);
        Assert.Contains("\"youHold\":true", claimed, StringComparison.Ordinal);
        Assert.Contains("\"youHold\":true", held, StringComparison.Ordinal);
        Assert.Contains("\"youHold\":false", released, StringComparison.Ordinal);
        Assert.Contains("\"youHold\":false", final, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ForcedTakeoverBlockedByTransaction_TellsCallerToWait()
    {
        await using var server = new MockAgentServer(
            supportMutationLease: true,
            mutationLeaseHeldByOther: true,
            mutationLeaseTransactionBlocked: true);
        await server.StartAsync();
        var session = new McpAgentSession
        {
            DefaultAgentHost = "127.0.0.1",
            DefaultAgentPort = server.Port
        };

        var result = await MutationLeaseTools.TakeControl(
            session,
            agentPort: server.Port,
            force: true);

        Assert.Contains("\"youHold\":false", result, StringComparison.Ordinal);
        Assert.Contains("Poll maui_control_status", result, StringComparison.Ordinal);
        Assert.DoesNotContain("Ask the user for approval", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReleaseControl_WhenAnotherHostOwnsLease_DoesNotRecommendTakeover()
    {
        await using var server = new MockAgentServer(
            supportMutationLease: true,
            mutationLeaseHeldByOther: true);
        await server.StartAsync();
        var session = new McpAgentSession
        {
            DefaultAgentHost = "127.0.0.1",
            DefaultAgentPort = server.Port
        };

        var result = await MutationLeaseTools.ReleaseControl(session, server.Port);

        Assert.Contains("nothing was released", result, StringComparison.Ordinal);
        Assert.DoesNotContain("force=true", result, StringComparison.Ordinal);
    }
}
