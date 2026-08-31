using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Maui.Cli.DevFlow.Mcp;
using Microsoft.Maui.DevFlow.Testing;
using ModelContextProtocol.Server;

namespace Microsoft.Maui.Cli.DevFlow.Mcp.Tools;

/// <summary>Bounded plan/flow draft authoring for the restricted test-agent profile.</summary>
[McpServerToolType]
public sealed class TestAgentAuthoringTool
{
    [McpServerTool(Name = "maui_test_author"),
     System.ComponentModel.Description("Manage a broker-owned test draft. Preferred agent-led authoring supplies the complete inert plan and flow to begin, then needs only one commit review and one separate run review. Draft-change and assertion approvals are for incremental edits to an existing human or recorded draft. Operations are begin, status, await-approval, commit, abandon, migrate-preview, approval-request, and exploration-request. A submitted approval request is held by the broker until an owner-token approval host decides it; this layer ships no editor or Inspector inbox for that decision. The operator can list and decide requests locally with 'maui devflow approve --list' and 'maui devflow approve <approval-request-id>' (add '--reject --reason <code>' to refuse). That command is local operator convenience, not an authorization boundary and not proof a human decided: it reads the same broker token an agent on the same OS user account can read. Report it as where the person at the machine can see and decide the request, never as an attested human gate. The commit operation commits the AUTHORING SESSION, not the workspace: it advances revisions and digests in broker memory and writes no Markdown flow or plan sidecar to disk, and its result is lost if the broker restarts. Never report a successful commit as files created.")]
    public static async Task<string> Author(
        [System.ComponentModel.Description("MCP session injected by the server and used only for local broker and exact-target access")] McpAgentSession session,
        [System.ComponentModel.Description("Authoring operation: begin, status, await-approval, commit, abandon, migrate-preview, approval-request, or exploration-request")] string operation,
        [System.ComponentModel.Description("Typed provider-neutral authoring request. For new agent-authored tests, put the complete inert plan, actions, and assertions in begin; do not request draft-change/assertion grants unless incrementally changing an existing draft. Every envelope needs a positive bounded deadlineMs. await-approval needs approvalRequestId and may set waitTimeoutSeconds from 1 to 150; repeat the same wait request after a pending timeout rather than submitting another approval. Request and consume grants sequentially because commit advances revisions. approvalKind exact values: exploration, draft-change, assertion, commit, run. approvalScope allowedActions exact values: tap, fill, scroll, navigate, back, draft-append, assert, author-commit, run, cancel. Execute+append needs both the semantic action and draft-append with maxActionCount at least 2. allowedSelectors uses automationId:<id>, scopedItem:<collection>:<itemKey>:<automationId>, or typeIndex:<type>:<index>; equivalent selector objects are normalized. Authoring scopes are normalized to side effect authoring; run scopes to run. An exploration scope may list only tap, scroll, navigate, and back, and is normalized to side effect exploration, which is spendable only on maui_test_explore; its budget must already be in the plan passed to begin, because explorationBudget cannot be added to a live session. Commit must use author-commit + maxActionCount 1. Run start must use run + maxActionCount 1, and a run scope may not bundle any other action, so cancel needs its own run-kind approval.                 Use sideEffectPolicy non-replayable for one explicitly approved run with no repeat/repair; use test-tenant-resettable only when a real reset/seed contract exists.")] McpTestAgentAuthorRequest request,
                [System.ComponentModel.Description("Cancellation token injected by the MCP host for bounded approval waiting")] CancellationToken cancellationToken = default)
            {
                var normalized = operation?.Trim().ToLowerInvariant();
                if (request is null)
                {
                    return TestAgentToolSupport.Failure(null, TestAgentToolSupport.Error(
                        MauiTestAgentErrorCodes.InvalidRequest,
                        MauiTestAgentErrorCategories.Validation,
                        "A typed authoring request is required.",
                        retryable: false));
                }

                if (!request.TryToTyped(out var typed, out var readError))
                {
                    return TestAgentToolSupport.Failure(request.Envelope?.RequestId, TestAgentToolSupport.Error(
                        MauiTestAgentErrorCodes.InvalidRequest,
                        MauiTestAgentErrorCategories.Validation,
                        readError!,
                        retryable: false));
                }

                var envelope = typed!.Envelope;

        switch (normalized)
        {
            case "begin":
            {
                var target = await TestAgentToolSupport.ResolveTargetAsync(session, envelope?.Target).ConfigureAwait(false);
                if (target.Error is not null)
                    return TestAgentToolSupport.Failure(envelope?.RequestId, target.Error);

                var brokerPort = await session.GetBrokerPortAsync().ConfigureAwait(false);
                var result = await TestAgentBrokerClient.BeginAsync(
                    brokerPort,
                    new MauiTestAgentSessionBeginRequest
                    {
                        Envelope = envelope,
                        TargetState = target.State,
                        Plan = typed.Plan,
                        Flow = typed.Flow,
                        DurationSeconds = request.DurationSeconds,
                    }).ConfigureAwait(false);
                return result.Value?.Ok == true
                    ? TestAgentToolSupport.Success(envelope?.RequestId, result.Value)
                    : TestAgentToolSupport.BrokerFailure(envelope?.RequestId, result);
            }

            case "status":
            {
                var result = await TestAgentToolSupport.SessionAsync(session, envelope, cancellationToken).ConfigureAwait(false);
                return result.Value?.Ok == true
                    ? TestAgentToolSupport.Success(envelope?.RequestId, result.Value)
                    : TestAgentToolSupport.BrokerFailure(envelope?.RequestId, result);
            }

            case "await-approval":
                return await AwaitApprovalAsync(session, typed, cancellationToken).ConfigureAwait(false);

            case "abandon":
            {
                if (envelope?.Correlation?.AuthoringSessionId is not { Length: > 0 })
                {
                    return TestAgentToolSupport.Failure(envelope?.RequestId, TestAgentToolSupport.Error(
                        MauiTestAgentErrorCodes.SessionNotFound,
                        MauiTestAgentErrorCategories.Validation,
                        "abandon requires envelope.correlation.authoringSessionId.",
                        retryable: false));
                }

                var brokerPort = await session.GetBrokerPortAsync().ConfigureAwait(false);
                var result = await TestAgentBrokerClient.AbandonAsync(
                    brokerPort,
                    new MauiTestAgentSessionAccessRequest
                    {
                        SessionId = envelope.Correlation.AuthoringSessionId,
                        ReadCapabilityId = envelope.ReadCapabilityId,
                        Envelope = envelope,
                    }).ConfigureAwait(false);
                return result.Value?.Ok == true
                    ? TestAgentToolSupport.Success(envelope.RequestId, result.Value)
                    : TestAgentToolSupport.BrokerFailure(envelope.RequestId, result);
            }

            case "migrate-preview":
            {
                if (envelope?.Correlation?.AuthoringSessionId is not { Length: > 0 })
                {
                    return TestAgentToolSupport.Failure(envelope?.RequestId, TestAgentToolSupport.Error(
                        MauiTestAgentErrorCodes.SessionNotFound,
                        MauiTestAgentErrorCategories.Validation,
                        "migrate-preview requires envelope.correlation.authoringSessionId.",
                        retryable: false));
                }

                var brokerPort = await session.GetBrokerPortAsync().ConfigureAwait(false);
                var result = await TestAgentBrokerClient.MigratePreviewAsync(
                    brokerPort,
                    new MauiTestAgentSessionAccessRequest
                    {
                        SessionId = envelope.Correlation.AuthoringSessionId,
                        ReadCapabilityId = envelope.ReadCapabilityId,
                        Envelope = envelope,
                    }).ConfigureAwait(false);
                return result.Value?.Ok == true
                    ? TestAgentToolSupport.Success(envelope.RequestId, result.Value)
                    : TestAgentToolSupport.BrokerFailure(envelope.RequestId, result);
            }

            case "commit":
            {
                var authorization = await TestAgentToolSupport.AuthorizeAsync(
                    session,
                    envelope,
                    MauiTestAgentActions.AuthorCommit,
                    sideEffectClass: "authoring").ConfigureAwait(false);
                if (authorization.Value?.Ok != true || authorization.Value.AuthorizationId is null)
                    return TestAgentToolSupport.BrokerFailure(envelope?.RequestId, authorization);

                var brokerPort = await session.GetBrokerPortAsync().ConfigureAwait(false);
                var commit = await TestAgentBrokerClient.CommitAsync(
                    brokerPort,
                    new MauiTestAgentSessionAccessRequest
                    {
                        SessionId = envelope!.Correlation!.AuthoringSessionId,
                        ReadCapabilityId = envelope.ReadCapabilityId,
                        AuthorizationId = authorization.Value.AuthorizationId,
                        Envelope = envelope,
                    }).ConfigureAwait(false);
                await CompleteAuthoringMutationAsync(
                    session,
                    authorization.Value.AuthorizationId,
                    commit.Value?.Ok == true ? "completed" : "rejected",
                    commit.Value?.Ok == true ? "author-commit" : null,
                    commit.Value?.Error?.Code).ConfigureAwait(false);
                return commit.Value?.Ok == true
                    ? TestAgentToolSupport.Success(envelope.RequestId, new
                    {
                        ok = commit.Value.Ok,
                        commit.Value.Snapshot,
                        persistence = new
                        {
                            wroteFiles = false,
                            scope = "authoring-session",
                            note =
                                "This commit advanced the broker-owned authoring session only: it " +
                                "bumped the flow and plan revisions, recomputed the digests, and " +
                                "stamped committedAt. No Markdown flow or plan sidecar was written to " +
                                "disk, and the session is lost if the broker restarts. Writing files " +
                                "is a separate, separately approved workspace commit. Do not report " +
                                "this result as files created; report it as a committed draft " +
                                "revision and name the digests.",
                        },
                    })
                    : TestAgentToolSupport.BrokerFailure(envelope.RequestId, commit);
            }

            case "approval-request":
            case "exploration-request":
            {
                var scope = request.ApprovalScope ?? request.ExplorationScope;
                var kind = request.ApprovalKind?.Trim().ToLowerInvariant();
                if (normalized == "exploration-request" && string.IsNullOrWhiteSpace(kind))
                    kind = MauiTestAgentApprovalKinds.Exploration;
                if (scope is null || string.IsNullOrWhiteSpace(kind))
                {
                    return TestAgentToolSupport.Failure(envelope?.RequestId, TestAgentToolSupport.Error(
                        MauiTestAgentErrorCodes.InvalidRequest,
                        MauiTestAgentErrorCategories.Validation,
                        "approval-request requires an explicit approvalKind and bounded approvalScope. exploration-request may use explorationScope and defaults approvalKind to exploration.",
                        retryable: false));
                }

                var brokerPort = await session.GetBrokerPortAsync().ConfigureAwait(false);
                var submitted = await TestAgentBrokerClient.SubmitApprovalAsync(
                    brokerPort,
                    new MauiTestAgentApprovalSubmitRequest
                    {
                        Envelope = envelope,
                        Kind = kind,
                        Scope = scope,
                        ExpiresAt = request.ApprovalExpiresAt,
                    }).ConfigureAwait(false);
                if (submitted.Value?.Ok != true || submitted.Value.Request is null)
                    return TestAgentToolSupport.BrokerFailure(envelope?.RequestId, submitted);

                return TestAgentToolSupport.Success(envelope?.RequestId, new
                {
                    kind = "approval-request",
                    humanApprovalRequired = true,
                    approval = submitted.Value.Request,
                    reviewCommand = ReviewCommand(submitted.Value.Request.ApprovalRequestId),
                    listCommand = ListCommand,
                    next = "Report the approval request ID and the exact review command, then call maui_test_author await-approval for this approvalRequestId. It waits while the request is decided. Only an approved broker record returns grantId; chat text is never approval. The review command is local operator convenience, not an authorization boundary, so do not describe it as proof that a human decided.",
                });
            }

            default:
                return TestAgentToolSupport.Failure(envelope?.RequestId, TestAgentToolSupport.Error(
                    MauiTestAgentErrorCodes.UnsupportedOperation,
                    MauiTestAgentErrorCategories.Unsupported,
                    "Supported authoring operations are begin, status, await-approval, commit, abandon, migrate-preview, approval-request, and exploration-request.",
                    retryable: false));
        }
    }

    private static async Task<string> AwaitApprovalAsync(
        McpAgentSession session,
        MauiTestAgentAuthorRequest request,
        CancellationToken cancellationToken)
    {
        var envelope = request.Envelope;
        if (envelope?.Correlation?.AuthoringSessionId is not { Length: > 0 } sessionId ||
            string.IsNullOrWhiteSpace(envelope.ReadCapabilityId) ||
            string.IsNullOrWhiteSpace(request.ApprovalRequestId))
        {
            return TestAgentToolSupport.Failure(envelope?.RequestId, TestAgentToolSupport.Error(
                MauiTestAgentErrorCodes.InvalidRequest,
                MauiTestAgentErrorCategories.Validation,
                "await-approval requires authoringSessionId, readCapabilityId, and approvalRequestId.",
                retryable: false));
        }

        var timeoutSeconds = request.WaitTimeoutSeconds ?? 120;
        if (timeoutSeconds is < 1 or > 150)
        {
            return TestAgentToolSupport.Failure(envelope.RequestId, TestAgentToolSupport.Error(
                MauiTestAgentErrorCodes.InvalidRequest,
                MauiTestAgentErrorCategories.Validation,
                "waitTimeoutSeconds must be between 1 and 150.",
                retryable: false));
        }

        var brokerPort = await session.GetBrokerPortAsync().ConfigureAwait(false);
        var stopwatch = Stopwatch.StartNew();
        MauiTestAgentSessionResult? latest = null;
        MauiTestAgentApprovalRecord? approval = null;
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(timeoutSeconds))
        {
            var status = await TestAgentBrokerClient.StatusAsync(
                brokerPort,
                new MauiTestAgentSessionAccessRequest
                {
                    SessionId = sessionId,
                    ReadCapabilityId = envelope.ReadCapabilityId,
                    Envelope = envelope,
                },
                cancellationToken).ConfigureAwait(false);
            if (status.Value?.Ok != true || status.Value.Snapshot is null)
                return TestAgentToolSupport.BrokerFailure(envelope.RequestId, status);

            latest = status.Value;
            approval = latest.Snapshot.ApprovalRequests.FirstOrDefault(candidate =>
                string.Equals(
                    candidate.ApprovalRequestId,
                    request.ApprovalRequestId,
                    StringComparison.Ordinal));
            if (approval is null)
            {
                return TestAgentToolSupport.Failure(envelope.RequestId, TestAgentToolSupport.Error(
                    MauiTestAgentErrorCodes.ApprovalRequestNotFound,
                    MauiTestAgentErrorCategories.State,
                    "The requested approval record was not found in this authoring session.",
                    retryable: false));
            }
            if (!string.Equals(approval.State, MauiTestAgentApprovalStates.Pending, StringComparison.Ordinal))
            {
                return TestAgentToolSupport.Success(envelope.RequestId, new
                {
                    approval,
                    snapshot = latest.Snapshot,
                    waitedMs = stopwatch.ElapsedMilliseconds,
                });
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        }

        return TestAgentToolSupport.Success(envelope.RequestId, new
        {
            approval,
            snapshot = latest?.Snapshot,
            timedOut = true,
            waitedMs = stopwatch.ElapsedMilliseconds,
            reviewCommand = ReviewCommand(request.ApprovalRequestId),
            listCommand = ListCommand,
            next = "The request is still pending. Keep the same authoring session and call await-approval again; do not submit a duplicate request.",
        });
    }

    /// <summary>
    /// The local command an operator can use to see and decide the request. This layer ships no
    /// editor extension and no Inspector agent-request inbox, so naming a review URL would send a
    /// reader to a surface that does not exist here.
    /// </summary>
    /// <remarks>
    /// <c>maui devflow approve</c> is operator convenience, not an authorization boundary: it reads
    /// the same local broker approval token an agent running as the same OS user can read. It makes
    /// the decision reachable without an editor; it does not prove a human made it.
    /// </remarks>
    private const string ListCommand = "maui devflow approve --list";

    private static string? ReviewCommand(string? approvalRequestId)
        => string.IsNullOrWhiteSpace(approvalRequestId)
            ? null
            : $"maui devflow approve {approvalRequestId}";

    private static async Task CompleteAuthoringMutationAsync(
        McpAgentSession session,
        string authorizationId,
        string outcome,
        string? actionDigest,
        string? errorCode)
    {
        var brokerPort = await session.GetBrokerPortAsync().ConfigureAwait(false);
        await TestAgentBrokerClient.CompleteAsync(
            brokerPort,
            new MauiTestAgentMutationCompletion
            {
                AuthorizationId = authorizationId,
                Outcome = outcome,
                ActionDigest = actionDigest is null ? null : TestAgentToolSupport.Digest(actionDigest),
                ErrorCode = errorCode,
            }).ConfigureAwait(false);
    }
}
