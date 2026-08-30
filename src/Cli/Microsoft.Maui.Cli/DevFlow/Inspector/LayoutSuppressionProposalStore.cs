namespace Microsoft.Maui.Cli.DevFlow.Inspector;

/// <summary>
/// One unapproved proposal to add or remove an exact layout suppression in the project's
/// <c>.mauidevflow</c>. It carries no authority on its own: a trusted native host still has to
/// confirm the exact policy and proposal digests before anything is written.
/// </summary>
internal sealed record LayoutSuppressionProposal(
    string ProposalId,
    string Action,
    string FindingId,
    string SuppressionKey,
    string Reason,
    string PolicyStartPath,
    string PolicyPath,
    string ExpectedPolicyDigest,
    string DiagnosticsRevision,
    string AgentId,
    string AgentInstanceId,
    DateTimeOffset ExpiresAt)
{
    public string ProposalDigest { get; init; } = "";
}

/// <summary>
/// Bounded, deterministic in-memory store for pending layout suppression proposals.
///
/// Proposals are created by an unauthenticated-in-itself Inspector interaction, so an unbounded
/// dictionary would let a page that keeps clicking "suppress" grow broker memory without limit —
/// every entry is retained for its full ten-minute expiry window. Expiry pruning alone is not a
/// bound, because the growth rate is under the caller's control and the expiry is not.
///
/// The store therefore prunes expired entries first and then, if the cap is still reached, evicts
/// the entries closest to expiring. Every proposal is issued with the same fixed lifetime, so in
/// practice that is the oldest entry and the one a person is least likely to still be looking at.
///
/// "Deterministic" here means the survivors are a pure function of the entries present: eviction
/// orders on expiry and breaks ties on the proposal id, so it never depends on dictionary or
/// insertion order. It is not a claim of reproducibility across runs — real proposal ids are fresh
/// GUIDs and real expiries come from the wall clock, so a second run of the same user actions
/// produces different entries and may keep different ones.
///
/// Eviction only ever removes an unapproved proposal: it can cost a person a re-click, never a
/// wrongly applied write.
/// </summary>
internal sealed class LayoutSuppressionProposalStore
{
    /// <summary>Small on purpose: a human reviews these one at a time in a native host.</summary>
    internal const int MaximumProposals = 32;

    private readonly object _gate = new();
    private readonly Dictionary<string, LayoutSuppressionProposal> _proposals =
        new(StringComparer.Ordinal);

    public int Count
    {
        get
        {
            lock (_gate)
                return _proposals.Count;
        }
    }

    /// <summary>Prunes expired entries, enforces the cap, then records <paramref name="proposal"/>.</summary>
    public void Add(LayoutSuppressionProposal proposal, DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        lock (_gate)
        {
            foreach (var expired in _proposals.Values
                         .Where(item => item.ExpiresAt <= utcNow)
                         .Select(item => item.ProposalId)
                         .ToList())
            {
                _proposals.Remove(expired);
            }

            if (!_proposals.ContainsKey(proposal.ProposalId))
            {
                foreach (var evicted in _proposals.Values
                             .OrderBy(item => item.ExpiresAt)
                             .ThenBy(item => item.ProposalId, StringComparer.Ordinal)
                             .Take(Math.Max(0, _proposals.Count - (MaximumProposals - 1)))
                             .Select(item => item.ProposalId)
                             .ToList())
                {
                    _proposals.Remove(evicted);
                }
            }

            _proposals[proposal.ProposalId] = proposal;
        }
    }

    /// <summary>
    /// Looks up a proposal without filtering on expiry, so a caller can still tell "never existed"
    /// apart from "expired" and report the accurate reason.
    /// </summary>
    public bool TryGet(string? proposalId, out LayoutSuppressionProposal? proposal)
    {
        proposal = null;
        if (string.IsNullOrWhiteSpace(proposalId))
            return false;
        lock (_gate)
            return _proposals.TryGetValue(proposalId, out proposal);
    }

    public void Remove(string? proposalId)
    {
        if (string.IsNullOrWhiteSpace(proposalId))
            return;
        lock (_gate)
            _proposals.Remove(proposalId);
    }
}
