using Microsoft.Maui.Cli.DevFlow.Inspector;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// The pending layout suppression proposal store is reachable from an Inspector page, so it has to
/// be bounded by something the caller does not control. Expiry alone is not a bound: the creation
/// rate is the caller's, the ten-minute window is not.
/// </summary>
public sealed class LayoutSuppressionProposalStoreTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Store_NeverGrowsBeyondItsCap()
    {
        var store = new LayoutSuppressionProposalStore();

        for (var index = 0; index < LayoutSuppressionProposalStore.MaximumProposals * 8; index++)
            store.Add(Proposal($"p{index:D4}", Now.AddMinutes(10)), Now);

        Assert.Equal(LayoutSuppressionProposalStore.MaximumProposals, store.Count);
    }

    [Fact]
    public void Store_PrunesExpiredProposalsBeforeEvictingLiveOnes()
    {
        var store = new LayoutSuppressionProposalStore();
        for (var index = 0; index < LayoutSuppressionProposalStore.MaximumProposals; index++)
            store.Add(Proposal($"stale{index:D4}", Now.AddMinutes(1)), Now);
        Assert.Equal(LayoutSuppressionProposalStore.MaximumProposals, store.Count);

        var later = Now.AddMinutes(5);
        store.Add(Proposal("fresh", later.AddMinutes(10)), later);

        Assert.Equal(1, store.Count);
        Assert.True(store.TryGet("fresh", out var fresh));
        Assert.Equal("fresh", fresh!.ProposalId);
        Assert.False(store.TryGet("stale0000", out _));
    }

    [Fact]
    public void Store_EvictsTheProposalsClosestToExpiringAndIsDeterministic()
    {
        var first = BuildSaturatedStore();
        var second = BuildSaturatedStore();

        var firstIds = SurvivingIds(first);
        var secondIds = SurvivingIds(second);

        Assert.Equal(firstIds, secondIds);
        Assert.Equal(LayoutSuppressionProposalStore.MaximumProposals, firstIds.Count);
        // The earliest-expiring entries went first, so the newest window is what survives.
        Assert.DoesNotContain("live0000", firstIds);
        Assert.Contains("newcomer", firstIds);
    }

    [Fact]
    public void Store_ReplacingAnExistingProposalDoesNotEvictAnything()
    {
        var store = new LayoutSuppressionProposalStore();
        for (var index = 0; index < LayoutSuppressionProposalStore.MaximumProposals; index++)
            store.Add(Proposal($"live{index:D4}", Now.AddMinutes(10 + index)), Now);
        Assert.Equal(LayoutSuppressionProposalStore.MaximumProposals, store.Count);

        store.Add(
            Proposal("live0000", Now.AddMinutes(10)) with { Reason = "Updated reason" },
            Now);

        Assert.Equal(LayoutSuppressionProposalStore.MaximumProposals, store.Count);
        Assert.True(store.TryGet("live0000", out var replaced));
        Assert.Equal("Updated reason", replaced!.Reason);
    }

    [Fact]
    public void Store_ReturnsExpiredProposalsSoCallersCanReportTheAccurateReason()
    {
        var store = new LayoutSuppressionProposalStore();
        store.Add(Proposal("only", Now.AddMinutes(10)), Now);

        Assert.True(store.TryGet("only", out var found));
        Assert.Equal(Now.AddMinutes(10), found!.ExpiresAt);
        Assert.False(store.TryGet("missing", out _));
        Assert.False(store.TryGet(null, out _));
        Assert.False(store.TryGet("   ", out _));

        store.Remove("only");
        Assert.False(store.TryGet("only", out _));
        Assert.Equal(0, store.Count);
    }

    private static LayoutSuppressionProposalStore BuildSaturatedStore()
    {
        var store = new LayoutSuppressionProposalStore();
        for (var index = 0; index < LayoutSuppressionProposalStore.MaximumProposals; index++)
            store.Add(Proposal($"live{index:D4}", Now.AddMinutes(10 + index)), Now);
        store.Add(Proposal("newcomer", Now.AddMinutes(90)), Now);
        return store;
    }

    private static List<string> SurvivingIds(LayoutSuppressionProposalStore store)
    {
        var ids = new List<string>();
        for (var index = 0; index < LayoutSuppressionProposalStore.MaximumProposals; index++)
        {
            if (store.TryGet($"live{index:D4}", out _))
                ids.Add($"live{index:D4}");
        }
        if (store.TryGet("newcomer", out _))
            ids.Add("newcomer");
        return ids;
    }

    private static LayoutSuppressionProposal Proposal(string id, DateTimeOffset expiresAt)
        => new(
            ProposalId: id,
            Action: "add-exact-suppression",
            FindingId: $"finding-{id}",
            SuppressionKey: $"key-{id}",
            Reason: "Reviewed in DevFlow Inspector",
            PolicyStartPath: "/repo/app",
            PolicyPath: "/repo/app/.mauidevflow",
            ExpectedPolicyDigest: "digest",
            DiagnosticsRevision: "revision",
            AgentId: "agent",
            AgentInstanceId: "instance",
            ExpiresAt: expiresAt);
}
