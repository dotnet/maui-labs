using Microsoft.Maui.Cli.DevFlow.Broker;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// How the lease registry stays bounded without losing anything a writer depends on.
/// <para>
/// The registry keeps per-key state forever by design: an entry outlives its holder because the
/// authority epoch recorded on it is what tells a client its hold changed hands. A long-lived
/// broker that sees a stream of one-off agent ids therefore accumulates an entry per id for its
/// whole lifetime. Reclaiming them is only safe if two things stay true — a key that is held, has
/// a transaction open, or was touched recently is never dropped, and an epoch is never allowed to
/// go backwards.
/// </para>
/// </summary>
public class MutationLeaseRegistryPruningTests
{
    private const string Inspector = "web-inspector";

    /// <summary>
    /// Idle is measured from the moment a key stops being in use, so the full window has to pass in
    /// silence after that — not after the claim.
    /// </summary>
    [Fact]
    public void AReleasedKeyIsKeptUntilTheWholeIdleWindowHasPassed()
    {
        var ticks = 0L;
        var leases = new MutationLeaseRegistry(1_000, 1_000, () => ticks);
        leases.Control("agent", "claim", "lease", Inspector, "Inspector", force: false);
        leases.Control("agent", "release", "lease", null, null, force: false);

        ticks = leases.IdleRetentionMs;
        leases.PruneIdleStates();
        Assert.Equal(1, leases.TrackedKeyCount);

        ticks = leases.IdleRetentionMs + 1;
        leases.PruneIdleStates();
        Assert.Equal(0, leases.TrackedKeyCount);
    }

    /// <summary>
    /// A lease that has only just expired is recent activity, not a long tail. The idle clock
    /// starts when the lease goes away, so an expiry observed during a sweep cannot also be the
    /// reason that sweep drops the key.
    /// </summary>
    [Fact]
    public void AnExpiredLeaseStartsTheIdleClockRatherThanBeingPrunedImmediately()
    {
        var ticks = 0L;
        var leases = new MutationLeaseRegistry(1_000, 1_000, () => ticks);
        leases.Control("agent", "claim", "lease", Inspector, "Inspector", force: false);

        // Far beyond both the lease window and the idle window measured from the claim.
        ticks = 10_000;
        leases.PruneIdleStates();
        Assert.Equal(1, leases.TrackedKeyCount);

        ticks = 10_000 + leases.IdleRetentionMs;
        leases.PruneIdleStates();
        Assert.Equal(1, leases.TrackedKeyCount);

        ticks = 10_000 + leases.IdleRetentionMs + 1;
        leases.PruneIdleStates();
        Assert.Equal(0, leases.TrackedKeyCount);
    }

    /// <summary>
    /// The two states a sweep must never touch: a live holder, and a key with a transaction in
    /// flight. Dropping either would admit a second writer to something already being mutated,
    /// which is the one thing the lease exists to prevent.
    /// </summary>
    [Fact]
    public void AHeldLeaseAndAnOpenTransactionSurviveASweepThatReclaimsEverythingElse()
    {
        var ticks = 0L;
        var leases = new MutationLeaseRegistry(10_000, 10_000, () => ticks);
        leases.Control("held", "claim", "lease-held", Inspector, "Inspector", force: false);
        leases.Control("txn", "claim", "lease-txn", Inspector, "Inspector", force: false);
        leases.Control("txn", "begin", "lease-txn", null, null, force: false, "transaction-1");
        leases.Control("idle", "claim", "lease-idle", Inspector, "Inspector", force: false);
        leases.Control("idle", "release", "lease-idle", null, null, force: false);
        Assert.Equal(3, leases.TrackedKeyCount);

        // Both live keys keep reporting in across a span far longer than the idle window; the
        // released one says nothing.
        for (var now = 5_000L; now <= 60_000L; now += 5_000L)
        {
            ticks = now;
            leases.Control("held", "heartbeat", "lease-held", null, null, force: false);
            leases.Control("txn", "heartbeat", "lease-txn", null, null, force: false, "transaction-1");
        }

        leases.PruneIdleStates();

        Assert.Equal(2, leases.TrackedKeyCount);
        Assert.True(leases.Control("held", "validate", "lease-held", null, null, force: false).YouHold);
        var transaction = leases.Control(
            "txn", "validate", "lease-txn", null, null, force: false, "transaction-1");
        Assert.True(transaction.YouHold);
        Assert.Equal("transaction-1", transaction.TransactionId);
    }

    /// <summary>
    /// Reclaiming an entry must not reclaim its authority epoch with it. A fresh entry restarting
    /// at zero would hand a client that cached a higher epoch a lower one from a genuinely
    /// different holder, and the client would read the difference as "nothing changed". The epoch
    /// is lifted into a registry-wide floor instead, so the sequence only ever climbs.
    /// </summary>
    [Fact]
    public void PruningLiftsTheAuthorityEpochRatherThanResettingIt()
    {
        var ticks = 0L;
        var leases = new MutationLeaseRegistry(1_000, 1_000, () => ticks);
        leases.Control("agent", "claim", "lease-1", Inspector, "Inspector", force: false);
        leases.Control("agent", "claim", "lease-2", Inspector, "Inspector", force: true);
        var before = leases.Control("agent", "status", null, null, null, force: false);
        Assert.True(before.AuthorityEpoch > 0);
        leases.Control("agent", "release", "lease-2", null, null, force: false);

        ticks = leases.IdleRetentionMs + 1;
        leases.PruneIdleStates();
        Assert.Equal(0, leases.TrackedKeyCount);

        var reclaimed = leases.Control("agent", "claim", "lease-3", Inspector, "Inspector", force: false);

        Assert.True(reclaimed.YouHold);
        Assert.True(reclaimed.AuthorityEpoch > before.AuthorityEpoch);
    }

    /// <summary>
    /// A key never seen before starts above every epoch the registry has ever issued, so the floor
    /// covers keys that were pruned before the new one existed too — not just the same name coming
    /// back.
    /// </summary>
    [Fact]
    public void ANewKeyStartsAboveTheHighestEpochAnyPrunedKeyReached()
    {
        var ticks = 0L;
        var leases = new MutationLeaseRegistry(1_000, 1_000, () => ticks);
        for (var claim = 1; claim <= 5; claim++)
            leases.Control("noisy", "claim", $"lease-{claim}", Inspector, "Inspector", force: true);
        var noisy = leases.Control("noisy", "status", null, null, null, force: false);
        leases.Control("noisy", "release", "lease-5", null, null, force: false);

        ticks = leases.IdleRetentionMs + 1;
        leases.PruneIdleStates();

        var fresh = leases.Control("brand-new", "claim", "lease-a", Inspector, "Inspector", force: false);
        Assert.True(fresh.AuthorityEpoch > noisy.AuthorityEpoch);
    }

    /// <summary>
    /// The bound holds without anyone asking for it: ordinary traffic sweeps the long tail while a
    /// key that keeps reporting in is carried through untouched.
    /// </summary>
    [Fact]
    public void OrdinaryTrafficReclaimsTheLongTailAndCarriesTheLiveKeyThrough()
    {
        var ticks = 0L;
        var leases = new MutationLeaseRegistry(1_000, 1_000, () => ticks);
        leases.Control("live", "claim", "lease-live", Inspector, "Inspector", force: false);
        for (var index = 0; index < 500; index++)
        {
            leases.Control($"gone-{index}", "claim", $"lease-{index}", Inspector, "one-off", force: false);
            leases.Control($"gone-{index}", "release", $"lease-{index}", null, null, force: false);
        }

        Assert.Equal(501, leases.TrackedKeyCount);

        for (var now = 500L; now <= 20_000L; now += 500L)
        {
            ticks = now;
            leases.Control("live", "heartbeat", "lease-live", null, null, force: false);
        }

        Assert.Equal(1, leases.TrackedKeyCount);
        Assert.True(leases.Control("live", "validate", "lease-live", null, null, force: false).YouHold);
        Assert.True(
            leases.Control("live", "validate", "lease-other", null, null, force: false).HeldByOther);
    }

    /// <summary>
    /// An unattributed holder — the Inspector's own hold, which recovery deliberately refuses to
    /// touch — is not reachable by a sweep either. Pruning is a bound on forgotten keys, not a
    /// second route to taking a lease away from whoever holds it.
    /// </summary>
    [Fact]
    public void AnUnattributedHolderIsNotReclaimedByASweep()
    {
        var ticks = 0L;
        var leases = new MutationLeaseRegistry(300_000, 300_000, () => ticks);
        leases.Control("agent", "claim", "lease-inspector", Inspector, "Inspector", force: false);

        // Well past the idle window, so the holder guard is the only thing that can keep the key:
        // stop the clock any earlier and the test would pass with that guard deleted.
        for (var now = 100_000L; now <= leases.IdleRetentionMs * 2; now += 100_000L)
        {
            ticks = now;
            leases.Control("agent", "heartbeat", "lease-inspector", null, null, force: false);
        }

        leases.PruneIdleStates();

        Assert.Equal(1, leases.TrackedKeyCount);
        Assert.Null(leases.OwnerOf("agent"));
        Assert.True(
            leases.Control("agent", "validate", "lease-other", null, null, force: false).HeldByOther);
    }

    /// <summary>
    /// Teardown removes keys under the same rules a sweep does, so it carries their epochs into the
    /// floor as well. Discarding the map wholesale reset the sequence, and a client that outlived
    /// the reset would then be handed a lower epoch by a genuinely different holder.
    /// </summary>
    [Fact]
    public void ClearingTheRegistryStillKeepsTheAuthorityEpochClimbing()
    {
        var ticks = 0L;
        var leases = new MutationLeaseRegistry(1_000, 1_000, () => ticks);
        for (var claim = 1; claim <= 4; claim++)
            leases.Control("agent", "claim", $"lease-{claim}", Inspector, "Inspector", force: true);
        var before = leases.Control("agent", "status", null, null, null, force: false);
        Assert.True(before.AuthorityEpoch > 0);

        leases.Clear();
        Assert.Equal(0, leases.TrackedKeyCount);

        var after = leases.Control("agent", "claim", "lease-next", Inspector, "Inspector", force: false);

        Assert.True(after.YouHold);
        Assert.True(after.AuthorityEpoch > before.AuthorityEpoch);
    }
}
