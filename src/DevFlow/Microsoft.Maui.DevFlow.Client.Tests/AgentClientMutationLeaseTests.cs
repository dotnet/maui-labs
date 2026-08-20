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
}
