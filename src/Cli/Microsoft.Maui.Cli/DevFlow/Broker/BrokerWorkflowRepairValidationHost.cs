using System.Globalization;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.Cli.DevFlow.Broker;

/// <summary>
/// Trusted attestation of the reset, seed, backend, and independent-oracle facts a broker cannot
/// observe from a running app. Implementations build, reset, and seed outside the broker and attest
/// only the state they actually applied. A broker never synthesises these facts and never echoes
/// them from a request.
/// </summary>
internal interface IWorkflowRepairResetAttester
{
    Task<WorkflowRepairResetAttestation?> AttestAsync(
        WorkflowRepairTransientValidationRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// The seed, backend, and collection facts the lifecycle owner currently has applied to the
    /// connected app. A broker uses these to classify repair eligibility, because the app itself has
    /// no way to report them and a request that carries them is only an echo.
    /// </summary>
    Task<WorkflowRepairAttestedState?> ObserveAttestedStateAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The three checkpoint facts no running app can prove about itself, as attested by the component
/// that applied them.
/// </summary>
/// <remarks>
/// These values are compared against a checkpoint the same owner produced, so a match establishes
/// that the owner re-applied the state it applied before — self-consistency, not independent
/// corroboration. That is the strongest claim available here, and it is what the checkpoint fields
/// are for: detecting that state drifted between recording and replay. It is deliberately not
/// evidence that the state is correct, only that it is unchanged.
/// </remarks>
internal sealed record WorkflowRepairAttestedState
{
    public required string SeedFingerprint { get; init; }
    public required string BackendStateFingerprint { get; init; }
    public required string CollectionItemKey { get; init; }
}

/// <summary>Reset, seed, backend, and oracle facts attested by a lifecycle-capable host.</summary>
internal sealed class WorkflowRepairResetAttestation
{
    public bool Succeeded { get; init; }

    /// <summary>
    /// The canonical reset evidence. Its seed and backend fingerprints are the only trustworthy
    /// source for the two checkpoint fields a connected app cannot report about itself.
    /// </summary>
    public MauiFlowResetResult? Reset { get; init; }

    /// <summary>The seeded collection identity, when the classified checkpoint pins one.</summary>
    public string? CollectionItemKey { get; init; }

    /// <summary>
    /// Outcomes for the independent business oracles the plan declares. The broker cannot evaluate
    /// them because they are, by definition, not observable through the UI it drives.
    /// </summary>
    public List<MauiIndependentBusinessOracleResult> BusinessOracles { get; init; } = [];

    public List<string> EvidenceIds { get; init; } = [];
    public string? FailureCode { get; init; }
}

/// <summary>One broker-retained run produced by a bounded transient repair replay.</summary>
internal sealed class WorkflowRepairTransientReplayOutcome
{
    public string? RunId { get; init; }
    public MauiFlowRunReport? Report { get; init; }
    public string? EvidenceId { get; init; }
    public string? FailureCode { get; init; }
}

/// <summary>Reads the checkpoint facts the connected app can prove about itself.</summary>
internal delegate Task<MauiFlowCheckpoint?> WorkflowRepairCheckpointObserver(CancellationToken cancellationToken);

/// <summary>Re-establishes a classified route on the connected app.</summary>
internal delegate Task<bool> WorkflowRepairRouteRestore(string route, CancellationToken cancellationToken);

/// <summary>Runs one bounded, broker-retained replay of a transient in-memory flow.</summary>
internal delegate Task<WorkflowRepairTransientReplayOutcome?> WorkflowRepairTransientReplayRunner(
    MauiFlow flow,
    MauiTestPlan? plan,
    MauiFlowRunContext context,
    CancellationToken cancellationToken);

/// <summary>
/// Broker-side lifecycle host for bounded transient repair validation. It composes existing
/// broker primitives — live checkpoint observation, route restore, and the retained workflow run
/// path — and never writes a flow, a plan, or a proposed selector to a workspace. The retained run
/// report does contain the candidate selector; that is broker evidence for the reviewer, not an
/// applied repair, and it is what <see cref="WorkflowRepairValidationRecord.EvidenceIds"/> cites.
/// </summary>
internal sealed class BrokerWorkflowRepairValidationHost : IWorkflowRepairValidationHost
{
    private readonly WorkflowRepairCheckpointObserver _observeCheckpoint;
    private readonly WorkflowRepairRouteRestore _restoreRoute;
    private readonly WorkflowRepairTransientReplayRunner _replay;
    private readonly IWorkflowRepairResetAttester? _resetAttester;
    private readonly Lock _gate = new();
    private AttestedReset? _lastReset;

    internal BrokerWorkflowRepairValidationHost(
        WorkflowRepairCheckpointObserver observeCheckpoint,
        WorkflowRepairRouteRestore restoreRoute,
        WorkflowRepairTransientReplayRunner replay,
        IWorkflowRepairResetAttester? resetAttester = null)
    {
        _observeCheckpoint = observeCheckpoint ?? throw new ArgumentNullException(nameof(observeCheckpoint));
        _restoreRoute = restoreRoute ?? throw new ArgumentNullException(nameof(restoreRoute));
        _replay = replay ?? throw new ArgumentNullException(nameof(replay));
        _resetAttester = resetAttester;
    }

    /// <summary>
    /// Asks the lifecycle attester what it currently has applied. Without an attester the answer is
    /// null, which keeps the classification honest about the facts it cannot establish.
    /// </summary>
    public async Task<WorkflowRepairAttestedState?> ObserveAttestedStateAsync(CancellationToken cancellationToken)
    {
        if (_resetAttester is null)
            return null;
        try
        {
            return await _resetAttester.ObserveAttestedStateAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    public async Task<WorkflowRepairLifecycleValidation> HardResetAsync(
        WorkflowRepairTransientValidationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var classified = request.ClassifiedCheckpoint;
        if (classified is null)
            return ResetFailed("classified-checkpoint-required");

        // Seed, backend-state, and collection-item facts are not observable from a running app.
        // Without a lifecycle attester the broker cannot prove the app was reset to the classified
        // checkpoint, so validation fails closed instead of echoing the request back as evidence.
        if (_resetAttester is null)
            return ResetFailed("repair-reset-attester-unavailable");

        WorkflowRepairResetAttestation? attestation;
        try
        {
            attestation = await _resetAttester.AttestAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return ResetFailed("repair-reset-attestation-failed");
        }

        if (attestation is null || !attestation.Succeeded)
            return ResetFailed(attestation?.FailureCode ?? "repair-reset-attestation-failed", attestation?.EvidenceIds);

        if (string.IsNullOrWhiteSpace(classified.Route))
            return ResetFailed("classified-checkpoint-route-missing", attestation.EvidenceIds);

        bool restored;
        try
        {
            restored = await _restoreRoute(classified.Route!, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return ResetFailed("post-reset-route-restore-failed", attestation.EvidenceIds);
        }

        if (!restored)
            return ResetFailed("post-reset-route-restore-failed", attestation.EvidenceIds);

        MauiFlowCheckpoint? observed;
        try
        {
            observed = await _observeCheckpoint(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return ResetFailed("post-reset-observation-unavailable", attestation.EvidenceIds);
        }

        if (observed is null)
            return ResetFailed("post-reset-observation-unavailable", attestation.EvidenceIds);

        var merged = new MauiFlowCheckpoint
        {
            AppBuildFingerprint = observed.AppBuildFingerprint,
            AgentInstanceId = observed.AgentInstanceId,
            SeedFingerprint = attestation.Reset?.SeedFingerprint,
            BackendStateFingerprint = attestation.Reset?.BackendStateFingerprint,
            Route = observed.Route,
            Window = observed.Window,
            Modal = observed.Modal,
            Locale = observed.Locale,
            Theme = observed.Theme,
            Orientation = observed.Orientation,
            DisplayProfile = observed.DisplayProfile,
            CollectionItemKey = attestation.CollectionItemKey,
        };
        var evidence = Bounded(attestation.EvidenceIds);
        lock (_gate)
        {
            _lastReset = new AttestedReset(
                request.Proposal.ProposalId,
                request.Proposal.PatchDigest,
                classified,
                merged,
                attestation.Reset,
                attestation.BusinessOracles,
                evidence.FirstOrDefault(),
                DateTimeOffset.UtcNow);
        }

        return new WorkflowRepairLifecycleValidation
        {
            Succeeded = true,
            ExpectedCheckpoint = classified,
            ObservedCheckpoint = merged,
            EvidenceIds = evidence,
        };
    }

    public async Task<WorkflowRepairReplayValidation> ReplayWithInMemorySelectorOverrideAsync(
        WorkflowRepairTransientValidationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.InMemorySelectorOverrideOnly)
            return ReplayFailed("in-memory-override-required");

        var proposal = request.Proposal;
        if (proposal.Candidate?.Fingerprint is null ||
            proposal.ProposedSelector is null ||
            string.IsNullOrWhiteSpace(proposal.SourceStepId))
        {
            return ReplayFailed("selector-only-invariant-required");
        }

        var source = request.SourceFlow;
        if (source is null)
            return ReplayFailed("repair-source-flow-unavailable");
        if (string.IsNullOrWhiteSpace(proposal.BaseFlow?.Digest) ||
            !string.Equals(
                MauiFlowRunReportSerializer.ComputeFlowDigest(source),
                proposal.BaseFlow!.Digest,
                StringComparison.Ordinal))
        {
            return ReplayFailed("repair-source-flow-digest-mismatch");
        }

        // The canonical generator rebuilds the selector-only patch from the proposal and returns a
        // clone. The retained source flow is never mutated and the override is never persisted.
        var patched = MauiFlowRepairPatchBuilder.ApplyVerified(source, proposal);
        if (!patched.Ok || patched.PatchedFlow is null)
            return ReplayFailed("repair-transient-patch-invalid");

        var transient = patched.PatchedFlow;
        // The patch builder resolves the canonical step identity the runner also reports, so the
        // replay is compared against the same step the patch replaced.
        var repairedStepId = patched.Diff?.StepId;
        if (string.IsNullOrWhiteSpace(repairedStepId))
            return ReplayFailed("repair-source-step-unavailable");
        var repairedStep = FindStep(transient, repairedStepId);
        if (repairedStep is null)
            return ReplayFailed("repair-source-step-unavailable");

        var downstreamAllowed = request.AllowDownstreamContinuation &&
            request.ReplaySafety?.DownstreamContinuationAllowed == true;
        if (!downstreamAllowed)
            transient.Steps.RemoveAll(step => step.Seq > repairedStep.Seq);

        // Replay is admitted against the reset the host just attested, not against caller-supplied
        // state. The attestation is single-use and short-lived so a replay can never inherit a
        // reset that some other replay has already dirtied.
        AttestedReset? reset;
        lock (_gate)
        {
            reset = _lastReset;
            _lastReset = null;
        }
        if (reset is null || !reset.Matches(proposal) || reset.IsStale(DateTimeOffset.UtcNow))
            return ReplayFailed("repair-reset-attestation-required");

        var context = new MauiFlowRunContext
        {
            Intent = MauiFlowReplayIntents.RepairValidation,
            Preconditions = new MauiFlowReplayPreconditions
            {
                Expected = reset.Expected,
                Observed = reset.Observed,
                CheckedAt = reset.AttestedAt,
                EvidenceReference = reset.EvidenceReference,
            },
            Reset = reset.Reset,
            BusinessOracles = [.. reset.BusinessOracles],
        };

        WorkflowRepairTransientReplayOutcome? outcome;
        try
        {
            outcome = await _replay(transient, request.SourcePlan, context, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return ReplayFailed("transient-replay-dispatch-failed");
        }

        if (outcome?.Report is null)
            return ReplayFailed(outcome?.FailureCode ?? "transient-replay-unavailable", outcome?.RunId);

        var report = outcome.Report;
        var attempt = report.Steps.FirstOrDefault(step =>
            string.Equals(step.StepId, repairedStepId, StringComparison.Ordinal));
        var reached = attempt is not null;
        var matchCount = attempt?.TargetResolution?.MatchCount;
        var fingerprintMatches = MauiRepairFingerprintComparer.SemanticallyMatches(
            proposal.Candidate.Fingerprint,
            attempt?.Fingerprint);
        // The proof is re-derived from the transient flow rather than trusted from the proposal.
        // The claim is only made when the executed prefix actually declares hard assertions and
        // every one of them was evaluated and passed — a prefix that asserted nothing, or whose
        // report was truncated, proves nothing about invariance.
        var declaredAssertions = transient.Steps
            .SelectMany(static step => step.Asserts ?? [])
            .Count(static assertion => assertion.Verify);
        var passedAssertions = report.Steps
            .SelectMany(static step => step.Assertions)
            .Count(static assertion => assertion.Passed == true && assertion.Skipped != true);
        var assertionsUnchanged = patched.Proof?.Unchanged == true &&
            patched.Proof.ActionsUnchanged == true &&
            patched.Proof.ValuesUnchanged == true &&
            patched.Proof.OrderUnchanged == true &&
            report.Truncated != true &&
            declaredAssertions > 0 &&
            passedAssertions == declaredAssertions;
        // Verified is the run path's own answer to "did an oracle independent of the UI selector
        // confirm the effect", and any recorded oracle must itself be independent and successful.
        var oracleSucceeded = report.Verification?.Verified == true &&
            report.ReplayEligibility?.RunVerificationAllowed == true &&
            report.BusinessOracles.All(static oracle =>
                oracle.Succeeded == true && oracle.Independent == true);
        var continuedDownstream = report.Steps.Any(step => step.Sequence > repairedStep.Seq);
        var outcomePassed = string.Equals(
            report.Outcome?.Status,
            MauiFlowRunOutcomes.Passed,
            StringComparison.Ordinal);
        var evidence = Bounded([outcome.EvidenceId, report.ReportPath, report.ReportDigest]);
        var passed = reached &&
            matchCount == 1 &&
            fingerprintMatches &&
            assertionsUnchanged &&
            oracleSucceeded &&
            outcomePassed &&
            (!continuedDownstream || downstreamAllowed);

        return new WorkflowRepairReplayValidation
        {
            ReachedFailedStep = reached,
            RunId = outcome.RunId ?? report.RunId,
            CandidateMatchCount = matchCount,
            ObservedFingerprint = attempt?.Fingerprint,
            SemanticFingerprintMatches = fingerprintMatches,
            HardAssertionsUnchanged = assertionsUnchanged,
            IndependentOracleSucceeded = oracleSucceeded,
            ContinuedDownstream = continuedDownstream,
            EvidenceIds = evidence,
            Passed = passed,
            FailureCode = passed ? null : "transient-replay-failed",
        };
    }

    /// <summary>Mirrors the canonical step identity so retained ids resolve to executable steps.</summary>
    private static FlowStep? FindStep(MauiFlow flow, string stepId)
        => flow.Steps.FirstOrDefault(step => string.Equals(
            string.IsNullOrWhiteSpace(step.StepId)
                ? step.Seq.ToString(CultureInfo.InvariantCulture)
                : step.StepId.Trim(),
            stepId,
            StringComparison.Ordinal));

    private static WorkflowRepairLifecycleValidation ResetFailed(
        string failureCode,
        IEnumerable<string?>? evidenceIds = null)
        => new()
        {
            Succeeded = false,
            FailureCode = failureCode,
            EvidenceIds = Bounded(evidenceIds),
        };

    private static WorkflowRepairReplayValidation ReplayFailed(string failureCode, string? runId = null)
        => new()
        {
            FailureCode = failureCode,
            RunId = runId,
        };

    private static List<string> Bounded(IEnumerable<string?>? values)
        => (values ?? [])
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(static id => id!)
            .Distinct(StringComparer.Ordinal)
            .Take(32)
            .ToList();

    /// <summary>
    /// The reset the host itself attested and observed, retained only for the single paired replay.
    /// It is bound to the proposal and expires, so a replay can never inherit an unrelated or
    /// already-consumed reset.
    /// </summary>
    private sealed record AttestedReset(
        string? ProposalId,
        string? PatchDigest,
        MauiFlowCheckpoint Expected,
        MauiFlowCheckpoint Observed,
        MauiFlowResetResult? Reset,
        List<MauiIndependentBusinessOracleResult> BusinessOracles,
        string? EvidenceReference,
        DateTimeOffset AttestedAt)
    {
        private static readonly TimeSpan MaximumAge = TimeSpan.FromMinutes(10);

        public bool Matches(MauiFlowRepairProposal proposal)
            => !string.IsNullOrWhiteSpace(ProposalId) &&
               string.Equals(ProposalId, proposal.ProposalId, StringComparison.Ordinal) &&
               !string.IsNullOrWhiteSpace(PatchDigest) &&
               string.Equals(PatchDigest, proposal.PatchDigest, StringComparison.Ordinal);

        public bool IsStale(DateTimeOffset now) => now - AttestedAt > MaximumAge;
    }
}
