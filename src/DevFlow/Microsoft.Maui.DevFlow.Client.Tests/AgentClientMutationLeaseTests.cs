using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Client.Tests;

/// <summary>
/// Mutating calls first claim a mutation lease, so the agent can coordinate several DevFlow hosts
/// driving the same app. That handshake is a precondition for every non-GET request, which makes it
/// worth pinning on both target families alongside the requests it guards.
/// </summary>
public class AgentClientMutationLeaseTests
{
    [Fact]
    public async Task TapAsync_ClaimsTheLeaseBeforeMutating()
    {
        using var agent = FakeAgent.Start(request => request.Path == "/api/v1/agent/lease"
            ? FakeAgent.Response.Json("""{"ok":true,"allowed":true,"youHold":true}""")
            : FakeAgent.Response.Json("""{"success":true}"""));
        using var client = new AgentClient("localhost", agent.Port)
        {
            MutationLeaseId = "test-lease",
            MutationLeaseHolderKind = "test",
        };

        Assert.True(await client.TapAsync("el-1"));

        Assert.Equal(2, agent.Requests.Count);
        var claim = agent.Requests[0];
        Assert.Equal("POST", claim.Method);
        Assert.Equal("/api/v1/agent/lease", claim.Path);

        using var body = System.Text.Json.JsonDocument.Parse(claim.Body);
        Assert.Equal("claim", body.RootElement.GetProperty("action").GetString());
        Assert.Equal("test-lease", body.RootElement.GetProperty("leaseId").GetString());
        Assert.Equal("test", body.RootElement.GetProperty("holderKind").GetString());

        Assert.Equal("/api/v1/ui/actions/tap", agent.Requests[1].Path);
    }

    [Fact]
    public async Task TapAsync_FailsWhenAnotherHostHoldsTheLease()
    {
        using var agent = FakeAgent.Start(request => request.Path == "/api/v1/agent/lease"
            ? FakeAgent.Response.Json("""{"ok":true,"allowed":false,"youHold":false,"heldByOther":true}""")
            : FakeAgent.Response.Json("""{"success":true}"""));
        using var client = new AgentClient("localhost", agent.Port);

        var failure = await Assert.ThrowsAsync<MutationLeaseException>(() => client.TapAsync("el-1"));

        Assert.True(failure.Status.HeldByOther);

        // The tap must never reach the agent once the lease was refused.
        Assert.Single(agent.Requests);
    }

    [Fact]
    public async Task TapAsync_ProceedsAgainstAnAgentThatPredatesLeases()
    {
        // Rolling-upgrade compatibility: an older agent answers 404 for the lease endpoint, and the
        // mutation must still go through.
        using var agent = FakeAgent.Start(request => request.Path == "/api/v1/agent/lease"
            ? FakeAgent.Response.Json("""{"error":"not_found"}""", statusCode: 404)
            : FakeAgent.Response.Json("""{"success":true}"""));
        using var client = new AgentClient("localhost", agent.Port);

        Assert.True(await client.TapAsync("el-1"));

        Assert.Equal(2, agent.Requests.Count);
        Assert.Equal("/api/v1/ui/actions/tap", agent.Requests[1].Path);
    }

    [Fact]
    public async Task QueryAsync_DoesNotClaimALeaseForReads()
    {
        // Reads never mutate, so they must not pay for — or be blocked by — the lease handshake.
        using var agent = FakeAgent.StartJson("[]");
        using var client = new AgentClient("localhost", agent.Port);

        await client.QueryAsync(type: "Button");

        var request = Assert.Single(agent.Requests);
        Assert.Equal("GET", request.Method);
        Assert.Equal("/api/v1/ui/elements", request.Path);
    }

    [Fact]
    public async Task UseActionContext_ScopesLifecycleCorrelationHeaders()
    {
        using var agent = FakeAgent.StartJson("""{"success":true}""");
        using var client = new AgentClient("localhost", agent.Port)
        {
            AutoAcquireMutationLease = false
        };

        using (client.UseActionContext(
            source: "replay",
            actionId: "action-1",
            streamId: "stream-1",
            frameId: "frame-1",
            frameRevision: 7,
            coordinateSpace: "appLogical",
            x: 12.5,
            y: 24.25,
            toX: 40,
            toY: 60,
            deltaX: 27.5,
            deltaY: 35.75))
        {
            Assert.True(await client.TapAsync("el-1"));
        }

        Assert.True(await client.TapAsync("el-2"));
        using (client.UseActionContext(
            actionId: "action-3",
            coordinateSpace: "appLogical\r\nX-Injected: yes"))
        {
            Assert.True(await client.TapAsync("el-3"));
        }

        Assert.Equal(3, agent.Requests.Count);
        var correlated = agent.Requests[0].Headers;
        Assert.Equal("replay", correlated["X-DevFlow-Action-Source"]);
        Assert.Equal("action-1", correlated["X-DevFlow-Action-Id"]);
        Assert.Equal("stream-1", correlated["X-DevFlow-Action-Stream-Id"]);
        Assert.Equal("frame-1", correlated["X-DevFlow-Action-Frame-Id"]);
        Assert.Equal("7", correlated["X-DevFlow-Action-Frame-Revision"]);
        Assert.Equal("appLogical", correlated["X-DevFlow-Action-Coordinate-Space"]);
        Assert.Equal("12.5", correlated["X-DevFlow-Action-X"]);
        Assert.Equal("24.25", correlated["X-DevFlow-Action-Y"]);
        Assert.Equal("40", correlated["X-DevFlow-Action-To-X"]);
        Assert.Equal("60", correlated["X-DevFlow-Action-To-Y"]);
        Assert.Equal("27.5", correlated["X-DevFlow-Action-Delta-X"]);
        Assert.Equal("35.75", correlated["X-DevFlow-Action-Delta-Y"]);

        var unscoped = agent.Requests[1].Headers;
        Assert.DoesNotContain("X-DevFlow-Action-Id", unscoped.Keys);
        Assert.DoesNotContain("X-DevFlow-Action-Source", unscoped.Keys);

        var unsafeContext = agent.Requests[2].Headers;
        Assert.Equal("action-3", unsafeContext["X-DevFlow-Action-Id"]);
        Assert.DoesNotContain("X-DevFlow-Action-Coordinate-Space", unsafeContext.Keys);
        Assert.DoesNotContain("X-Injected", unsafeContext.Keys);
    }
}
