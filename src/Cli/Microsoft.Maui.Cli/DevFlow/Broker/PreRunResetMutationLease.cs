namespace Microsoft.Maui.Cli.DevFlow.Broker;

internal static class WorkflowMutationLeasePolicy
{
    internal static IReadOnlyCollection<string> AdoptableApprovalHostKinds { get; } =
        Array.AsReadOnly(new[] { "vscode", "web", "canvas" });
}

/// <summary>
/// Holds the app's single-writer lease while the broker establishes reset evidence for a run.
/// </summary>
/// <remarks>
/// The Inspector issues the human approval and normally keeps an idle writer lease while it is
/// open. A pre-run reset must adopt that trusted lease before invoking the app's reset action, or
/// the approved run deadlocks behind the surface that approved it. Adoption stays fail-closed for
/// untrusted holders and while any transaction is active.
/// </remarks>
internal sealed class PreRunResetMutationLease : IDisposable
{
    private const string HolderKind = "repair-validation";
    private const string Label = "app-state-reset";

    private readonly IWorkflowMutationLeaseRegistry _leases;
    private readonly string _agentId;
    private bool _disposed;
    private bool _transactionOpen = true;

    private PreRunResetMutationLease(
        IWorkflowMutationLeaseRegistry leases,
        string agentId,
        string leaseId,
        string transactionId)
    {
        _leases = leases;
        _agentId = agentId;
        LeaseId = leaseId;
        TransactionId = transactionId;
    }

    internal string LeaseId { get; }
    internal string TransactionId { get; }
    internal WorkflowRunLeaseHandoff? Handoff { get; private set; }

    internal static PreRunResetMutationLease? TryAcquire(
        IWorkflowMutationLeaseRegistry leases,
        string agentId)
    {
        ArgumentNullException.ThrowIfNull(leases);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        var leaseId = $"pre-run-reset-{Guid.NewGuid():N}";
        var transactionId = $"pre-run-reset-{Guid.NewGuid():N}";
        var claimed = leases.Control(
            agentId,
            "claim",
            leaseId,
            HolderKind,
            Label,
            force: false,
            transactionId: null);

        var acquired = claimed.YouHold
            ? leases.Control(
                agentId,
                "begin",
                leaseId,
                HolderKind,
                Label,
                force: false,
                transactionId)
            : leases.TryAdoptIdleLease(
                agentId,
                leaseId,
                transactionId,
                HolderKind,
                Label,
                WorkflowMutationLeasePolicy.AdoptableApprovalHostKinds);

        if (!acquired.Allowed ||
            !acquired.YouHold ||
            !string.Equals(acquired.TransactionId, transactionId, StringComparison.Ordinal))
        {
            Release(leases, agentId, leaseId, transactionId);
            return null;
        }

        return new PreRunResetMutationLease(leases, agentId, leaseId, transactionId);
    }

    /// <summary>
    /// Ends the reset transaction while retaining the writer lease for an atomic run handoff.
    /// </summary>
    internal bool PrepareHandoff()
    {
        if (_disposed || !_transactionOpen)
            return false;

        var ended = _leases.Control(
            _agentId,
            "end",
            LeaseId,
            HolderKind,
            Label,
            force: false,
            TransactionId);
        _transactionOpen = false;
        if (!ended.YouHold)
            return false;

        Handoff = new WorkflowRunLeaseHandoff(LeaseId, HolderKind, Label);
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Release(_leases, _agentId, LeaseId, TransactionId, _transactionOpen);
    }

    private static void Release(
        IWorkflowMutationLeaseRegistry leases,
        string agentId,
        string leaseId,
        string transactionId,
        bool transactionOpen = true)
    {
        if (transactionOpen)
        {
            leases.Control(
                agentId,
                "end",
                leaseId,
                HolderKind,
                Label,
                force: false,
                transactionId);
        }

        leases.Control(
            agentId,
            "release",
            leaseId,
            HolderKind,
            Label,
            force: false,
            transactionId: null);
    }
}
