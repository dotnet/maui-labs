using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.Cli.DevFlow.Broker;

/// <summary>
/// Broker-owned services exposed to one Inspector target. This is deliberately an adapter over
/// the coordinator and trust stores: it does not implement replay, artifact parsing, or policy.
/// </summary>
internal sealed class InspectorWorkflowServices
{
    private readonly WorkflowRunCoordinator _runs;
    private readonly ArtifactTrustImportService _imports;
    private readonly ArtifactTrustStore _artifacts;
    private readonly WorkflowRepairProposalStore _repairs;
    private readonly WorkflowXamlSourceProposalStore _sources;
    private readonly WorkflowCSharpSourceProposalStore _csharpSources;
    private readonly WorkflowRunTarget _target;
    private readonly string _dispatchTicket;
    private readonly Func<bool> _isTargetCurrent;
    private readonly CancellationToken _cancellationToken;

    public InspectorWorkflowServices(
        WorkflowRunCoordinator runs,
        ArtifactTrustImportService imports,
        ArtifactTrustStore artifacts,
        WorkflowRepairProposalStore repairs,
        WorkflowXamlSourceProposalStore sources,
        WorkflowCSharpSourceProposalStore csharpSources,
        WorkflowRunTarget target,
        string dispatchTicket,
        Func<bool> isTargetCurrent,
        CancellationToken cancellationToken)
    {
        _runs = runs ?? throw new ArgumentNullException(nameof(runs));
        _imports = imports ?? throw new ArgumentNullException(nameof(imports));
        _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
        _repairs = repairs ?? throw new ArgumentNullException(nameof(repairs));
        _sources = sources ?? throw new ArgumentNullException(nameof(sources));
        _csharpSources = csharpSources ?? throw new ArgumentNullException(nameof(csharpSources));
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _dispatchTicket = dispatchTicket ?? throw new ArgumentNullException(nameof(dispatchTicket));
        _isTargetCurrent = isTargetCurrent ?? throw new ArgumentNullException(nameof(isTargetCurrent));
        _cancellationToken = cancellationToken;
    }

    public WorkflowRunTarget Target => _target;

    public bool IsTargetCurrent() => _isTargetCurrent();

    public WorkflowRunCapabilitiesResponse GetCapabilities() => _runs.GetCapabilities();

    public WorkflowRunPreflightResult Preflight(WorkflowRunStartRequest request)
        => _runs.Preflight(request, _target, _isTargetCurrent);

    public WorkflowRunStartResult Start(
        WorkflowRunStartRequest request,
        Func<AgentClient, IFlowReplayEvidenceCapture?>? evidenceCaptureFactory = null,
        WorkflowRunLeaseHandoff? leaseHandoff = null,
        bool recordDeviceRun = false,
        Action<string, string>? deviceRecordingCaptured = null)
    {
        if (!_isTargetCurrent())
        {
            return WorkflowRunStartResult.Conflict(
                "The requested agent instance is stale or no longer connected.");
        }

        return _runs.Start(
            request,
            _target,
            _isTargetCurrent,
            new WorkflowRunExecutionOptions
            {
                EvidenceCaptureFactory = evidenceCaptureFactory,
                ReproductionExpectation = request.ReproductionExpectation,
                RecordDeviceRun = recordDeviceRun,
                DeviceRecordingCaptured = deviceRecordingCaptured,
            },
            leaseHandoff,
            dispatchOrigin: WorkflowRunDispatchOrigin.InspectorWorkbench,
            dispatchTicket: _dispatchTicket);
    }

    public WorkflowRunAccessResult GetRunStatus(string runId, string? capabilityToken)
        => _runs.GetStatus(runId, capabilityToken);

    public WorkflowRunRepairContextResult GetRunRepairContext(string runId, string? capabilityToken)
    {
        var result = _runs.GetRepairContext(runId, capabilityToken);
        if (result.Context is null)
            return result;
        if (!string.Equals(result.Context.Target.AgentId, _target.AgentId, StringComparison.Ordinal) ||
            !string.Equals(
                result.Context.Target.AgentInstanceId,
                _target.AgentInstanceId,
                StringComparison.Ordinal))
        {
            return WorkflowRunRepairContextResult.Unavailable(
                "The broker-owned run does not target this exact Inspector app instance.");
        }
        return result;
    }

    public WorkflowRunCancelResult CancelRun(string runId, string? capabilityToken)
        => _runs.Cancel(runId, capabilityToken);

    public WorkflowRunPriorSelectorResolutionResult GetPriorSelectorResolution(
        string sourceRunId,
        string sourceStepId)
        => _runs.GetPriorSelectorResolution(sourceRunId, sourceStepId);

    public InspectorArtifactTrustResult ImportArtifact(ReadOnlyMemory<byte> bytes, string? kind)
    {
        if (!ArtifactTrustImportKinds.IsKnown(kind))
        {
            return InspectorArtifactTrustResult.Failure(
                400,
                "An explicit supported artifact kind is required.");
        }

        var imported = _imports.Import(bytes, kind!, policy: null, verifiedProvenance: null, _cancellationToken);
        if (!imported.Ok || imported.Artifact is null)
        {
            return InspectorArtifactTrustResult.Failure(
                400,
                imported.Error ?? "The artifact could not be imported.");
        }

        var stored = _artifacts.Add(imported.Artifact);
        return !stored.Ok
            ? InspectorArtifactTrustResult.Failure(
                409,
                stored.Error ?? "The artifact could not be retained.")
            : InspectorArtifactTrustResult.Success(
                201,
                new ArtifactTrustRouteResponse
                {
                    Ok = true,
                    CapabilityToken = stored.CapabilityToken,
                    Status = stored.Status,
                });
    }

    public ArtifactTrustStoreReadResult GetArtifactStatus(string artifactId, string? capabilityToken)
        => _artifacts.GetStatus(artifactId, capabilityToken);

    public ArtifactTrustStoreReadResult GetArtifactProjection(string artifactId, string? capabilityToken)
        => _artifacts.GetSafeProjection(artifactId, capabilityToken);

    public ArtifactTrustStoreRepairResult GetArtifactRepairTrust(
        string artifactId,
        string? capabilityToken,
        string localRunId)
        => _artifacts.GetRepairTrust(artifactId, capabilityToken, localRunId);

    public ArtifactTrustStoreBindResult BindLocalReproduction(
        string artifactId,
        string? capabilityToken,
        MauiLocalReproductionExpectation current,
        string localRunId)
    {
        var local = _runs.GetLocalReproductionFacts(localRunId);
        if (!local.Ok || local.Facts is null)
        {
            return new ArtifactTrustStoreBindResult
            {
                StatusCode = 409,
                Error = local.Error ?? "The local run cannot establish reproduction facts.",
            };
        }

        return _artifacts.BindLocalReproduction(
            artifactId,
            capabilityToken,
            local.Facts,
            current);
    }

    public WorkflowRepairStoreResult ProposeRepair(
        MauiFlowRepairProposal proposal,
        WorkflowRepairTrustedContext trustedContext,
        bool agentOriginated = false,
        Func<WorkflowRepairProposalSnapshot, string, WorkflowRepairHistoryAppendResult>? historyWriter = null)
        => _repairs.Propose(proposal, agentOriginated, trustedContext, historyWriter);

    public WorkflowRepairStoreResult GetRepair(string proposalId)
        => _repairs.Get(proposalId);

    public WorkflowRepairStoreResult PreviewRepair(
        string proposalId,
        Func<WorkflowRepairProposalSnapshot, string, WorkflowRepairHistoryAppendResult>? historyWriter = null)
        => _repairs.Preview(proposalId, historyWriter);

    public WorkflowRepairStoreResult RejectRepair(
        string proposalId,
        string? reviewer,
        string? reasonCode,
        Func<WorkflowRepairProposalSnapshot, string, WorkflowRepairHistoryAppendResult>? historyWriter = null)
        => _repairs.Reject(proposalId, reviewer, reasonCode, historyWriter);

    public WorkflowRepairGrantIssueResult IssueRepairGrant(
        WorkflowRepairGrantIssueRequest request,
        Func<WorkflowRepairProposalSnapshot, string, WorkflowRepairHistoryAppendResult>? historyWriter = null)
        => _repairs.IssueGrant(request, historyWriter);

    public WorkflowRepairStoreResult RecordRepairValidation(
        string proposalId,
        string validationGrant,
        WorkflowRepairValidationRecord validation,
        Func<WorkflowRepairProposalSnapshot, string, WorkflowRepairHistoryAppendResult>? historyWriter = null)
        => _repairs.RecordValidation(proposalId, validationGrant, validation, historyWriter);

    /// <summary>Confirms a repair grant is redeemable before any device-visible work begins.</summary>
    public bool CanRedeemRepairGrant(
        string proposalId,
        string? grant,
        string kind,
        out string? error)
        => _repairs.CanRedeemGrant(proposalId, grant, kind, out error);

    public WorkflowRepairStoreResult BeginRepairApply(
        string proposalId,
        string approvalGrant,
        WorkflowRepairGrantBinding binding)
        => _repairs.BeginApply(proposalId, approvalGrant, binding);

    public WorkflowRepairStoreResult CompleteRepairApply(
        string proposalId,
        WorkflowRepairApplyRecord apply)
        => _repairs.CompleteApply(proposalId, apply);

    public WorkflowRepairStoreResult RecordRepairVerification(
        string proposalId,
        IReadOnlyList<WorkflowRepairVerificationAccess> verification,
        Func<WorkflowRepairProposalSnapshot, string, WorkflowRepairHistoryAppendResult>? historyWriter = null)
    {
        var proposal = _repairs.Get(proposalId);
        if (!proposal.Ok || proposal.Proposal is null)
            return proposal;
        if (verification.Count != 3 ||
            verification.Select(static item => item.RunId)
                .Where(static id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .Count() != 3)
        {
            return WorkflowRepairStoreResult.Failure(
                "verification-runs-invalid",
                "Verification requires exactly three distinct retained broker runs.");
        }

        var retained = new List<WorkflowRepairVerificationRun>(3);
        foreach (var access in verification)
        {
            var run = _runs.GetRepairContext(access.RunId ?? string.Empty, access.CapabilityToken);
            if (run.Context is null)
            {
                return WorkflowRepairStoreResult.Failure(
                    "verification-run-unavailable",
                    run.Error ?? "A verification run is unavailable or its capability is invalid.");
            }
            if (!string.Equals(run.Context.Target.AgentId, _target.AgentId, StringComparison.Ordinal) ||
                !string.Equals(run.Context.Target.AgentInstanceId, _target.AgentInstanceId, StringComparison.Ordinal))
            {
                return WorkflowRepairStoreResult.Failure(
                    "verification-target-mismatch",
                    "Every verification run must target this exact retained broker agent instance.");
            }

            retained.Add(BuildVerificationRun(proposal.Proposal, run.Context));
        }
        return _repairs.RecordVerification(proposalId, retained, historyWriter);
    }

    public WorkflowRepairStoreResult BeginRepairRollback(
        string proposalId,
        string rollbackGrant,
        WorkflowRepairGrantBinding binding)
        => _repairs.BeginRollback(proposalId, rollbackGrant, binding);

    public WorkflowRepairStoreResult CompleteRepairRollback(
        string proposalId,
        WorkflowRepairRollbackRecord rollback)
        => _repairs.CompleteRollback(proposalId, rollback);

    public WorkflowXamlSourceStoreResult ProposeXamlSource(
        MauiXamlSourceProposal proposal,
        bool agentOriginated = false)
        => _sources.Propose(proposal, agentOriginated);

    public WorkflowXamlSourceStoreResult GetXamlSource(string proposalId)
        => _sources.Get(proposalId);

    public WorkflowXamlSourceStoreResult PreviewXamlSource(string proposalId)
        => _sources.Preview(proposalId);

    public WorkflowXamlSourceStoreResult RejectXamlSource(
        string proposalId,
        string? reviewer,
        string? reasonCode)
        => _sources.Reject(proposalId, reviewer, reasonCode);

    private static WorkflowRepairVerificationRun BuildVerificationRun(
        WorkflowRepairProposalSnapshot proposal,
        WorkflowRunRepairContext run)
    {
        var report = run.Report;
        var step = report.Steps.FirstOrDefault(candidate =>
            string.Equals(candidate.StepId, proposal.SourceStepId, StringComparison.Ordinal));
        var flowMatches = !string.IsNullOrWhiteSpace(proposal.AppliedFlowDigest) &&
            string.Equals(run.FlowDigest, proposal.AppliedFlowDigest, StringComparison.Ordinal) &&
            string.Equals(report.FlowDigest, proposal.AppliedFlowDigest, StringComparison.Ordinal);
        var cleanReset = report.Reset?.Succeeded == true &&
            report.Reset.AppStateSucceeded != false &&
            report.Reset.BackendTestDataSucceeded != false;
        var checkpointMatched = CheckpointsMatch(
            proposal.TrustedContext.ClassifiedCheckpoint,
            step?.ObservedCheckpoint) &&
            CheckpointsMatch(step?.ExpectedCheckpoint, step?.ObservedCheckpoint);
        var fingerprintMatched = proposal.Proposal.Candidate?.Fingerprint is not null &&
            MauiRepairFingerprintComparer.SemanticallyMatches(
                proposal.Proposal.Candidate.Fingerprint,
                step?.Fingerprint);
        var uniqueResolution = step?.TargetResolution?.MatchCount == 1 &&
            string.Equals(step.TargetResolution.Status, "resolved", StringComparison.Ordinal);
        var assertionsPassed = proposal.Proposal.UnchangedAssertionsProof?.Unchanged == true &&
            report.Steps.SelectMany(static candidate => candidate.Assertions)
                .All(static assertion => assertion.Passed == true && assertion.Skipped != true);
        var independentlyVerified = report.Verification?.Verified == true &&
            run.Admission.RunVerificationAllowed;
        var passed = flowMatches &&
            string.Equals(report.Outcome?.Status, MauiFlowRunOutcomes.Passed, StringComparison.Ordinal);

        return new WorkflowRepairVerificationRun
        {
            RunId = run.RunId,
            EvidenceId = report.ReportPath ?? report.ReportDigest,
            ReportDigest = report.ReportDigest,
            StartedAt = report.StartedAt,
            BrokerRetained = true,
            CleanReset = cleanReset,
            CheckpointMatched = checkpointMatched,
            FingerprintMatched = fingerprintMatched,
            UniqueResolution = uniqueResolution,
            HardAssertionsUnchanged = assertionsPassed,
            IndependentOracleSucceeded = independentlyVerified,
            Passed = passed,
            FailureCode = cleanReset &&
                checkpointMatched &&
                fingerprintMatched &&
                uniqueResolution &&
                assertionsPassed &&
                independentlyVerified &&
                passed
                    ? null
                    : "retained-verification-failed",
        };
    }

    private static bool CheckpointsMatch(MauiFlowCheckpoint? expected, MauiFlowCheckpoint? observed)
    {
        if (expected is null || observed is null)
            return false;
        static bool Match(string? value, string? current)
            => string.IsNullOrWhiteSpace(value) ||
               string.Equals(value, current, StringComparison.Ordinal);
        return Match(expected.AppBuildFingerprint, observed.AppBuildFingerprint) &&
            Match(expected.AgentInstanceId, observed.AgentInstanceId) &&
            Match(expected.SeedFingerprint, observed.SeedFingerprint) &&
            Match(expected.BackendStateFingerprint, observed.BackendStateFingerprint) &&
            Match(expected.Route, observed.Route) &&
            Match(expected.Window, observed.Window) &&
            Match(expected.Modal, observed.Modal) &&
            Match(expected.Locale, observed.Locale) &&
            Match(expected.Theme, observed.Theme) &&
            Match(expected.Orientation, observed.Orientation) &&
            Match(expected.DisplayProfile, observed.DisplayProfile) &&
            Match(expected.CollectionItemKey, observed.CollectionItemKey);
    }

    public WorkflowCSharpSourceStoreResult ProposeCSharpSource(MauiCSharpSourceProposal proposal)
        => _csharpSources.Propose(proposal);

    public WorkflowCSharpSourceStoreResult GetCSharpSource(string proposalId)
        => _csharpSources.Get(proposalId);

    public WorkflowCSharpSourceStoreResult PreviewCSharpSource(string proposalId)
        => _csharpSources.Preview(proposalId);

    public WorkflowCSharpSourceStoreResult RejectCSharpSource(
        string proposalId,
        string? reviewer,
        string? reasonCode)
        => _csharpSources.Reject(proposalId, reviewer, reasonCode);
}

internal sealed class WorkflowRepairVerificationAccess
{
    public string? RunId { get; init; }
    public string? CapabilityToken { get; init; }
}

internal sealed class InspectorArtifactTrustResult
{
    public int StatusCode { get; private init; }
    public ArtifactTrustRouteResponse Response { get; private init; } = ArtifactTrustRouteResponse.Failure("Unknown error.");

    public static InspectorArtifactTrustResult Success(int statusCode, ArtifactTrustRouteResponse response)
        => new() { StatusCode = statusCode, Response = response };

    public static InspectorArtifactTrustResult Failure(int statusCode, string error)
        => Success(statusCode, ArtifactTrustRouteResponse.Failure(error));
}
