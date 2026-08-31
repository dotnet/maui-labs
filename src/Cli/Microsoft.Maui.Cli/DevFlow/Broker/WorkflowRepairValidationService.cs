using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.Cli.DevFlow.Broker;

/// <summary>
/// Host-owned lifecycle hook for transient repair validation. Implementations build/reset/seed
/// outside the broker, then replay only with an in-memory candidate override.
/// </summary>
internal interface IWorkflowRepairValidationHost
{
    Task<WorkflowRepairLifecycleValidation> HardResetAsync(
        WorkflowRepairTransientValidationRequest request,
        CancellationToken cancellationToken);

    Task<WorkflowRepairReplayValidation> ReplayWithInMemorySelectorOverrideAsync(
        WorkflowRepairTransientValidationRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// The seed, backend, and collection facts a lifecycle owner has applied to the connected app,
    /// or null when no component can attest them. A host that cannot attest them returns null so the
    /// classification records the fact as missing rather than inventing one.
    /// </summary>
    Task<WorkflowRepairAttestedState?> ObserveAttestedStateAsync(CancellationToken cancellationToken)
        => Task.FromResult<WorkflowRepairAttestedState?>(null);
}

/// <summary>One bounded transient validation request. It contains no source-write operation.</summary>
internal sealed class WorkflowRepairTransientValidationRequest
{
    public MauiFlowRepairProposal Proposal { get; init; } = new();
    public MauiFlowRepairEligibilityDecision? Eligibility { get; init; }
    public MauiFlowReplayEligibilityDecision? ReplaySafety { get; init; }
    public MauiFlowCheckpoint? ClassifiedCheckpoint { get; init; }
    public string? ValidationGrantDigest { get; init; }
    public bool InMemorySelectorOverrideOnly { get; init; } = true;
    public bool AllowDownstreamContinuation { get; init; }

    /// <summary>
    /// The trusted workspace flow the proposal was generated against. A host clones it in memory
    /// to apply the proposed selector; neither the clone nor the override is ever persisted.
    /// </summary>
    public MauiFlow? SourceFlow { get; init; }

    /// <summary>The trusted workspace plan that carries the flow's safety policy.</summary>
    public MauiTestPlan? SourcePlan { get; init; }
}

/// <summary>Observed reset/seed/checkpoint proof returned by a platform lifecycle implementation.</summary>
internal sealed class WorkflowRepairLifecycleValidation
{
    public bool Succeeded { get; init; }
    public MauiFlowCheckpoint? ExpectedCheckpoint { get; init; }
    public MauiFlowCheckpoint? ObservedCheckpoint { get; init; }
    public MauiElementFingerprint? ObservedFingerprint { get; init; }
    public List<string> EvidenceIds { get; init; } = [];
    public string? FailureCode { get; init; }
}

/// <summary>Observed replay proof returned by a platform lifecycle implementation.</summary>
internal sealed class WorkflowRepairReplayValidation
{
    public bool ReachedFailedStep { get; init; }
    public bool Passed { get; init; }
    public string? RunId { get; init; }
    public int? CandidateMatchCount { get; init; }
    public MauiElementFingerprint? ObservedFingerprint { get; init; }
    public bool SemanticFingerprintMatches { get; init; }
    public bool HardAssertionsUnchanged { get; init; }
    public bool IndependentOracleSucceeded { get; init; }
    public bool ContinuedDownstream { get; init; }
    public List<string> EvidenceIds { get; init; } = [];
    public string? FailureCode { get; init; }
}

/// <summary>
/// Deterministically evaluates lifecycle/replay facts for one transient selector override. The
/// service never persists the override or invokes source-writing APIs.
/// </summary>
internal sealed class WorkflowRepairValidationService
{
    private readonly IWorkflowRepairValidationHost _host;

    internal WorkflowRepairValidationService(IWorkflowRepairValidationHost host)
        => _host = host ?? throw new ArgumentNullException(nameof(host));

    internal async Task<WorkflowRepairValidationRecord> ValidateAsync(
        WorkflowRepairTransientValidationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var facts = new List<string>();
        if (!request.InMemorySelectorOverrideOnly)
            facts.Add("in-memory-override-required");
        if (request.Eligibility?.Eligible != true)
            facts.Add("repair-eligibility-required");
        if (request.ReplaySafety?.RepairValidationAllowed != true ||
            request.ReplaySafety.RepairEligibility != true ||
            string.Equals(
                request.ReplaySafety.SideEffectPolicy,
                MauiFlowSideEffectPolicies.NonReplayable,
                StringComparison.Ordinal))
        {
            facts.Add("repair-replay-safety-required");
        }
        if (request.ClassifiedCheckpoint is null ||
            !CheckpointsMatch(request.Eligibility?.CurrentCheckpoint, request.ClassifiedCheckpoint))
        {
            facts.Add("classified-checkpoint-required");
        }
        if (request.Proposal.Candidate is null ||
            request.Proposal.ProposedSelector is null ||
            request.Proposal.UnchangedAssertionsProof?.Unchanged != true ||
            request.Proposal.UnchangedAssertionsProof.ActionsUnchanged != true ||
            request.Proposal.UnchangedAssertionsProof.ValuesUnchanged != true ||
            request.Proposal.UnchangedAssertionsProof.OrderUnchanged != true)
        {
            facts.Add("selector-only-invariant-required");
        }
        if (facts.Count > 0)
            return Failed(facts, "validation-request-invalid");

        WorkflowRepairLifecycleValidation lifecycle;
        try
        {
            lifecycle = await _host.HardResetAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return Failed(["host-reset-exception"], "reset-failed");
        }

        if (!lifecycle.Succeeded)
            return Failed(["hard-reset-failed"], lifecycle.FailureCode ?? "reset-failed", lifecycle.EvidenceIds);
        if (!CheckpointsMatch(request.ClassifiedCheckpoint, lifecycle.ObservedCheckpoint) ||
            lifecycle.ExpectedCheckpoint is not null &&
            !CheckpointsMatch(request.ClassifiedCheckpoint, lifecycle.ExpectedCheckpoint))
            return Failed(["post-reset-checkpoint-mismatch"], "precondition-unsatisfied", lifecycle.EvidenceIds);

        WorkflowRepairReplayValidation replay;
        try
        {
            replay = await _host.ReplayWithInMemorySelectorOverrideAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return Failed(["host-replay-exception"], "infrastructure", lifecycle.EvidenceIds);
        }

        var evidence = lifecycle.EvidenceIds
            .Concat(replay.EvidenceIds)
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .Take(32)
            .ToList();
        if (!replay.ReachedFailedStep)
            facts.Add("failed-step-not-reached");
        if (replay.CandidateMatchCount != 1)
            facts.Add("candidate-not-uniquely-resolved");
        if (replay.SemanticFingerprintMatches != true ||
            !MauiRepairFingerprintComparer.SemanticallyMatches(
                request.Proposal.Candidate!.Fingerprint,
                replay.ObservedFingerprint))
        {
            facts.Add("semantic-fingerprint-mismatch");
        }
        if (!replay.HardAssertionsUnchanged)
            facts.Add("hard-assertion-invariant-failed");
        if (!replay.IndependentOracleSucceeded)
            facts.Add("independent-oracle-failed");
        if (replay.ContinuedDownstream &&
            request.ReplaySafety?.DownstreamContinuationAllowed != true)
        {
            facts.Add("downstream-continuation-prohibited");
        }
        if (!replay.Passed)
            facts.Add("transient-replay-failed");

        return facts.Count == 0
            ? new WorkflowRepairValidationRecord
            {
                Passed = true,
                RunIds = string.IsNullOrWhiteSpace(replay.RunId) ? [] : [replay.RunId],
                EvidenceIds = evidence,
                RecordedAt = DateTimeOffset.UtcNow,
            }
            : Failed(facts, replay.FailureCode ?? "validation-failed", evidence, replay.RunId);
    }

    private static WorkflowRepairValidationRecord Failed(
        IEnumerable<string> facts,
        string failureCode,
        IEnumerable<string>? evidenceIds = null,
        string? runId = null)
        => new()
        {
            Passed = false,
            RunIds = string.IsNullOrWhiteSpace(runId) ? [] : [runId],
            EvidenceIds = (evidenceIds ?? [])
                .Where(static id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .Take(32)
                .ToList(),
            FailureCode = failureCode,
            FailureFacts = facts
                .Where(static fact => !string.IsNullOrWhiteSpace(fact))
                .Distinct(StringComparer.Ordinal)
                .Take(32)
                .ToList(),
            RecordedAt = DateTimeOffset.UtcNow,
        };

    private static bool CheckpointsMatch(MauiFlowCheckpoint? expected, MauiFlowCheckpoint? observed)
    {
        if (expected is null || observed is null)
            return false;
        return Equals(expected.AppBuildFingerprint, observed.AppBuildFingerprint) &&
            Equals(expected.AgentInstanceId, observed.AgentInstanceId) &&
            Equals(expected.SeedFingerprint, observed.SeedFingerprint) &&
            Equals(expected.BackendStateFingerprint, observed.BackendStateFingerprint) &&
            Equals(expected.Route, observed.Route) &&
            Equals(expected.Window, observed.Window) &&
            Equals(expected.Modal, observed.Modal) &&
            Equals(expected.Locale, observed.Locale) &&
            Equals(expected.Theme, observed.Theme) &&
            Equals(expected.Orientation, observed.Orientation) &&
            Equals(expected.DisplayProfile, observed.DisplayProfile) &&
            Equals(expected.CollectionItemKey, observed.CollectionItemKey);
    }

    private static bool Equals(string? left, string? right)
        => !string.IsNullOrWhiteSpace(left) &&
           string.Equals(left, right, StringComparison.Ordinal);
}

/// <summary>Honest default when no lifecycle-capable platform host is connected.</summary>
internal sealed class UnavailableWorkflowRepairValidationHost : IWorkflowRepairValidationHost
{
    internal static readonly UnavailableWorkflowRepairValidationHost Instance = new();

    public Task<WorkflowRepairLifecycleValidation> HardResetAsync(
        WorkflowRepairTransientValidationRequest request,
        CancellationToken cancellationToken)
        => Task.FromResult(new WorkflowRepairLifecycleValidation
        {
            Succeeded = false,
            FailureCode = "host-lifecycle-unavailable",
        });

    public Task<WorkflowRepairReplayValidation> ReplayWithInMemorySelectorOverrideAsync(
        WorkflowRepairTransientValidationRequest request,
        CancellationToken cancellationToken)
        => Task.FromResult(new WorkflowRepairReplayValidation
        {
            FailureCode = "host-lifecycle-unavailable",
        });
}
