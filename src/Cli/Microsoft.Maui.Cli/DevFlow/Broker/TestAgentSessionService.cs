using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.Cli.DevFlow.Broker;

/// <summary>
/// Broker-owned policy boundary for the restricted test-agent profile. It retains only bounded
/// draft metadata and digests; app content, prompts, screenshots, source, and grant secrets never
/// enter its journal.
/// </summary>
internal sealed class TestAgentSessionService
{
    /// <summary>
    /// The fixed side-effect class every exploration step authorizes under. A human-approved
    /// exploration grant must list it explicitly, so an ordinary "ui" action grant can never be
    /// spent on exploration and an exploration grant can never be spent on an ordinary action.
    /// </summary>
    internal const string ExplorationSideEffectClass = "exploration";

    /// <summary>
    /// Upper bound on the number of named scopes a plan-declared exploration budget may retain.
    /// The list is echoed on every session snapshot, so it is bounded at normalization time.
    /// </summary>
    private const int MaxExplorationAllowedScopes = 32;

    private readonly object _gate = new();
    private readonly TestAgentSessionServiceOptions _options;
    private readonly TimeProvider _clock;
    private readonly Dictionary<string, SessionRecord> _sessions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GrantRecord> _grants = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ApprovalRequestRecord> _approvalRequests = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _expiredGrantDigests = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AuthorizationRecord> _authorizations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IdempotencyRecord> _idempotency = new(StringComparer.Ordinal);
    private readonly LinkedList<MauiTestAgentAuditEntry> _journal = [];
    private long _auditSequence;

    internal TestAgentSessionService(
        TestAgentSessionServiceOptions? options = null,
        TimeProvider? clock = null)
    {
        _options = options ?? new TestAgentSessionServiceOptions();
        _clock = clock ?? TimeProvider.System;

        if (_options.MaxSessions < 1 ||
            _options.MaxGrants < 1 ||
            _options.MaxApprovalRequests < 1 ||
            _options.MaxAuditEntries < 1 ||
            _options.MaxPatchesPerSession < 1 ||
            _options.MaxPatchRequestBytes is < 1 or > 1_048_576 ||
            _options.MaxActionsPerGrant is < 1 or > 256 ||
            _options.SessionLifetime <= TimeSpan.Zero ||
            _options.ApprovalRequestLifetime <= TimeSpan.Zero ||
            _options.ApprovalRetention <= TimeSpan.Zero ||
            _options.MaximumGrantLifetime <= TimeSpan.Zero ||
            _options.AuditRetention <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Test-agent session limits are invalid.");
        }
    }

    internal MauiTestAgentSessionResult Begin(MauiTestAgentSessionBeginRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            PurgeExpiredLocked();
            var envelope = request.Envelope;
            var error = ValidateEnvelope(envelope, requireSession: false);
            if (error is not null)
                return SessionFailure(error);

            if (!CanonicalTargetMatchesState(envelope!.Target, request.TargetState))
                return SessionFailure(Error(
                    MauiTestAgentErrorCodes.TargetStale,
                    MauiTestAgentErrorCategories.Target,
                    "The supplied target state does not describe the exact target in the request.",
                    retryable: false));

            var requestDigest = DigestEnvelope(envelope);
            if (_idempotency.TryGetValue(envelope.IdempotencyKey!, out var previous))
            {
                if (FixedEquals(previous.RequestDigest, requestDigest) &&
                    previous.SessionId is { } priorSessionId &&
                    _sessions.TryGetValue(priorSessionId, out var prior))
                {
                    if (!TryValidateActiveSessionLocked(prior, out error))
                        return SessionFailure(error!);

                    return new MauiTestAgentSessionResult
                    {
                        Ok = true,
                        Snapshot = CreateSnapshot(prior, includeReadCapability: true),
                    };
                }

                return SessionFailure(Error(
                    MauiTestAgentErrorCodes.IdempotencyReused,
                    MauiTestAgentErrorCategories.Conflict,
                    "The idempotency key was already used for a different authoring request.",
                    retryable: false));
            }

            if (_sessions.Count >= _options.MaxSessions)
                EvictOldestTerminalSessionLocked();
            if (_sessions.Count >= _options.MaxSessions)
            {
                return SessionFailure(Error(
                    MauiTestAgentErrorCodes.InvalidRequest,
                    MauiTestAgentErrorCategories.Capability,
                    "The broker has reached its bounded authoring-session capacity.",
                    retryable: true));
            }

            var now = _clock.GetUtcNow();
            var duration = request.DurationSeconds is { } seconds
                ? TimeSpan.FromSeconds(seconds)
                : _options.SessionLifetime;
            if (duration <= TimeSpan.Zero || duration > _options.SessionLifetime)
            {
                return SessionFailure(Error(
                    MauiTestAgentErrorCodes.InvalidRequest,
                    MauiTestAgentErrorCategories.Validation,
                    $"durationSeconds must be between 1 and {(int)_options.SessionLifetime.TotalSeconds}.",
                    retryable: false));
            }

            var sessionId = OpaqueId("author");
            var flowId = envelope.Correlation?.FlowId ?? OpaqueId("flow");
            var flow = CloneFlow(request.Flow) ?? new MauiFlow
            {
                Name = "agent-draft",
                RecordedAt = now.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            };
            var plan = NormalizePlan(
                request.Plan,
                envelope,
                flowId,
                flow,
                planId: envelope.Correlation?.PlanId ?? OpaqueId("plan"),
                revision: Math.Max(1, envelope.Correlation?.PlanRevision ?? 1),
                now);
            var planId = plan.PlanId!;
            var planRevision = plan.Revision!.Value;
            var flowDigest = MauiFlowRunReportSerializer.ComputeFlowDigest(flow);
            plan = NormalizePlan(plan, envelope, flowId, flow, planId, planRevision, now);

            var readCapability = OpaqueSecret("read");
            var record = new SessionRecord(
                sessionId,
                readCapability,
                Hash(readCapability),
                envelope.Target!,
                CloneTargetState(request.TargetState!),
                CloneProvenance(envelope.Provenance!)!,
                planId,
                planRevision,
                flowId,
                Math.Max(1, envelope.Correlation?.FlowRevision ?? 1),
                plan,
                flow,
                flowDigest,
                now,
                now.Add(duration));

            _sessions.Add(sessionId, record);
            _idempotency.Add(envelope.IdempotencyKey!, new IdempotencyRecord(requestDigest, sessionId, now));
            AppendAuditLocked(record, "session-begin", envelope, "accepted", null, null, null, null);

            var snapshot = CreateSnapshot(record, includeReadCapability: true);
            return new MauiTestAgentSessionResult { Ok = true, Snapshot = snapshot };
        }
    }

    internal MauiTestAgentSessionResult Status(MauiTestAgentSessionAccessRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            PurgeExpiredLocked();
            if (!TryGetReadableSessionLocked(
                    request.SessionId,
                    request.ReadCapabilityId,
                    request.Envelope,
                    out var session,
                    out var error))
                return SessionFailure(error!);

            return new MauiTestAgentSessionResult
            {
                Ok = true,
                Snapshot = CreateSnapshot(session!, includeReadCapability: false),
            };
        }
    }

    internal MauiTestAgentSessionResult Abandon(MauiTestAgentSessionAccessRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            PurgeExpiredLocked();
            if (!TryGetReadableSessionLocked(
                    request.SessionId,
                    request.ReadCapabilityId,
                    request.Envelope,
                    out var session,
                    out var error))
                return SessionFailure(error!);

            if (session!.State == SessionState.Active)
            {
                session.State = SessionState.Abandoned;
                AppendAuditLocked(session, "session-abandon", request.Envelope, "abandoned", null, null, null, null);
            }

            return new MauiTestAgentSessionResult
            {
                Ok = true,
                Snapshot = CreateSnapshot(session, includeReadCapability: false),
            };
        }
    }

    internal MauiTestAgentApprovalResult SubmitApprovalRequest(MauiTestAgentApprovalSubmitRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            PurgeExpiredLocked();
            var envelope = request.Envelope;
            var error = ValidateEnvelope(envelope, requireSession: true);
            if (error is not null)
                return ApprovalFailure(error);

            if (!string.Equals(envelope!.PolicyVersion, MauiTestAgentProtocolVersions.PolicyVersion, StringComparison.Ordinal))
            {
                return ApprovalFailure(Error(
                    MauiTestAgentErrorCodes.InvalidRequest,
                    MauiTestAgentErrorCategories.Validation,
                    "The approval request policy version is not supported.",
                    retryable: false));
            }

            var kind = NormalizeApprovalKind(request.Kind);
            if (kind is null || !MauiTestAgentApprovalKinds.All.Contains(kind))
            {
                return ApprovalFailure(Error(
                    MauiTestAgentErrorCodes.InvalidRequest,
                    MauiTestAgentErrorCategories.Validation,
                    "The approval request kind is not supported.",
                    retryable: false));
            }

            NormalizeApprovalScope(kind, request.Scope);
            var scopeError = ValidateScope(request.Scope);
            if (scopeError is not null)
                return ApprovalFailure(scopeError);
            if (!ApprovalKindAllowsScope(kind, request.Scope!))
            {
                return ApprovalFailure(Error(
                    MauiTestAgentErrorCodes.ApprovalRequestScopeDenied,
                    MauiTestAgentErrorCategories.Authorization,
                    "The requested actions do not match the stated approval purpose.",
                    retryable: false));
            }

            var sessionId = envelope.Correlation!.AuthoringSessionId;
            if (!TryGetReadableSessionLocked(
                    sessionId,
                    envelope.ReadCapabilityId,
                    envelope,
                    out var session,
                    out error))
                return ApprovalFailure(error!);
            if (!CanonicalTargetsMatch(session!.Target, envelope.Target) ||
                !CorrelationMatches(session, envelope.Correlation, requireAll: true) ||
                !ProvenanceMatches(session.Actor, envelope.Provenance))
            {
                return ApprovalFailure(Error(
                    MauiTestAgentErrorCodes.MutationGrantStale,
                    MauiTestAgentErrorCategories.State,
                    "The approval request does not match the current target, actor, plan, or flow revision.",
                    retryable: false));
            }

            var requestDigest = DigestApprovalRequest(request);
            if (_idempotency.TryGetValue(envelope.IdempotencyKey!, out var previous))
            {
                if (FixedEquals(previous.RequestDigest, requestDigest) &&
                    previous.ApprovalRequestId is { } priorRequestId &&
                    _approvalRequests.TryGetValue(priorRequestId, out var prior))
                {
                    return new MauiTestAgentApprovalResult
                    {
                        Ok = true,
                        Request = CloneApprovalRequest(prior, includeGrant: false),
                    };
                }

                return ApprovalFailure(Error(
                    MauiTestAgentErrorCodes.IdempotencyReused,
                    MauiTestAgentErrorCategories.Conflict,
                    "The idempotency key was already used for a different request.",
                    retryable: false));
            }

            if (_approvalRequests.Count >= _options.MaxApprovalRequests)
                EvictOldestApprovalRequestLocked();
            if (_approvalRequests.Count >= _options.MaxApprovalRequests)
            {
                return ApprovalFailure(Error(
                    MauiTestAgentErrorCodes.InvalidRequest,
                    MauiTestAgentErrorCategories.Capability,
                    "The broker has reached its bounded approval-request capacity.",
                    retryable: true));
            }

            var now = _clock.GetUtcNow();
            var defaultExpiresAt = now.Add(_options.ApprovalRequestLifetime);
            var requested = request.ExpiresAt ?? defaultExpiresAt;

            // Validate what the agent actually asked for *before* clamping. Clamping
            // first would let a far-future expiry slip through whenever the session
            // happened to end soon enough, which author.md refuses by contract.
            if (requested <= now ||
                requested > now.Add(_options.ApprovalRequestLifetime))
            {
                return ApprovalFailure(Error(
                    MauiTestAgentErrorCodes.InvalidRequest,
                    MauiTestAgentErrorCategories.Validation,
                    "The approval request expiration is outside the bounded session lifetime.",
                    retryable: false));
            }

            // An approval request must not outlive its session, but rejecting an
            // in-policy expiry that merely overshoots the session would leave an agent
            // unable to ask for review late in a session -- exactly when it needs one.
            var expiresAt = requested > session.ExpiresAt ? session.ExpiresAt : requested;
            if (expiresAt <= now)
            {
                return ApprovalFailure(Error(
                    MauiTestAgentErrorCodes.InvalidRequest,
                    MauiTestAgentErrorCategories.Validation,
                    "The approval request expiration is outside the bounded session lifetime.",
                    retryable: false));
            }

            var approvalRequestId = OpaqueId("approval");
            var record = new ApprovalRequestRecord(
                approvalRequestId,
                kind,
                session.SessionId,
                envelope.RequestId!,
                envelope.Intent!,
                session.Target,
                session.TargetState,
                session.Actor,
                SnapshotCorrelation(session),
                request.Scope!,
                now,
                expiresAt);
            _approvalRequests.Add(approvalRequestId, record);
            _idempotency.Add(
                envelope.IdempotencyKey!,
                new IdempotencyRecord(requestDigest, session.SessionId, now, approvalRequestId));
            AppendAuditLocked(session, "approval-requested", envelope, "pending", null, null, null, null);

            return new MauiTestAgentApprovalResult
            {
                Ok = true,
                Request = CloneApprovalRequest(record, includeGrant: false),
            };
        }
    }

    internal IReadOnlyList<MauiTestAgentApprovalRecord> ListApprovalRequests(
        string? agentId,
        string? agentInstanceId,
        bool includeGrant)
    {
        lock (_gate)
        {
            PurgeExpiredLocked();
            if (string.IsNullOrWhiteSpace(agentId) || string.IsNullOrWhiteSpace(agentInstanceId))
                return [];

            return _approvalRequests.Values
                .Where(request =>
                    string.Equals(request.Target.AgentId, agentId, StringComparison.Ordinal) &&
                    string.Equals(request.Target.AgentInstanceId, agentInstanceId, StringComparison.Ordinal))
                .OrderByDescending(static request => request.CreatedAt)
                .Select(request => CloneApprovalRequest(request, includeGrant))
                .ToArray();
        }
    }

    internal MauiTestAgentApprovalResult GetApprovalRequest(string? approvalRequestId, bool includeGrant)
    {
        lock (_gate)
        {
            PurgeExpiredLocked();
            if (string.IsNullOrWhiteSpace(approvalRequestId) ||
                !_approvalRequests.TryGetValue(approvalRequestId, out var request))
            {
                return ApprovalFailure(Error(
                    MauiTestAgentErrorCodes.ApprovalRequestNotFound,
                    MauiTestAgentErrorCategories.State,
                    "The approval request was not found.",
                    retryable: false));
            }

            return new MauiTestAgentApprovalResult
            {
                Ok = true,
                Request = CloneApprovalRequest(request, includeGrant),
            };
        }
    }

    internal MauiTestAgentApprovalResult ApproveApprovalRequest(
        string? approvalRequestId,
        MauiTestAgentMutationScope? approvedScope,
        MauiTestAgentTargetState? currentTargetState,
        MauiTestAgentHumanApproval? approval,
        DateTimeOffset? grantExpiresAt)
    {
        lock (_gate)
        {
            PurgeExpiredLocked();
            if (!TryGetPendingApprovalLocked(approvalRequestId, out var request, out var session, out var error))
                return ApprovalFailure(error!);
            var pendingRequest = request!;
            var owner = session!;
            if (!IsHumanHostApproval(approval))
            {
                return ApprovalFailure(Error(
                    MauiTestAgentErrorCodes.HumanApprovalRequired,
                    MauiTestAgentErrorCategories.Authorization,
                    "An explicit approval from an owner-token approval host is required.",
                    retryable: false));
            }

            if (!CanonicalTargetMatchesState(pendingRequest.Target, currentTargetState) ||
                !TargetStatesMatch(pendingRequest.TargetState, currentTargetState) ||
                !CorrelationMatches(owner, pendingRequest.Correlation, requireAll: true) ||
                !ProvenanceMatches(owner.Actor, pendingRequest.Provenance))
            {
                MarkApprovalLocked(pendingRequest, MauiTestAgentApprovalStates.Stale, "target-or-revision-stale");
                AppendAuditLocked(
                    owner,
                    "approval-stale",
                    ApprovalEnvelope(pendingRequest),
                    "stale",
                    null,
                    null,
                    null,
                    MauiTestAgentErrorCodes.MutationGrantStale);
                return ApprovalFailure(Error(
                    MauiTestAgentErrorCodes.MutationGrantStale,
                    MauiTestAgentErrorCategories.State,
                    "The target, actor, plan, or flow changed before approval.",
                    retryable: false));
            }

            var scope = approvedScope ?? pendingRequest.RequestedScope;
            NormalizeApprovalScope(pendingRequest.Kind, scope);
            var scopeError = ValidateScope(scope);
            if (scopeError is not null)
                return ApprovalFailure(scopeError);
            if (!ScopeIsSubset(scope!, pendingRequest.RequestedScope) ||
                !ApprovalKindAllowsScope(pendingRequest.Kind, scope!))
            {
                return ApprovalFailure(Error(
                    MauiTestAgentErrorCodes.ApprovalRequestScopeDenied,
                    MauiTestAgentErrorCategories.Authorization,
                    "An approval may narrow, but never broaden or repurpose, the agent-requested scope.",
                    retryable: false));
            }

            var grant = IssueGrantLocked(
                owner,
                scope!,
                grantExpiresAt,
                MauiTestAgentProtocolVersions.PolicyVersion,
                pendingRequest.ApprovalRequestId);
            if (!grant.Ok)
                return ApprovalFailure(grant.Error!);

            pendingRequest.State = MauiTestAgentApprovalStates.Approved;
            pendingRequest.ApprovedScope = CloneScope(scope!);
            pendingRequest.DecidedAt = _clock.GetUtcNow();
            pendingRequest.DecidedBy = DescribeDecisionIssuer(approval);
            pendingRequest.GrantId = grant.GrantId;
            pendingRequest.GrantDigest = grant.GrantDigest;
            pendingRequest.GrantExpiresAt = grant.ExpiresAt;
            AppendAuditLocked(
                owner,
                "approval-approved",
                ApprovalEnvelope(pendingRequest),
                "approved",
                grant.GrantDigest,
                null,
                null,
                null,
                decidedBy: pendingRequest.DecidedBy);
            return new MauiTestAgentApprovalResult
            {
                Ok = true,
                Request = CloneApprovalRequest(pendingRequest, includeGrant: false),
            };
        }
    }

    internal MauiTestAgentApprovalResult RejectApprovalRequest(
        string? approvalRequestId,
        MauiTestAgentHumanApproval? decision,
        string? reasonCode)
    {
        lock (_gate)
        {
            PurgeExpiredLocked();
            if (!TryGetPendingApprovalLocked(approvalRequestId, out var request, out var session, out var error))
                return ApprovalFailure(error!);
            var pendingRequest = request!;
            var owner = session!;
            if (!IsHumanHostDecision(decision))
            {
                return ApprovalFailure(Error(
                    MauiTestAgentErrorCodes.HumanApprovalRequired,
                    MauiTestAgentErrorCategories.Authorization,
                    "A decision from an owner-token approval host is required.",
                    retryable: false));
            }

            MarkApprovalLocked(pendingRequest, MauiTestAgentApprovalStates.Rejected, Bounded(reasonCode, 128) ?? "human-rejected");
            pendingRequest.DecidedBy = DescribeDecisionIssuer(decision);
            AppendAuditLocked(
                owner,
                "approval-rejected",
                ApprovalEnvelope(pendingRequest),
                "rejected",
                null,
                null,
                null,
                null,
                decidedBy: pendingRequest.DecidedBy);
            return new MauiTestAgentApprovalResult
            {
                Ok = true,
                Request = CloneApprovalRequest(pendingRequest, includeGrant: false),
            };
        }
    }

    /// <summary>
    /// Mints a bounded, single-family grant for an already-approved request.
    /// </summary>
    /// <remarks>
    /// This is deliberately the <b>only</b> way a grant comes into existence, and it is private:
    /// its callers are the approval decision paths, which have already matched a persisted pending
    /// request against the live target, actor, plan, and flow. There is no entry point that mints a
    /// grant from a caller-supplied "approved" flag, because such a flag is only ever the assertion
    /// of whoever is calling — an agent on the same OS user account can assert it as easily as a
    /// person can, so accepting it would make the whole approval ceremony decorative.
    /// </remarks>
    private MauiTestAgentGrantIssueResult IssueGrantLocked(
        SessionRecord session,
        MauiTestAgentMutationScope scope,
        DateTimeOffset? requestedExpiresAt,
        string policyVersion,
        string? approvalRequestId)
    {
            if (_grants.Count >= _options.MaxGrants)
                EvictExpiredGrantsLocked();
            if (_grants.Count >= _options.MaxGrants)
            {
                return GrantFailure(Error(
                    MauiTestAgentErrorCodes.InvalidRequest,
                    MauiTestAgentErrorCategories.Capability,
                    "The broker has reached its bounded mutation-grant capacity.",
                    retryable: true));
            }

            var now = _clock.GetUtcNow();
            var requested = requestedExpiresAt ?? now.Add(_options.DefaultGrantLifetime);

            // Validate the requested expiry against policy *before* clamping. Clamping
            // first would silently accept a far-future request whenever the session
            // happened to end within the maximum grant lifetime.
            if (requested <= now || requested > now.Add(_options.MaximumGrantLifetime))
            {
                return GrantFailure(Error(
                    MauiTestAgentErrorCodes.InvalidRequest,
                    MauiTestAgentErrorCategories.Validation,
                    "The requested grant expiration is outside the allowed session and policy bounds.",
                    retryable: false));
            }

            // A grant must never outlive its authoring session, but rejecting on that
            // basis makes human approval impossible: the longer a reviewer takes, the
            // further "now + requested duration" runs past the session end. Clamp to
            // the session instead, so a late approval simply yields a shorter grant.
            var expiresAt = requested > session.ExpiresAt ? session.ExpiresAt : requested;
            if (expiresAt <= now)
            {
                return GrantFailure(Error(
                    MauiTestAgentErrorCodes.InvalidRequest,
                    MauiTestAgentErrorCategories.Validation,
                    "The requested grant expiration is outside the allowed session and policy bounds.",
                    retryable: false));
            }

            var grantId = OpaqueSecret("grant");
            var grantDigest = Hash(grantId);
            var grant = new GrantRecord(
                grantDigest,
                session.SessionId,
                CloneTarget(session.Target),
                CloneTargetState(session.TargetState),
                CloneProvenance(session.Actor)!,
                SnapshotCorrelation(session),
                CloneScope(scope),
                expiresAt,
                policyVersion,
                approvalRequestId);
            _grants.Add(grantDigest, grant);
            AppendAuditLocked(session, "grant-issued", null, "approved", grantDigest, null, null, null);

            return new MauiTestAgentGrantIssueResult
            {
                Ok = true,
                GrantId = grantId,
                GrantDigest = grantDigest,
                RemainingActions = grant.RemainingActions,
                ExpiresAt = expiresAt,
            };
    }

    internal MauiTestAgentMutationAuthorizationResult AuthorizeMutation(
        MauiTestAgentMutationAuthorizationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            return AuthorizeMutationLocked(request, explorationOrigin: false);
        }
    }

    private MauiTestAgentMutationAuthorizationResult AuthorizeMutationLocked(
        MauiTestAgentMutationAuthorizationRequest request,
        bool explorationOrigin,
        bool purge = true)
    {
        // The exploration route purges before it reads the idempotency table, and purging a
        // second time here could age an entry out between those two reads, so the caller that
        // already purged suppresses it.
        if (purge)
            PurgeExpiredLocked();
        var envelope = request.Envelope;
        var error = ValidateEnvelope(envelope, requireSession: true);
        if (error is not null)
            return AuthorizationFailure(error);

        if (!_sessions.TryGetValue(envelope!.Correlation!.AuthoringSessionId!, out var session))
            return AuthorizationFailure(Error(
                MauiTestAgentErrorCodes.SessionNotFound,
                MauiTestAgentErrorCategories.State,
                "The authoring session was not found.",
                retryable: false));

        if (!TryValidateActiveSessionLocked(session, out error))
            return AuthorizationFailure(error!);

        // The exploration side-effect class belongs to the budget-enforcing exploration route
        // alone. Without this the ordinary action route could spend an approved exploration
        // grant while the budget counter, the allowed scopes, and the duration window are all
        // skipped, which would make the enforcement decorative. Both directions matter: the
        // request may not name the class, and a grant that approved it may not be redeemed
        // here even when the request names no class at all.
        TryPeekGrantLocked(envelope.ApprovalGrantId, out var peekedGrant);
        if (!explorationOrigin &&
            (string.Equals(request.SideEffectClass, ExplorationSideEffectClass, StringComparison.Ordinal) ||
             peekedGrant?.Scope.AllowedSideEffectClasses.Contains(ExplorationSideEffectClass, StringComparer.Ordinal) == true))
        {
            AppendAuditLocked(session, "mutation-denied", envelope, "denied", peekedGrant?.GrantDigest, null, null, MauiTestAgentErrorCodes.MutationGrantScopeDenied);
            return AuthorizationFailure(Error(
                MauiTestAgentErrorCodes.MutationGrantScopeDenied,
                MauiTestAgentErrorCategories.Authorization,
                "The exploration side-effect class may only be authorized through the bounded exploration route, which enforces the exploration budget.",
                retryable: false));
        }

        if (!CanonicalTargetsMatch(session.Target, envelope.Target) ||
            !CanonicalTargetMatchesState(session.Target, request.CurrentTargetState) ||
            !TargetStatesMatch(session.TargetState, request.CurrentTargetState))
        {
            AppendAuditLocked(session, "mutation-denied", envelope, "denied", null, null, null, MauiTestAgentErrorCodes.TargetStale);
            return AuthorizationFailure(Error(
                MauiTestAgentErrorCodes.TargetStale,
                MauiTestAgentErrorCategories.Target,
                "The target process, app build, or seed is stale. Refresh discovery and obtain a new human approval.",
                retryable: false));
        }

        if (!CorrelationMatches(session, envelope.Correlation, requireAll: true) &&
            !CorrelationMatchesGrantSequence(session, envelope.Correlation))
        {
            AppendAuditLocked(session, "mutation-denied", envelope, "denied", null, null, null, MauiTestAgentErrorCodes.MutationGrantStale);
            return AuthorizationFailure(Error(
                MauiTestAgentErrorCodes.MutationGrantStale,
                MauiTestAgentErrorCategories.State,
                "The request does not match the current plan or flow revision.",
                retryable: false));
        }

        if (!ProvenanceMatches(session.Actor, envelope.Provenance))
        {
            AppendAuditLocked(session, "mutation-denied", envelope, "denied", null, null, null, MauiTestAgentErrorCodes.MutationGrantScopeDenied);
            return AuthorizationFailure(Error(
                MauiTestAgentErrorCodes.MutationGrantScopeDenied,
                MauiTestAgentErrorCategories.Authorization,
                "The request actor, channel, or provider does not match the approved authoring session.",
                retryable: false));
        }

        if (!string.Equals(envelope.PolicyVersion, MauiTestAgentProtocolVersions.PolicyVersion, StringComparison.Ordinal))
            return AuthorizationFailure(Error(
                MauiTestAgentErrorCodes.MutationGrantScopeDenied,
                MauiTestAgentErrorCategories.Authorization,
                "The request policy version is not supported.",
                retryable: false));

        if (envelope.DeadlineMs is not { } deadlineMs || deadlineMs <= 0 || deadlineMs > 300_000)
        {
            return AuthorizationFailure(Error(
                MauiTestAgentErrorCodes.DeadlineExpired,
                MauiTestAgentErrorCategories.Validation,
                "deadlineMs must be a positive bounded duration.",
                retryable: false));
        }

        var requestDigest = DigestMutationRequest(request);
        if (_idempotency.TryGetValue(envelope.IdempotencyKey!, out var priorIdempotency))
        {
            if (FixedEquals(priorIdempotency.RequestDigest, requestDigest) &&
                priorIdempotency.AuthorizationId is { } priorAuthorizationId &&
                _authorizations.TryGetValue(priorAuthorizationId, out var priorAuthorization))
            {
                return new MauiTestAgentMutationAuthorizationResult
                {
                    Ok = true,
                    DispatchAllowed = true,
                    AuthorizationId = priorAuthorization.AuthorizationId,
                    RemainingActions = priorAuthorization.RemainingActions,
                    GrantDigest = priorAuthorization.GrantDigest,
                };
            }

            AppendAuditLocked(
                session,
                "mutation-denied",
                envelope,
                "idempotency-reused",
                null,
                null,
                null,
                MauiTestAgentErrorCodes.IdempotencyReused);
            return AuthorizationFailure(Error(
                MauiTestAgentErrorCodes.IdempotencyReused,
                MauiTestAgentErrorCategories.Conflict,
                "This idempotency key has already been used for a different request.",
                retryable: false));
        }

        if (string.IsNullOrWhiteSpace(envelope.ApprovalGrantId))
            return AuthorizationFailure(Error(
                MauiTestAgentErrorCodes.MutationGrantRequired,
                MauiTestAgentErrorCategories.Authorization,
                "A human-issued mutation grant is required before dispatch.",
                retryable: false));

        var grantDigest = Hash(envelope.ApprovalGrantId);
        if (!_grants.TryGetValue(grantDigest, out var grant) ||
            !FixedEquals(grantDigest, grant.GrantDigest))
        {
            if (_expiredGrantDigests.ContainsKey(grantDigest))
            {
                return AuthorizationFailure(Error(
                    MauiTestAgentErrorCodes.MutationGrantExpired,
                    MauiTestAgentErrorCategories.Authorization,
                    "The human-issued mutation grant has expired.",
                    retryable: false));
            }
            return AuthorizationFailure(Error(
                MauiTestAgentErrorCodes.MutationGrantRequired,
                MauiTestAgentErrorCategories.Authorization,
                "The mutation grant is not valid for this broker.",
                retryable: false));
        }

        var now = _clock.GetUtcNow();
        if (grant.ExpiresAt <= now)
        {
            AppendAuditLocked(session, "mutation-denied", envelope, "denied", grantDigest, null, null, MauiTestAgentErrorCodes.MutationGrantExpired);
            return AuthorizationFailure(Error(
                MauiTestAgentErrorCodes.MutationGrantExpired,
                MauiTestAgentErrorCategories.Authorization,
                "The human-issued mutation grant has expired.",
                retryable: false));
        }

        if (grant.RemainingActions <= 0)
        {
            AppendAuditLocked(session, "mutation-denied", envelope, "denied", grantDigest, null, null, MauiTestAgentErrorCodes.MutationGrantReused);
            return AuthorizationFailure(Error(
                MauiTestAgentErrorCodes.MutationGrantReused,
                MauiTestAgentErrorCategories.Authorization,
                "The mutation grant was already consumed.",
                retryable: false));
        }

        if (!GrantMatchesSession(grant, session))
        {
            AppendAuditLocked(session, "mutation-denied", envelope, "denied", grantDigest, null, null, MauiTestAgentErrorCodes.MutationGrantStale);
            return AuthorizationFailure(Error(
                MauiTestAgentErrorCodes.MutationGrantStale,
                MauiTestAgentErrorCategories.State,
                "The grant is bound to a stale target, revision, or actor.",
                retryable: false));
        }

        var scopeError = ValidateMutationScope(grant.Scope, request);
        if (scopeError is not null)
        {
            AppendAuditLocked(session, "mutation-denied", envelope, "denied", grantDigest, null, null, scopeError.Code);
            return AuthorizationFailure(scopeError);
        }

        grant.RemainingActions--;
        if (grant.RemainingActions == 0 &&
            grant.ApprovalRequestId is { } approvalRequestId &&
            _approvalRequests.TryGetValue(approvalRequestId, out var approvalRequest) &&
            approvalRequest.State == MauiTestAgentApprovalStates.Approved)
        {
            MarkApprovalLocked(approvalRequest, MauiTestAgentApprovalStates.Consumed, "grant-consumed");
            AppendAuditLocked(
                session,
                "approval-consumed",
                ApprovalEnvelope(approvalRequest),
                "consumed",
                grantDigest,
                null,
                null,
                null);
        }
        var authorizationId = OpaqueId("auth");
        _authorizations.Add(authorizationId, new AuthorizationRecord(
            authorizationId,
            session.SessionId,
            grantDigest,
            requestDigest,
            request.Action!,
            DigestAction(request),
            now,
            grant.RemainingActions,
            explorationOrigin));
        _idempotency.Add(
            envelope.IdempotencyKey!,
            new IdempotencyRecord(requestDigest, session.SessionId, now, authorizationId: authorizationId));
        AppendAuditLocked(session, "mutation-authorized", envelope, "authorized", grantDigest, DigestAction(request), null, null);

        return new MauiTestAgentMutationAuthorizationResult
        {
            Ok = true,
            DispatchAllowed = true,
            AuthorizationId = authorizationId,
            RemainingActions = grant.RemainingActions,
            GrantDigest = grantDigest,
        };
    }

    /// <summary>
    /// Authorizes exactly one bounded exploration step and consumes one unit of the approved
    /// exploration budget. The budget is enforced here, in the broker, and never by the caller:
    /// a session-scoped counter and duration window are checked and advanced under the same lock
    /// that consumes the human-issued grant, so a client cannot skip, reset, or over-report it.
    /// </summary>
    internal MauiTestAgentExplorationResult AuthorizeExploration(MauiTestAgentExplorationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            PurgeExpiredLocked();
            var envelope = request.Envelope;
            var error = ValidateEnvelope(envelope, requireSession: true);
            if (error is not null)
                return ExplorationFailure(error);

            if (!_sessions.TryGetValue(envelope!.Correlation!.AuthoringSessionId!, out var session))
                return ExplorationFailure(Error(
                    MauiTestAgentErrorCodes.SessionNotFound,
                    MauiTestAgentErrorCategories.State,
                    "The authoring session was not found.",
                    retryable: false));

            if (!TryValidateActiveSessionLocked(session, out error))
                return ExplorationFailure(error!);

            var action = request.Action?.Trim();
            if (string.IsNullOrEmpty(action) || !MauiTestAgentActions.Exploration.Contains(action))
            {
                AppendAuditLocked(session, "exploration-denied", envelope, "denied", null, null, null, MauiTestAgentErrorCodes.UnsupportedOperation);
                return ExplorationFailure(Error(
                    MauiTestAgentErrorCodes.UnsupportedOperation,
                    MauiTestAgentErrorCategories.Unsupported,
                    "Exploration may only tap, scroll, navigate, or back. Data entry, assertions, drafting, and runs each need their own approval.",
                    retryable: false));
            }

            // The grant must be one a human could only have issued for exploration: it names the
            // "exploration" side-effect class explicitly, and its actions are navigation-only. A
            // grant that also permits fill, assert, draft-append, run, cancel, or author-commit is
            // not an exploration grant and must not be spent here. The class requirement is what
            // keeps the two grant families disjoint, and it is re-checked by ValidateMutationScope
            // on the delegated path below, so this is a clearer refusal rather than the only one.
            if (TryPeekGrantLocked(envelope.ApprovalGrantId, out var peekedGrant) &&
                (!peekedGrant!.Scope.AllowedSideEffectClasses.Contains(ExplorationSideEffectClass, StringComparer.Ordinal) ||
                 !ApprovalKindAllowsScope(MauiTestAgentApprovalKinds.Exploration, peekedGrant.Scope) ||
                 peekedGrant.Scope.AllowedActions.Count == 0 ||
                 !peekedGrant.Scope.AllowedActions.All(MauiTestAgentActions.Exploration.Contains)))
            {
                AppendAuditLocked(session, "exploration-denied", envelope, "denied", peekedGrant!.GrantDigest, null, null, MauiTestAgentErrorCodes.MutationGrantScopeDenied);
                return ExplorationFailure(Error(
                    MauiTestAgentErrorCodes.MutationGrantScopeDenied,
                    MauiTestAgentErrorCategories.Authorization,
                    "Exploration requires a human-approved grant that names the \"exploration\" side-effect class and whose scope allows only navigation actions.",
                    retryable: false));
            }

            var now = _clock.GetUtcNow();
            var budget = session.Plan?.ExplorationBudget;
            if (budget is null ||
                budget.MaxActions is not > 0 ||
                budget.MaxDurationSeconds is not > 0 ||
                budget.AllowedScopes.Count == 0)
            {
                AppendAuditLocked(session, "exploration-denied", envelope, "denied", null, null, null, MauiTestAgentErrorCodes.ExplorationBudgetRequired);
                return ExplorationFailure(Error(
                    MauiTestAgentErrorCodes.ExplorationBudgetRequired,
                    MauiTestAgentErrorCategories.Authorization,
                    "The plan committed at session begin must declare an explorationBudget with a positive maxActions, a positive maxDurationSeconds, and at least one allowedScope before any exploration step is authorized. It cannot be added to a live session.",
                    retryable: false));
            }

            var scope = request.Scope?.Trim();
            if (string.IsNullOrEmpty(scope) || !budget.AllowedScopes.Contains(scope, StringComparer.Ordinal))
            {
                AppendAuditLocked(session, "exploration-denied", envelope, "denied", null, null, null, MauiTestAgentErrorCodes.ExplorationScopeDenied);
                return ExplorationFailure(Error(
                    MauiTestAgentErrorCodes.ExplorationScopeDenied,
                    MauiTestAgentErrorCategories.Authorization,
                    "The requested exploration scope is not listed in the session plan's explorationBudget.allowedScopes.",
                    retryable: false));
            }

            // The time window is enforced on every step. Only the action counter tolerates a
            // replay, because a retry of the last allowed step returns the prior authorization
            // without consuming a grant action and so must not be charged twice.
            if (EffectiveExplorationExpiry(session, budget) is { } windowExpiry && now >= windowExpiry)
            {
                AppendAuditLocked(session, "exploration-denied", envelope, "denied", null, null, null, MauiTestAgentErrorCodes.ExplorationBudgetExhausted);
                return ExplorationFailure(
                    Error(
                        MauiTestAgentErrorCodes.ExplorationBudgetExhausted,
                        MauiTestAgentErrorCategories.Authorization,
                        "The exploration time budget for this authoring session has elapsed. A new human approval is required.",
                        retryable: false),
                    DescribeExplorationBudgetLocked(session, now));
            }

            // The step must have a durable identity before any budget is spent, because that
            // identity is the only thing binding this authorization to one dispatched step. An
            // element action needs a selector with a scope key; a navigation needs a route.
            var stepDigest = DigestExplorationStep(action, request.Selector, request.Route);
            if (stepDigest is null)
            {
                AppendAuditLocked(session, "exploration-denied", envelope, "denied", null, null, null, MauiTestAgentErrorCodes.ExplorationScopeDenied);
                return ExplorationFailure(
                    Error(
                        MauiTestAgentErrorCodes.ExplorationScopeDenied,
                        MauiTestAgentErrorCategories.Authorization,
                        "An exploration step must name what it will touch: tap and scroll require a selector with a durable key such as an automationId, and navigate requires a route. A step that cannot be pinned cannot be bound to the budget it spends.",
                        retryable: false),
                    DescribeExplorationBudgetLocked(session, now));
            }

            // The prior authorization id, captured before dispatch, is what distinguishes a replay
            // from a fresh step. It is read after the single purge above, and the delegated call
            // is told not to purge again, so an entry cannot age out between the two reads and
            // mint an uncharged authorization.
            _idempotency.TryGetValue(envelope.IdempotencyKey!, out var priorIdempotency);
            var priorAuthorizationId = priorIdempotency?.AuthorizationId;
            var scopeDigest = Hash(scope);

            // The scope is not part of the delegated mutation request, so it is not covered by the
            // idempotency digest. Comparing it here keeps one key bound to one step: replaying a
            // key under a different approved scope is a different request, not a retry.
            if (priorAuthorizationId is not null &&
                _authorizations.TryGetValue(priorAuthorizationId, out var priorAuthorization) &&
                !string.Equals(priorAuthorization.ExplorationScopeDigest, scopeDigest, StringComparison.Ordinal))
            {
                AppendAuditLocked(session, "exploration-denied", envelope, "denied", null, null, null, MauiTestAgentErrorCodes.IdempotencyReused);
                return ExplorationFailure(
                    Error(
                        MauiTestAgentErrorCodes.IdempotencyReused,
                        MauiTestAgentErrorCategories.Conflict,
                        "This idempotency key was already used for a different exploration scope. Use a fresh key.",
                        retryable: false),
                    DescribeExplorationBudgetLocked(session, now));
            }

            if (priorAuthorizationId is null &&
                session.ExplorationActionsUsed >= EffectiveExplorationActions(budget))
            {
                AppendAuditLocked(session, "exploration-denied", envelope, "denied", null, null, null, MauiTestAgentErrorCodes.ExplorationBudgetExhausted);
                return ExplorationFailure(
                    Error(
                        MauiTestAgentErrorCodes.ExplorationBudgetExhausted,
                        MauiTestAgentErrorCategories.Authorization,
                        "The exploration action budget for this authoring session is fully spent. A new human approval is required.",
                        retryable: false),
                    DescribeExplorationBudgetLocked(session, now));
            }

            // Every remaining check — grant validity and expiry, scope subset, target freshness,
            // correlation, provenance, policy version, deadline, and idempotency — is delegated to
            // the single mutation authorization path so exploration can never be a softer door.
            // The lock is already held, so the budget decision and the grant consumption are atomic.
            var authorization = AuthorizeMutationLocked(
                new MauiTestAgentMutationAuthorizationRequest
                {
                    Envelope = envelope,
                    Action = action,
                    Selector = request.Selector,
                    Route = request.Route,
                    SideEffectClass = ExplorationSideEffectClass,
                    CurrentTargetState = request.CurrentTargetState,
                },
                explorationOrigin: true,
                purge: false);
            if (authorization.Ok != true || authorization.AuthorizationId is null)
            {
                return ExplorationFailure(
                    authorization.Error ?? Error(
                        MauiTestAgentErrorCodes.MutationGrantRequired,
                        MauiTestAgentErrorCategories.Authorization,
                        "The exploration step was not authorized.",
                        retryable: false),
                    DescribeExplorationBudgetLocked(session, now));
            }

            if (!string.Equals(authorization.AuthorizationId, priorAuthorizationId, StringComparison.Ordinal))
            {
                session.ExplorationStartedAt ??= now;
                session.ExplorationActionsUsed++;
            }

            if (_authorizations.TryGetValue(authorization.AuthorizationId, out var mintedAuthorization))
            {
                mintedAuthorization.ExplorationScopeDigest = scopeDigest;
                mintedAuthorization.ExplorationStepDigest = stepDigest;
            }

            AppendAuditLocked(
                session,
                "exploration-authorized",
                envelope,
                "authorized",
                authorization.GrantDigest,
                Hash(action + "\u001f" + scope),
                null,
                null);

            var remaining = DescribeExplorationBudgetLocked(session, now);
            return new MauiTestAgentExplorationResult
            {
                Ok = true,
                DispatchAllowed = true,
                AuthorizationId = authorization.AuthorizationId,
                RemainingActions = remaining.RemainingActions,
                ExplorationBudget = remaining,
            };
        }
    }

    private bool TryPeekGrantLocked(string? approvalGrantId, out GrantRecord? grant)
    {
        grant = null;
        if (string.IsNullOrWhiteSpace(approvalGrantId))
            return false;
        var grantDigest = Hash(approvalGrantId);
        return _grants.TryGetValue(grantDigest, out grant) && FixedEquals(grantDigest, grant.GrantDigest);
    }

    private int EffectiveExplorationActions(MauiExplorationBudget budget)
        => Math.Min(budget.MaxActions ?? 0, _options.MaxExplorationActions);

    /// <summary>
    /// The window a declared budget actually buys, clamped by broker policy. Callers reach this
    /// only after establishing that <c>MaxDurationSeconds</c> is positive, so the fallback exists
    /// solely to keep the clamp total; it is not a default a plan can inherit by omission.
    /// </summary>
    private int EffectiveExplorationDurationSeconds(MauiExplorationBudget budget)
        => (int) Math.Min(
            budget.MaxDurationSeconds is > 0 and var seconds ? seconds : 0,
            _options.MaximumExplorationWindow.TotalSeconds);

    private DateTimeOffset? EffectiveExplorationExpiry(SessionRecord session, MauiExplorationBudget budget)
    {
        if (session.ExplorationStartedAt is not { } startedAt)
            return null;
        var expiresAt = startedAt + TimeSpan.FromSeconds(EffectiveExplorationDurationSeconds(budget));
        return expiresAt < session.ExpiresAt ? expiresAt : session.ExpiresAt;
    }

    private MauiTestAgentExplorationBudgetState DescribeExplorationBudgetLocked(
        SessionRecord session,
        DateTimeOffset now)
    {
        var budget = session.Plan?.ExplorationBudget;
        if (budget is null ||
            budget.MaxActions is not > 0 ||
            budget.MaxDurationSeconds is not > 0 ||
            budget.AllowedScopes.Count == 0)
        {
            return new MauiTestAgentExplorationBudgetState
            {
                Declared = false,
                UsedActions = session.ExplorationActionsUsed,
                RemainingActions = 0,
                Exhausted = true,
            };
        }

        var maxActions = EffectiveExplorationActions(budget);
        var remainingActions = Math.Max(0, maxActions - session.ExplorationActionsUsed);
        var expiresAt = EffectiveExplorationExpiry(session, budget);
        var remainingSeconds = expiresAt is { } expiry
            ? (int) Math.Max(0, Math.Ceiling((expiry - now).TotalSeconds))
            : (int?) null;
        return new MauiTestAgentExplorationBudgetState
        {
            Declared = true,
            MaxActions = maxActions,
            UsedActions = session.ExplorationActionsUsed,
            RemainingActions = remainingActions,
            // Always the enforced ceiling, never the plan's unclamped ask: an agent that read the
            // larger declared number would plan for time the broker will not grant.
            MaxDurationSeconds = EffectiveExplorationDurationSeconds(budget),
            RemainingSeconds = remainingSeconds,
            StartedAt = session.ExplorationStartedAt,
            ExpiresAt = expiresAt,
            AllowedScopes = [.. budget.AllowedScopes],
            Exhausted = remainingActions == 0 || remainingSeconds == 0,
        };
    }

    /// <summary>
    /// Verifies, broker-side, that a workflow-run dispatch is backed by a live human-issued grant.
    /// The calling client already checks this, but the broker must not rely on a client to enforce
    /// its own authorization boundary. Each authorization dispatches at most one run.
    /// </summary>
    /// <param name="steps">
    /// The steps of the flow this dispatch would replay, as the broker read them from the run
    /// request. An exploration authorization is bound to exactly one navigation step, so an unknown
    /// flow is refused rather than assumed benign. There is deliberately no overload that omits
    /// this: a caller that could not name the flow would silently drop that binding.
    /// </param>
    internal bool TryConsumeRunDispatchAuthorization(
        string? authorizationId,
        string? agentId,
        string? agentInstanceId,
        IReadOnlyList<FlowStep>? steps,
        string? flowDigest,
        out string? error)
    {
        lock (_gate)
        {
            PurgeExpiredLocked();
            if (!TryResolveRunDispatchAuthorizationLocked(
                    authorizationId,
                    agentId,
                    agentInstanceId,
                    steps,
                    flowDigest,
                    out var authorization,
                    out error))
            {
                return false;
            }

            authorization!.RunDispatched = true;
            return true;
        }
    }

    /// <summary>
    /// Answers the same question without spending the grant, so a caller can refuse an obviously
    /// unauthorized request early. This is a filter, never the authorization itself: the single-use
    /// decision stays with <see cref="TryConsumeRunDispatchAuthorization"/>.
    /// </summary>
    internal bool CanDispatchRunAuthorization(
        string? authorizationId,
        string? agentId,
        string? agentInstanceId,
        IReadOnlyList<FlowStep>? steps,
        string? flowDigest,
        out string? error)
    {
        lock (_gate)
        {
            PurgeExpiredLocked();
            return TryResolveRunDispatchAuthorizationLocked(
                authorizationId,
                agentId,
                agentInstanceId,
                steps,
                flowDigest,
                out _,
                out error);
        }
    }

    private bool TryResolveRunDispatchAuthorizationLocked(
        string? authorizationId,
        string? agentId,
        string? agentInstanceId,
        IReadOnlyList<FlowStep>? steps,
        string? flowDigest,
        out AuthorizationRecord? authorization,
        out string? error)
    {
        authorization = null;
        if (string.IsNullOrWhiteSpace(authorizationId) ||
            !_authorizations.TryGetValue(authorizationId, out var candidate))
        {
            error = "A human-issued mutation authorization is required to start a workflow run.";
            return false;
        }

        if (candidate.ExplorationOrigin)
        {
            // One unit of exploration budget buys exactly one navigation step, and only the step
            // that was authorized — same action, same selector, same route. Without this the budget
            // would bound how *often* the agent may dispatch, not what it may do: a unit approved
            // to tap "cart" could replay a tap on "delete-account", or an arbitrary flow of fills
            // and assertions the exploration grant never approved. An unreadable flow is refused
            // for the same reason, and so is a step with no durable identity: two digests that are
            // both null describe two steps nobody can tell apart, which is not a match.
            var dispatchedDigest = steps is { Count: 1 } && MauiTestAgentActions.Exploration.Contains(steps[0].Action)
                ? DigestExplorationStep(steps[0])
                : null;
            if (candidate.ExplorationStepDigest is null ||
                dispatchedDigest is null ||
                !string.Equals(dispatchedDigest, candidate.ExplorationStepDigest, StringComparison.Ordinal))
            {
                error = "An exploration authorization dispatches exactly the one navigation step it authorized. A different or wider flow needs its own human approval.";
                return false;
            }
        }

        if (candidate.RunDispatched)
        {
            error = "The mutation authorization was already used to start a workflow run.";
            return false;
        }

        if (!_sessions.TryGetValue(candidate.SessionId, out var session))
        {
            error = "The authoring session for this authorization is no longer active.";
            return false;
        }
        if (!_grants.TryGetValue(candidate.GrantDigest, out var sourceGrant) ||
            !CorrelationMatches(session, sourceGrant.Correlation, requireAll: true))
        {
            error = "The mutation authorization is stale because the approved plan or flow changed.";
            return false;
        }

        if (!string.Equals(session.Target?.AgentId, agentId, StringComparison.Ordinal) ||
            !string.Equals(session.Target?.AgentInstanceId, agentInstanceId, StringComparison.Ordinal))
        {
            error = "The authorization is bound to a different agent instance.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(flowDigest) ||
            !FixedEquals(session.FlowDigest, flowDigest) ||
            !FixedEquals(sourceGrant.Correlation.FlowDigest ?? string.Empty, flowDigest))
        {
            error = "The mutation authorization is bound to a different committed flow digest.";
            return false;
        }

        authorization = candidate;
        error = null;
        return true;
    }

    internal MauiTestAgentToolResult CompleteMutation(MauiTestAgentMutationCompletion completion)
    {
        ArgumentNullException.ThrowIfNull(completion);
        lock (_gate)
        {
            PurgeExpiredLocked();
            if (string.IsNullOrWhiteSpace(completion.AuthorizationId) ||
                !_authorizations.TryGetValue(completion.AuthorizationId, out var authorization) ||
                !_sessions.TryGetValue(authorization.SessionId, out var session))
            {
                return ToolFailure(Error(
                    MauiTestAgentErrorCodes.InvalidRequest,
                    MauiTestAgentErrorCategories.Authorization,
                    "The mutation authorization is not valid.",
                    retryable: false));
            }

            if (authorization.Completed)
            {
                return ToolFailure(Error(
                    MauiTestAgentErrorCodes.IdempotencyReused,
                    MauiTestAgentErrorCategories.Conflict,
                    "The mutation completion was already recorded.",
                    retryable: false));
            }

            authorization.Completed = true;
            authorization.Outcome = NormalizeOutcome(completion.Outcome);
            authorization.RunId = Bounded(completion.RunId, 256);
            var actionDigest = BoundedDigest(completion.ActionDigest) ?? authorization.ActionDigest;
            var resultDigest = BoundedDigest(completion.ResultDigest);
            var errorCode = Bounded(completion.ErrorCode, 128);
            AppendAuditLocked(
                session,
                "mutation-completed",
                null,
                authorization.Outcome!,
                authorization.GrantDigest,
                actionDigest,
                resultDigest,
                errorCode,
                authorization.RunId);

            return new MauiTestAgentToolResult { Ok = true };
        }
    }

    internal MauiTestAgentSessionResult AppendAction(
        string? sessionId,
        string? authorizationId,
        MauiTestAgentActionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            PurgeExpiredLocked();
            if (!TryGetAuthorizedSessionLocked(
                    sessionId,
                    authorizationId,
                    new HashSet<string>(StringComparer.Ordinal) { MauiTestAgentActions.DraftAppend },
                    out var session,
                    out var error))
                return SessionFailure(error!);
            error = ValidateBoundSessionEnvelope(session!, request.Envelope);
            if (error is not null)
                return SessionFailure(error);
            if (!MatchesActionAuthorization(
                    authorizationId,
                    MauiTestAgentActions.DraftAppend,
                    DigestDraftAppend(request),
                    out error))
            {
                return SessionFailure(error!);
            }

            if (!TryCreateFlowStep(request, session!, out var step, out error))
                return SessionFailure(error!);
            ConsumeAuthorizationLocked(authorizationId);

            session!.Flow.Steps.Add(step!);
            session.FlowDigest = MauiFlowRunReportSerializer.ComputeFlowDigest(session.Flow);
            AdvanceGrantCorrelationLocked(authorizationId, session);
            InvalidatePendingApprovalsLocked(session, "draft-changed");
            AppendAuditLocked(
                session,
                "draft-action-appended",
                request.Envelope,
                "draft-updated",
                null,
                DigestDraftAppend(request),
                null,
                null);
            return new MauiTestAgentSessionResult
            {
                Ok = true,
                Snapshot = CreateSnapshot(session, includeReadCapability: false),
            };
        }
    }

    internal MauiTestAgentSessionResult AddAssertion(
        string? sessionId,
        string? authorizationId,
        MauiTestAgentAssertionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            PurgeExpiredLocked();
            if (!TryGetAuthorizedSessionLocked(
                sessionId,
                authorizationId,
                new HashSet<string>(StringComparer.Ordinal) { MauiTestAgentActions.Assert },
                out var session,
                out var error))
                return SessionFailure(error!);
            error = ValidateBoundSessionEnvelope(session!, request.Envelope);
            if (error is not null)
                return SessionFailure(error);

            var assertion = request.Assertion;
            if (assertion is null || string.IsNullOrWhiteSpace(assertion.Kind))
            {
                return SessionFailure(Error(
                    MauiTestAgentErrorCodes.InvalidRequest,
                    MauiTestAgentErrorCategories.Validation,
                    "A typed assertion is required.",
                    retryable: false));
            }
            if (FlowSecretReference.LooksSensitive(assertion.Name, assertion.Expected, assertion.Note))
            {
                return SessionFailure(Error(
                    MauiTestAgentErrorCodes.UntrustedPolicyInput,
                    MauiTestAgentErrorCategories.Validation,
                    "Sensitive assertion text cannot be retained by the restricted protocol.",
                    retryable: false));
            }
            var assertionDigest = BuildActionDigest(
                MauiTestAgentActions.Assert,
                assertion.Selector,
                route: null,
                valueLength: assertion.Expected is null ? null : Encoding.UTF8.GetByteCount(assertion.Expected),
                valueDigest: assertion.Expected is null ? null : Hash(assertion.Expected));
            if (!MatchesActionAuthorization(
                    authorizationId,
                    MauiTestAgentActions.Assert,
                    assertionDigest,
                    out error))
            {
                return SessionFailure(error!);
            }
            ConsumeAuthorizationLocked(authorizationId);

            var target = request.StepSequence is { } sequence
                ? session!.Flow.Steps.LastOrDefault(step => step.Seq == sequence)
                : session!.Flow.Steps.LastOrDefault();
            if (target is null)
            {
                target = new FlowStep
                {
                    Seq = NextSequence(session.Flow),
                    Action = FlowActions.Assert,
                    Intent = request.Envelope?.Intent,
                };
                session.Flow.Steps.Add(target);
            }
            target.Asserts ??= [];
            target.Asserts.Add(CloneAssertion(assertion));
            session.FlowDigest = MauiFlowRunReportSerializer.ComputeFlowDigest(session.Flow);
            AdvanceGrantCorrelationLocked(authorizationId, session);
            InvalidatePendingApprovalsLocked(session, "draft-changed");
            AppendAuditLocked(session, "draft-assertion-added", request.Envelope, "draft-updated", null, assertionDigest, null, null);

            return new MauiTestAgentSessionResult
            {
                Ok = true,
                Snapshot = CreateSnapshot(session, includeReadCapability: false),
            };
        }
    }

    internal MauiTestAgentSessionResult Commit(
        MauiTestAgentSessionAccessRequest request,
        string? authorizationId)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            PurgeExpiredLocked();
            if (!TryGetAuthorizedSessionLocked(
                request.SessionId,
                authorizationId,
                new HashSet<string>(StringComparer.Ordinal) { MauiTestAgentActions.AuthorCommit },
                out var session,
                out var error))
                return SessionFailure(error!);
            error = ValidateBoundSessionEnvelope(session!, request.Envelope);
            if (error is not null)
                return SessionFailure(error);

            var flowValidation = MauiFlowValidator.Validate(session!.Flow);
            if (!flowValidation.Ok)
            {
                return SessionFailure(Error(
                    MauiTestAgentErrorCodes.InvalidRequest,
                    MauiTestAgentErrorCategories.Validation,
                    "The draft flow is not valid: " + string.Join(" ", flowValidation.Errors.Take(4)),
                    retryable: false));
            }
            ConsumeAuthorizationLocked(authorizationId);

            session.FlowRevision++;
            session.FlowDigest = MauiFlowRunReportSerializer.ComputeFlowDigest(session.Flow);
            session.PlanRevision++;
            session.Plan = NormalizePlan(
                session.Plan,
                new MauiTestAgentRequestEnvelope
                {
                    Correlation = new MauiTestAgentCorrelation
                    {
                        PlanId = session.PlanId,
                        PlanRevision = session.PlanRevision,
                        FlowId = session.FlowId,
                        FlowRevision = session.FlowRevision,
                        FlowDigest = session.FlowDigest,
                    },
                    Provenance = session.Actor,
                    Intent = "Human-approved test-agent draft commit",
                },
                session.FlowId,
                session.Flow,
                session.PlanId,
                session.PlanRevision,
                _clock.GetUtcNow());
            var planValidation = MauiTestPlanValidator.Validate(session.Plan);
            if (!planValidation.IsValid)
            {
                return SessionFailure(Error(
                    MauiTestAgentErrorCodes.InvalidRequest,
                    MauiTestAgentErrorCategories.Validation,
                    "The draft plan is not valid: " + string.Join(" ", planValidation.Errors.Take(4)),
                    retryable: false));
            }

            session.PlanDigest = ComputePlanDigest(session.Plan);
            session.CommittedAt = _clock.GetUtcNow();
            InvalidatePendingApprovalsLocked(session, "draft-committed");
            AppendAuditLocked(session, "draft-commit", request.Envelope, "committed", null, Hash(session.FlowDigest), session.PlanDigest, null);
            return new MauiTestAgentSessionResult
            {
                Ok = true,
                Snapshot = CreateSnapshot(session, includeReadCapability: false),
            };
        }
    }

    internal MauiTestAgentSessionResult MigratePreview(MauiTestAgentSessionAccessRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            PurgeExpiredLocked();
            if (!TryGetReadableSessionLocked(
                    request.SessionId,
                    request.ReadCapabilityId,
                    request.Envelope,
                    out var session,
                    out var error))
                return SessionFailure(error!);

            var preview = MauiFlowMigration.Preview(session!.Flow);
            var snapshot = CreateSnapshot(session, includeReadCapability: false);
            if (preview.NormalizedFlow is not null)
                snapshot.Flow = preview.NormalizedFlow;
            return new MauiTestAgentSessionResult { Ok = true, Snapshot = snapshot };
        }
    }

    internal MauiTestAgentPatchResult Patch(MauiTestAgentPatchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            PurgeExpiredLocked();
            var envelope = request.Envelope;
            if (envelope?.Correlation?.AuthoringSessionId is not { Length: > 0 } sessionId)
            {
                return new MauiTestAgentPatchResult { Error = Error(
                    MauiTestAgentErrorCodes.ReadCapabilityRequired,
                    MauiTestAgentErrorCategories.Authorization,
                    "A read capability is required.",
                    retryable: false) };
            }
            if (!TryGetReadableSessionLocked(
                    sessionId,
                    envelope.ReadCapabilityId,
                    envelope,
                    out var session,
                    out var error))
            {
                return new MauiTestAgentPatchResult { Error = error };
            }

            var operation = request.Operation?.Trim().ToLowerInvariant();
            var requestBytes = JsonSerializer.SerializeToUtf8Bytes(
                request,
                MauiTestingJsonContext.Default.MauiTestAgentPatchRequest);
            if (requestBytes.Length > _options.MaxPatchRequestBytes)
            {
                return new MauiTestAgentPatchResult
                {
                    Error = Error(
                        MauiTestAgentErrorCodes.ValueLimitExceeded,
                        MauiTestAgentErrorCategories.Validation,
                        "The inert patch request exceeds the bounded restricted-channel size.",
                        retryable: false),
                };
            }

            var requestDigest = Convert.ToHexString(SHA256.HashData(requestBytes)).ToLowerInvariant();
            if (_idempotency.TryGetValue(envelope.IdempotencyKey!, out var prior))
            {
                if (FixedEquals(prior.RequestDigest, requestDigest) &&
                    prior.PatchProposalId is { } priorProposalId &&
                    session!.Patches.TryGetValue(priorProposalId, out var priorPatch))
                {
                    return new MauiTestAgentPatchResult
                    {
                        Ok = true,
                        Record = ClonePatchRecord(priorPatch),
                    };
                }

                return new MauiTestAgentPatchResult
                {
                    Error = Error(
                        MauiTestAgentErrorCodes.IdempotencyReused,
                        MauiTestAgentErrorCategories.Conflict,
                        "The patch idempotency key was already used for a different restricted request.",
                        retryable: false),
                };
            }

            if (operation is "apply" or "approve" or "rollback")
            {
                AppendAuditLocked(session!, "patch-denied", envelope, "denied", null, null, null, MauiTestAgentErrorCodes.PatchApplyForbidden);
                return new MauiTestAgentPatchResult
                {
                    Error = Error(
                        MauiTestAgentErrorCodes.PatchApplyForbidden,
                        MauiTestAgentErrorCategories.Authorization,
                        "Restricted test agents can only propose, preview, or reject inert patches. They cannot approve or apply a patch.",
                        retryable: false),
                };
            }

            switch (operation)
            {
                case "proposal":
                    if (request.Proposal is null ||
                        !TryCreateSafePatchProposal(session!, request.Proposal, out var proposal, out error))
                    {
                        return new MauiTestAgentPatchResult
                        {
                            Error = error ?? Error(
                                MauiTestAgentErrorCodes.InvalidRequest,
                                MauiTestAgentErrorCategories.Validation,
                                "A strict selector-only inert patch proposal is required.",
                                retryable: false),
                        };
                    }

                    if (session!.Patches.TryGetValue(proposal!.ProposalId!, out var duplicate))
                    {
                        _idempotency.Add(
                            envelope.IdempotencyKey!,
                            new IdempotencyRecord(
                                requestDigest,
                                session.SessionId,
                                _clock.GetUtcNow(),
                                patchProposalId: duplicate.ProposalId));
                        return new MauiTestAgentPatchResult
                        {
                            Ok = true,
                            Record = ClonePatchRecord(duplicate),
                        };
                    }
                    if (session.Patches.Count >= _options.MaxPatchesPerSession)
                    {
                        return new MauiTestAgentPatchResult
                        {
                            Error = Error(
                                MauiTestAgentErrorCodes.InvalidRequest,
                                MauiTestAgentErrorCategories.Capability,
                                "The bounded inert patch proposal capacity was reached for this session.",
                                retryable: false),
                        };
                    }
                    var record = new MauiTestAgentPatchRecord
                    {
                        ProposalId = proposal.ProposalId,
                        State = "proposed",
                        Proposal = proposal,
                        RecordedAt = _clock.GetUtcNow(),
                    };
                    session.Patches.Add(record.ProposalId!, record);
                    _idempotency.Add(
                        envelope.IdempotencyKey!,
                        new IdempotencyRecord(
                            requestDigest,
                            session.SessionId,
                            _clock.GetUtcNow(),
                            patchProposalId: record.ProposalId));
                    AppendAuditLocked(session, "patch-proposed", envelope, "inert-proposal", null, HashProposal(proposal), null, null);
                    return new MauiTestAgentPatchResult { Ok = true, Record = ClonePatchRecord(record) };

                case "preview":
                    // A preview that names a candidate selector instead of an existing proposal is
                    // the repair counterpart of the reset offer. Admission can declare a failure
                    // repair-eligible and still leave the agent unable to act on it: a proposal is
                    // accepted only when it carries the canonical patch digest, and that digest is
                    // a pure function of the committed flow the broker holds. Recomputing it
                    // outside the broker would mean reimplementing the patch builder, so without
                    // this the channel advertises a repair no caller can actually submit.
                    // Nothing is stored and nothing is approved: this reports what the canonical
                    // patch for that selector would be, and the proposal path still rebuilds and
                    // compares it before accepting anything.
                    if (string.IsNullOrWhiteSpace(request.ProposalId) && request.Proposal is not null)
                    {
                        if (!TryDescribeCanonicalPatch(session!, request.Proposal, out var offered, out error))
                            return new MauiTestAgentPatchResult { Error = error };

                        return new MauiTestAgentPatchResult { Ok = true, Record = offered };
                    }

                    if (string.IsNullOrWhiteSpace(request.ProposalId))
                    {
                        return new MauiTestAgentPatchResult
                        {
                            Error = Error(
                                MauiTestAgentErrorCodes.InvalidRequest,
                                MauiTestAgentErrorCategories.Validation,
                                "A patch preview requires either proposalId, or proposal.sourceStepId with " +
                                "proposal.proposedSelector to be told the canonical patch digest to propose.",
                                retryable: false),
                        };
                    }
                    if (!session!.Patches.TryGetValue(request.ProposalId, out var existing))
                    {
                        return new MauiTestAgentPatchResult
                        {
                            Error = Error(
                                MauiTestAgentErrorCodes.InvalidRequest,
                                MauiTestAgentErrorCategories.State,
                                "The inert patch proposal was not found.",
                                retryable: false),
                        };
                    }
                    if (existing.State == MauiFlowRepairOutcomeStates.Proposed)
                        existing.State = MauiFlowRepairOutcomeStates.Previewed;
                    _idempotency.Add(
                        envelope.IdempotencyKey!,
                        new IdempotencyRecord(
                            requestDigest,
                            session.SessionId,
                            _clock.GetUtcNow(),
                            patchProposalId: existing.ProposalId));
                    return new MauiTestAgentPatchResult { Ok = true, Record = ClonePatchRecord(existing) };

                case "reject":
                    if (string.IsNullOrWhiteSpace(request.ProposalId) ||
                        !session!.Patches.TryGetValue(request.ProposalId, out var rejected))
                    {
                        return new MauiTestAgentPatchResult
                        {
                            Error = Error(
                                MauiTestAgentErrorCodes.InvalidRequest,
                                MauiTestAgentErrorCategories.State,
                                "The inert patch proposal was not found.",
                                retryable: false),
                        };
                    }
                    if (rejected.State is not MauiFlowRepairOutcomeStates.Proposed and
                        not MauiFlowRepairOutcomeStates.Previewed)
                    {
                        return new MauiTestAgentPatchResult
                        {
                            Error = Error(
                                MauiTestAgentErrorCodes.InvalidRequest,
                                MauiTestAgentErrorCategories.State,
                                "Only a proposed or previewed inert patch can be rejected.",
                                retryable: false),
                        };
                    }
                    rejected.State = MauiFlowRepairOutcomeStates.Rejected;
                    rejected.ReasonDigest = Hash(Bounded(request.Reason, 1024) ?? string.Empty);
                    _idempotency.Add(
                        envelope.IdempotencyKey!,
                        new IdempotencyRecord(
                            requestDigest,
                            session.SessionId,
                            _clock.GetUtcNow(),
                            patchProposalId: rejected.ProposalId));
                    AppendAuditLocked(session, "patch-rejected", envelope, "rejected", null, null, rejected.ReasonDigest, null);
                    return new MauiTestAgentPatchResult { Ok = true, Record = ClonePatchRecord(rejected) };

                default:
                    return new MauiTestAgentPatchResult
                    {
                        Error = Error(
                            MauiTestAgentErrorCodes.UnsupportedOperation,
                            MauiTestAgentErrorCategories.Unsupported,
                            "Supported patch operations are proposal, preview, and reject.",
                            retryable: false),
                    };
            }
        }
    }

    internal MauiTestAgentAuditResult Audit(MauiTestAgentSessionAccessRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            PurgeExpiredLocked();
            if (!TryGetReadableSessionLocked(
                    request.SessionId,
                    request.ReadCapabilityId,
                    request.Envelope,
                    out var session,
                    out var error))
                return new MauiTestAgentAuditResult { Error = error };

            var entries = _journal
                .Where(entry => string.Equals(entry.SessionId, session!.SessionId, StringComparison.Ordinal))
                .TakeLast(_options.MaxAuditEntries)
                .Select(CloneAudit)
                .ToList();
            return new MauiTestAgentAuditResult
            {
                Ok = true,
                Entries = entries,
                Truncated = _journal.Count(entry => string.Equals(entry.SessionId, session!.SessionId, StringComparison.Ordinal)) > entries.Count,
            };
        }
    }

    internal MauiTestAgentRunBindingResult BindRun(MauiTestAgentRunBindingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            PurgeExpiredLocked();
            if (!TryGetReadableSessionLocked(
                    request.SessionId,
                    request.ReadCapabilityId,
                    request.Envelope,
                    out var session,
                    out var error))
                return RunBindingFailure(error!);
            if (string.IsNullOrWhiteSpace(request.RunId) || string.IsNullOrWhiteSpace(request.RunCapabilityToken))
            {
                return RunBindingFailure(Error(
                    MauiTestAgentErrorCodes.InvalidRequest,
                    MauiTestAgentErrorCategories.Validation,
                    "runId and runCapabilityToken are required.",
                    retryable: false));
            }

            session!.Runs[request.RunId] = Hash(request.RunCapabilityToken);
            AppendAuditLocked(session, "run-bound", null, "bound", null, Hash(request.RunId), null, null, request.RunId);
            return new MauiTestAgentRunBindingResult { Ok = true, RunId = request.RunId };
        }
    }

    internal MauiTestAgentRunBindingResult GetRunBinding(MauiTestAgentRunBindingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            PurgeExpiredLocked();
            if (!TryGetReadableSessionLocked(
                    request.SessionId,
                    request.ReadCapabilityId,
                    request.Envelope,
                    out var session,
                    out var error))
                return RunBindingFailure(error!);
            if (string.IsNullOrWhiteSpace(request.RunId) || !session!.Runs.TryGetValue(request.RunId, out var capability))
            {
                return RunBindingFailure(Error(
                    MauiTestAgentErrorCodes.InvalidRequest,
                    MauiTestAgentErrorCategories.State,
                    "The requested run is not bound to this authoring session.",
                    retryable: false));
            }

            // The service stores only a hash and therefore cannot reveal a token after it has been
            // bound. The MCP process retains it for its own lifetime; this route remains a safe
            // existence check rather than a credential recovery mechanism.
            return new MauiTestAgentRunBindingResult { Ok = true, RunId = request.RunId };
        }
    }

    internal MauiTestAgentRunBindingResult ValidateRunBinding(MauiTestAgentRunBindingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            PurgeExpiredLocked();
            if (!TryGetReadableSessionLocked(
                    request.SessionId,
                    request.ReadCapabilityId,
                    request.Envelope,
                    out var session,
                    out var error))
                return RunBindingFailure(error!);
            if (string.IsNullOrWhiteSpace(request.RunId) ||
                string.IsNullOrWhiteSpace(request.RunCapabilityToken) ||
                !session!.Runs.TryGetValue(request.RunId, out var capability) ||
                !FixedEquals(capability, Hash(request.RunCapabilityToken)))
            {
                return RunBindingFailure(Error(
                    MauiTestAgentErrorCodes.ReadCapabilityRequired,
                    MauiTestAgentErrorCategories.Authorization,
                    "The supplied run capability is not bound to this authoring session and run.",
                    retryable: false));
            }

            return new MauiTestAgentRunBindingResult { Ok = true, RunId = request.RunId };
        }
    }

    internal MauiTestAgentSessionResult GetSnapshotForRead(string? sessionId, string? readCapabilityId)
    {
        lock (_gate)
        {
            PurgeExpiredLocked();
            return !TryGetReadableSessionCoreLocked(sessionId, readCapabilityId, out var session, out var error)
                ? SessionFailure(error!)
                : new MauiTestAgentSessionResult
                {
                    Ok = true,
                    Snapshot = CreateSnapshot(session!, includeReadCapability: false),
                };
        }
    }

    private static MauiTestAgentSessionResult SessionFailure(MauiTestAgentError error)
        => new() { Error = error };

    private static MauiTestAgentGrantIssueResult GrantFailure(MauiTestAgentError error)
        => new() { Error = error };

    private static MauiTestAgentApprovalResult ApprovalFailure(MauiTestAgentError error)
        => new() { Error = error };

    private static MauiTestAgentMutationAuthorizationResult AuthorizationFailure(MauiTestAgentError error)
        => new() { Error = error, DispatchAllowed = false };

    private static MauiTestAgentExplorationResult ExplorationFailure(
        MauiTestAgentError error,
        MauiTestAgentExplorationBudgetState? budget = null)
        => new() { Error = error, DispatchAllowed = false, ExplorationBudget = budget };

    private static MauiTestAgentRunBindingResult RunBindingFailure(MauiTestAgentError error)
        => new() { Error = error };

    private static MauiTestAgentToolResult ToolFailure(MauiTestAgentError error)
        => new() { Error = error };

    private MauiTestAgentError? ValidateEnvelope(MauiTestAgentRequestEnvelope? envelope, bool requireSession)
    {
        if (envelope is null ||
            string.IsNullOrWhiteSpace(envelope.RequestId) ||
            string.IsNullOrWhiteSpace(envelope.IdempotencyKey) ||
            envelope.Target is null ||
            string.IsNullOrWhiteSpace(envelope.Target.AgentId) ||
            string.IsNullOrWhiteSpace(envelope.Target.AgentInstanceId) ||
            envelope.Provenance is null ||
            string.IsNullOrWhiteSpace(envelope.Provenance.ActorKind) ||
            string.IsNullOrWhiteSpace(envelope.Provenance.ActorId) ||
            string.IsNullOrWhiteSpace(envelope.Provenance.Channel) ||
            string.IsNullOrWhiteSpace(envelope.Provenance.Provider) ||
            string.IsNullOrWhiteSpace(envelope.Intent) ||
            string.IsNullOrWhiteSpace(envelope.PolicyVersion))
        {
            return Error(
                MauiTestAgentErrorCodes.InvalidRequest,
                MauiTestAgentErrorCategories.Validation,
                "The request envelope requires request/idempotency IDs, an explicit target, provenance, intent, and policy version.",
                retryable: false);
        }

        if (requireSession &&
            (envelope.Correlation is null || string.IsNullOrWhiteSpace(envelope.Correlation.AuthoringSessionId)))
        {
            return Error(
                MauiTestAgentErrorCodes.SessionNotFound,
                MauiTestAgentErrorCategories.Validation,
                "An authoringSessionId is required for this request.",
                retryable: false);
        }

        if (envelope.RequestId.Length > 256 ||
            envelope.IdempotencyKey.Length > 256 ||
            envelope.Intent.Length > 1024 ||
            envelope.Provenance.ActorId!.Length > 256 ||
            envelope.Provenance.Channel!.Length > 128 ||
            envelope.Provenance.Provider!.Length > 256)
        {
            return Error(
                MauiTestAgentErrorCodes.InvalidRequest,
                MauiTestAgentErrorCategories.Validation,
                "A request-envelope field exceeds its bounded protocol length.",
                retryable: false);
        }

        return null;
    }

    private bool TryGetReadableSessionLocked(
        string? sessionId,
        string? readCapabilityId,
        MauiTestAgentRequestEnvelope? envelope,
        out SessionRecord? session,
        out MauiTestAgentError? error)
    {
        if (!TryGetReadableSessionCoreLocked(sessionId, readCapabilityId, out session, out error))
            return false;

        var envelopeError = ValidateEnvelope(envelope, requireSession: true);
        if (envelopeError is not null)
        {
            session = null;
            error = envelopeError;
            return false;
        }

        if (!string.Equals(
                envelope!.Correlation!.AuthoringSessionId,
                session!.SessionId,
                StringComparison.Ordinal) ||
            !CanonicalTargetsMatch(session.Target, envelope.Target) ||
            !ProvenanceMatches(session.Actor, envelope.Provenance) ||
            !string.Equals(
                envelope.PolicyVersion,
                MauiTestAgentProtocolVersions.PolicyVersion,
                StringComparison.Ordinal))
        {
            session = null;
            error = Error(
                MauiTestAgentErrorCodes.TargetStale,
                MauiTestAgentErrorCategories.Authorization,
                "The read capability is valid only for its canonical authoring target, actor, and policy.",
                retryable: false);
            return false;
        }

        return true;
    }

    private bool TryGetReadableSessionCoreLocked(
        string? sessionId,
        string? readCapabilityId,
        out SessionRecord? session,
        out MauiTestAgentError? error)
    {
        session = null;
        error = null;
        if (string.IsNullOrWhiteSpace(sessionId) || !_sessions.TryGetValue(sessionId, out var found))
        {
            error = Error(
                MauiTestAgentErrorCodes.SessionNotFound,
                MauiTestAgentErrorCategories.State,
                "The authoring session was not found.",
                retryable: false);
            return false;
        }

        if (!TryValidateActiveSessionLocked(found, out error))
            return false;

        if (string.IsNullOrWhiteSpace(readCapabilityId))
        {
            // Naming the field and where it lives is the whole point. "A read capability is
            // required" sends an agent hunting for a field name and a place to put it, and the
            // observed cost of that hunt was several wasted calls per session before anything
            // could be validated.
            error = Error(
                MauiTestAgentErrorCodes.ReadCapabilityRequired,
                MauiTestAgentErrorCategories.Authorization,
                "The envelope must name a readCapabilityId. It is an envelope field alongside " +
                "correlation, not a tool argument, and 'maui_test_author begin' returns it for " +
                "this authoring session.",
                retryable: false);
            return false;
        }

        if (!FixedEquals(found.ReadCapabilityDigest, Hash(readCapabilityId)))
        {
            error = Error(
                MauiTestAgentErrorCodes.ReadCapabilityRequired,
                MauiTestAgentErrorCategories.Authorization,
                "The envelope's readCapabilityId does not belong to this authoring session. Use " +
                "the value 'maui_test_author begin' returned for this session rather than one " +
                "from an earlier session.",
                retryable: false);
            return false;
        }

        session = found;
        return true;
    }

    private bool TryGetAuthorizedSessionLocked(
        string? sessionId,
        string? authorizationId,
        IReadOnlySet<string> allowedActions,
        out SessionRecord? session,
        out MauiTestAgentError? error)
    {
        session = null;
        error = null;
        if (string.IsNullOrWhiteSpace(sessionId) || !_sessions.TryGetValue(sessionId, out var found))
        {
            error = Error(
                MauiTestAgentErrorCodes.SessionNotFound,
                MauiTestAgentErrorCategories.State,
                "The authoring session was not found.",
                retryable: false);
            return false;
        }

        if (!TryValidateActiveSessionLocked(found, out error))
            return false;

        if (string.IsNullOrWhiteSpace(authorizationId) ||
            !_authorizations.TryGetValue(authorizationId, out var authorization) ||
            !string.Equals(authorization.SessionId, found.SessionId, StringComparison.Ordinal) ||
            !allowedActions.Contains(authorization.Action) ||
            authorization.Consumed ||
            authorization.Completed)
        {
            error = Error(
                MauiTestAgentErrorCodes.MutationGrantRequired,
                MauiTestAgentErrorCategories.Authorization,
                "The requested authoring mutation was not authorized by a valid human-issued grant.",
                retryable: false);
            return false;
        }

        session = found;
        return true;
    }

    private MauiTestAgentError? ValidateBoundSessionEnvelope(
        SessionRecord session,
        MauiTestAgentRequestEnvelope? envelope)
    {
        var error = ValidateEnvelope(envelope, requireSession: true);
        if (error is not null)
            return error;
        if (!CanonicalTargetsMatch(session.Target, envelope!.Target) ||
            !CorrelationMatches(session, envelope.Correlation, requireAll: true) &&
            !CorrelationMatchesGrantSequence(session, envelope.Correlation) ||
            !ProvenanceMatches(session.Actor, envelope.Provenance) ||
            !string.Equals(
                envelope.PolicyVersion,
                MauiTestAgentProtocolVersions.PolicyVersion,
                StringComparison.Ordinal))
        {
            return Error(
                MauiTestAgentErrorCodes.MutationGrantStale,
                MauiTestAgentErrorCategories.Authorization,
                "The authorized mutation no longer matches the canonical target, actor, plan, or flow.",
                retryable: false);
        }
        return null;
    }

    private void ConsumeAuthorizationLocked(string? authorizationId)
    {
        if (string.IsNullOrWhiteSpace(authorizationId) ||
            !_authorizations.TryGetValue(authorizationId, out var authorization) ||
            authorization.Consumed)
        {
            throw new InvalidOperationException("The mutation authorization was not available for atomic consumption.");
        }

        authorization.Consumed = true;
    }

    private bool TryGetPendingApprovalLocked(
        string? approvalRequestId,
        out ApprovalRequestRecord? request,
        out SessionRecord? session,
        out MauiTestAgentError? error)
    {
        request = null;
        session = null;
        error = null;
        if (string.IsNullOrWhiteSpace(approvalRequestId) ||
            !_approvalRequests.TryGetValue(approvalRequestId, out var found))
        {
            error = Error(
                MauiTestAgentErrorCodes.ApprovalRequestNotFound,
                MauiTestAgentErrorCategories.State,
                "The approval request was not found.",
                retryable: false);
            return false;
        }

        if (found.State == MauiTestAgentApprovalStates.Expired)
        {
            error = Error(
                MauiTestAgentErrorCodes.ApprovalRequestExpired,
                MauiTestAgentErrorCategories.State,
                "The approval request expired before a human decision.",
                retryable: false);
            return false;
        }
        if (found.State != MauiTestAgentApprovalStates.Pending)
        {
            error = Error(
                MauiTestAgentErrorCodes.ApprovalRequestDecided,
                MauiTestAgentErrorCategories.Conflict,
                "The approval request already has a terminal decision.",
                retryable: false);
            return false;
        }

        if (!_sessions.TryGetValue(found.SessionId, out var owner) ||
            !TryValidateActiveSessionLocked(owner, out error))
        {
            MarkApprovalLocked(found, MauiTestAgentApprovalStates.Stale, "authoring-session-unavailable");
            return false;
        }

        request = found;
        session = owner;
        return true;
    }

    private bool MatchesActionAuthorization(
        string? authorizationId,
        string? action,
        string actionDigest,
        out MauiTestAgentError? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(authorizationId) ||
            !_authorizations.TryGetValue(authorizationId, out var authorization) ||
            !string.Equals(authorization.Action, action, StringComparison.Ordinal) ||
            !FixedEquals(authorization.ActionDigest, actionDigest))
        {
            error = Error(
                MauiTestAgentErrorCodes.MutationGrantScopeDenied,
                MauiTestAgentErrorCategories.Authorization,
                "The draft mutation does not match the action, selector, route, or bounded value authorized by the human grant.",
                retryable: false);
            return false;
        }
        return true;
    }

    private void AdvanceGrantCorrelationLocked(string? authorizationId, SessionRecord session)
    {
        if (string.IsNullOrWhiteSpace(authorizationId) ||
            !_authorizations.TryGetValue(authorizationId, out var authorization) ||
            !_grants.TryGetValue(authorization.GrantDigest, out var grant))
        {
            return;
        }

        grant.Correlation = SnapshotCorrelation(session);
    }

    private void InvalidatePendingApprovalsLocked(SessionRecord session, string reasonCode)
    {
        foreach (var request in _approvalRequests.Values.Where(request =>
                     string.Equals(request.SessionId, session.SessionId, StringComparison.Ordinal) &&
                     request.State == MauiTestAgentApprovalStates.Pending &&
                     !CorrelationMatches(session, request.Correlation, requireAll: true)))
        {
            MarkApprovalLocked(request, MauiTestAgentApprovalStates.Stale, reasonCode);
            AppendAuditLocked(
                session,
                "approval-stale",
                ApprovalEnvelope(request),
                "stale",
                null,
                null,
                null,
                MauiTestAgentErrorCodes.MutationGrantStale);
        }
    }

    private bool TryValidateActiveSessionLocked(SessionRecord session, out MauiTestAgentError? error)
    {
        error = null;
        if (session.State == SessionState.Abandoned)
        {
            error = Error(
                MauiTestAgentErrorCodes.SessionAbandoned,
                MauiTestAgentErrorCategories.State,
                "The authoring session was abandoned.",
                retryable: false);
            return false;
        }
        if (session.State == SessionState.Expired || session.ExpiresAt <= _clock.GetUtcNow())
        {
            session.State = SessionState.Expired;
            error = Error(
                MauiTestAgentErrorCodes.SessionExpired,
                MauiTestAgentErrorCategories.State,
                "The authoring session has expired.",
                retryable: false);
            return false;
        }
        return true;
    }

    private MauiTestAgentError? ValidateScope(MauiTestAgentMutationScope? scope)
    {
        if (scope is null || scope.AllowedActions.Count == 0 || scope.MaxActionCount is not > 0)
        {
            return Error(
                MauiTestAgentErrorCodes.InvalidRequest,
                MauiTestAgentErrorCategories.Validation,
                "A mutation grant must have explicitly allowed actions and a bounded action count.",
                retryable: false);
        }
        if (scope.MaxActionCount > _options.MaxActionsPerGrant ||
            scope.MaxValueBytes is < 0 or > 65_536 ||
            scope.AllowedActions.Count > 32 ||
            scope.AllowedSelectors.Count > 64 ||
            scope.AllowedRoutes.Count > 64 ||
            scope.AllowedSideEffectClasses.Count > 16)
        {
            return Error(
                MauiTestAgentErrorCodes.InvalidRequest,
                MauiTestAgentErrorCategories.Validation,
                "The requested grant scope exceeds the bounded test-agent policy.",
                retryable: false);
        }
        if (scope.AllowedActions.Any(action => !MauiTestAgentActions.All.Contains(action)) ||
            scope.AllowedActions.Any(action => action == "*") ||
            scope.AllowedSelectors.Any(selector => string.IsNullOrWhiteSpace(selector) || selector.Length > 512 || selector == "*") ||
            scope.AllowedRoutes.Any(route => string.IsNullOrWhiteSpace(route) || route.Length > 512 || route == "*") ||
            scope.AllowedSideEffectClasses.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 128 || value == "*"))
        {
            return Error(
                MauiTestAgentErrorCodes.InvalidRequest,
                MauiTestAgentErrorCategories.Validation,
                "Grant scopes must use explicit bounded actions, selectors, routes, and side-effect classes.",
                retryable: false);
        }
        if (scope.AllowedActions.Contains(MauiTestAgentActions.AuthorCommit, StringComparer.Ordinal) &&
            (scope.AllowedActions.Count != 1 || scope.MaxActionCount != 1))
        {
            return Error(
                MauiTestAgentErrorCodes.InvalidRequest,
                MauiTestAgentErrorCategories.Validation,
                "An author-commit grant must be single-purpose and single-use because commit advances the plan and flow revisions.",
                retryable: false);
        }
        // Both approval paths run NormalizeApprovalScope first, so a run scope arrives here with
        // MaxActionCount already forced to 1 and this check rejects a bundled scope. The count is
        // still asserted rather than assumed: it is the invariant the grant is minted against, and
        // ValidateScope is the last gate before IssueGrantLocked.
        if (scope.AllowedActions.Contains(MauiTestAgentActions.Run, StringComparer.Ordinal) &&
            (scope.AllowedActions.Count != 1 || scope.MaxActionCount != 1))
        {
            return Error(
                MauiTestAgentErrorCodes.InvalidRequest,
                MauiTestAgentErrorCategories.Validation,
                "A run grant must be single-purpose and single-use because one human run approval authorizes exactly one dispatch of the approved flow against the live app.",
                retryable: false);
        }
        return null;
    }

    private static bool ApprovalKindAllowsScope(string kind, MauiTestAgentMutationScope scope)
        => kind switch
        {
            MauiTestAgentApprovalKinds.Exploration =>
                scope.AllowedActions.All(static action =>
                    action is not MauiTestAgentActions.Run and
                        not MauiTestAgentActions.Cancel and
                        not MauiTestAgentActions.AuthorCommit),
            MauiTestAgentApprovalKinds.DraftChange =>
                scope.AllowedActions.All(static action =>
                    action is not MauiTestAgentActions.Run and
                        not MauiTestAgentActions.Cancel and
                        not MauiTestAgentActions.AuthorCommit),
            MauiTestAgentApprovalKinds.Assertion =>
                scope.AllowedActions.All(static action => action == MauiTestAgentActions.Assert),
            MauiTestAgentApprovalKinds.Commit =>
                scope.AllowedActions.All(static action => action == MauiTestAgentActions.AuthorCommit),
            MauiTestAgentApprovalKinds.Run =>
                scope.AllowedActions.All(static action =>
                    action is MauiTestAgentActions.Run or MauiTestAgentActions.Cancel),
            _ => false,
        };

    private static string? NormalizeApprovalKind(string? kind)
        => kind?.Trim().ToLowerInvariant() switch
        {
            MauiTestAgentApprovalKinds.Exploration or "exploration-request" => MauiTestAgentApprovalKinds.Exploration,
            MauiTestAgentApprovalKinds.DraftChange or "draft" or "draft-action" or "draft-actions" or
                "authoring" or "authoring-action" or "authoring-actions" => MauiTestAgentApprovalKinds.DraftChange,
            MauiTestAgentApprovalKinds.Assertion or "assertions" or "assertion-add" => MauiTestAgentApprovalKinds.Assertion,
            MauiTestAgentApprovalKinds.Commit or "authoring-commit" or "draft-commit" or "author-commit" =>
                MauiTestAgentApprovalKinds.Commit,
            MauiTestAgentApprovalKinds.Run or "test-run" or "execution" => MauiTestAgentApprovalKinds.Run,
            _ => null,
        };

    private static void NormalizeApprovalScope(string kind, MauiTestAgentMutationScope? scope)
    {
        if (scope is null)
            return;

        for (var index = 0; index < scope.AllowedActions.Count; index++)
        {
            scope.AllowedActions[index] = (kind, scope.AllowedActions[index].Trim().ToLowerInvariant()) switch
            {
                (MauiTestAgentApprovalKinds.Commit, "commit") => MauiTestAgentActions.AuthorCommit,
                (MauiTestAgentApprovalKinds.Run, "start") or
                (MauiTestAgentApprovalKinds.Run, "start-run") or
                (MauiTestAgentApprovalKinds.Run, "test-run") => MauiTestAgentActions.Run,
                (MauiTestAgentApprovalKinds.Run, "stop") or
                (MauiTestAgentApprovalKinds.Run, "cancel-run") => MauiTestAgentActions.Cancel,
                (_, var action) => action,
            };
        }
        scope.AllowedActions = scope.AllowedActions
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (kind == MauiTestAgentApprovalKinds.Run)
        {
            // The restricted run tool always authorizes the fixed "run" side-effect class.
            // Normalize model/host vocabulary rather than forcing trial-and-error approvals.
            scope.AllowedSideEffectClasses = ["run"];

            // One human run decision authorizes exactly one dispatch. A run replays the whole
            // approved flow against the live app, so a multi-use run grant would let the agent
            // re-drive the app without a second human decision.
            scope.MaxActionCount = 1;
        }
        else if (kind is MauiTestAgentApprovalKinds.DraftChange or
                 MauiTestAgentApprovalKinds.Assertion or
                 MauiTestAgentApprovalKinds.Commit)
        {
            // Draft edits and commit mutate only broker-owned authoring state. Plan side-effect
            // vocabulary such as "non-replayable" belongs to run admission, not approval scope.
            scope.AllowedSideEffectClasses = ["authoring"];
        }

        // The exploration kind is deliberately left alone. Its side-effect class list is what the
        // agent asked for and what the human read before approving, and rewriting it here would
        // hand back a grant that no longer matches the text that was approved. A grant becomes an
        // exploration grant by listing the "exploration" class explicitly; the two families are
        // kept disjoint at redemption instead, where the exploration route requires that class and
        // the ordinary mutation route refuses any grant that carries it.
    }

    private static bool ScopeIsSubset(
        MauiTestAgentMutationScope candidate,
        MauiTestAgentMutationScope requested)
        => candidate.AllowedActions.All(requested.AllowedActions.Contains) &&
           candidate.AllowedSelectors.All(requested.AllowedSelectors.Contains) &&
           candidate.AllowedRoutes.All(requested.AllowedRoutes.Contains) &&
           candidate.AllowedSideEffectClasses.All(requested.AllowedSideEffectClasses.Contains) &&
           candidate.MaxActionCount is > 0 &&
           requested.MaxActionCount is > 0 &&
           candidate.MaxActionCount <= requested.MaxActionCount &&
           (candidate.MaxValueBytes is null ||
            (requested.MaxValueBytes is null
                ? candidate.MaxValueBytes == 0
                : candidate.MaxValueBytes <= requested.MaxValueBytes));

    private static MauiTestAgentError? ValidateMutationScope(
        MauiTestAgentMutationScope scope,
        MauiTestAgentMutationAuthorizationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Action) || !scope.AllowedActions.Contains(request.Action, StringComparer.Ordinal))
        {
            return Error(
                MauiTestAgentErrorCodes.MutationGrantScopeDenied,
                MauiTestAgentErrorCategories.Authorization,
                "The requested semantic action is not in the human-approved grant scope.",
                retryable: false);
        }

        if (request.Selector is not null)
        {
            var selectorKey = SelectorScopeKey(request.Selector);
            if (selectorKey is null || !scope.AllowedSelectors.Contains(selectorKey, StringComparer.Ordinal))
            {
                return Error(
                    MauiTestAgentErrorCodes.MutationGrantScopeDenied,
                    MauiTestAgentErrorCategories.Authorization,
                    "The requested selector is not an explicit durable selector in the human-approved grant scope.",
                    retryable: false);
            }
        }

        if (!string.IsNullOrWhiteSpace(request.Route) &&
            !scope.AllowedRoutes.Contains(request.Route, StringComparer.Ordinal))
        {
            return Error(
                MauiTestAgentErrorCodes.MutationGrantScopeDenied,
                MauiTestAgentErrorCategories.Authorization,
                "The requested route is not in the human-approved grant scope.",
                retryable: false);
        }

        if (!string.IsNullOrWhiteSpace(request.SideEffectClass) &&
            !scope.AllowedSideEffectClasses.Contains(request.SideEffectClass, StringComparer.Ordinal))
        {
            return Error(
                MauiTestAgentErrorCodes.MutationGrantScopeDenied,
                MauiTestAgentErrorCategories.Authorization,
                "The requested side-effect class is not in the human-approved grant scope.",
                retryable: false);
        }

        if (request.ValueLength is { } valueLength &&
            (valueLength < 0 || scope.MaxValueBytes is null || valueLength > scope.MaxValueBytes))
        {
            return Error(
                MauiTestAgentErrorCodes.ValueLimitExceeded,
                MauiTestAgentErrorCategories.Authorization,
                "The requested value exceeds the human-approved grant value limit.",
                retryable: false);
        }
        return null;
    }

    private static bool IsHumanHostApproval(MauiTestAgentHumanApproval? approval)
        => approval?.Approved == true &&
           approval.Actor is { ActorKind: "human" or "host", ActorId: { Length: > 0 }, Channel: { Length: > 0 }, Provider: { Length: > 0 } } &&
           approval.ApprovalChannel is "workbench" or "host";

    private static bool IsHumanHostDecision(MauiTestAgentHumanApproval? decision)
        => decision?.Actor is { ActorKind: "human" or "host", ActorId: { Length: > 0 }, Channel: { Length: > 0 }, Provider: { Length: > 0 } } &&
           decision.ApprovalChannel is "workbench" or "host";

    /// <summary>
    /// Bounded, non-secret provenance label naming the surface that claims to have issued a human
    /// decision, such as <c>workbench/inspector-server</c> or <c>cli/maui-cli</c>.
    ///
    /// <para>
    /// The label is <b>self-asserted</b> by the deciding caller, so it is a way to read intent out
    /// of an audit trail, not evidence of which surface decided: any holder of the broker's native
    /// host approval token can choose any label. It never participates in any authorization check.
    /// The actor identity is deliberately excluded so reading the record cannot reveal a local user.
    /// </para>
    /// </summary>
    private static string? DescribeDecisionIssuer(MauiTestAgentHumanApproval? decision)
    {
        if (decision?.Actor is not { Channel: { Length: > 0 } channel, Provider: { Length: > 0 } provider })
            return null;
        return Bounded($"{channel}/{provider}", 128);
    }

    private static bool GrantMatchesSession(GrantRecord grant, SessionRecord session)
        => string.Equals(grant.SessionId, session.SessionId, StringComparison.Ordinal) &&
           CanonicalTargetsMatch(grant.Target, session.Target) &&
           TargetStatesMatch(grant.TargetState, session.TargetState) &&
           ProvenanceMatches(grant.Actor, session.Actor) &&
           CorrelationMatches(session, grant.Correlation, requireAll: true) &&
           string.Equals(grant.PolicyVersion, MauiTestAgentProtocolVersions.PolicyVersion, StringComparison.Ordinal);

    private static bool CorrelationMatches(
        SessionRecord session,
        MauiTestAgentCorrelation? correlation,
        bool requireAll)
    {
        if (correlation is null)
            return !requireAll;
        return Matches(correlation.AuthoringSessionId, session.SessionId, requireAll) &&
               Matches(correlation.PlanId, session.PlanId, requireAll) &&
               Matches(correlation.PlanRevision, session.PlanRevision, requireAll) &&
               Matches(correlation.PlanDigest, session.PlanDigest, requireAll) &&
               Matches(correlation.FlowId, session.FlowId, requireAll) &&
               Matches(correlation.FlowRevision, session.FlowRevision, requireAll) &&
               Matches(correlation.FlowDigest, session.FlowDigest, requireAll);
    }

    private static bool CorrelationMatchesGrantSequence(
        SessionRecord session,
        MauiTestAgentCorrelation? correlation)
        => correlation is not null &&
           Matches(correlation.AuthoringSessionId, session.SessionId, required: true) &&
           Matches(correlation.PlanId, session.PlanId, required: true) &&
           Matches(correlation.PlanRevision, session.PlanRevision, required: true) &&
           Matches(correlation.PlanDigest, session.PlanDigest, required: true) &&
           Matches(correlation.FlowId, session.FlowId, required: true) &&
           Matches(correlation.FlowRevision, session.FlowRevision, required: true);

    private static bool Matches(string? supplied, string? expected, bool required)
        => required
            ? !string.IsNullOrWhiteSpace(supplied) && string.Equals(supplied, expected, StringComparison.Ordinal)
            : string.IsNullOrWhiteSpace(supplied) || string.Equals(supplied, expected, StringComparison.Ordinal);

    private static bool Matches(int? supplied, int expected, bool required)
        => required ? supplied == expected : supplied is null || supplied == expected;

    private static bool TargetsMatch(MauiTestAgentTarget? expected, MauiTestAgentTarget? actual)
        => expected is not null &&
           actual is not null &&
           string.Equals(expected.AgentId, actual.AgentId, StringComparison.Ordinal) &&
           string.Equals(expected.AgentInstanceId, actual.AgentInstanceId, StringComparison.Ordinal);

    private static bool CanonicalTargetsMatch(MauiTestAgentTarget? expected, MauiTestAgentTarget? actual)
        => TargetsMatch(expected, actual) &&
           string.Equals(expected!.AppBuildFingerprint, actual!.AppBuildFingerprint, StringComparison.Ordinal) &&
           string.Equals(expected.SeedFingerprint, actual.SeedFingerprint, StringComparison.Ordinal) &&
           string.Equals(expected.BackendStateFingerprint, actual.BackendStateFingerprint, StringComparison.Ordinal);

    private static bool TargetsMatch(MauiTestAgentTarget? expected, MauiTestAgentTargetState? actual)
        => expected is not null &&
           actual is not null &&
           string.Equals(expected.AgentId, actual.AgentId, StringComparison.Ordinal) &&
           string.Equals(expected.AgentInstanceId, actual.AgentInstanceId, StringComparison.Ordinal);

    private static bool CanonicalTargetMatchesState(
        MauiTestAgentTarget? expected,
        MauiTestAgentTargetState? actual)
        => TargetsMatch(expected, actual) &&
           string.Equals(expected!.AppBuildFingerprint, actual!.AppBuildFingerprint, StringComparison.Ordinal) &&
           string.Equals(expected.SeedFingerprint, actual.SeedFingerprint, StringComparison.Ordinal) &&
           string.Equals(expected.BackendStateFingerprint, actual.BackendStateFingerprint, StringComparison.Ordinal);

    private static bool TargetStatesMatch(MauiTestAgentTargetState? expected, MauiTestAgentTargetState? actual)
        => expected is not null &&
           actual is not null &&
           string.Equals(expected.AgentId, actual.AgentId, StringComparison.Ordinal) &&
           string.Equals(expected.AgentInstanceId, actual.AgentInstanceId, StringComparison.Ordinal) &&
           MatchesOptional(expected.AppBuildFingerprint, actual.AppBuildFingerprint) &&
           MatchesOptional(expected.SeedFingerprint, actual.SeedFingerprint) &&
           MatchesOptional(expected.BackendStateFingerprint, actual.BackendStateFingerprint) &&
           MatchesOptional(expected.Route, actual.Route) &&
           MatchesOptional(expected.Window, actual.Window);

    private static bool MatchesOptional(string? expected, string? actual)
        => IsUnspecifiedFingerprint(expected) || string.Equals(expected, actual, StringComparison.Ordinal);

    private static bool IsUnspecifiedFingerprint(string? value)
        => string.IsNullOrWhiteSpace(value) ||
           string.Equals(value, "unknown", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(value, "unknown:unknown", StringComparison.OrdinalIgnoreCase);

    private static bool ProvenanceMatches(MauiActorProvenance? expected, MauiActorProvenance? actual)
        => expected is not null &&
           actual is not null &&
           string.Equals(expected.ActorKind, actual.ActorKind, StringComparison.Ordinal) &&
           string.Equals(expected.ActorId, actual.ActorId, StringComparison.Ordinal) &&
           string.Equals(expected.Channel, actual.Channel, StringComparison.Ordinal) &&
           string.Equals(expected.Provider, actual.Provider, StringComparison.Ordinal);

    private static bool TryCreateFlowStep(
        MauiTestAgentActionRequest request,
        SessionRecord session,
        out FlowStep? step,
        out MauiTestAgentError? error)
    {
        step = null;
        error = null;
        if (string.IsNullOrWhiteSpace(request.Action) ||
            request.Action is not (MauiTestAgentActions.Tap or MauiTestAgentActions.Fill or MauiTestAgentActions.Scroll or MauiTestAgentActions.Navigate or MauiTestAgentActions.Back))
        {
            error = Error(
                MauiTestAgentErrorCodes.UnsupportedOperation,
                MauiTestAgentErrorCategories.Unsupported,
                "Only tap, fill, scroll, navigate, and back are typed semantic test actions.",
                retryable: false);
            return false;
        }
        if (FlowSecretReference.LooksSensitive(request.Selector?.AutomationId, request.Value, request.Route))
        {
            error = Error(
                MauiTestAgentErrorCodes.UntrustedPolicyInput,
                MauiTestAgentErrorCategories.Validation,
                "Sensitive values cannot be recorded in a restricted test-agent draft.",
                retryable: false);
            return false;
        }

        var selector = CloneSelector(request.Selector);
        step = new FlowStep
        {
            Seq = NextSequence(session.Flow),
            Action = request.Action,
            Intent = request.Envelope?.Intent,
            Target = selector,
            Value = request.Action == MauiTestAgentActions.Navigate ? request.Route : request.Value,
            Args = request.Action switch
            {
                MauiTestAgentActions.Fill => new FlowStepArgs { Selector = selector, Text = request.Value },
                MauiTestAgentActions.Scroll => new FlowStepArgs
                {
                    Selector = selector,
                    Dx = request.DeltaX,
                    Dy = request.DeltaY,
                    ItemIndex = request.ItemIndex,
                },
                MauiTestAgentActions.Navigate => new FlowStepArgs { Route = request.Route },
                _ => selector is null ? null : new FlowStepArgs { Selector = selector },
            },
            Fragile = FlowSelector.IsFragile(selector),
        };
        return true;
    }

    /// <summary>
    /// Reports the canonical selector-only patch a candidate selector would produce, without
    /// creating, approving, or applying anything.
    /// </summary>
    /// <remarks>
    /// The proposal path accepts a patch only when the caller's <c>patchDigest</c> equals the one
    /// the broker rebuilds from its own committed flow. That digest covers canonical before/after
    /// flow digests and the serialized operation, so it cannot be derived from anything the
    /// restricted protocol otherwise exposes. This is the same shape as the reset offer: state the
    /// value admission will compare against, rather than making a caller guess a value that fails
    /// closed. It stays inert — the returned record is not stored in the session, carries no
    /// proposal id, and grants nothing.
    /// </remarks>
    private static bool TryDescribeCanonicalPatch(
        SessionRecord session,
        MauiFlowRepairProposal supplied,
        out MauiTestAgentPatchRecord? record,
        out MauiTestAgentError? error)
    {
        record = null;
        error = null;

        if (string.IsNullOrWhiteSpace(supplied.SourceStepId) ||
            supplied.ProposedSelector is null ||
            !IsSafeRepairSelector(supplied.ProposedSelector))
        {
            error = Error(
                MauiTestAgentErrorCodes.InvalidRequest,
                MauiTestAgentErrorCategories.Validation,
                "A patch preview requires sourceStepId and a durable AutomationId-only proposedSelector.",
                retryable: false);
            return false;
        }

        var built = MauiFlowRepairPatchBuilder.Build(
            session.Flow,
            supplied.SourceStepId,
            supplied.ProposedSelector);
        if (!built.Ok || built.Patch is null || built.Diff is null || built.Proof is null ||
            string.IsNullOrWhiteSpace(built.PatchDigest))
        {
            error = Error(
                MauiTestAgentErrorCodes.InvalidRequest,
                MauiTestAgentErrorCategories.Validation,
                built.Error ?? "The candidate selector does not produce a selector-only patch for that step.",
                retryable: false);
            return false;
        }

        var sourceStep = FindFlowStep(session.Flow, supplied.SourceStepId);
        var oldSelector = sourceStep is null ? null : sourceStep.Args?.Selector ?? sourceStep.Target;

        record = new MauiTestAgentPatchRecord
        {
            State = "preview",
            RecordedAt = DateTimeOffset.UtcNow,
            Proposal = new MauiFlowRepairProposal
            {
                Revision = 1,
                State = "preview",
                SourceStepId = Bounded(supplied.SourceStepId, 128),
                SourceFailureCode = MauiFlowFailureClasses.LocatorNotFound,
                PreDispatch = true,
                BaseFlow = new MauiFlowReference
                {
                    Path = session.Plan.Flow?.Path,
                    FlowId = session.Plan.Flow?.FlowId,
                    Revision = session.FlowRevision,
                    Digest = session.FlowDigest,
                },
                OldSelector = CloneSelector(oldSelector),
                ProposedSelector = CloneSelector(supplied.ProposedSelector),
                Patch = built.Patch,
                PatchDigest = built.PatchDigest,
                Diff = built.Diff,
                UnchangedAssertionsProof = built.Proof,
                Trust = "broker-preview-not-proposed",
                Provenance = CloneProvenance(session.Actor),
            },
        };
        return true;
    }

    private static bool TryCreateSafePatchProposal(
        SessionRecord session,
        MauiFlowRepairProposal supplied,
        out MauiFlowRepairProposal? proposal,
        out MauiTestAgentError? error)
    {
        proposal = null;
        error = null;
        var flowPath = session.Plan.Flow?.Path;
        if (string.IsNullOrWhiteSpace(flowPath) ||
            string.IsNullOrWhiteSpace(supplied.SourceRunId) ||
            !session.Runs.ContainsKey(supplied.SourceRunId) ||
            string.IsNullOrWhiteSpace(supplied.SourceStepId) ||
            !string.Equals(
                supplied.SourceFailureCode,
                MauiFlowFailureClasses.LocatorNotFound,
                StringComparison.Ordinal) ||
            supplied.PreDispatch != true ||
            supplied.BaseFlow is null ||
            !string.Equals(supplied.BaseFlow.Path, flowPath, StringComparison.Ordinal) ||
            !FixedEquals(supplied.BaseFlow.Digest ?? string.Empty, session.FlowDigest) ||
            supplied.BaseFlow.Revision != session.FlowRevision ||
            supplied.ProposedSelector is null ||
            !IsSafeRepairSelector(supplied.ProposedSelector) ||
            supplied.OldSelector is null ||
            supplied.Patch?.SelectorOnly != true ||
            supplied.Patch.Operations.Count != 1 ||
            supplied.UnchangedAssertionsProof?.Unchanged != true ||
            supplied.UnchangedAssertionsProof.ActionsUnchanged != true ||
            supplied.UnchangedAssertionsProof.ValuesUnchanged != true ||
            supplied.UnchangedAssertionsProof.OrderUnchanged != true ||
            !ProvenanceMatches(session.Actor, supplied.Provenance))
        {
            error = Error(
                MauiTestAgentErrorCodes.InvalidRequest,
                MauiTestAgentErrorCategories.Validation,
                "The restricted patch channel accepts only a broker-run-bound, provenance-matched, single selector-only proposal for the canonical draft.",
                retryable: false);
            return false;
        }

        var sourceStep = FindFlowStep(session.Flow, supplied.SourceStepId);
        var oldSelector = sourceStep is null ? null : sourceStep.Args?.Selector ?? sourceStep.Target;
        if (oldSelector is null ||
            !string.Equals(
                SelectorScopeKey(oldSelector),
                SelectorScopeKey(supplied.OldSelector),
                StringComparison.Ordinal))
        {
            error = Error(
                MauiTestAgentErrorCodes.InvalidRequest,
                MauiTestAgentErrorCategories.Conflict,
                "The proposed patch does not replace the canonical selector for its stable source step.",
                retryable: false);
            return false;
        }

        var rebuilt = MauiFlowRepairPatchBuilder.Build(
            session.Flow,
            supplied.SourceStepId,
            supplied.ProposedSelector);
        if (!rebuilt.Ok ||
            rebuilt.Patch is null ||
            rebuilt.Diff is null ||
            rebuilt.Proof is null ||
            string.IsNullOrWhiteSpace(rebuilt.PatchDigest) ||
            !FixedEquals(rebuilt.PatchDigest, supplied.PatchDigest ?? string.Empty))
        {
            error = Error(
                MauiTestAgentErrorCodes.InvalidRequest,
                MauiTestAgentErrorCategories.Validation,
                "The selector-only patch digest or invariant proof does not match the canonical draft.",
                retryable: false);
            return false;
        }

        var proposalId = "proposal_" + Hash(string.Join(
            "\u001f",
            session.SessionId,
            supplied.SourceRunId,
            supplied.SourceStepId,
            rebuilt.PatchDigest))[..32];
        proposal = new MauiFlowRepairProposal
        {
            ProposalId = proposalId,
            Revision = 1,
            State = MauiFlowRepairOutcomeStates.Proposed,
            CreatedAt = DateTimeOffset.UtcNow,
            SourceRunId = Bounded(supplied.SourceRunId, 256),
            SourceStepId = Bounded(supplied.SourceStepId, 128),
            SourceFailureId = Bounded(supplied.SourceFailureId, 256),
            SourceFailureCode = MauiFlowFailureClasses.LocatorNotFound,
            PreDispatch = true,
            BaseFlow = new MauiFlowReference
            {
                Path = flowPath,
                FlowId = session.Plan.Flow?.FlowId,
                Revision = session.FlowRevision,
                Digest = session.FlowDigest,
            },
            OldSelector = CloneSelector(oldSelector),
            ProposedSelector = CloneSelector(supplied.ProposedSelector),
            Patch = rebuilt.Patch,
            PatchDigest = rebuilt.PatchDigest,
            Diff = rebuilt.Diff,
            UnchangedAssertionsProof = rebuilt.Proof,
            Trust = "broker-current-local-run",
            Provenance = CloneProvenance(session.Actor),
        };
        return true;
    }

    private static bool IsSafeRepairSelector(FlowSelector selector)
        => !string.IsNullOrWhiteSpace(selector.AutomationId) &&
           selector.AutomationId.Length <= 256 &&
           string.IsNullOrWhiteSpace(selector.Text) &&
           string.IsNullOrWhiteSpace(selector.Id) &&
           selector.TypeIndex is null &&
           string.IsNullOrWhiteSpace(selector.Type) &&
           selector.Index is null &&
           selector.ExtensionData is null &&
           (!selector.HasScopedStableItem ||
            FlowSelector.IsOpaqueStableItemKey(selector.StableItemKey) &&
            selector.CollectionScope!.Length <= 256);

    private static FlowStep? FindFlowStep(MauiFlow flow, string stepId)
    {
        var normalized = stepId.Trim();
        var stable = flow.Steps.Where(step =>
            !string.IsNullOrWhiteSpace(step.StepId) &&
            string.Equals(step.StepId.Trim(), normalized, StringComparison.Ordinal)).Take(2).ToArray();
        if (stable.Length == 1)
            return stable[0];
        if (stable.Length > 1)
            return null;

        var sequenceText = normalized.StartsWith("step-", StringComparison.Ordinal)
            ? normalized[5..]
            : normalized;
        return int.TryParse(
                sequenceText,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var sequence)
            ? flow.Steps.FirstOrDefault(step => step.Seq == sequence)
            : null;
    }

    private static string? SelectorScopeKey(FlowSelector selector)
        => MauiTestAgentSelectorScopeKey.FromSelector(selector);

    /// <summary>
    /// Canonical identity of one exploration step: the action, the selector's scope key, and — for
    /// a navigation — the route. It is computed identically from the authorization request and from
    /// the flow step later presented for dispatch, so the two can be compared without trusting the
    /// client to describe them the same way.
    /// <para>
    /// It returns null whenever the step has no durable identity to pin — an element action whose
    /// selector has no scope key, a navigation without a route, or a selector on an action that
    /// does not target an element. A null digest is always a refusal, never a comparison: two
    /// steps that cannot be told apart must not be allowed to spend each other's budget.
    /// </para>
    /// </summary>
    private static string? DigestExplorationStep(string? action, FlowSelector? selector, string? route)
    {
        var targetsElement =
            string.Equals(action, MauiTestAgentActions.Tap, StringComparison.Ordinal) ||
            string.Equals(action, MauiTestAgentActions.Scroll, StringComparison.Ordinal);
        var hasSelector = selector is not null && !selector.IsEmpty;

        string? scopeKey = null;
        if (targetsElement)
        {
            if (!hasSelector)
                return null;
            scopeKey = SelectorScopeKey(selector!);
            // A text-only selector, for example, has no scope key. Hashing it as "no selector"
            // would let one approved tap stand in for a tap on any other unkeyed element.
            if (string.IsNullOrEmpty(scopeKey))
                return null;
        }
        else if (hasSelector)
        {
            // navigate and back do not target an element, so the digest has nowhere to record a
            // selector. Ignoring it silently would leave it unbound.
            return null;
        }

        string? routeKey = null;
        if (string.Equals(action, MauiTestAgentActions.Navigate, StringComparison.Ordinal))
        {
            routeKey = route?.Trim();
            if (string.IsNullOrEmpty(routeKey))
                return null;
        }

        return Hash(string.Join("\n", action, scopeKey, routeKey));
    }

    private static string? DigestExplorationStep(FlowStep step)
    {
        // A step carries the selector twice. The runner drives FlowValidator.EffectiveSelector, so
        // that is what the digest must bind to; a step whose two copies disagree is only ever a
        // forgery, because the one honest producer assigns them the same instance.
        var target = step.Target;
        var argsSelector = step.Args?.Selector;
        if (target is not null && !target.IsEmpty &&
            argsSelector is not null && !argsSelector.IsEmpty &&
            !string.Equals(SelectorScopeKey(target), SelectorScopeKey(argsSelector), StringComparison.Ordinal))
        {
            return null;
        }

        return DigestExplorationStep(
            step.Action,
            FlowValidator.EffectiveSelector(step),
            step.Args?.Route ?? step.Value);
    }

    private static int NextSequence(MauiFlow flow)
        => flow.Steps.Count == 0 ? 1 : flow.Steps.Max(step => step.Seq) + 1;

    private static string NormalizeOutcome(string? outcome)
        => outcome is "completed" or "queued" or "rejected" or "unknown-completion"
            ? outcome
            : "completed";

    private static MauiTestAgentError Error(
        string code,
        string category,
        string message,
        bool retryable)
        => new()
        {
            Code = code,
            Category = category,
            Message = message,
            Retryable = retryable,
        };

    private void PurgeExpiredLocked()
    {
        var now = _clock.GetUtcNow();
        foreach (var session in _sessions.Values)
        {
            if (session.State == SessionState.Active && session.ExpiresAt <= now)
            {
                session.State = SessionState.Expired;
                AppendAuditLocked(session, "session-expired", null, "expired", null, null, null, MauiTestAgentErrorCodes.SessionExpired);
            }
        }

        foreach (var approval in _approvalRequests.Values)
        {
            if (approval.State != MauiTestAgentApprovalStates.Pending)
                continue;

            if (approval.ExpiresAt <= now)
            {
                MarkApprovalLocked(approval, MauiTestAgentApprovalStates.Expired, "decision-window-expired");
                if (_sessions.TryGetValue(approval.SessionId, out var session))
                {
                    AppendAuditLocked(
                        session,
                        "approval-expired",
                        ApprovalEnvelope(approval),
                        "expired",
                        null,
                        null,
                        null,
                        MauiTestAgentErrorCodes.ApprovalRequestExpired);
                }
                continue;
            }

            if (!_sessions.TryGetValue(approval.SessionId, out var owner) ||
                owner.State != SessionState.Active ||
                !CorrelationMatches(owner, approval.Correlation, requireAll: true) ||
                !CanonicalTargetsMatch(owner.Target, approval.Target) ||
                !ProvenanceMatches(owner.Actor, approval.Provenance))
            {
                MarkApprovalLocked(approval, MauiTestAgentApprovalStates.Stale, "target-or-revision-stale");
                if (owner is not null)
                {
                    AppendAuditLocked(
                        owner,
                        "approval-stale",
                        ApprovalEnvelope(approval),
                        "stale",
                        null,
                        null,
                        null,
                        MauiTestAgentErrorCodes.MutationGrantStale);
                }
            }
        }

        foreach (var key in _grants
                     .Where(pair => pair.Value.ExpiresAt <= now)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            ExpireGrantLocked(key, now);
        }
        foreach (var key in _expiredGrantDigests
                     .Where(pair => pair.Value < now - _options.AuditRetention)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            _expiredGrantDigests.Remove(key);
        }

        foreach (var approval in _approvalRequests.Values)
        {
            if (approval.State == MauiTestAgentApprovalStates.Approved &&
                approval.GrantExpiresAt <= now)
            {
                MarkApprovalLocked(approval, MauiTestAgentApprovalStates.Expired, "grant-expired");
            }
        }
        foreach (var key in _approvalRequests
                     .Where(pair =>
                         pair.Value.State != MauiTestAgentApprovalStates.Pending &&
                         (pair.Value.DecidedAt ?? pair.Value.ExpiresAt) < now - _options.ApprovalRetention)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            _approvalRequests.Remove(key);
        }

        while (_journal.First is { Value.At: var at } && at < now - _options.AuditRetention)
            _journal.RemoveFirst();
        while (_journal.Count > _options.MaxAuditEntries)
            _journal.RemoveFirst();

        var staleIdempotency = _idempotency
            .Where(pair => pair.Value.CreatedAt < now - _options.IdempotencyRetention)
            .Select(pair => pair.Key)
            .ToArray();
        foreach (var key in staleIdempotency)
            _idempotency.Remove(key);
    }

    private void EvictExpiredGrantsLocked()
    {
        var now = _clock.GetUtcNow();
        foreach (var key in _grants.Where(pair => pair.Value.ExpiresAt <= now).Select(pair => pair.Key).ToArray())
            ExpireGrantLocked(key, now);
    }

    private void EvictOldestApprovalRequestLocked()
    {
        var removable = _approvalRequests.Values
            .Where(static request => request.State != MauiTestAgentApprovalStates.Pending)
            .OrderBy(static request => request.DecidedAt ?? request.ExpiresAt)
            .FirstOrDefault();
        if (removable is not null)
            _approvalRequests.Remove(removable.ApprovalRequestId);
    }

    private void ExpireGrantLocked(string grantDigest, DateTimeOffset now)
    {
        if (_grants.Remove(grantDigest))
            _expiredGrantDigests[grantDigest] = now;
        while (_expiredGrantDigests.Count > _options.MaxGrants)
        {
            var oldest = _expiredGrantDigests.OrderBy(pair => pair.Value).First();
            _expiredGrantDigests.Remove(oldest.Key);
        }
    }

    private void EvictOldestTerminalSessionLocked()
    {
        var removable = _sessions.Values
            .Where(session => session.State is SessionState.Abandoned or SessionState.Expired)
            .OrderBy(session => session.ExpiresAt)
            .FirstOrDefault();
        if (removable is not null)
        {
            _sessions.Remove(removable.SessionId);
            foreach (var key in _approvalRequests
                         .Where(pair => string.Equals(pair.Value.SessionId, removable.SessionId, StringComparison.Ordinal))
                         .Select(pair => pair.Key)
                         .ToArray())
            {
                _approvalRequests.Remove(key);
            }
        }
    }

    private void AppendAuditLocked(
        SessionRecord session,
        string kind,
        MauiTestAgentRequestEnvelope? envelope,
        string decision,
        string? grantDigest,
        string? actionDigest,
        string? resultDigest,
        string? errorCode,
        string? runId = null,
        string? decidedBy = null)
    {
        _journal.AddLast(new MauiTestAgentAuditEntry
        {
            Sequence = ++_auditSequence,
            At = _clock.GetUtcNow(),
            Kind = kind,
            RequestId = Bounded(envelope?.RequestId, 256),
            SessionId = session.SessionId,
            AgentId = session.Target.AgentId,
            AgentInstanceId = session.Target.AgentInstanceId,
            PlanRevision = session.PlanRevision,
            FlowRevision = session.FlowRevision,
            RunId = Bounded(runId ?? envelope?.Correlation?.RunId, 256),
            PolicyDecision = Bounded(decision, 128),
            DecidedBy = Bounded(decidedBy, 128),
            IntentDigest = string.IsNullOrWhiteSpace(envelope?.Intent) ? null : Hash(envelope.Intent),
            GrantDigest = BoundedDigest(grantDigest),
            ActionDigest = BoundedDigest(actionDigest),
            ResultDigest = BoundedDigest(resultDigest),
            ErrorCode = Bounded(errorCode, 128),
        });
        while (_journal.Count > _options.MaxAuditEntries)
            _journal.RemoveFirst();
    }

    private MauiTestAgentAuthoringSnapshot CreateSnapshot(SessionRecord session, bool includeReadCapability)
        => new()
        {
            SessionId = session.SessionId,
            State = session.State.ToWireValue(),
            ExpiresAt = session.ExpiresAt,
            Target = CloneTarget(session.Target),
            TargetState = CloneTargetState(session.TargetState),
            Plan = ClonePlan(session.Plan),
            PlanDigest = session.PlanDigest,
            Flow = CloneFlow(session.Flow),
            FlowDigest = session.FlowDigest,
            FlowRevision = session.FlowRevision,
            CommittedAt = session.CommittedAt,
            ApprovalRequests = _approvalRequests.Values
                .Where(request => string.Equals(request.SessionId, session.SessionId, StringComparison.Ordinal))
                .OrderByDescending(static request => request.CreatedAt)
                .Select(request => CloneApprovalRequest(request, includeGrant: true))
                .ToList(),
            ReadCapabilityId = includeReadCapability ? session.ReadCapabilityId : null,
            ExplorationBudget = DescribeExplorationBudgetLocked(session, _clock.GetUtcNow()),
        };

    private static MauiTestPlan NormalizePlan(
        MauiTestPlan? source,
        MauiTestAgentRequestEnvelope envelope,
        string flowId,
        MauiFlow flow,
        string planId,
        int revision,
        DateTimeOffset now)
    {
        var plan = ClonePlan(source) ?? new MauiTestPlan
        {
            Title = "Restricted test-agent draft",
            Goal = envelope.Intent,
            Reset = new MauiTestResetRequirement { Required = false, Strategy = "host-owned" },
            SideEffectPolicy = MauiFlowSideEffectPolicies.None,
            Provenance = CloneProvenance(envelope.Provenance!)!,
        };
        var node = JsonSerializer.SerializeToNode(plan, MauiTestingJsonContext.Default.MauiTestPlan)!.AsObject();
        node["schema"] = 1;
        node["planId"] = planId;
        node["revision"] = revision;
        node["flow"] = JsonSerializer.SerializeToNode(new MauiFlowReference
        {
            Path = plan.Flow?.Path,
            FlowId = flowId,
            Revision = Math.Max(1, envelope.Correlation?.FlowRevision ?? 1),
            Digest = MauiFlowRunReportSerializer.ComputeFlowDigest(flow),
        }, MauiTestingJsonContext.Default.MauiFlowReference);
        node["goal"] ??= Bounded(envelope.Intent, 512);
        node["sideEffectPolicy"] ??= MauiFlowSideEffectPolicies.None;
        node["reset"] ??= JsonSerializer.SerializeToNode(new MauiTestResetRequirement
        {
            Required = false,
            Strategy = "host-owned",
        }, MauiTestingJsonContext.Default.MauiTestResetRequirement);
        node["provenance"] ??= JsonSerializer.SerializeToNode(CloneProvenance(envelope.Provenance!), MauiTestingJsonContext.Default.MauiActorProvenance);
        var normalized = node.Deserialize(MauiTestingJsonContext.Default.MauiTestPlan)
            ?? throw new InvalidOperationException("The authoring plan could not be normalized.");
        NormalizeExplorationBudget(normalized.ExplorationBudget);
        return normalized;
    }

    /// <summary>
    /// Bounds the client-supplied exploration budget so an oversized scope list cannot be echoed
    /// back on every snapshot. The ceilings that matter for authorization are applied at
    /// redemption time by <see cref="EffectiveExplorationActions"/> and
    /// <see cref="EffectiveExplorationDurationSeconds"/>; this only bounds the retained shape.
    /// </summary>
    private static void NormalizeExplorationBudget(MauiExplorationBudget? budget)
    {
        if (budget is null)
            return;
        var scopes = budget.AllowedScopes
            .Where(static scope => !string.IsNullOrWhiteSpace(scope) && scope.Length <= 256)
            .Select(static scope => scope.Trim())
            .Distinct(StringComparer.Ordinal)
            .Take(MaxExplorationAllowedScopes)
            .ToList();
        budget.AllowedScopes.Clear();
        budget.AllowedScopes.AddRange(scopes);
    }

    private static MauiTestAgentCorrelation SnapshotCorrelation(SessionRecord session)
        => new()
        {
            AuthoringSessionId = session.SessionId,
            PlanId = session.PlanId,
            PlanRevision = session.PlanRevision,
            PlanDigest = session.PlanDigest,
            FlowId = session.FlowId,
            FlowRevision = session.FlowRevision,
            FlowDigest = session.FlowDigest,
        };

    internal static string ComputePlanDigest(MauiTestPlan plan)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(plan, MauiTestingJsonContext.Default.MauiTestPlan);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static string DigestEnvelope(MauiTestAgentRequestEnvelope envelope)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, MauiTestingJsonContext.Default.MauiTestAgentRequestEnvelope);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static string DigestMutationRequest(MauiTestAgentMutationAuthorizationRequest request)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(request, MauiTestingJsonContext.Default.MauiTestAgentMutationAuthorizationRequest);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static string DigestApprovalRequest(MauiTestAgentApprovalSubmitRequest request)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            request,
            MauiTestingJsonContext.Default.MauiTestAgentApprovalSubmitRequest);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static string DigestAction(MauiTestAgentMutationAuthorizationRequest request)
        => BuildActionDigest(request.Action, request.Selector, request.Route, request.ValueLength, request.ValueDigest);

    private static string DigestAction(MauiTestAgentActionRequest request)
        => BuildActionDigest(
            request.Action,
            request.Selector,
            request.Route,
            request.Value is null ? null : Encoding.UTF8.GetByteCount(request.Value),
            request.Value is null ? null : Hash(request.Value));

    private static string DigestDraftAppend(MauiTestAgentActionRequest request)
        => BuildActionDigest(
            MauiTestAgentActions.DraftAppend,
            request.Selector,
            request.Route,
            request.Value is null ? null : Encoding.UTF8.GetByteCount(request.Value),
            request.Value is null ? null : Hash(request.Value));

    private static string BuildActionDigest(
        string? action,
        FlowSelector? selector,
        string? route,
        int? valueLength,
        string? valueDigest)
        => Hash(string.Join(
            "\n",
            action,
            SelectorScopeKey(selector ?? new FlowSelector()),
            route,
            valueLength,
            valueDigest));

    private static string HashProposal(MauiFlowRepairProposal proposal)
        => Hash(JsonSerializer.Serialize(proposal, MauiTestingJsonContext.Default.MauiFlowRepairProposal));

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty))).ToLowerInvariant();

    private static string OpaqueId(string prefix)
        => $"{prefix}_{Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant()}";

    private static string OpaqueSecret(string prefix)
        => $"{prefix}_{Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_')}";

    private static bool FixedEquals(string left, string right)
        => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));

    private static string? Bounded(string? value, int maximum)
        => string.IsNullOrWhiteSpace(value) ? null : value.Length <= maximum ? value : value[..maximum];

    private static string? BoundedDigest(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Length <= 128 ? value : value[..128];

    private static MauiTestAgentTarget CloneTarget(MauiTestAgentTarget source) => new()
    {
        AgentId = source.AgentId,
        AgentInstanceId = source.AgentInstanceId,
        AppBuildFingerprint = source.AppBuildFingerprint,
        SeedFingerprint = source.SeedFingerprint,
        BackendStateFingerprint = source.BackendStateFingerprint,
    };

    private static MauiTestAgentTargetState CloneTargetState(MauiTestAgentTargetState source) => new()
    {
        AgentId = source.AgentId,
        AgentInstanceId = source.AgentInstanceId,
        AppBuildFingerprint = source.AppBuildFingerprint,
        SeedFingerprint = source.SeedFingerprint,
        BackendStateFingerprint = source.BackendStateFingerprint,
        Route = source.Route,
        Window = source.Window,
        ObservedAt = source.ObservedAt,
    };

    private static MauiActorProvenance? CloneProvenance(MauiActorProvenance? source) => source is null
        ? null
        : new MauiActorProvenance
        {
            ActorKind = source.ActorKind,
            ActorId = source.ActorId,
            Channel = source.Channel,
            Provider = source.Provider,
            Intent = source.Intent,
            RecordedAt = source.RecordedAt,
        };

    private static MauiTestAgentMutationScope CloneScope(MauiTestAgentMutationScope source) => new()
    {
        AllowedActions = source.AllowedActions.ToList(),
        AllowedSelectors = source.AllowedSelectors.ToList(),
        AllowedRoutes = source.AllowedRoutes.ToList(),
        AllowedSideEffectClasses = source.AllowedSideEffectClasses.ToList(),
        MaxActionCount = source.MaxActionCount,
        MaxValueBytes = source.MaxValueBytes,
    };

    private static MauiTestAgentApprovalRecord CloneApprovalRequest(
        ApprovalRequestRecord source,
        bool includeGrant)
        => new()
        {
            ApprovalRequestId = source.ApprovalRequestId,
            Kind = source.Kind,
            State = source.State,
            SessionId = source.SessionId,
            Intent = source.Intent,
            Provenance = CloneProvenance(source.Provenance),
            Target = CloneTarget(source.Target),
            TargetState = CloneTargetState(source.TargetState),
            Correlation = new MauiTestAgentCorrelation
            {
                AuthoringSessionId = source.Correlation.AuthoringSessionId,
                PlanId = source.Correlation.PlanId,
                PlanRevision = source.Correlation.PlanRevision,
                PlanDigest = source.Correlation.PlanDigest,
                FlowId = source.Correlation.FlowId,
                FlowRevision = source.Correlation.FlowRevision,
                FlowDigest = source.Correlation.FlowDigest,
                RunId = source.Correlation.RunId,
            },
            RequestedScope = CloneScope(source.RequestedScope),
            ApprovedScope = source.ApprovedScope is null ? null : CloneScope(source.ApprovedScope),
            CreatedAt = source.CreatedAt,
            ExpiresAt = source.ExpiresAt,
            DecidedAt = source.DecidedAt,
            DecidedBy = source.DecidedBy,
            ReasonCode = source.ReasonCode,
            GrantId = includeGrant && source.State == MauiTestAgentApprovalStates.Approved
                ? source.GrantId
                : null,
            GrantExpiresAt = source.GrantExpiresAt,
        };

    private static MauiTestAgentRequestEnvelope ApprovalEnvelope(ApprovalRequestRecord source)
        => new()
        {
            RequestId = source.RequestId,
            Target = CloneTarget(source.Target),
            Correlation = new MauiTestAgentCorrelation
            {
                AuthoringSessionId = source.Correlation.AuthoringSessionId,
                PlanId = source.Correlation.PlanId,
                PlanRevision = source.Correlation.PlanRevision,
                PlanDigest = source.Correlation.PlanDigest,
                FlowId = source.Correlation.FlowId,
                FlowRevision = source.Correlation.FlowRevision,
                FlowDigest = source.Correlation.FlowDigest,
                RunId = source.Correlation.RunId,
            },
            Provenance = CloneProvenance(source.Provenance),
            Intent = source.Intent,
            PolicyVersion = MauiTestAgentProtocolVersions.PolicyVersion,
        };

    private void MarkApprovalLocked(ApprovalRequestRecord request, string state, string reasonCode)
    {
        request.State = state;
        request.ReasonCode = Bounded(reasonCode, 128);
        request.DecidedAt ??= _clock.GetUtcNow();
    }

    private static MauiFlow? CloneFlow(MauiFlow? source)
    {
        if (source is null)
            return null;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(source, MauiFlowJsonContext.Default.MauiFlow);
        return JsonSerializer.Deserialize(bytes, MauiFlowJsonContext.Default.MauiFlow);
    }

    private static MauiTestPlan? ClonePlan(MauiTestPlan? source)
    {
        if (source is null)
            return null;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(source, MauiTestingJsonContext.Default.MauiTestPlan);
        return JsonSerializer.Deserialize(bytes, MauiTestingJsonContext.Default.MauiTestPlan);
    }

    private static FlowSelector? CloneSelector(FlowSelector? source)
    {
        if (source is null)
            return null;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(source, MauiFlowJsonContext.Default.FlowSelector);
        return JsonSerializer.Deserialize(bytes, MauiFlowJsonContext.Default.FlowSelector);
    }

    private static FlowAssert CloneAssertion(FlowAssert source)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(source, MauiFlowJsonContext.Default.FlowAssert);
        return JsonSerializer.Deserialize(bytes, MauiFlowJsonContext.Default.FlowAssert)
            ?? throw new InvalidOperationException("The assertion could not be cloned.");
    }

    private static MauiFlowRepairProposal CloneProposal(MauiFlowRepairProposal source)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(source, MauiTestingJsonContext.Default.MauiFlowRepairProposal);
        return JsonSerializer.Deserialize(bytes, MauiTestingJsonContext.Default.MauiFlowRepairProposal)
            ?? throw new InvalidOperationException("The patch proposal could not be cloned.");
    }

    private static MauiTestAgentPatchRecord ClonePatchRecord(MauiTestAgentPatchRecord source) => new()
    {
        ProposalId = source.ProposalId,
        State = source.State,
        Proposal = source.Proposal is null ? null : CloneProposal(source.Proposal),
        ReasonDigest = source.ReasonDigest,
        RecordedAt = source.RecordedAt,
    };

    private static MauiTestAgentAuditEntry CloneAudit(MauiTestAgentAuditEntry source) => new()
    {
        Sequence = source.Sequence,
        At = source.At,
        Kind = source.Kind,
        RequestId = source.RequestId,
        SessionId = source.SessionId,
        AgentId = source.AgentId,
        AgentInstanceId = source.AgentInstanceId,
        PlanRevision = source.PlanRevision,
        FlowRevision = source.FlowRevision,
        RunId = source.RunId,
        PolicyDecision = source.PolicyDecision,
        DecidedBy = source.DecidedBy,
        IntentDigest = source.IntentDigest,
        GrantDigest = source.GrantDigest,
        ActionDigest = source.ActionDigest,
        ResultDigest = source.ResultDigest,
        ErrorCode = source.ErrorCode,
    };

    private sealed class SessionRecord
    {
        public SessionRecord(
            string sessionId,
            string readCapabilityId,
            string readCapabilityDigest,
            MauiTestAgentTarget target,
            MauiTestAgentTargetState targetState,
            MauiActorProvenance actor,
            string planId,
            int planRevision,
            string flowId,
            int flowRevision,
            MauiTestPlan plan,
            MauiFlow flow,
            string flowDigest,
            DateTimeOffset createdAt,
            DateTimeOffset expiresAt)
        {
            SessionId = sessionId;
            ReadCapabilityId = readCapabilityId;
            ReadCapabilityDigest = readCapabilityDigest;
            Target = CloneTarget(target);
            TargetState = CloneTargetState(targetState);
            Actor = CloneProvenance(actor)!;
            PlanId = planId;
            PlanRevision = planRevision;
            FlowId = flowId;
            FlowRevision = flowRevision;
            Plan = ClonePlan(plan)!;
            PlanDigest = ComputePlanDigest(Plan);
            Flow = CloneFlow(flow)!;
            FlowDigest = flowDigest;
            CreatedAt = createdAt;
            ExpiresAt = expiresAt;
        }

        public string SessionId { get; }
        public string ReadCapabilityId { get; }
        public string ReadCapabilityDigest { get; }
        public MauiTestAgentTarget Target { get; }
        public MauiTestAgentTargetState TargetState { get; }
        public MauiActorProvenance Actor { get; }
        public string PlanId { get; }
        public int PlanRevision { get; set; }
        public string FlowId { get; }
        public int FlowRevision { get; set; }
        public MauiTestPlan Plan { get; set; }
        public string PlanDigest { get; set; }
        public MauiFlow Flow { get; }
        public string FlowDigest { get; set; }
        public DateTimeOffset CreatedAt { get; }
        public DateTimeOffset ExpiresAt { get; }
        public DateTimeOffset? CommittedAt { get; set; }
        public SessionState State { get; set; } = SessionState.Active;
        public int ExplorationActionsUsed { get; set; }
        public DateTimeOffset? ExplorationStartedAt { get; set; }
        public Dictionary<string, MauiTestAgentPatchRecord> Patches { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> Runs { get; } = new(StringComparer.Ordinal);
    }

    private sealed class ApprovalRequestRecord
    {
        public ApprovalRequestRecord(
            string approvalRequestId,
            string kind,
            string sessionId,
            string requestId,
            string intent,
            MauiTestAgentTarget target,
            MauiTestAgentTargetState targetState,
            MauiActorProvenance provenance,
            MauiTestAgentCorrelation correlation,
            MauiTestAgentMutationScope requestedScope,
            DateTimeOffset createdAt,
            DateTimeOffset expiresAt)
        {
            ApprovalRequestId = approvalRequestId;
            Kind = kind;
            SessionId = sessionId;
            RequestId = requestId;
            Intent = intent;
            Target = CloneTarget(target);
            TargetState = CloneTargetState(targetState);
            Provenance = CloneProvenance(provenance)!;
            Correlation = correlation;
            RequestedScope = CloneScope(requestedScope);
            CreatedAt = createdAt;
            ExpiresAt = expiresAt;
        }

        public string ApprovalRequestId { get; }
        public string Kind { get; }
        public string SessionId { get; }
        public string RequestId { get; }
        public string Intent { get; }
        public MauiTestAgentTarget Target { get; }
        public MauiTestAgentTargetState TargetState { get; }
        public MauiActorProvenance Provenance { get; }
        public MauiTestAgentCorrelation Correlation { get; set; }
        public MauiTestAgentMutationScope RequestedScope { get; }
        public DateTimeOffset CreatedAt { get; }
        public DateTimeOffset ExpiresAt { get; }
        public string State { get; set; } = MauiTestAgentApprovalStates.Pending;
        public MauiTestAgentMutationScope? ApprovedScope { get; set; }
        public DateTimeOffset? DecidedAt { get; set; }
        public string? DecidedBy { get; set; }
        public string? ReasonCode { get; set; }
        public string? GrantId { get; set; }
        public string? GrantDigest { get; set; }
        public DateTimeOffset? GrantExpiresAt { get; set; }
    }

    private sealed class GrantRecord
    {
        public GrantRecord(
            string grantDigest,
            string sessionId,
            MauiTestAgentTarget target,
            MauiTestAgentTargetState targetState,
            MauiActorProvenance actor,
            MauiTestAgentCorrelation correlation,
            MauiTestAgentMutationScope scope,
            DateTimeOffset expiresAt,
            string policyVersion,
            string? approvalRequestId)
        {
            GrantDigest = grantDigest;
            SessionId = sessionId;
            Target = target;
            TargetState = targetState;
            Actor = actor;
            Correlation = correlation;
            Scope = scope;
            RemainingActions = scope.MaxActionCount!.Value;
            ExpiresAt = expiresAt;
            PolicyVersion = policyVersion;
            ApprovalRequestId = approvalRequestId;
        }

        public string GrantDigest { get; }
        public string SessionId { get; }
        public MauiTestAgentTarget Target { get; }
        public MauiTestAgentTargetState TargetState { get; }
        public MauiActorProvenance Actor { get; }
        public MauiTestAgentCorrelation Correlation { get; set; }
        public MauiTestAgentMutationScope Scope { get; }
        public int RemainingActions { get; set; }
        public DateTimeOffset ExpiresAt { get; }
        public string PolicyVersion { get; }
        public string? ApprovalRequestId { get; }
    }

    private sealed class AuthorizationRecord
    {
        public AuthorizationRecord(
            string authorizationId,
            string sessionId,
            string grantDigest,
            string requestDigest,
            string action,
            string actionDigest,
            DateTimeOffset createdAt,
            int remainingActions,
            bool explorationOrigin = false)
        {
            AuthorizationId = authorizationId;
            SessionId = sessionId;
            GrantDigest = grantDigest;
            RequestDigest = requestDigest;
            Action = action;
            CreatedAt = createdAt;
            ActionDigest = actionDigest;
            RemainingActions = remainingActions;
            ExplorationOrigin = explorationOrigin;
        }

        public string AuthorizationId { get; }
        public string SessionId { get; }
        public string GrantDigest { get; }
        public string RequestDigest { get; }
        public string Action { get; }
        public string ActionDigest { get; }
        public DateTimeOffset CreatedAt { get; }
        public int RemainingActions { get; }

        /// <summary>
        /// True when this authorization was minted by the bounded exploration route. Such an
        /// authorization buys exactly one navigation step and may never be redeemed to start a
        /// workflow run, which would otherwise let a single budget unit drive an arbitrary flow.
        /// </summary>
        public bool ExplorationOrigin { get; }

        /// <summary>
        /// Digest of the exploration scope this authorization was minted for. A replay of the same
        /// idempotency key naming a different scope is refused rather than silently returning this
        /// authorization, which would misattribute the step in the audit trail.
        /// </summary>
        public string? ExplorationScopeDigest { get; set; }

        /// <summary>
        /// Canonical identity of the single navigation step this authorization was minted for. The
        /// dispatched flow must reduce to the same digest, so one unit of exploration budget cannot
        /// be redeemed against a different element, route, or a wider flow.
        /// </summary>
        public string? ExplorationStepDigest { get; set; }
        public bool Consumed { get; set; }
        public bool RunDispatched { get; set; }
        public bool Completed { get; set; }
        public string? Outcome { get; set; }
        public string? RunId { get; set; }
    }

    private sealed class IdempotencyRecord
    {
        public IdempotencyRecord(
            string requestDigest,
            string? sessionId,
            DateTimeOffset createdAt,
            string? approvalRequestId = null,
            string? authorizationId = null,
            string? patchProposalId = null)
        {
            RequestDigest = requestDigest;
            SessionId = sessionId;
            CreatedAt = createdAt;
            ApprovalRequestId = approvalRequestId;
            AuthorizationId = authorizationId;
            PatchProposalId = patchProposalId;
        }

        public string RequestDigest { get; }
        public string? SessionId { get; }
        public DateTimeOffset CreatedAt { get; }
        public string? ApprovalRequestId { get; }
        public string? AuthorizationId { get; }
        public string? PatchProposalId { get; }
    }

    private enum SessionState
    {
        Active,
        Abandoned,
        Expired,
    }
}

internal static class TestAgentSessionStateExtensions
{
    internal static string ToWireValue(this object state) => state.ToString()!.ToLowerInvariant();
}

internal sealed class TestAgentSessionServiceOptions
{
    public int MaxSessions { get; init; } = 32;
    public int MaxGrants { get; init; } = 128;
    public int MaxApprovalRequests { get; init; } = 128;
    public int MaxAuditEntries { get; init; } = 512;
    public int MaxPatchesPerSession { get; init; } = 32;
    public int MaxPatchRequestBytes { get; init; } = 65_536;
    public int MaxActionsPerGrant { get; init; } = 64;
    public int MaxExplorationActions { get; init; } = 32;
    public TimeSpan MaximumExplorationWindow { get; init; } = TimeSpan.FromMinutes(10);
    public TimeSpan SessionLifetime { get; init; } = TimeSpan.FromMinutes(30);
    public TimeSpan ApprovalRequestLifetime { get; init; } = TimeSpan.FromMinutes(10);
    public TimeSpan ApprovalRetention { get; init; } = TimeSpan.FromMinutes(30);
    public TimeSpan DefaultGrantLifetime { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan MaximumGrantLifetime { get; init; } = TimeSpan.FromMinutes(15);
    public TimeSpan AuditRetention { get; init; } = TimeSpan.FromHours(24);
    public TimeSpan IdempotencyRetention { get; init; } = TimeSpan.FromHours(1);
}
