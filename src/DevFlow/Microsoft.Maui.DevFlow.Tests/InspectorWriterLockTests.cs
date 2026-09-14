using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.Cli.DevFlow.Inspector;

namespace Microsoft.Maui.DevFlow.Tests;

public class InspectorWriterLockTests
{
    [Fact]
    public async Task BoundedBrokerBodyReader_RejectsChunkedSizedOverflow()
    {
        using var within = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("12345"));
        Assert.Equal("12345", await BrokerServer.ReadBoundedBodyAsync(within, System.Text.Encoding.UTF8, 5));

        using var overflow = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("123456"));
        await Assert.ThrowsAnyAsync<Exception>(
            () => BrokerServer.ReadBoundedBodyAsync(overflow, System.Text.Encoding.UTF8, 5));
    }

    [Fact]
    public void FirstLeaseAcquires_SecondIsDenied_ForceTakeoverTransfersOwnership()
    {
        var leases = new MutationLeaseRegistry();

        var first = leases.Control("agent", "claim", "A", "web", "Browser", force: false);
        Assert.True(first.YouHold);
        Assert.False(first.HeldByOther);

        var second = leases.Control("agent", "claim", "B", "mcp", "MCP", force: false);
        Assert.False(second.YouHold);
        Assert.True(second.HeldByOther);
        Assert.Equal("Browser", second.Label);

        var takeover = leases.Control("agent", "claim", "B", "mcp", "MCP", force: true);
        Assert.True(takeover.YouHold);
        Assert.Equal("MCP", takeover.Label);

        var oldHolder = leases.Control("agent", "status", "A", "web", "Browser", force: false);
        Assert.False(oldHolder.YouHold);
        Assert.True(oldHolder.HeldByOther);
    }

    [Fact]
    public void ReleaseAllowsAnotherLeaseToAcquire()
    {
        var leases = new MutationLeaseRegistry();
        Assert.True(leases.Control("agent", "claim", "A", "web", "Browser", false).YouHold);
        Assert.False(leases.Control("agent", "release", "A", "web", "Browser", false).YouHold);
        Assert.True(leases.Control("agent", "claim", "B", "cli", "CLI", false).YouHold);
    }

    [Fact]
    public void LeaseTransaction_BlocksForcedTakeoverUntilEnd()
    {
        var leases = new MutationLeaseRegistry();
        Assert.True(leases.Control("agent", "claim", "A", "web", "Browser", false).YouHold);
        Assert.Equal("transaction-a", leases.Control(
            "agent", "begin", "A", "web", "Browser", false, "transaction-a").TransactionId);

        var blocked = leases.Control("agent", "claim", "B", "mcp", "MCP", force: true);
        Assert.False(blocked.YouHold);
        Assert.True(blocked.HeldByOther);

        Assert.True(leases.Control(
            "agent", "end", "A", "web", "Browser", false, "transaction-a").YouHold);
        Assert.True(leases.Control("agent", "claim", "B", "mcp", "MCP", force: true).YouHold);
    }

    [Fact]
    public void LeaseTransaction_StaleEndCannotReleaseNewerTransaction()
    {
        var leases = new MutationLeaseRegistry();
        Assert.True(leases.Control("agent", "claim", "A", "web", "Browser", false).YouHold);
        Assert.Equal("new", leases.Control(
            "agent", "begin", "A", "web", "Browser", false, "new").TransactionId);

        leases.Control("agent", "end", "A", "web", "Browser", false, "stale");

        Assert.False(leases.Control("agent", "claim", "B", "mcp", "MCP", force: true).YouHold);
        leases.Control("agent", "end", "A", "web", "Browser", false, "new");
        Assert.True(leases.Control("agent", "claim", "B", "mcp", "MCP", force: true).YouHold);
    }

    [Fact]
    public void LeaseTransaction_AbandonedTokenExpires()
    {
        long ticks = 0;
        var leases = new MutationLeaseRegistry(
            leaseDurationMs: 1_000,
            transactionDurationMs: 5_000,
            getTicks: () => ticks);
        Assert.True(leases.Control("agent", "claim", "A", "web", "Browser", false).YouHold);
        Assert.Equal("abandoned", leases.Control(
            "agent", "begin", "A", "web", "Browser", false, "abandoned").TransactionId);

        ticks = 5_001;

        Assert.True(leases.Control("agent", "claim", "B", "mcp", "MCP", force: true).YouHold);
    }

    [Fact]
    public async Task ExpiredLease_CanBeReclaimedByTheSameIdentity()
    {
        var leases = new MutationLeaseRegistry(leaseDurationMs: 1_000);
        Assert.True(leases.Control("agent", "claim", "A", "mcp", "MCP", false).YouHold);

        await Task.Delay(1_100);

        Assert.False(leases.Control("agent", "validate", "A", "mcp", "MCP", false).Allowed);
        Assert.True(leases.Control("agent", "claim", "A", "mcp", "MCP", false).YouHold);
    }

    [Theory]
    [InlineData("/api/tap", true)]
    [InlineData("/api/fill", true)]
    [InlineData("/api/setProperty", true)]
    [InlineData("/api/navigate", true)]
    [InlineData("/api/cdp/eval", true)]
    [InlineData("/api/flows/replay", true)]
    [InlineData("/api/flows/record/step", true)]
    [InlineData("/api/checkpoint", false)]
    [InlineData("/api/state", false)]
    [InlineData("/api/getProperty", false)]
    [InlineData("/api/logs", false)]
    [InlineData("/api/control", false)]
    public void IsMutation_GatesOnlyStateChangingPosts(string path, bool mutation)
        => Assert.Equal(mutation, InspectorServer.IsMutation(path));
}
