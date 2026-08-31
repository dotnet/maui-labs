using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.Cli.DevFlow.Mcp;
using Microsoft.Maui.DevFlow.Testing;
using ModelContextProtocol.Server;

namespace Microsoft.Maui.Cli.DevFlow.Mcp.Tools;

/// <summary>Approved typed semantic actions for the restricted test-agent profile.</summary>
[McpServerToolType]
public sealed class TestAgentActionTool
{
    [McpServerTool(Name = "maui_test_action"),
     System.ComponentModel.Description("Execute only a human-approved typed semantic action (tap, fill, scroll, navigate, or back) against the authoring session's canonical target. Appending its normalized form requires a separately authorized and consumed draft-append action; execute+append therefore needs a grant scope containing both actions with maxActionCount at least 2. Never invokes arbitrary actions or mutates arbitrary properties.")]
    public static Task<string> Action(
        [System.ComponentModel.Description("MCP session injected by the server and used for the local broker and exact target")] McpAgentSession session,
        [System.ComponentModel.Description("Typed semantic action request containing a complete envelope, durable selector or route, bounded value, and optional draft append")] MauiTestAgentActionRequest request)
        => ExecuteAsync(session, request, explorationScope: null);

    /// <summary>
    /// Shared dispatch path for approved typed actions. When <paramref name="explorationScope"/> is
    /// supplied the step is authorized through the broker's exploration endpoint instead, so the
    /// broker — not this client — decides whether an exploration budget still permits the step.
    /// </summary>
    internal static async Task<string> ExecuteAsync(
        McpAgentSession session,
        MauiTestAgentActionRequest request,
        string? explorationScope)
    {
        if (request is null)
        {
            return TestAgentToolSupport.Failure(null, TestAgentToolSupport.Error(
                MauiTestAgentErrorCodes.InvalidRequest,
                MauiTestAgentErrorCategories.Validation,
                "A typed semantic action request is required.",
                retryable: false));
        }
        var envelope = request.Envelope;
        if (envelope is null)
        {
            return TestAgentToolSupport.Failure(null, TestAgentToolSupport.Error(
                MauiTestAgentErrorCodes.InvalidRequest,
                MauiTestAgentErrorCategories.Validation,
                "A typed semantic action request with a protocol envelope is required.",
                retryable: false));
        }

        // Exploration is a single executed navigation step and nothing else. The broker refuses a
        // draft append or a value against an exploration grant independently; pinning it here keeps
        // the invariant visible at the seam the two tools share.
        if (explorationScope is not null &&
            (!request.Execute || request.AppendDraft || request.Value is not null))
        {
            return TestAgentToolSupport.Failure(envelope.RequestId, TestAgentToolSupport.Error(
                MauiTestAgentErrorCodes.UnsupportedOperation,
                MauiTestAgentErrorCategories.Unsupported,
                "An exploration step executes exactly one navigation action. It cannot append to the draft or carry a value.",
                retryable: false));
        }
        if (!request.Execute && !request.AppendDraft)
        {
            return TestAgentToolSupport.Failure(envelope.RequestId, TestAgentToolSupport.Error(
                MauiTestAgentErrorCodes.InvalidRequest,
                MauiTestAgentErrorCategories.Validation,
                "An action request must execute, append a draft step, or both.",
                retryable: false));
        }
        if (request.Execute &&
            string.Equals(request.SideEffectClass, "authoring", StringComparison.Ordinal))
        {
            return TestAgentToolSupport.Failure(envelope.RequestId, TestAgentToolSupport.Error(
                MauiTestAgentErrorCodes.MutationGrantScopeDenied,
                MauiTestAgentErrorCategories.Authorization,
                "The authoring side-effect class is append-only and cannot execute against the app.",
                retryable: false));
        }
        if (!TryCreateActionFlow(request, out var actionFlow, out var error))
            return TestAgentToolSupport.Failure(envelope.RequestId, error!);

        TestAgentSessionTargetResolution? sessionTarget = null;
        string? runJson = null;
        WorkflowRunStartRequest? runRequest = null;
        if (request.Execute)
        {
            sessionTarget = await TestAgentToolSupport.ResolveSessionTargetAsync(session, envelope).ConfigureAwait(false);
            if (sessionTarget.Error is not null)
                return TestAgentToolSupport.Failure(envelope.RequestId, sessionTarget.Error);

            var flowDigest = MauiFlowRunReportSerializer.ComputeFlowDigest(actionFlow!);
            var actionPlan = CreateActionPlan(sessionTarget.Snapshot!.Plan, flowDigest);
            runRequest = new WorkflowRunStartRequest
            {
                AgentId = sessionTarget.Target!.AgentId,
                AgentInstanceId = sessionTarget.Target.AgentInstanceId,
                IdempotencyKey = envelope.IdempotencyKey,
                Flow = actionFlow,
                Plan = actionPlan,
                Context = TestAgentRunTool.CreateRunContext(
                    actionPlan,
                    sessionTarget.Snapshot.TargetState,
                    sessionTarget.LiveTarget!.State),
                TimeoutMs = 30_000,
                DeadlineMs = envelope.DeadlineMs,
            };
        }

        string? executeAuthorizationId = null;
        MauiTestAgentExplorationBudgetState? explorationBudget = null;
        if (request.Execute)
        {
            if (explorationScope is null)
            {
                var executeAuthorization = await TestAgentToolSupport.AuthorizeAsync(
                    session,
                    envelope,
                    request.Action ?? string.Empty,
                    request.Selector,
                    request.Route,
                    ResolveSideEffectClass(request),
                    request.Value).ConfigureAwait(false);
                if (executeAuthorization.Value?.Ok != true ||
                    executeAuthorization.Value.AuthorizationId is null)
                {
                    return TestAgentToolSupport.BrokerFailure(envelope.RequestId, executeAuthorization);
                }
                executeAuthorizationId = executeAuthorization.Value.AuthorizationId;
            }
            else
            {
                var exploration = await TestAgentToolSupport.AuthorizeExplorationAsync(
                    session,
                    envelope,
                    request.Action ?? string.Empty,
                    explorationScope,
                    request.Selector,
                    request.Route,
                    request.DeltaX,
                    request.DeltaY,
                    request.ItemIndex).ConfigureAwait(false);
                if (exploration.Value?.Ok != true || exploration.Value.AuthorizationId is null)
                    return TestAgentToolSupport.BrokerFailure(envelope.RequestId, exploration);
                executeAuthorizationId = exploration.Value.AuthorizationId;
                explorationBudget = exploration.Value.ExplorationBudget;
            }

            // The broker verifies this authorization itself before dispatching the run.
            runRequest!.AuthorizationId = executeAuthorizationId;
            runJson = TestAgentBrokerClient.SerializeWorkflowRunRequest(
                runRequest,
                DevFlowCliJsonContext.Default.WorkflowRunStartRequest);
        }

        MauiTestAgentRequestEnvelope? appendEnvelope = null;
        TestAgentBrokerResponse<MauiTestAgentMutationAuthorizationResult>? appendAuthorization = null;
        if (request.AppendDraft)
        {
            appendEnvelope = TestAgentToolSupport.CreateSubrequestEnvelope(envelope, "draft-append");
            appendAuthorization = await TestAgentToolSupport.AuthorizeAsync(
                session,
                appendEnvelope,
                MauiTestAgentActions.DraftAppend,
                request.Selector,
                request.Route,
                sideEffectClass: "authoring",
                request.Value).ConfigureAwait(false);
            if (appendAuthorization.Value?.Ok != true ||
                appendAuthorization.Value.AuthorizationId is null)
            {
                if (executeAuthorizationId is { } unusedExecuteAuthorization)
                {
                    await CompleteAsync(
                        session,
                        unusedExecuteAuthorization,
                        "rejected",
                        request,
                        null,
                        MauiTestAgentErrorCodes.MutationGrantScopeDenied).ConfigureAwait(false);
                }
                return TestAgentToolSupport.BrokerFailure(envelope.RequestId, appendAuthorization);
            }
        }

        var brokerPort = await session.GetBrokerPortAsync().ConfigureAwait(false);
        string? runId = null;
        string? runCapability = null;
        if (request.Execute)
        {
            var started = await TestAgentBrokerClient.PostWorkflowRunJsonAsync(
                brokerPort,
                "/api/workflow-runs/start",
                runJson!).ConfigureAwait(false);
            if (!started.HasValue)
            {
                // The broker may have accepted a request before the client lost its response. The
                // grant stays consumed and the caller must not retry this idempotency key.
                await CompleteAsync(
                    session,
                    executeAuthorizationId!,
                    "unknown-completion",
                    request,
                    null,
                    MauiTestAgentErrorCodes.UnknownCompletion).ConfigureAwait(false);
                return TestAgentToolSupport.Failure(envelope.RequestId, TestAgentToolSupport.Error(
                    MauiTestAgentErrorCodes.UnknownCompletion,
                    MauiTestAgentErrorCategories.UnknownCompletion,
                    started.TransportError ?? "Action dispatch completion is unknown and will not be retried.",
                    retryable: false));
            }
            if (!TryReadStartedRun(started.Value, out runId, out runCapability))
            {
                await CompleteAsync(
                    session,
                    executeAuthorizationId!,
                    "rejected",
                    request,
                    null,
                    "workflow-run-rejected").ConfigureAwait(false);
                return TestAgentToolSupport.Failure(envelope.RequestId, TestAgentToolSupport.Error(
                    MauiTestAgentErrorCodes.InvalidRequest,
                    MauiTestAgentErrorCategories.State,
                    "The broker rejected the approved typed action before dispatch.",
                    retryable: false));
            }

            if (envelope.Correlation?.AuthoringSessionId is { Length: > 0 } sessionId && runCapability is not null)
            {
                session.RememberTestRunCapability(sessionId, runId!, runCapability);
                var bindingFailure = await BindStartedActionRunAsync(
                    envelope.RequestId,
                    runId!,
                    runCapability,
                    () => TestAgentBrokerClient.BindRunAsync(
                        brokerPort,
                        new MauiTestAgentRunBindingRequest
                        {
                            SessionId = sessionId,
                            ReadCapabilityId = envelope.ReadCapabilityId,
                            Envelope = envelope,
                            RunId = runId,
                            RunCapabilityToken = runCapability,
                        }),
                    () => CompleteAsync(
                        session,
                        executeAuthorizationId!,
                        "unknown-completion",
                        request,
                        runId,
                        "run-binding-unavailable")).ConfigureAwait(false);
                if (bindingFailure is not null)
                    return bindingFailure;
            }
        }

        MauiTestAgentSessionResult? appended = null;
        if (request.AppendDraft)
        {
            request.Envelope = appendEnvelope;
            request.AuthorizationId = appendAuthorization!.Value!.AuthorizationId;
            var append = await TestAgentBrokerClient.AppendActionAsync(brokerPort, request).ConfigureAwait(false);
            if (append.Value?.Ok != true)
            {
                await CompleteAsync(
                    session,
                    appendAuthorization.Value.AuthorizationId!,
                    "rejected",
                    request,
                    null,
                    append.Value?.Error?.Code).ConfigureAwait(false);
                if (executeAuthorizationId is { } completedExecuteAuthorization)
                {
                    await CompleteAsync(
                        session,
                        completedExecuteAuthorization,
                        request.Execute ? "queued" : "rejected",
                        request,
                        runId,
                        append.Value?.Error?.Code).ConfigureAwait(false);
                }
                return TestAgentToolSupport.BrokerFailure(envelope.RequestId, append);
            }
            appended = append.Value;
            await CompleteAsync(
                session,
                appendAuthorization.Value.AuthorizationId!,
                "completed",
                request,
                null,
                null).ConfigureAwait(false);
        }

        if (executeAuthorizationId is { } queuedExecuteAuthorizationId)
        {
            await CompleteAsync(
                session,
                queuedExecuteAuthorizationId,
                "queued",
                request,
                runId,
                null).ConfigureAwait(false);
        }
        return TestAgentToolSupport.Success(envelope.RequestId, new
        {
            action = request.Action,
            runId,
            queued = request.Execute,
            draft = appended?.Snapshot,
            explorationScope,
            explorationBudget,
            completion = request.Execute ? "queued" : "completed",
        });
    }

    internal static string ResolveSideEffectClass(MauiTestAgentActionRequest request)
        => request.Execute ? request.SideEffectClass ?? "ui" : "authoring";

    internal static Task<string?> BindStartedActionRunAsync(
        string? requestId,
        string runId,
        string runCapabilityToken,
        Func<Task<TestAgentBrokerResponse<MauiTestAgentRunBindingResult>>> bind,
        Func<Task> recordUnknownCompletion)
        => TestAgentRunTool.BindStartedRunAsync(
            requestId,
            runId,
            runCapabilityToken,
            bind,
            recordUnknownCompletion);

    private static bool TryCreateActionFlow(
        MauiTestAgentActionRequest request,
        out MauiFlow? flow,
        out MauiTestAgentError? error)
    {
        flow = null;
        error = null;
        if (request.Action is not (MauiTestAgentActions.Tap or MauiTestAgentActions.Fill or MauiTestAgentActions.Scroll or MauiTestAgentActions.Navigate or MauiTestAgentActions.Back))
        {
            error = TestAgentToolSupport.Error(
                MauiTestAgentErrorCodes.UnsupportedOperation,
                MauiTestAgentErrorCategories.Unsupported,
                "Only tap, fill, scroll, navigate, and back are available as typed test actions.",
                retryable: false);
            return false;
        }
        if (FlowSecretReference.LooksSensitive(request.Selector?.AutomationId, request.Value, request.Route))
        {
            error = TestAgentToolSupport.Error(
                MauiTestAgentErrorCodes.UntrustedPolicyInput,
                MauiTestAgentErrorCategories.Validation,
                "The restricted protocol does not carry sensitive action values.",
                retryable: false);
            return false;
        }

        var step = new FlowStep
        {
            Seq = 1,
            Action = request.Action,
            Intent = request.Envelope?.Intent,
            Target = request.Selector,
            Value = request.Action == MauiTestAgentActions.Navigate ? request.Route : request.Value,
            Args = request.Action switch
            {
                MauiTestAgentActions.Tap => new FlowStepArgs { Selector = request.Selector },
                MauiTestAgentActions.Fill => new FlowStepArgs { Selector = request.Selector, Text = request.Value },
                MauiTestAgentActions.Scroll => new FlowStepArgs
                {
                    Selector = request.Selector,
                    Dx = request.DeltaX,
                    Dy = request.DeltaY,
                    ItemIndex = request.ItemIndex,
                },
                MauiTestAgentActions.Navigate => new FlowStepArgs { Route = request.Route },
                _ => null,
            },
            Fragile = FlowSelector.IsFragile(request.Selector),
        };
        flow = new MauiFlow
        {
            Name = "approved-test-agent-action",
            Steps = [step],
        };
        var validation = MauiFlowValidator.Validate(flow);
        if (!validation.Ok)
        {
            error = TestAgentToolSupport.Error(
                MauiTestAgentErrorCodes.InvalidRequest,
                MauiTestAgentErrorCategories.Validation,
                "The typed action is structurally invalid: " + string.Join(" ", validation.Errors.Take(3)),
                retryable: false);
            flow = null;
            return false;
        }
        return true;
    }

    private static MauiTestPlan? CreateActionPlan(MauiTestPlan? source, string flowDigest)
    {
        if (source is null)
            return null;
        var node = JsonSerializer.SerializeToNode(source, MauiTestingJsonContext.Default.MauiTestPlan)!.AsObject();
        var flow = node["flow"] as JsonObject ?? new JsonObject();
        flow["digest"] = flowDigest;
        node["flow"] = flow;
        return node.Deserialize(MauiTestingJsonContext.Default.MauiTestPlan);
    }

    private static bool TryReadStartedRun(JsonElement result, out string? runId, out string? capabilityToken)
    {
        runId = null;
        capabilityToken = null;
        if (result.ValueKind != JsonValueKind.Object ||
            !result.TryGetProperty("ok", out var ok) ||
            ok.ValueKind != JsonValueKind.True ||
            !result.TryGetProperty("run", out var run) ||
            run.ValueKind != JsonValueKind.Object ||
            !run.TryGetProperty("runId", out var runIdElement) ||
            runIdElement.ValueKind != JsonValueKind.String ||
            !result.TryGetProperty("capabilityToken", out var token) ||
            token.ValueKind != JsonValueKind.String)
        {
            return false;
        }
        runId = runIdElement.GetString();
        capabilityToken = token.GetString();
        return !string.IsNullOrWhiteSpace(runId) && !string.IsNullOrWhiteSpace(capabilityToken);
    }

    private static async Task CompleteAsync(
        McpAgentSession session,
        string authorizationId,
        string outcome,
        MauiTestAgentActionRequest request,
        string? runId,
        string? errorCode)
    {
        var brokerPort = await session.GetBrokerPortAsync().ConfigureAwait(false);
        await TestAgentBrokerClient.CompleteAsync(
            brokerPort,
            new MauiTestAgentMutationCompletion
            {
                AuthorizationId = authorizationId,
                Outcome = outcome,
                ActionDigest = TestAgentToolSupport.Digest(request.Action ?? string.Empty),
                ResultDigest = runId is null ? null : TestAgentToolSupport.Digest(runId),
                RunId = runId,
                ErrorCode = errorCode,
            }).ConfigureAwait(false);
    }
}

/// <summary>Typed assertion add and verify operations for the restricted test-agent profile.</summary>
[McpServerToolType]
public sealed class TestAgentAssertionTool
{
    [McpServerTool(Name = "maui_test_assertion"),
     System.ComponentModel.Description("Add a human-approved typed assertion to a broker draft or verify one read-only against an explicit target using Testing semantics. Verification returns only bounded digests for observed UI values.")]
    public static async Task<string> Assertion(
        [System.ComponentModel.Description("MCP session injected by the server and used for the local broker and exact target")] McpAgentSession session,
        [System.ComponentModel.Description("Typed assertion operation: add or verify")] string operation,
        [System.ComponentModel.Description("Typed assertion request containing a complete envelope and a Testing FlowAssert")] MauiTestAgentAssertionRequest request)
    {
        var normalized = operation?.Trim().ToLowerInvariant();
        if (request is null || request.Envelope is null)
        {
            return TestAgentToolSupport.Failure(null, TestAgentToolSupport.Error(
                MauiTestAgentErrorCodes.InvalidRequest,
                MauiTestAgentErrorCategories.Validation,
                "A typed assertion request with an envelope is required.",
                retryable: false));
        }

        if (normalized == "add")
        {
            var authorization = await TestAgentToolSupport.AuthorizeAsync(
                session,
                request.Envelope,
                MauiTestAgentActions.Assert,
                request.Assertion?.Selector,
                sideEffectClass: "authoring",
                value: request.Assertion?.Expected).ConfigureAwait(false);
            if (authorization.Value?.Ok != true || authorization.Value.AuthorizationId is null)
                return TestAgentToolSupport.BrokerFailure(request.Envelope.RequestId, authorization);

            request.AuthorizationId = authorization.Value.AuthorizationId;
            var brokerPort = await session.GetBrokerPortAsync().ConfigureAwait(false);
            var added = await TestAgentBrokerClient.AddAssertionAsync(brokerPort, request).ConfigureAwait(false);
            await TestAgentBrokerClient.CompleteAsync(
                brokerPort,
                new MauiTestAgentMutationCompletion
                {
                    AuthorizationId = authorization.Value.AuthorizationId,
                    Outcome = added.Value?.Ok == true ? "completed" : "rejected",
                    ActionDigest = TestAgentToolSupport.Digest("assert"),
                    ErrorCode = added.Value?.Error?.Code,
                }).ConfigureAwait(false);
            return added.Value?.Ok == true
                ? TestAgentToolSupport.Success(request.Envelope.RequestId, added.Value)
                : TestAgentToolSupport.BrokerFailure(request.Envelope.RequestId, added);
        }

        if (normalized == "verify")
        {
            var sessionResult = await TestAgentToolSupport.SessionAsync(session, request.Envelope).ConfigureAwait(false);
            if (sessionResult.Value?.Ok != true || sessionResult.Value.Snapshot is null)
                return TestAgentToolSupport.BrokerFailure(request.Envelope.RequestId, sessionResult);
            if (request.Assertion is null)
            {
                return TestAgentToolSupport.Failure(request.Envelope.RequestId, TestAgentToolSupport.Error(
                    MauiTestAgentErrorCodes.InvalidRequest,
                    MauiTestAgentErrorCategories.Validation,
                    "verify requires a typed assertion.",
                    retryable: false));
            }

            var target = await TestAgentToolSupport.ResolveTargetAsync(
                session,
                sessionResult.Value.Snapshot.Target).ConfigureAwait(false);
            if (target.Error is not null)
                return TestAgentToolSupport.Failure(request.Envelope.RequestId, target.Error);
            using var agent = await session.GetTestAgentClientAsync(sessionResult.Value.Snapshot.Target).ConfigureAwait(false);
            var verified = await MauiFlowAssertionVerifier.VerifyAsync(
                new AgentClientMauiFlowDriver(agent),
                request.Assertion,
                pollTries: 10,
                pollGapMs: 250).ConfigureAwait(false);
            return TestAgentToolSupport.Success(
                request.Envelope.RequestId,
                new
                {
                    passed = verified.Passed,
                    skipped = verified.Skipped,
                    observationOnly = verified.ObservationOnly,
                    matchCount = verified.MatchCount,
                    quality = verified.Quality,
                    actual = verified.Actual is null ? null : new
                    {
                        length = verified.Actual.Length,
                        digest = TestAgentToolSupport.Digest(verified.Actual),
                    },
                    error = verified.Error,
                },
                [TestAgentToolSupport.Untrusted("ui-property")]);
        }

        return TestAgentToolSupport.Failure(request.Envelope.RequestId, TestAgentToolSupport.Error(
            MauiTestAgentErrorCodes.UnsupportedOperation,
            MauiTestAgentErrorCategories.Unsupported,
            "Supported assertion operations are add and verify.",
            retryable: false));
    }
}
