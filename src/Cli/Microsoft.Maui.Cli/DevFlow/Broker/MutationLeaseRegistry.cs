using System.Collections.Concurrent;

namespace Microsoft.Maui.Cli.DevFlow.Broker;

internal sealed class MutationLeaseRegistry : IWorkflowMutationLeaseRegistry
{
    internal const int DefaultLeaseDurationMs = 10_000;
    internal const int DefaultTransactionDurationMs = 5 * 60_000;

    private readonly ConcurrentDictionary<string, LeaseState> _leases = new(StringComparer.Ordinal);
    private readonly long _leaseDurationMs;
    private readonly long _transactionDurationMs;
    private readonly Func<long> _getTicks;

    public MutationLeaseRegistry(
        int leaseDurationMs = DefaultLeaseDurationMs,
        int transactionDurationMs = DefaultTransactionDurationMs,
        Func<long>? getTicks = null)
    {
        _leaseDurationMs = Math.Max(1_000, leaseDurationMs);
        _transactionDurationMs = Math.Max(1_000, transactionDurationMs);
        _getTicks = getTicks ?? (() => Environment.TickCount64);
    }

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
    {
        var state = _leases.GetOrAdd(agentId, static _ => new LeaseState());
        MutationLeaseSnapshot snapshot;
        lock (state.Gate)
        {
            ExpireIfNeeded(state);
            switch (action)
            {
                case "claim":
                    if (!string.IsNullOrWhiteSpace(leaseId) &&
                        (state.TransactionIds.Count == 0 || state.TransactionLeaseId == leaseId) &&
                        (force || state.LeaseId is null || state.LeaseId == leaseId))
                    {
                        SetHolder(state, leaseId, holderKind, label);
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

            snapshot = Snapshot(state, leaseId, transactionId);
        }
        return snapshot;
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

        var state = _leases.GetOrAdd(agentId, static _ => new LeaseState());
        lock (state.Gate)
        {
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

        var state = _leases.GetOrAdd(agentId, static _ => new LeaseState());
        lock (state.Gate)
        {
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

        var state = _leases.GetOrAdd(agentId, static _ => new LeaseState());
        lock (state.Gate)
        {
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

        var state = _leases.GetOrAdd(agentId, static _ => new LeaseState());
        lock (state.Gate)
        {
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
    }

    public void Remove(string agentId) => _leases.TryRemove(agentId, out _);

    public void Clear() => _leases.Clear();

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
    {
        if (!string.Equals(state.LeaseId, leaseId, StringComparison.Ordinal))
            state.AuthorityEpoch = checked(state.AuthorityEpoch + 1);
        state.LeaseId = leaseId;
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

    private static void Clear(LeaseState state)
    {
        state.LeaseId = null;
        state.HolderKind = null;
        state.Label = null;
        state.LastSeenTicks = 0;
        state.TransactionLeaseId = null;
        state.TransactionIds.Clear();
    }

    private sealed class LeaseState
    {
        public object Gate { get; } = new();
        public string? LeaseId { get; set; }
        public string? HolderKind { get; set; }
        public string? Label { get; set; }
        public long LastSeenTicks { get; set; }
        public string? TransactionLeaseId { get; set; }
        public Dictionary<string, long> TransactionIds { get; } = new(StringComparer.Ordinal);
        public long AuthorityEpoch { get; set; }
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
