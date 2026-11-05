using System.Collections.Concurrent;

namespace Microsoft.Maui.Cli.DevFlow.Broker;

internal sealed class MutationLeaseRegistry
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
                        state.LastSeenTicks = Environment.TickCount64;
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
                : Math.Max(0, _leaseDurationMs - (_getTicks() - state.LastSeenTicks)));
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
    long ExpiresInMs);
