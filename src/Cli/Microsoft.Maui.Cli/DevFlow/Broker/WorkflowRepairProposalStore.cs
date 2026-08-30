using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.Cli.DevFlow.Broker;

/// <summary>
/// Bounded broker-owned lifecycle store for human-reviewed selector repair proposals. It contains
/// no workspace write path: applying and history persistence remain an explicit host transaction.
/// </summary>
internal sealed class WorkflowRepairProposalStore
{
    private readonly object _gate = new();
    private readonly WorkflowRepairProposalStoreOptions _options;
    private readonly TimeProvider _clock;
    private readonly Dictionary<string, RepairRecord> _records = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GrantRecord> _grants = new(StringComparer.Ordinal);

    internal WorkflowRepairProposalStore(
        WorkflowRepairProposalStoreOptions? options = null,
        TimeProvider? clock = null)
    {
        _options = options ?? new WorkflowRepairProposalStoreOptions();
        _clock = clock ?? TimeProvider.System;
        if (_options.MaxProposals < 1 || _options.MaxGrants < 1 ||
            _options.MaximumGrantLifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Repair proposal store limits are invalid.");
        }
    }

    internal WorkflowRepairStoreResult Propose(
        MauiFlowRepairProposal proposal,
        bool agentOriginated = false,
        WorkflowRepairTrustedContext? trustedContext = null,
        Func<WorkflowRepairProposalSnapshot, string, WorkflowRepairHistoryAppendResult>? historyWriter = null)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        lock (_gate)
        {
            PurgeExpiredGrantsLocked();
            if (!ValidateProposedRepair(proposal, out var error))
                return WorkflowRepairStoreResult.Failure("proposal-invalid", error!);
            if (!ValidateTrustedContext(proposal, trustedContext, out error))
                return WorkflowRepairStoreResult.Failure("proposal-evidence-invalid", error!);

            var id = proposal.ProposalId;
            if (string.IsNullOrWhiteSpace(id))
                id = OpaqueId("repair");
            if (_records.TryGetValue(id, out var existing))
                return WorkflowRepairStoreResult.Success(CreateSnapshot(existing));

            EvictTerminalLocked();
            if (_records.Count >= _options.MaxProposals)
            {
                return WorkflowRepairStoreResult.Failure(
                    "proposal-capacity-exhausted",
                    "The broker repair proposal store is full; resolve or discard an existing proposal first.");
            }

            var normalized = WithProposalMetadata(
                proposal,
                id,
                revision: 1,
                state: MauiFlowRepairOutcomeStates.Proposed,
                createdAt: _clock.GetUtcNow(),
                reviewer: null,
                grantDigest: null,
                expiresAt: null);
            var record = new RepairRecord(
                normalized,
                MauiFlowRepairOutcomeStates.Proposed,
                revision: 1,
                agentOriginated,
                CloneTrustedContext(trustedContext!),
                _clock.GetUtcNow());
            if (!TryPersistHistory(
                    historyWriter,
                    record,
                    MauiFlowRepairOutcomeStates.Proposed,
                    out error))
            {
                return WorkflowRepairStoreResult.Failure("history-persistence-failed", error!);
            }
            _records.Add(id, record);
            return WorkflowRepairStoreResult.Success(CreateSnapshot(record));
        }
    }

    internal WorkflowRepairStoreResult Get(string? proposalId)
    {
        lock (_gate)
        {
            RefreshExpiredApprovalLocked();
            return !TryGetLocked(proposalId, out var record, out var error)
                ? WorkflowRepairStoreResult.Failure("proposal-not-found", error!)
                : WorkflowRepairStoreResult.Success(CreateSnapshot(record!));
        }
    }

    internal WorkflowRepairStoreResult Preview(
        string? proposalId,
        Func<WorkflowRepairProposalSnapshot, string, WorkflowRepairHistoryAppendResult>? historyWriter = null)
    {
        lock (_gate)
        {
            RefreshExpiredApprovalLocked();
            if (!TryGetLocked(proposalId, out var record, out var error))
                return WorkflowRepairStoreResult.Failure("proposal-not-found", error!);
            if (record!.State == MauiFlowRepairOutcomeStates.Proposed)
            {
                var priorState = record.State;
                var priorRevision = record.Revision;
                var priorUpdatedAt = record.UpdatedAt;
                TransitionLocked(record, MauiFlowRepairOutcomeStates.Previewed);
                if (!TryPersistHistory(
                        historyWriter,
                        record,
                        MauiFlowRepairOutcomeStates.Previewed,
                        out error))
                {
                    record.State = priorState;
                    record.Revision = priorRevision;
                    record.UpdatedAt = priorUpdatedAt;
                    return WorkflowRepairStoreResult.Failure("history-persistence-failed", error!);
                }
            }
            if (record.State is not MauiFlowRepairOutcomeStates.Previewed and
                not MauiFlowRepairOutcomeStates.Approved and
                not MauiFlowRepairOutcomeStates.Applying and
                not MauiFlowRepairOutcomeStates.Applied and
                not MauiFlowRepairOutcomeStates.Verified and
                not MauiFlowRepairOutcomeStates.RollbackRequired and
                not MauiFlowRepairOutcomeStates.Reverted)
            {
                return WorkflowRepairStoreResult.Failure(
                    "proposal-not-previewable",
                    $"Repair proposal state '{record.State}' cannot be previewed.");
            }
            return WorkflowRepairStoreResult.Success(CreateSnapshot(record));
        }
    }

    internal WorkflowRepairStoreResult Reject(
        string? proposalId,
        string? reviewer,
        string? reasonCode,
        Func<WorkflowRepairProposalSnapshot, string, WorkflowRepairHistoryAppendResult>? historyWriter = null)
    {
        lock (_gate)
        {
            RefreshExpiredApprovalLocked();
            if (!TryGetLocked(proposalId, out var record, out var error))
                return WorkflowRepairStoreResult.Failure("proposal-not-found", error!);
            if (record!.State is not MauiFlowRepairOutcomeStates.Proposed and
                not MauiFlowRepairOutcomeStates.Previewed)
            {
                return WorkflowRepairStoreResult.Failure(
                    "proposal-not-rejectable",
                    $"Only a proposed or previewed repair can be rejected; current state is '{record.State}'.");
            }
            var priorReviewer = record.Reviewer;
            var priorReasonCode = record.ReasonCode;
            var priorState = record.State;
            var priorRevision = record.Revision;
            var priorUpdatedAt = record.UpdatedAt;
            record.Reviewer = Bounded(reviewer, 256);
            record.ReasonCode = Bounded(reasonCode, 128) ?? "rejected";
            TransitionLocked(record, MauiFlowRepairOutcomeStates.Rejected);
            if (!TryPersistHistory(
                    historyWriter,
                    record,
                    MauiFlowRepairOutcomeStates.Rejected,
                    out error))
            {
                record.Reviewer = priorReviewer;
                record.ReasonCode = priorReasonCode;
                record.State = priorState;
                record.Revision = priorRevision;
                record.UpdatedAt = priorUpdatedAt;
                return WorkflowRepairStoreResult.Failure("history-persistence-failed", error!);
            }
            return WorkflowRepairStoreResult.Success(CreateSnapshot(record));
        }
    }

    internal WorkflowRepairGrantIssueResult IssueGrant(
        WorkflowRepairGrantIssueRequest request,
        Func<WorkflowRepairProposalSnapshot, string, WorkflowRepairHistoryAppendResult>? historyWriter = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            RefreshExpiredApprovalLocked();
            PurgeExpiredGrantsLocked();
            if (!TryGetLocked(request.ProposalId, out var record, out var error))
                return WorkflowRepairGrantIssueResult.Failure("proposal-not-found", error!);
            var found = record!;
            if (!request.HumanConfirmed || string.IsNullOrWhiteSpace(request.Reviewer))
            {
                return WorkflowRepairGrantIssueResult.Failure(
                    "human-approval-required",
                    "A named human reviewer must explicitly confirm a repair grant.");
            }
            if (!IsGrantAllowed(found, request.Kind, out error))
                return WorkflowRepairGrantIssueResult.Failure("grant-state-invalid", error!);
            if (!BindingMatches(
                    found,
                    request.Binding,
                    request.Kind == WorkflowRepairGrantKinds.Rollback,
                    out error))
                return WorkflowRepairGrantIssueResult.Failure("grant-binding-mismatch", error!);
            if (_grants.Count >= _options.MaxGrants)
            {
                return WorkflowRepairGrantIssueResult.Failure(
                    "grant-capacity-exhausted",
                    "The broker repair grant store is full.");
            }

            var now = _clock.GetUtcNow();
            var expiresAt = request.ExpiresAt ?? now.Add(_options.DefaultGrantLifetime);
            if (expiresAt <= now || expiresAt > now.Add(_options.MaximumGrantLifetime))
            {
                return WorkflowRepairGrantIssueResult.Failure(
                    "grant-expiry-invalid",
                    "The requested repair grant expiry is outside the broker policy bounds.");
            }

            var secret = OpaqueSecret("repairgrant");
            var digest = Hash(secret);
            var priorState = found.State;
            var priorRevision = found.Revision;
            var priorUpdatedAt = found.UpdatedAt;
            var priorReviewer = found.Reviewer;
            var priorGrantDigest = found.GrantDigest;
            var priorExpiresAt = found.ExpiresAt;
            if (request.Kind == WorkflowRepairGrantKinds.Apply)
                TransitionLocked(found, MauiFlowRepairOutcomeStates.Approved);
            var grant = new GrantRecord(
                secret,
                digest,
                request.Kind,
                found.Proposal.ProposalId!,
                found.Revision,
                found.Proposal.PatchDigest!,
                request.Binding!.FlowDigest!,
                request.Binding.FlowRevision,
                request.Binding?.TargetId,
                request.Binding?.Policy,
                request.Binding!.PlanDigest!,
                request.Binding.PlanRevision!.Value,
                request.Binding.SafetyPolicy!,
                Bounded(request.Reviewer, 256)!,
                expiresAt);
            found.Reviewer = grant.Reviewer;
            found.GrantDigest = digest;
            found.ExpiresAt = expiresAt;
            if (request.Kind == WorkflowRepairGrantKinds.Apply &&
                !TryPersistHistory(
                    historyWriter,
                    found,
                    MauiFlowRepairOutcomeStates.Approved,
                    out error))
            {
                found.State = priorState;
                found.Revision = priorRevision;
                found.UpdatedAt = priorUpdatedAt;
                found.Reviewer = priorReviewer;
                found.GrantDigest = priorGrantDigest;
                found.ExpiresAt = priorExpiresAt;
                return WorkflowRepairGrantIssueResult.Failure("history-persistence-failed", error!);
            }
            _grants.Add(secret, grant);

            return WorkflowRepairGrantIssueResult.Success(
                secret,
                digest,
                expiresAt,
                CreateSnapshot(found));
        }
    }

    /// <summary>
    /// Checks — without consuming, expiring, or transitioning anything — whether a grant can still
    /// be redeemed for a proposal. Hosts call this before any device-visible work so a bounded
    /// transient replay never runs on an unauthorised or stale grant.
    /// </summary>
    internal bool CanRedeemGrant(string? proposalId, string? secret, string expectedKind, out string? error)
    {
        lock (_gate)
        {
            if (!TryGetLocked(proposalId, out var record, out error))
                return false;
            if (string.IsNullOrWhiteSpace(secret) || !_grants.TryGetValue(secret, out var grant))
            {
                error = "The repair grant is missing, stale, or already used.";
                return false;
            }
            if (grant.Used || grant.ExpiresAt <= _clock.GetUtcNow())
            {
                error = grant.Used ? "The repair grant was already used." : "The repair grant expired.";
                return false;
            }
            if (!GrantBindsToRecord(grant, record!, expectedKind))
            {
                error = "The repair grant is not bound to this current proposal revision.";
                return false;
            }
            error = null;
            return true;
        }
    }

    internal WorkflowRepairStoreResult RecordValidation(
        string? proposalId,
        string? validationGrant,
        WorkflowRepairValidationRecord validation,
        Func<WorkflowRepairProposalSnapshot, string, WorkflowRepairHistoryAppendResult>? historyWriter = null)
    {
        ArgumentNullException.ThrowIfNull(validation);
        lock (_gate)
        {
            RefreshExpiredApprovalLocked();
            if (!TryGetLocked(proposalId, out var record, out var error))
                return WorkflowRepairStoreResult.Failure("proposal-not-found", error!);
            var found = record!;
            if (!ConsumeGrantLocked(validationGrant, found, WorkflowRepairGrantKinds.Validation, out error))
                return WorkflowRepairStoreResult.Failure("validation-grant-invalid", error!);

            var grant = _grants[validationGrant!];
            var priorState = found.State;
            var priorRevision = found.Revision;
            var priorUpdatedAt = found.UpdatedAt;
            var priorReasonCode = found.ReasonCode;
            var priorValidationRunIds = found.ValidationRunIds.ToList();
            var priorValidationCount = found.Validation.Count;
            found.Validation.Add(CloneValidation(validation));
            found.ValidationRunIds = found.Validation
                .SelectMany(static item => item.RunIds)
                .Where(static id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .Take(_options.MaxRunReferencesPerProposal)
                .ToList();
            found.ReasonCode = validation.Passed ? null : validation.FailureCode ?? "validation-failed";
            // Validation is non-persistent and keeps the proposal in a reviewable preview state.
            if (found.State == MauiFlowRepairOutcomeStates.Proposed)
                TransitionLocked(found, MauiFlowRepairOutcomeStates.Previewed);
            if (!TryPersistHistory(
                    historyWriter,
                    found,
                    MauiFlowRepairOutcomeStates.Previewed,
                    out error))
            {
                grant.Used = false;
                while (found.Validation.Count > priorValidationCount)
                    found.Validation.RemoveAt(found.Validation.Count - 1);
                found.ValidationRunIds = priorValidationRunIds;
                found.ReasonCode = priorReasonCode;
                found.State = priorState;
                found.Revision = priorRevision;
                found.UpdatedAt = priorUpdatedAt;
                return WorkflowRepairStoreResult.Failure("history-persistence-failed", error!);
            }
            return WorkflowRepairStoreResult.Success(CreateSnapshot(found));
        }
    }

    internal WorkflowRepairStoreResult BeginApply(
        string? proposalId,
        string? approvalGrant,
        WorkflowRepairGrantBinding? currentBinding)
    {
        lock (_gate)
        {
            RefreshExpiredApprovalLocked();
            if (!TryGetLocked(proposalId, out var record, out var error))
                return WorkflowRepairStoreResult.Failure("proposal-not-found", error!);
            if (record!.State == MauiFlowRepairOutcomeStates.ApprovalExpired)
            {
                return WorkflowRepairStoreResult.Failure(
                    "approval-expired",
                    "The repair approval expired and must be reviewed again.");
            }
            if (record.State != MauiFlowRepairOutcomeStates.Approved)
            {
                return WorkflowRepairStoreResult.Failure(
                    "approval-required",
                    "A current human-issued approval grant is required before applying a repair.");
            }
            if (!BindingMatches(record, currentBinding, againstAppliedRevision: false, out error))
            {
                TransitionLocked(record, MauiFlowRepairOutcomeStates.Stale);
                record.ReasonCode = "flow-or-target-stale";
                return WorkflowRepairStoreResult.Failure("proposal-stale", error!);
            }
            if (!GrantMatchesCurrentBinding(approvalGrant, currentBinding, out error))
                return WorkflowRepairStoreResult.Failure("approval-grant-invalid", error!);
            if (!ConsumeGrantLocked(approvalGrant, record, WorkflowRepairGrantKinds.Apply, out error))
                return WorkflowRepairStoreResult.Failure("approval-grant-invalid", error!);

            TransitionLocked(record, MauiFlowRepairOutcomeStates.Applying);
            return WorkflowRepairStoreResult.Success(CreateSnapshot(record));
        }
    }

    internal WorkflowRepairStoreResult CompleteApply(
        string? proposalId,
        WorkflowRepairApplyRecord apply)
    {
        ArgumentNullException.ThrowIfNull(apply);
        lock (_gate)
        {
            if (!TryGetLocked(proposalId, out var record, out var error))
                return WorkflowRepairStoreResult.Failure("proposal-not-found", error!);
            if (record!.State != MauiFlowRepairOutcomeStates.Applying)
            {
                return WorkflowRepairStoreResult.Failure(
                    "apply-state-invalid",
                    "The repair is not awaiting an apply result.");
            }

            if (!apply.Applied)
            {
                record.ReasonCode = apply.ErrorCode ?? "apply-failed";
                TransitionLocked(record, MauiFlowRepairOutcomeStates.Stale);
                return WorkflowRepairStoreResult.Failure(
                    record.ReasonCode,
                    apply.Error ?? "The flow update was not applied.");
            }

            record.NewFlowRevision = apply.NewFlowRevision;
            record.AppliedFlowDigest = apply.AppliedFlowDigest;
            record.AppliedPlanDigest = apply.AppliedPlanDigest;
            record.AppliedPlanRevision = apply.AppliedPlanRevision;
            record.AppliedSafetyPolicy = apply.AppliedSafetyPolicy;
            record.ApplyRunId = apply.ApplyRunId;
            record.AppliedAt = _clock.GetUtcNow();
            TransitionLocked(record, MauiFlowRepairOutcomeStates.Applied);
            return WorkflowRepairStoreResult.Success(CreateSnapshot(record));
        }
    }

    internal WorkflowRepairStoreResult RecordVerification(
        string? proposalId,
        IReadOnlyList<WorkflowRepairVerificationRun>? verificationRuns,
        Func<WorkflowRepairProposalSnapshot, string, WorkflowRepairHistoryAppendResult>? historyWriter = null)
    {
        lock (_gate)
        {
            if (!TryGetLocked(proposalId, out var record, out var error))
                return WorkflowRepairStoreResult.Failure("proposal-not-found", error!);
            if (record!.State != MauiFlowRepairOutcomeStates.Applied)
            {
                return WorkflowRepairStoreResult.Failure(
                    "verification-state-invalid",
                    "Only an applied repair can receive verification results.");
            }

            var priorState = record.State;
            var priorRevision = record.Revision;
            var priorUpdatedAt = record.UpdatedAt;
            var priorReasonCode = record.ReasonCode;
            var priorRecoveryState = record.LastRecoveryState;
            var priorVerification = record.Verification;
            var priorVerificationRunIds = record.VerificationRunIds;
            var runs = (verificationRuns ?? [])
                .Take(_options.MaxVerificationRuns)
                .Select(CloneVerification)
                .ToList();
            record.Verification = runs;
            record.VerificationRunIds = runs
                .Select(static run => run.RunId!)
                .Where(static id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (runs.Count == _options.MaxVerificationRuns &&
                runs.Select(static run => run.RunId)
                    .Where(static id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.Ordinal)
                    .Count() == _options.MaxVerificationRuns &&
                runs.All(static run => run.BrokerRetained && run.IsCleanVerifiedReplay) &&
                runs.All(run => run.StartedAt is not null &&
                    record.AppliedAt is not null &&
                    run.StartedAt >= record.AppliedAt))
            {
                TransitionLocked(record, MauiFlowRepairOutcomeStates.Verified);
                if (!TryPersistHistory(
                        historyWriter,
                        record,
                        MauiFlowRepairOutcomeStates.Verified,
                        out error))
                {
                    RestoreVerification();
                    return WorkflowRepairStoreResult.Failure("history-persistence-failed", error!);
                }
                return WorkflowRepairStoreResult.Success(CreateSnapshot(record));
            }

            record.ReasonCode = "verification-failed";
            record.LastRecoveryState = MauiFlowRepairOutcomeStates.VerificationFailed;
            TransitionLocked(record, MauiFlowRepairOutcomeStates.RollbackRequired);
            if (!TryPersistHistory(
                    historyWriter,
                    record,
                    MauiFlowRepairOutcomeStates.RollbackRequired,
                    out error))
            {
                RestoreVerification();
                return WorkflowRepairStoreResult.Failure("history-persistence-failed", error!);
            }
            return WorkflowRepairStoreResult.Success(CreateSnapshot(record));

            void RestoreVerification()
            {
                record.State = priorState;
                record.Revision = priorRevision;
                record.UpdatedAt = priorUpdatedAt;
                record.ReasonCode = priorReasonCode;
                record.LastRecoveryState = priorRecoveryState;
                record.Verification = priorVerification;
                record.VerificationRunIds = priorVerificationRunIds;
            }
        }
    }

    internal WorkflowRepairStoreResult BeginRollback(
        string? proposalId,
        string? rollbackGrant,
        WorkflowRepairGrantBinding? currentBinding)
    {
        lock (_gate)
        {
            if (!TryGetLocked(proposalId, out var record, out var error))
                return WorkflowRepairStoreResult.Failure("proposal-not-found", error!);
            if (record!.State != MauiFlowRepairOutcomeStates.RollbackRequired)
            {
                return WorkflowRepairStoreResult.Failure(
                    "rollback-not-required",
                    "This repair is not in rollback-required state.");
            }
            if (!BindingMatches(record, currentBinding, againstAppliedRevision: true, out error))
            {
                return WorkflowRepairStoreResult.Failure("rollback-binding-mismatch", error!);
            }
            if (!GrantMatchesCurrentBinding(rollbackGrant, currentBinding, out error))
                return WorkflowRepairStoreResult.Failure("rollback-grant-invalid", error!);
            if (!ConsumeGrantLocked(rollbackGrant, record, WorkflowRepairGrantKinds.Rollback, out error))
                return WorkflowRepairStoreResult.Failure("rollback-grant-invalid", error!);

            return WorkflowRepairStoreResult.Success(CreateSnapshot(record));
        }
    }

    internal WorkflowRepairStoreResult CompleteRollback(
        string? proposalId,
        WorkflowRepairRollbackRecord rollback)
    {
        ArgumentNullException.ThrowIfNull(rollback);
        lock (_gate)
        {
            if (!TryGetLocked(proposalId, out var record, out var error))
                return WorkflowRepairStoreResult.Failure("proposal-not-found", error!);
            if (record!.State != MauiFlowRepairOutcomeStates.RollbackRequired)
            {
                return WorkflowRepairStoreResult.Failure(
                    "rollback-state-invalid",
                    "The repair is not awaiting rollback.");
            }

            record.RollbackRevision = rollback.RollbackRevision;
            record.RollbackRunId = rollback.RollbackRunId;
            record.ReasonCode = rollback.ErrorCode;
            TransitionLocked(
                record,
                rollback.Reverted
                    ? MauiFlowRepairOutcomeStates.Reverted
                    : MauiFlowRepairOutcomeStates.RollbackFailed);
            return rollback.Reverted
                ? WorkflowRepairStoreResult.Success(CreateSnapshot(record))
                : WorkflowRepairStoreResult.Failure(
                    rollback.ErrorCode ?? "rollback-failed",
                    rollback.Error ?? "The rollback did not complete.",
                    CreateSnapshot(record));
        }
    }

    private static bool TryPersistHistory(
        Func<WorkflowRepairProposalSnapshot, string, WorkflowRepairHistoryAppendResult>? historyWriter,
        RepairRecord record,
        string state,
        out string? error)
    {
        error = null;
        if (historyWriter is null)
            return true;
        WorkflowRepairHistoryAppendResult result;
        try
        {
            result = historyWriter(CreateSnapshot(record), state);
        }
        catch (Exception exception)
        {
            error = "Repair history persistence failed: " + exception.Message;
            return false;
        }
        if (result.Ok)
            return true;
        error = result.Error ?? "Repair history persistence failed.";
        return false;
    }

    private static bool ValidateProposedRepair(MauiFlowRepairProposal proposal, out string? error)
    {
        error = null;
        if (proposal.BaseFlow is null ||
            string.IsNullOrWhiteSpace(proposal.BaseFlow.Path) ||
            string.IsNullOrWhiteSpace(proposal.BaseFlow.Digest) ||
            proposal.BaseFlow.Revision is null or < 1 ||
            string.IsNullOrWhiteSpace(proposal.SourceRunId) ||
            string.IsNullOrWhiteSpace(proposal.SourceStepId) ||
            !string.Equals(proposal.SourceFailureCode, MauiFlowFailureClasses.LocatorNotFound, StringComparison.Ordinal) ||
            proposal.PreDispatch != true ||
            proposal.OldSelector is null ||
            proposal.ProposedSelector is null ||
            proposal.Candidate is null ||
            proposal.Candidate.Unique != true ||
            proposal.Candidate.Fingerprint is null ||
            proposal.UniquenessProof?.MatchCount != 1 ||
            proposal.Patch is null ||
            proposal.Patch.SelectorOnly != true ||
            proposal.Patch.Operations.Count != 1 ||
            string.IsNullOrWhiteSpace(proposal.PatchDigest) ||
            proposal.UnchangedAssertionsProof?.Unchanged != true ||
            proposal.UnchangedAssertionsProof.ActionsUnchanged != true ||
            proposal.UnchangedAssertionsProof.ValuesUnchanged != true ||
            proposal.UnchangedAssertionsProof.OrderUnchanged != true)
        {
            error = "The repair proposal must contain a pre-dispatch locator-not-found source, base flow, unique fingerprinted candidate, unique-resolution proof, single selector-only patch, digest, and invariant proof.";
            return false;
        }
        return true;
    }

    private static bool ValidateTrustedContext(
        MauiFlowRepairProposal proposal,
        WorkflowRepairTrustedContext? context,
        out string? error)
    {
        error = null;
        if (context?.Eligibility?.Eligible != true ||
            context.ReplaySafety?.RepairValidationAllowed != true ||
            context.ReplaySafety.RepairEligibility != true ||
            context.ClassifiedCheckpoint is null ||
            string.IsNullOrWhiteSpace(context.PlanDigest) ||
            context.PlanRevision is null or < 1 ||
            string.IsNullOrWhiteSpace(context.SafetyPolicy) ||
            string.Equals(
                context.SafetyPolicy,
                MauiFlowSideEffectPolicies.NonReplayable,
                StringComparison.Ordinal) ||
            !string.Equals(
                context.Eligibility.SourceRunId,
                proposal.SourceRunId,
                StringComparison.Ordinal) ||
            !string.Equals(
                context.Eligibility.SourceStepId,
                proposal.SourceStepId,
                StringComparison.Ordinal) ||
            !CheckpointsMatch(context.Eligibility.CurrentCheckpoint, context.ClassifiedCheckpoint))
        {
            error = "A repair proposal requires retained broker eligibility, replay safety, classified checkpoint, and exact plan safety identity.";
            return false;
        }
        return true;
    }

    private bool IsGrantAllowed(RepairRecord record, string? kind, out string? error)
    {
        error = null;
        switch (kind)
        {
            case WorkflowRepairGrantKinds.Validation:
                if (record.State is MauiFlowRepairOutcomeStates.Proposed or MauiFlowRepairOutcomeStates.Previewed)
                    return true;
                break;
            case WorkflowRepairGrantKinds.Apply:
                if (record.State == MauiFlowRepairOutcomeStates.Previewed &&
                    record.Validation.LastOrDefault()?.Passed == true)
                    return true;
                error = "A successful bounded transient validation is required before an apply approval can be issued.";
                return false;
            case WorkflowRepairGrantKinds.Rollback:
                if (record.State == MauiFlowRepairOutcomeStates.RollbackRequired)
                    return true;
                break;
            default:
                error = "The requested repair grant kind is not supported.";
                return false;
        }

        error ??= $"Repair proposal state '{record.State}' cannot receive a {kind} grant.";
        return false;
    }

    private static bool BindingMatches(
        RepairRecord record,
        WorkflowRepairGrantBinding? binding,
        bool againstAppliedRevision,
        out string? error)
    {
        error = null;
        var baseFlow = record.Proposal.BaseFlow;
        var expectedDigest = againstAppliedRevision ? record.AppliedFlowDigest : baseFlow?.Digest;
        var expectedRevision = againstAppliedRevision ? record.NewFlowRevision : baseFlow?.Revision;
        var expectedPlanDigest = againstAppliedRevision
            ? record.AppliedPlanDigest
            : record.TrustedContext.PlanDigest;
        var expectedPlanRevision = againstAppliedRevision
            ? record.AppliedPlanRevision
            : record.TrustedContext.PlanRevision;
        var expectedSafetyPolicy = againstAppliedRevision
            ? record.AppliedSafetyPolicy
            : record.TrustedContext.SafetyPolicy;
        if (binding is null ||
            string.IsNullOrWhiteSpace(binding.FlowPath) ||
            string.IsNullOrWhiteSpace(binding.FlowDigest) ||
            !string.Equals(baseFlow?.Path, binding.FlowPath, StringComparison.Ordinal) ||
            !FixedEquals(expectedDigest, binding.FlowDigest) ||
            !string.Equals(record.Proposal.PatchDigest, binding.PatchDigest, StringComparison.Ordinal) ||
            expectedRevision != binding.FlowRevision ||
            string.IsNullOrWhiteSpace(binding.TargetId) ||
            string.IsNullOrWhiteSpace(binding.Policy) ||
            string.IsNullOrWhiteSpace(binding.PlanDigest) ||
            binding.PlanRevision is null ||
            string.IsNullOrWhiteSpace(binding.SafetyPolicy) ||
            !FixedEquals(expectedPlanDigest, binding.PlanDigest) ||
            expectedPlanRevision != binding.PlanRevision ||
            !string.Equals(
                expectedSafetyPolicy,
                binding.SafetyPolicy,
                StringComparison.Ordinal))
        {
            error = "The grant must be bound to the exact proposal patch, base flow path/digest/revision, target, and side-effect policy.";
            return false;
        }
        return true;
    }

    private bool ConsumeGrantLocked(
        string? secret,
        RepairRecord record,
        string expectedKind,
        out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(secret) || !_grants.TryGetValue(secret, out var grant))
        {
            error = "The repair grant is missing, stale, or already used.";
            return false;
        }
        var now = _clock.GetUtcNow();
        if (grant.Used || grant.ExpiresAt <= now)
        {
            _grants.Remove(secret);
            if (expectedKind == WorkflowRepairGrantKinds.Apply &&
                record.State == MauiFlowRepairOutcomeStates.Approved)
            {
                TransitionLocked(record, MauiFlowRepairOutcomeStates.ApprovalExpired);
            }
            error = grant.Used ? "The repair grant was already used." : "The repair grant expired.";
            return false;
        }
        if (!GrantBindsToRecord(grant, record, expectedKind))
        {
            error = "The repair grant is not bound to this current proposal revision.";
            return false;
        }

        grant.Used = true;
        return true;
    }

    private static bool GrantBindsToRecord(GrantRecord grant, RepairRecord record, string expectedKind)
        => grant.Kind == expectedKind &&
            string.Equals(grant.ProposalId, record.Proposal.ProposalId, StringComparison.Ordinal) &&
            grant.ProposalRevision == record.Revision &&
            FixedEquals(grant.PatchDigest, record.Proposal.PatchDigest) &&
            FixedEquals(
                grant.BaseFlowDigest,
                expectedKind == WorkflowRepairGrantKinds.Rollback
                    ? record.AppliedFlowDigest
                    : record.Proposal.BaseFlow?.Digest) &&
            grant.FlowRevision ==
                (expectedKind == WorkflowRepairGrantKinds.Rollback
                    ? record.NewFlowRevision
                    : record.Proposal.BaseFlow?.Revision) &&
            FixedEquals(
                grant.PlanDigest,
                expectedKind == WorkflowRepairGrantKinds.Rollback
                    ? record.AppliedPlanDigest
                    : record.TrustedContext.PlanDigest) &&
            grant.PlanRevision ==
                (expectedKind == WorkflowRepairGrantKinds.Rollback
                    ? record.AppliedPlanRevision
                    : record.TrustedContext.PlanRevision) &&
            string.Equals(
                grant.SafetyPolicy,
                expectedKind == WorkflowRepairGrantKinds.Rollback
                    ? record.AppliedSafetyPolicy
                    : record.TrustedContext.SafetyPolicy,
                StringComparison.Ordinal);

    private bool GrantMatchesCurrentBinding(
        string? secret,
        WorkflowRepairGrantBinding? current,
        out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(secret) ||
            !_grants.TryGetValue(secret, out var grant) ||
            current is null ||
            !string.Equals(grant.TargetId, current.TargetId, StringComparison.Ordinal) ||
            !string.Equals(grant.Policy, current.Policy, StringComparison.Ordinal) ||
            !FixedEquals(grant.PlanDigest, current.PlanDigest) ||
            grant.PlanRevision != current.PlanRevision ||
            !string.Equals(grant.SafetyPolicy, current.SafetyPolicy, StringComparison.Ordinal))
        {
            error = "The repair grant target or side-effect policy binding no longer matches.";
            return false;
        }
        return true;
    }

    private void RefreshExpiredApprovalLocked()
    {
        var now = _clock.GetUtcNow();
        foreach (var record in _records.Values)
        {
            if (record.State == MauiFlowRepairOutcomeStates.Approved &&
                record.ExpiresAt is { } expiry &&
                expiry <= now)
            {
                record.ReasonCode = "approval-expired";
                TransitionLocked(record, MauiFlowRepairOutcomeStates.ApprovalExpired);
            }
        }
    }

    private void PurgeExpiredGrantsLocked()
    {
        var now = _clock.GetUtcNow();
        foreach (var key in _grants
                     .Where(pair => pair.Value.ExpiresAt <= now || pair.Value.Used)
                     .Select(static pair => pair.Key)
                     .ToArray())
        {
            _grants.Remove(key);
        }
    }

    private void EvictTerminalLocked()
    {
        while (_records.Count >= _options.MaxProposals)
        {
            var candidate = _records.Values
                .Where(static record => IsTerminal(record.State))
                .OrderBy(static record => record.UpdatedAt)
                .FirstOrDefault();
            if (candidate is null)
                return;
            _records.Remove(candidate.Proposal.ProposalId!);
        }
    }

    private static bool IsTerminal(string state) => state is
        MauiFlowRepairOutcomeStates.Rejected or
        MauiFlowRepairOutcomeStates.Stale or
        MauiFlowRepairOutcomeStates.ApprovalExpired or
        MauiFlowRepairOutcomeStates.Verified or
        MauiFlowRepairOutcomeStates.Reverted or
        MauiFlowRepairOutcomeStates.RollbackFailed;

    private static void TransitionLocked(RepairRecord record, string state)
    {
        record.State = state;
        record.Revision++;
        record.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private bool TryGetLocked(string? proposalId, out RepairRecord? record, out string? error)
    {
        record = null;
        error = null;
        if (string.IsNullOrWhiteSpace(proposalId) || !_records.TryGetValue(proposalId, out record))
        {
            error = "The repair proposal was not found.";
            return false;
        }
        return true;
    }

    private static WorkflowRepairProposalSnapshot CreateSnapshot(RepairRecord record) => new()
    {
        Proposal = WithProposalMetadata(
            record.Proposal,
            record.Proposal.ProposalId!,
            record.Revision,
            record.State,
            record.Proposal.CreatedAt ?? record.CreatedAt,
            record.Reviewer,
            record.GrantDigest,
            record.ExpiresAt,
            record.ValidationRunIds,
            record.VerificationRunIds),
        State = record.State,
        Revision = record.Revision,
        AgentOriginated = record.AgentOriginated,
        TrustedContext = CloneTrustedContext(record.TrustedContext),
        Trust = record.Proposal.Trust,
        Reviewer = record.Reviewer,
        GrantDigest = record.GrantDigest,
        ExpiresAt = record.ExpiresAt,
        SourceRunId = record.Proposal.SourceRunId,
        SourceFailureId = record.Proposal.SourceFailureId,
        SourceStepId = record.Proposal.SourceStepId,
        BaseFlow = record.Proposal.BaseFlow,
        PatchDigest = record.Proposal.PatchDigest,
        ValidationRunIds = record.ValidationRunIds.ToList(),
        VerificationRunIds = record.VerificationRunIds.ToList(),
        NewFlowRevision = record.NewFlowRevision,
        AppliedFlowDigest = record.AppliedFlowDigest,
        AppliedPlanDigest = record.AppliedPlanDigest,
        AppliedPlanRevision = record.AppliedPlanRevision,
        AppliedSafetyPolicy = record.AppliedSafetyPolicy,
        RollbackRevision = record.RollbackRevision,
        AppliedAt = record.AppliedAt,
        LastRecoveryState = record.LastRecoveryState,
        ReasonCode = record.ReasonCode,
        Validation = record.Validation.Select(CloneValidation).ToList(),
        Verification = record.Verification.Select(CloneVerification).ToList(),
    };

    private static MauiFlowRepairProposal WithProposalMetadata(
        MauiFlowRepairProposal source,
        string id,
        int revision,
        string state,
        DateTimeOffset createdAt,
        string? reviewer,
        string? grantDigest,
        DateTimeOffset? expiresAt,
        IReadOnlyList<string>? validationRunIds = null,
        IReadOnlyList<string>? verificationRunIds = null)
    {
        var node = JsonSerializer.SerializeToNode(source, MauiTestingJsonContext.Default.MauiFlowRepairProposal)!.AsObject();
        node["proposalId"] = id;
        node["revision"] = revision;
        node["state"] = state;
        node["createdAt"] = createdAt;
        node["reviewer"] = reviewer;
        node["grantDigest"] = grantDigest;
        node["expiresAt"] = expiresAt;
        if (!string.IsNullOrWhiteSpace(reviewer) ||
            state is MauiFlowRepairOutcomeStates.Approved or MauiFlowRepairOutcomeStates.ApprovalExpired)
        {
            node["approval"] = new JsonObject
            {
                ["state"] = state,
                ["reviewer"] = reviewer,
                ["expiresAt"] = expiresAt,
            };
        }
        if (validationRunIds is not null)
            node["validationRunIds"] = JsonSerializer.SerializeToNode(validationRunIds);
        if (verificationRunIds is not null)
            node["verificationRunIds"] = JsonSerializer.SerializeToNode(verificationRunIds);
        return node.Deserialize(MauiTestingJsonContext.Default.MauiFlowRepairProposal)
            ?? throw new InvalidOperationException("A repair proposal could not be normalized.");
    }

    private static WorkflowRepairValidationRecord CloneValidation(WorkflowRepairValidationRecord value) => new()
    {
        Passed = value.Passed,
        RunIds = value.RunIds.Where(static id => !string.IsNullOrWhiteSpace(id)).Take(16).ToList(),
        EvidenceIds = value.EvidenceIds.Where(static id => !string.IsNullOrWhiteSpace(id)).Take(32).ToList(),
        FailureCode = Bounded(value.FailureCode, 128),
        FailureFacts = value.FailureFacts.Where(static fact => !string.IsNullOrWhiteSpace(fact)).Take(32).ToList(),
        RecordedAt = value.RecordedAt,
    };

    private static WorkflowRepairVerificationRun CloneVerification(WorkflowRepairVerificationRun value) => new()
    {
        RunId = Bounded(value.RunId, 128),
        EvidenceId = Bounded(value.EvidenceId, 256),
        ReportDigest = Bounded(value.ReportDigest, 256),
        StartedAt = value.StartedAt,
        BrokerRetained = value.BrokerRetained,
        CleanReset = value.CleanReset,
        CheckpointMatched = value.CheckpointMatched,
        FingerprintMatched = value.FingerprintMatched,
        UniqueResolution = value.UniqueResolution,
        HardAssertionsUnchanged = value.HardAssertionsUnchanged,
        IndependentOracleSucceeded = value.IndependentOracleSucceeded,
        Passed = value.Passed,
        FailureCode = Bounded(value.FailureCode, 128),
    };

    private static WorkflowRepairTrustedContext CloneTrustedContext(WorkflowRepairTrustedContext value)
        => new()
        {
            Eligibility = Clone(value.Eligibility, MauiTestingJsonContext.Default.MauiFlowRepairEligibilityDecision),
            ReplaySafety = Clone(value.ReplaySafety, MauiTestingJsonContext.Default.MauiFlowReplayEligibilityDecision),
            ClassifiedCheckpoint = Clone(value.ClassifiedCheckpoint, MauiTestingJsonContext.Default.MauiFlowCheckpoint),
            PlanDigest = value.PlanDigest,
            PlanRevision = value.PlanRevision,
            SafetyPolicy = value.SafetyPolicy,
        };

    private static T? Clone<T>(T? value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
        where T : class
    {
        if (value is null)
            return null;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, typeInfo);
        return JsonSerializer.Deserialize(bytes, typeInfo);
    }

    private static bool CheckpointsMatch(MauiFlowCheckpoint? first, MauiFlowCheckpoint? second)
    {
        if (first is null || second is null)
            return false;
        return string.Equals(first.AppBuildFingerprint, second.AppBuildFingerprint, StringComparison.Ordinal) &&
            string.Equals(first.AgentInstanceId, second.AgentInstanceId, StringComparison.Ordinal) &&
            string.Equals(first.SeedFingerprint, second.SeedFingerprint, StringComparison.Ordinal) &&
            string.Equals(first.BackendStateFingerprint, second.BackendStateFingerprint, StringComparison.Ordinal) &&
            string.Equals(first.Route, second.Route, StringComparison.Ordinal) &&
            string.Equals(first.Window, second.Window, StringComparison.Ordinal) &&
            string.Equals(first.Modal, second.Modal, StringComparison.Ordinal) &&
            string.Equals(first.Locale, second.Locale, StringComparison.Ordinal) &&
            string.Equals(first.Theme, second.Theme, StringComparison.Ordinal) &&
            string.Equals(first.Orientation, second.Orientation, StringComparison.Ordinal) &&
            string.Equals(first.DisplayProfile, second.DisplayProfile, StringComparison.Ordinal) &&
            string.Equals(first.CollectionItemKey, second.CollectionItemKey, StringComparison.Ordinal);
    }

    private static string OpaqueId(string prefix)
        => prefix + "_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    private static string OpaqueSecret(string prefix)
        => prefix + "_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();

    private static string Hash(string? value)
        => "sha256:" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty))).ToLowerInvariant();

    private static bool FixedEquals(string? first, string? second)
        => !string.IsNullOrWhiteSpace(first) &&
            !string.IsNullOrWhiteSpace(second) &&
            CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(first),
                Encoding.UTF8.GetBytes(second));

    private static string? Bounded(string? value, int length)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        return trimmed.Length <= length ? trimmed : null;
    }

    private sealed class RepairRecord
    {
        public RepairRecord(
            MauiFlowRepairProposal proposal,
            string state,
            int revision,
            bool agentOriginated,
            WorkflowRepairTrustedContext trustedContext,
            DateTimeOffset createdAt)
        {
            Proposal = proposal;
            State = state;
            Revision = revision;
            AgentOriginated = agentOriginated;
            TrustedContext = trustedContext;
            CreatedAt = createdAt;
            UpdatedAt = createdAt;
        }

        public MauiFlowRepairProposal Proposal { get; }
        public string State { get; set; }
        public int Revision { get; set; }
        public bool AgentOriginated { get; }
        public WorkflowRepairTrustedContext TrustedContext { get; }
        public DateTimeOffset CreatedAt { get; }
        public DateTimeOffset UpdatedAt { get; set; }
        public string? Reviewer { get; set; }
        public string? GrantDigest { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
        public string? ReasonCode { get; set; }
        public string? LastRecoveryState { get; set; }
        public int? NewFlowRevision { get; set; }
        public string? AppliedFlowDigest { get; set; }
        public string? AppliedPlanDigest { get; set; }
        public int? AppliedPlanRevision { get; set; }
        public string? AppliedSafetyPolicy { get; set; }
        public DateTimeOffset? AppliedAt { get; set; }
        public int? RollbackRevision { get; set; }
        public string? ApplyRunId { get; set; }
        public string? RollbackRunId { get; set; }
        public List<string> ValidationRunIds { get; set; } = [];
        public List<string> VerificationRunIds { get; set; } = [];
        public List<WorkflowRepairValidationRecord> Validation { get; } = [];
        public List<WorkflowRepairVerificationRun> Verification { get; set; } = [];
    }

    private sealed class GrantRecord
    {
        public GrantRecord(
            string secret,
            string digest,
            string? kind,
            string proposalId,
            int proposalRevision,
            string patchDigest,
            string baseFlowDigest,
            int? flowRevision,
            string? targetId,
            string? policy,
            string planDigest,
            int planRevision,
            string safetyPolicy,
            string reviewer,
            DateTimeOffset expiresAt)
        {
            Secret = secret;
            Digest = digest;
            Kind = kind ?? "";
            ProposalId = proposalId;
            ProposalRevision = proposalRevision;
            PatchDigest = patchDigest;
            BaseFlowDigest = baseFlowDigest;
            FlowRevision = flowRevision;
            TargetId = targetId;
            Policy = policy;
            PlanDigest = planDigest;
            PlanRevision = planRevision;
            SafetyPolicy = safetyPolicy;
            Reviewer = reviewer;
            ExpiresAt = expiresAt;
        }

        public string Secret { get; }
        public string Digest { get; }
        public string Kind { get; }
        public string ProposalId { get; }
        public int ProposalRevision { get; }
        public string PatchDigest { get; }
        public string BaseFlowDigest { get; }
        public int? FlowRevision { get; }
        public string? TargetId { get; }
        public string? Policy { get; }
        public string PlanDigest { get; }
        public int PlanRevision { get; }
        public string SafetyPolicy { get; }
        public string Reviewer { get; }
        public DateTimeOffset ExpiresAt { get; }
        public bool Used { get; set; }
    }
}

internal sealed class WorkflowRepairProposalStoreOptions
{
    public int MaxProposals { get; init; } = 128;
    public int MaxGrants { get; init; } = 256;
    public int MaxRunReferencesPerProposal { get; init; } = 32;
    public int MaxVerificationRuns { get; init; } = 3;
    public TimeSpan DefaultGrantLifetime { get; init; } = TimeSpan.FromMinutes(10);
    public TimeSpan MaximumGrantLifetime { get; init; } = TimeSpan.FromMinutes(30);
}

internal static class WorkflowRepairGrantKinds
{
    public const string Validation = "validation";
    public const string Apply = "apply";
    public const string Rollback = "rollback";
}

internal sealed class WorkflowRepairGrantBinding
{
    public string? FlowPath { get; init; }
    public string? FlowDigest { get; init; }
    public int? FlowRevision { get; init; }
    public string? PatchDigest { get; init; }
    public string? TargetId { get; init; }
    public string? Policy { get; init; }
    public string? PlanDigest { get; init; }
    public int? PlanRevision { get; init; }
    public string? SafetyPolicy { get; init; }
}

internal sealed class WorkflowRepairTrustedContext
{
    public MauiFlowRepairEligibilityDecision? Eligibility { get; init; }
    public MauiFlowReplayEligibilityDecision? ReplaySafety { get; init; }
    public MauiFlowCheckpoint? ClassifiedCheckpoint { get; init; }
    public string? PlanDigest { get; init; }
    public int? PlanRevision { get; init; }
    public string? SafetyPolicy { get; init; }
}

internal sealed class WorkflowRepairGrantIssueRequest
{
    public string? ProposalId { get; init; }
    public string? Kind { get; init; }
    public string? Reviewer { get; init; }
    public bool HumanConfirmed { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public WorkflowRepairGrantBinding? Binding { get; init; }
}

internal sealed class WorkflowRepairGrantIssueResult
{
    public bool Ok { get; private init; }
    public string? Grant { get; private init; }
    public string? GrantDigest { get; private init; }
    public DateTimeOffset? ExpiresAt { get; private init; }
    public WorkflowRepairProposalSnapshot? Proposal { get; private init; }
    public string? Code { get; private init; }
    public string? Error { get; private init; }

    public static WorkflowRepairGrantIssueResult Success(
        string grant,
        string digest,
        DateTimeOffset expiresAt,
        WorkflowRepairProposalSnapshot proposal) => new()
    {
        Ok = true,
        Grant = grant,
        GrantDigest = digest,
        ExpiresAt = expiresAt,
        Proposal = proposal,
    };

    public static WorkflowRepairGrantIssueResult Failure(string code, string error) => new()
    {
        Code = code,
        Error = error,
    };
}

internal sealed class WorkflowRepairValidationRecord
{
    public bool Passed { get; init; }
    public List<string> RunIds { get; init; } = [];
    public List<string> EvidenceIds { get; init; } = [];
    public string? FailureCode { get; init; }
    public List<string> FailureFacts { get; init; } = [];
    public DateTimeOffset RecordedAt { get; init; }
}

internal sealed class WorkflowRepairApplyRecord
{
    public bool Applied { get; init; }
    public int? NewFlowRevision { get; init; }
    public string? AppliedFlowDigest { get; init; }
    public string? AppliedPlanDigest { get; init; }
    public int? AppliedPlanRevision { get; init; }
    public string? AppliedSafetyPolicy { get; init; }
    public string? ApplyRunId { get; init; }
    public string? ErrorCode { get; init; }
    public string? Error { get; init; }
}

internal sealed class WorkflowRepairVerificationRun
{
    public string? RunId { get; init; }
    public string? EvidenceId { get; init; }
    public string? ReportDigest { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public bool BrokerRetained { get; init; }
    public bool CleanReset { get; init; }
    public bool CheckpointMatched { get; init; }
    public bool FingerprintMatched { get; init; }
    public bool UniqueResolution { get; init; }
    public bool HardAssertionsUnchanged { get; init; }
    public bool IndependentOracleSucceeded { get; init; }
    public bool Passed { get; init; }
    public string? FailureCode { get; init; }

    public bool IsCleanVerifiedReplay =>
        CleanReset &&
        CheckpointMatched &&
        FingerprintMatched &&
        UniqueResolution &&
        HardAssertionsUnchanged &&
        IndependentOracleSucceeded &&
        Passed;
}

internal sealed class WorkflowRepairRollbackRecord
{
    public bool Reverted { get; init; }
    public int? RollbackRevision { get; init; }
    public string? RollbackRunId { get; init; }
    public string? ErrorCode { get; init; }
    public string? Error { get; init; }
}

internal sealed class WorkflowRepairProposalSnapshot
{
    public MauiFlowRepairProposal Proposal { get; init; } = new();
    public string State { get; init; } = "";
    public int Revision { get; init; }
    public bool AgentOriginated { get; init; }
    public WorkflowRepairTrustedContext TrustedContext { get; init; } = new();
    public string? Trust { get; init; }
    public string? Reviewer { get; init; }
    public string? GrantDigest { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public string? SourceRunId { get; init; }
    public string? SourceFailureId { get; init; }
    public string? SourceStepId { get; init; }
    public MauiFlowReference? BaseFlow { get; init; }
    public string? PatchDigest { get; init; }
    public List<string> ValidationRunIds { get; init; } = [];
    public List<string> VerificationRunIds { get; init; } = [];
    public int? NewFlowRevision { get; init; }
    public string? AppliedFlowDigest { get; init; }
    public string? AppliedPlanDigest { get; init; }
    public int? AppliedPlanRevision { get; init; }
    public string? AppliedSafetyPolicy { get; init; }
    public DateTimeOffset? AppliedAt { get; init; }
    public int? RollbackRevision { get; init; }
    public string? LastRecoveryState { get; init; }
    public string? ReasonCode { get; init; }
    public List<WorkflowRepairValidationRecord> Validation { get; init; } = [];
    public List<WorkflowRepairVerificationRun> Verification { get; init; } = [];
}

internal sealed class WorkflowRepairStoreResult
{
    public bool Ok { get; private init; }
    public string? Code { get; private init; }
    public string? Error { get; private init; }
    public WorkflowRepairProposalSnapshot? Proposal { get; private init; }

    public static WorkflowRepairStoreResult Success(WorkflowRepairProposalSnapshot proposal) => new()
    {
        Ok = true,
        Proposal = proposal,
    };

    public static WorkflowRepairStoreResult Failure(
        string code,
        string error,
        WorkflowRepairProposalSnapshot? proposal = null) => new()
    {
        Code = code,
        Error = error,
        Proposal = proposal,
    };
}
