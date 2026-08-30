using System.Collections.Concurrent;

namespace Microsoft.Maui.Cli.DevFlow.Broker;

internal sealed class MutationLeaseRegistry : IWorkflowMutationLeaseRegistry
{
    internal const int DefaultLeaseDurationMs = 10_000;
    internal const int DefaultTransactionDurationMs = 5 * 60_000;

    /// <summary>
    /// Above this many tracked keys a sweep is worth running often; below it, once per idle
    /// retention window is enough to keep the map from growing without bound.
    /// </summary>
    private const int CrowdedKeyCount = 64;

    private readonly ConcurrentDictionary<string, LeaseState> _leases = new(StringComparer.Ordinal);
    private readonly long _leaseDurationMs;
    private readonly long _transactionDurationMs;
    private readonly long _idleRetentionMs;
    private readonly Func<long> _getTicks;
    private long _authorityEpochFloor;
    private long _lastSweepTicks;
    private int _sweeping;

    public MutationLeaseRegistry(
        int leaseDurationMs = DefaultLeaseDurationMs,
        int transactionDurationMs = DefaultTransactionDurationMs,
        Func<long>? getTicks = null)
    {
        _leaseDurationMs = Math.Max(1_000, leaseDurationMs);
        _transactionDurationMs = Math.Max(1_000, transactionDurationMs);
        // Long enough that neither window can be the reason a key looks idle: a lease that has only
        // just expired, or a transaction that has only just aged out, is still recent activity.
        _idleRetentionMs = Math.Max(_leaseDurationMs, _transactionDurationMs) * 4;
        _getTicks = getTicks ?? (() => Environment.TickCount64);
    }

    /// <summary>
    /// How long a key with no holder and no transaction is kept before it may be pruned. Exposed so
    /// tests can pin the boundary rather than guess at it.
    /// </summary>
    internal long IdleRetentionMs => _idleRetentionMs;

    /// <summary>The number of keys currently tracked, for tests that assert the map stays bounded.</summary>
    internal int TrackedKeyCount => _leases.Count;

    public MutationLeaseSnapshot Control(
        string agentId,
        string action,
        string? leaseId,
        string? holderKind,
        string? label,
        bool force)
        => Control(agentId, action, leaseId, holderKind, label, force, transactionId: null);

    public MutationLeaseSnapshot Control(
        string agentId,
        string action,
        string? leaseId,
        string? holderKind,
        string? label,
        bool force,
        string? transactionId)
        => Control(agentId, action, leaseId, holderKind, label, force, transactionId, owner: null);

    /// <summary>
    /// Claims and drives a lease, recording <paramref name="owner"/> as the identity the lease was
    /// taken on behalf of.
    /// <para>
    /// Only a caller that knows the lease is bound to one agent instance supplies an owner. A
    /// device-keyed lease is shared by surfaces that are not an app — device control, the companion
    /// MCP — and those stay unattributed on purpose, because "no recorded owner" is what makes
    /// disconnect recovery refuse to touch them.
    /// </para>
    /// </summary>
    public MutationLeaseSnapshot Control(
        string agentId,
        string action,
        string? leaseId,
        string? holderKind,
        string? label,
        bool force,
        string? transactionId,
        string? owner)
    {
        using var scope = EnterState(agentId);
        var state = scope.State!;
        ExpireIfNeeded(state);
        switch (action)
        {
            case "claim":
                if (!string.IsNullOrWhiteSpace(leaseId) &&
                    (state.TransactionIds.Count == 0 || state.TransactionLeaseId == leaseId) &&
                    (force || state.LeaseId is null || state.LeaseId == leaseId))
                {
                    SetHolder(state, leaseId, holderKind, label, owner);
                }
                break;
            case "heartbeat":
            case "validate":
                if (!string.IsNullOrWhiteSpace(leaseId) && state.LeaseId == leaseId)
                {
                    var now = _getTicks();
                    state.LastSeenTicks = now;
                    if (!string.IsNullOrWhiteSpace(transactionId) &&
                        state.TransactionLeaseId == leaseId &&
                        state.TransactionIds.ContainsKey(transactionId))
                    {
                        state.TransactionIds[transactionId] = now;
                    }
                }
                break;
            case "release":
                if (!string.IsNullOrWhiteSpace(leaseId) && state.LeaseId == leaseId &&
                    state.TransactionIds.Count == 0)
                {
                    Clear(state);
                }
                break;
            case "begin":
                if (string.IsNullOrWhiteSpace(transactionId))
                    throw new ArgumentException("transactionId is required for begin.", nameof(transactionId));
                if (!string.IsNullOrWhiteSpace(leaseId) && state.LeaseId == leaseId)
                {
                    state.TransactionLeaseId = leaseId;
                    state.TransactionIds[transactionId] = _getTicks();
                    state.LastSeenTicks = _getTicks();
                }
                break;
            case "end":
                if (string.IsNullOrWhiteSpace(transactionId))
                    throw new ArgumentException("transactionId is required for end.", nameof(transactionId));
                if (!string.IsNullOrWhiteSpace(leaseId) && state.TransactionLeaseId == leaseId &&
                    state.TransactionIds.Remove(transactionId))
                {
                    if (state.TransactionIds.Count == 0)
                        state.TransactionLeaseId = null;
                    if (state.LeaseId == leaseId)
                        state.LastSeenTicks = _getTicks();
                }
                break;
            case "status":
                break;
            default:
                throw new ArgumentException($"Unknown lease action '{action}'.", nameof(action));
        }

        return Snapshot(state, leaseId, transactionId);
    }

    public MutationLeaseSnapshot TransferAndBegin(
        string agentId,
        string sourceLeaseId,
        string targetLeaseId,
        string transactionId,
        string? holderKind,
        string? label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceLeaseId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetLeaseId);
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);

        using var scope = EnterState(agentId);
        var state = scope.State!;
        ExpireIfNeeded(state);
        if (!string.Equals(state.LeaseId, sourceLeaseId, StringComparison.Ordinal) ||
            state.TransactionIds.Count != 0)
        {
            return Snapshot(state, targetLeaseId, transactionId);
        }

        SetHolder(state, targetLeaseId, holderKind, label);
        state.TransactionLeaseId = targetLeaseId;
        state.TransactionIds[transactionId] = _getTicks();
        state.LastSeenTicks = _getTicks();
        return Snapshot(state, targetLeaseId, transactionId);
    }

    /// <summary>
    /// Atomically claims an available resource and begins its only active transaction.
    /// Device operations use this instead of the re-entrant app lease path because two hardware
    /// mutations from one Inspector tab are no safer to overlap than mutations from two tabs.
    /// </summary>
    public MutationLeaseSnapshot ClaimAndBeginExclusive(
        string agentId,
        string leaseId,
        string transactionId,
        string? holderKind,
        string? label,
        out bool claimed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseId);
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);

        using var scope = EnterState(agentId);
        var state = scope.State!;
        ExpireIfNeeded(state);
        claimed = false;
        if (state.TransactionIds.Count != 0 ||
            (state.LeaseId is not null &&
             !string.Equals(state.LeaseId, leaseId, StringComparison.Ordinal)))
        {
            return Snapshot(state, leaseId, transactionId);
        }

        if (state.LeaseId is null)
        {
            SetHolder(state, leaseId, holderKind, label);
            claimed = true;
        }

        state.TransactionLeaseId = leaseId;
        state.TransactionIds[transactionId] = _getTicks();
        state.LastSeenTicks = _getTicks();
        return Snapshot(state, leaseId, transactionId);
    }

    /// <summary>
    /// Begins a transaction only when the caller owns the resource and no transaction is active.
    /// </summary>
    public MutationLeaseSnapshot BeginExclusive(
        string agentId,
        string leaseId,
        string transactionId,
        string? holderKind,
        string? label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseId);
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);

        using var scope = EnterState(agentId);
        var state = scope.State!;
        ExpireIfNeeded(state);
        if (!string.Equals(state.LeaseId, leaseId, StringComparison.Ordinal) ||
            state.TransactionIds.Count != 0)
        {
            return Snapshot(state, leaseId, transactionId);
        }

        state.TransactionLeaseId = leaseId;
        state.TransactionIds[transactionId] = _getTicks();
        state.LastSeenTicks = _getTicks();
        return Snapshot(state, leaseId, transactionId);
    }

    /// <summary>
    /// Atomically takes an idle lease held by an allow-listed trusted host and opens a transaction on
    /// it. The Inspector is both the surface a human approves a run in and the holder of the app's
    /// single-writer lease while it is open, so an approved agent run would otherwise deadlock behind
    /// the very window that authorized it. Adoption is deliberately narrow: it refuses while any
    /// transaction is open, so a human actually driving the app is never interrupted mid-mutation,
    /// and it refuses any holder kind outside <paramref name="adoptableHolderKinds"/>.
    /// </summary>
    public MutationLeaseSnapshot TryAdoptIdleLease(
        string agentId,
        string targetLeaseId,
        string transactionId,
        string? holderKind,
        string? label,
        IReadOnlyCollection<string> adoptableHolderKinds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetLeaseId);
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);
        ArgumentNullException.ThrowIfNull(adoptableHolderKinds);

        using var scope = EnterState(agentId);
        var state = scope.State!;
        ExpireIfNeeded(state);

        // Nothing to adopt, or the caller already holds it: the ordinary claim path applies.
        if (state.LeaseId is null || string.Equals(state.LeaseId, targetLeaseId, StringComparison.Ordinal))
            return Snapshot(state, targetLeaseId, transactionId);

        // A transaction in flight means someone is mid-mutation. Never interrupt that.
        if (state.TransactionIds.Count != 0)
            return Snapshot(state, targetLeaseId, transactionId);

        if (state.HolderKind is null ||
            !adoptableHolderKinds.Contains(state.HolderKind, StringComparer.OrdinalIgnoreCase))
        {
            return Snapshot(state, targetLeaseId, transactionId);
        }

        SetHolder(state, targetLeaseId, holderKind, label);
        state.TransactionLeaseId = targetLeaseId;
        state.TransactionIds[transactionId] = _getTicks();
        state.LastSeenTicks = _getTicks();
        return Snapshot(state, targetLeaseId, transactionId);
    }

    /// <summary>
    /// Releases a lease only when the recorded owner still matches — the fast path back to a usable
    /// app or device after the agent holding it disappeared. This is the <em>only</em> way a lease
    /// is dropped by key, for every key shape.
    /// <para>
    /// No key is private enough to skip the check. A device-keyed lease is obviously shared: the
    /// same key serializes the app, the Inspector's device controls, and the companion MCP, and it
    /// survives the app that was running inside the device. But an app-keyed lease is shared too —
    /// the Inspector holds it, an approved run adopts it, and a relaunched app re-registers under
    /// the same id — so "the agent that owned this id went away" never on its own justifies taking
    /// the lease from whoever holds it now. The lease is cleared only when it is still held by the
    /// exact identity that took it, and an unattributed holder — every non-agent surface — is never
    /// touched.
    /// </para>
    /// <para>
    /// An open transaction refuses recovery outright, exactly as an explicit <c>release</c> does.
    /// A transaction is a mutation already in flight against hardware — a tap being delivered, a
    /// device rotating — and clearing the lease under it would admit a second writer mid-operation,
    /// which is the one thing the lease exists to prevent. Nothing is lost by waiting: transactions
    /// carry their own expiry, so the next call after that window recovers the lease normally.
    /// </para>
    /// </summary>
    /// <returns><c>true</c> when a lease was actually released.</returns>
    public bool RemoveIfOwnedBy(string leaseKey, string owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);

        using var scope = EnterExistingState(leaseKey);
        if (scope.State is not { } state)
            return false;

        ExpireIfNeeded(state);
        if (state.LeaseId is null ||
            state.TransactionIds.Count != 0 ||
            !string.Equals(state.Owner, owner, StringComparison.Ordinal))
        {
            return false;
        }

        Clear(state);
        // A cleared lease is an authority change even though nobody holds it yet: a client
        // caching an epoch has to see that its hold is gone.
        state.AuthorityEpoch = checked(state.AuthorityEpoch + 1);
        return true;
    }

    /// <summary>
    /// The identity currently recorded against a lease, for tests and diagnostics.
    /// <para>
    /// There is deliberately no unconditional per-key removal. Every release — explicit, or the
    /// recovery one a disconnect triggers — goes through <see cref="RemoveIfOwnedBy"/>, so no path
    /// exists that can drop a live holder or cut an open transaction. The idle sweep in
    /// <see cref="PruneIdleStates"/> is not an exception to that: it refuses the same states, and
    /// carries the authority epoch of anything it does reclaim into a floor rather than discarding
    /// it, so no client is ever handed a lower epoch than one it has already seen.
    /// </para>
    /// </summary>
    internal string? OwnerOf(string leaseKey)
    {
        using var scope = EnterExistingState(leaseKey);
        if (scope.State is not { } state)
            return null;

        ExpireIfNeeded(state);
        return state.LeaseId is null ? null : state.Owner;
    }

    /// <summary>
    /// Drops every tracked key, one at a time under its own gate, so teardown obeys the same rules
    /// a sweep does: the authority epoch is carried into the floor before the entry goes, and a
    /// caller that already holds the gate sees the pruned flag and re-acquires instead of mutating
    /// a detached state. Discarding the map wholesale skipped both for any key added while the
    /// removal was running.
    /// </summary>
    public void Clear()
    {
        // Enumeration is not a snapshot of one instant, so repeat while anything remains. The pass
        // count is bounded because a caller racing teardown can re-create a key indefinitely, and a
        // stray tracked key is a far better outcome than a discarded epoch or a detached lease.
        for (var pass = 0; pass < 8 && !_leases.IsEmpty; pass++)
        {
            foreach (var entry in _leases)
            {
                var state = entry.Value;
                lock (state.Gate)
                {
                    RaiseAuthorityEpochFloor(state.AuthorityEpoch);
                    state.Pruned = true;
                    if (!_leases.TryRemove(entry))
                        state.Pruned = false;
                }
            }
        }
    }

    /// <summary>
    /// Drops per-key state that has been unheld, transaction-free, and untouched for longer than
    /// <see cref="IdleRetentionMs"/>, so a broker that sees a long tail of one-off agent ids does
    /// not accumulate an entry per id for its whole lifetime.
    /// <para>
    /// The bound is deliberately the *only* thing this adds. It never touches a key that holds a
    /// lease, has a transaction open, or has been used recently — those are exactly the states an
    /// active writer depends on, and the registry's whole job is to not lose them. A key that has
    /// merely been cleared is not enough on its own either: clearing sets the idle clock, so the
    /// full retention window has to pass in silence after it.
    /// </para>
    /// <para>
    /// Removing an entry would otherwise discard its authority epoch, and a later key of the same
    /// name would restart at zero — a client caching a higher epoch would then be handed a lower
    /// one by a genuinely different holder and see no change. So the epoch is not discarded: it is
    /// lifted into a registry-wide floor that every entry created afterwards starts from. Epochs
    /// stay monotonic, which is the only property anything compares them for.
    /// </para>
    /// </summary>
    internal void PruneIdleStates()
    {
        Interlocked.Exchange(ref _lastSweepTicks, _getTicks());
        foreach (var entry in _leases)
            TryPruneIdleState(entry);
    }

    private void PruneIdleStatesIfDue(string excludedKey)
    {
        // A crowded map is swept often enough to stay bounded; a quiet one is left alone, because
        // sweeping takes every key's gate and there is nothing to reclaim.
        var interval = _leases.Count > CrowdedKeyCount ? _leaseDurationMs : _idleRetentionMs;
        if (_getTicks() - Interlocked.Read(ref _lastSweepTicks) < interval)
            return;
        if (Interlocked.CompareExchange(ref _sweeping, 1, 0) != 0)
            return;

        try
        {
            Interlocked.Exchange(ref _lastSweepTicks, _getTicks());
            foreach (var entry in _leases)
            {
                if (string.Equals(entry.Key, excludedKey, StringComparison.Ordinal))
                    continue;
                TryPruneIdleState(entry);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _sweeping, 0);
        }
    }

    private void TryPruneIdleState(KeyValuePair<string, LeaseState> entry)
    {
        var state = entry.Value;
        // A gate someone else holds means the key is in use right now, which is the opposite of
        // idle. Waiting for it would only make a sweep contend with real work.
        if (!Monitor.TryEnter(state.Gate))
            return;

        try
        {
            if (state.Pruned)
                return;
            ExpireIfNeeded(state);
            if (state.LeaseId is not null || state.TransactionIds.Count != 0)
                return;
            if (_getTicks() - state.IdleSinceTicks <= _idleRetentionMs)
                return;

            RaiseAuthorityEpochFloor(state.AuthorityEpoch);
            state.Pruned = true;
            // Compare-and-remove on the value, so a state some other thread already replaced is
            // left alone. A caller that entered the gate before the flag was set re-reads it and
            // starts again on the live entry.
            if (!_leases.TryRemove(entry))
                state.Pruned = false;
        }
        finally
        {
            Monitor.Exit(state.Gate);
        }
    }

    private void RaiseAuthorityEpochFloor(long epoch)
    {
        while (true)
        {
            var current = Interlocked.Read(ref _authorityEpochFloor);
            if (epoch <= current)
                return;
            if (Interlocked.CompareExchange(ref _authorityEpochFloor, epoch, current) == current)
                return;
        }
    }

    /// <summary>
    /// Takes the gate for <paramref name="agentId"/>, creating the state when there is none, and
    /// re-checks that a concurrent sweep did not remove it from under the caller. Without that
    /// re-check a claim could land on a detached object and vanish.
    /// </summary>
    private LeaseScope EnterState(string agentId)
    {
        PruneIdleStatesIfDue(agentId);
        while (true)
        {
            var state = _leases.GetOrAdd(agentId, _ => NewState());
            Monitor.Enter(state.Gate);
            if (!state.Pruned)
                return new LeaseScope(state);
            Monitor.Exit(state.Gate);
        }
    }

    /// <summary>As <see cref="EnterState"/>, but never creates a key that is not already tracked.</summary>
    private LeaseScope EnterExistingState(string agentId)
    {
        while (_leases.TryGetValue(agentId, out var state))
        {
            Monitor.Enter(state.Gate);
            if (!state.Pruned)
                return new LeaseScope(state);
            Monitor.Exit(state.Gate);
        }
        return new LeaseScope(null);
    }

    private LeaseState NewState() => new()
    {
        AuthorityEpoch = Interlocked.Read(ref _authorityEpochFloor),
        IdleSinceTicks = _getTicks(),
    };

    private readonly struct LeaseScope(LeaseState? state) : IDisposable
    {
        public LeaseState? State { get; } = state;

        public void Dispose()
        {
            if (State is not null)
                Monitor.Exit(State.Gate);
        }
    }

    private void ExpireIfNeeded(LeaseState state)
    {
        var now = _getTicks();
        foreach (var transactionId in state.TransactionIds
            .Where(pair => now - pair.Value > _transactionDurationMs)
            .Select(pair => pair.Key)
            .ToArray())
        {
            state.TransactionIds.Remove(transactionId);
        }
        if (state.TransactionIds.Count == 0)
            state.TransactionLeaseId = null;
        if (state.TransactionIds.Count > 0)
            return;
        if (state.LeaseId is not null &&
            now - state.LastSeenTicks > _leaseDurationMs)
        {
            Clear(state);
        }
    }

    private void SetHolder(LeaseState state, string leaseId, string? holderKind, string? label)
        => SetHolder(state, leaseId, holderKind, label, owner: null);

    private void SetHolder(LeaseState state, string leaseId, string? holderKind, string? label, string? owner)
    {
        if (!string.Equals(state.LeaseId, leaseId, StringComparison.Ordinal))
            state.AuthorityEpoch = checked(state.AuthorityEpoch + 1);
        state.LeaseId = leaseId;
        state.Owner = owner;
        state.HolderKind = Clean(holderKind) ?? "unknown";
        state.Label = Clean(label);
        state.LastSeenTicks = _getTicks();
    }

    private MutationLeaseSnapshot Snapshot(
        LeaseState state,
        string? callerLeaseId,
        string? callerTransactionId)
    {
        var youHold = state.LeaseId is not null &&
            !string.IsNullOrWhiteSpace(callerLeaseId) &&
            string.Equals(state.LeaseId, callerLeaseId, StringComparison.Ordinal);
        return new MutationLeaseSnapshot(
            Allowed: youHold,
            YouHold: youHold,
            HeldByOther: state.LeaseId is not null && !youHold,
            LeaseId: youHold ? state.LeaseId : null,
            TransactionId: youHold && !string.IsNullOrWhiteSpace(callerTransactionId) &&
                state.TransactionIds.ContainsKey(callerTransactionId) ? callerTransactionId : null,
            HolderKind: state.HolderKind,
            Label: state.Label,
            ExpiresInMs: state.LeaseId is null
                ? 0
                : Math.Max(0, _leaseDurationMs - (_getTicks() - state.LastSeenTicks)))
        {
            AuthorityEpoch = state.AuthorityEpoch
        };
    }

    private static string? Clean(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void Clear(LeaseState state)
    {
        state.LeaseId = null;
        state.Owner = null;
        state.HolderKind = null;
        state.Label = null;
        state.LastSeenTicks = 0;
        state.TransactionLeaseId = null;
        state.TransactionIds.Clear();
        // Becoming unheld starts the idle clock rather than making the key immediately prunable:
        // a lease that has only just been released or expired is recent activity, not a long tail.
        state.IdleSinceTicks = _getTicks();
    }

    private sealed class LeaseState
    {
        public object Gate { get; } = new();
        public string? LeaseId { get; set; }
        public string? Owner { get; set; }
        public string? HolderKind { get; set; }
        public string? Label { get; set; }
        public long LastSeenTicks { get; set; }
        public string? TransactionLeaseId { get; set; }
        public Dictionary<string, long> TransactionIds { get; } = new(StringComparer.Ordinal);
        public long AuthorityEpoch { get; set; }

        /// <summary>
        /// When this key last stopped being in use. Only meaningful while nothing is held, and it
        /// is what makes "long idle" a measurement rather than a guess.
        /// </summary>
        public long IdleSinceTicks { get; set; }

        /// <summary>
        /// Set under <see cref="Gate"/> when a sweep removes the entry, so a caller holding a
        /// reference from just before the removal notices and re-acquires the live one instead of
        /// mutating an object nothing can see.
        /// </summary>
        public bool Pruned { get; set; }
    }
}

internal sealed record MutationLeaseSnapshot(
    bool Allowed,
    bool YouHold,
    bool HeldByOther,
    string? LeaseId,
    string? TransactionId,
    string? HolderKind,
    string? Label,
    long ExpiresInMs)
{
    public long AuthorityEpoch { get; init; }
}
