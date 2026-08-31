using System.ComponentModel;
using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.Cli.DevFlow.Mcp;
using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.DevFlow.Testing;
using ModelContextProtocol.Server;

namespace Microsoft.Maui.Cli.DevFlow.Mcp.Tools;

/// <summary>Bounded, read-only trace projection for the restricted test-agent profile.</summary>
[McpServerToolType]
public sealed class TestAgentTraceTool
{
    [McpServerTool(Name = "maui_test_trace"),
     System.ComponentModel.Description("Read a bounded safe projection of one explicitly named broker-owned local run. Name the runId that maui_test_run start returned for this session, together with its runCapabilityToken when the MCP process has restarted; do not create or migrate an authoring draft to discover a run. It never returns raw screenshots, source, logs, network bodies, file content, or imported artifact content.")]
    public static async Task<string> Trace(
        [System.ComponentModel.Description("MCP session injected by the server and used for the local broker")] McpAgentSession session,
        [System.ComponentModel.Description("Typed trace request with envelope, runId, and target/run-bound capability token when needed")] MauiTestAgentTraceRequest request)
    {
        if (request?.Envelope is null)
        {
            return TestAgentToolSupport.Failure(null, TestAgentToolSupport.Error(
                MauiTestAgentErrorCodes.InvalidRequest,
                MauiTestAgentErrorCategories.Validation,
                "A trace request with a protocol envelope is required.",
                retryable: false));
        }

        var sessionResult = await TestAgentToolSupport.SessionAsync(session, request.Envelope).ConfigureAwait(false);
        if (sessionResult.Value?.Ok != true)
            return TestAgentToolSupport.BrokerFailure(request.Envelope.RequestId, sessionResult);
        var access = await GetBoundRunAccessAsync(
            session,
            request.Envelope,
            request.RunId,
            request.RunCapabilityToken,
            "trace").ConfigureAwait(false);
        if (access.Error is not null)
            return TestAgentToolSupport.Failure(request.Envelope.RequestId, access.Error);

        var brokerPort = await session.GetBrokerPortAsync().ConfigureAwait(false);
        var result = await TestAgentBrokerClient.PostWorkflowRunAsync(
            brokerPort,
            $"/api/workflow-runs/{Uri.EscapeDataString(access.RunId!)}/status",
            new WorkflowRunAccessRequest { CapabilityToken = access.CapabilityToken },
            DevFlowCliJsonContext.Default.WorkflowRunAccessRequest).ConfigureAwait(false);
        if (!result.HasValue)
        {
            return TestAgentToolSupport.Failure(request.Envelope.RequestId, TestAgentToolSupport.Error(
                MauiTestAgentErrorCodes.TargetUnavailable,
                MauiTestAgentErrorCategories.Transport,
                result.TransportError ?? "The broker did not return the requested trace projection.",
                retryable: true));
        }

        return TestAgentToolSupport.Success(request.Envelope.RequestId, new
        {
            run = TestAgentRunTool.SafeRunProjection(result.Value),
            diagnostic = TestAgentFailureTool.ReadDiagnostic(result.Value),
            trust = new
            {
                state = "locally-produced",
                importedArtifactContentIncluded = false,
                executableAuthority = false,
                proposalAuthority = false,
            },
            omissions = new[]
            {
                "raw-screenshots", "raw-source", "raw-logs", "raw-network-bodies", "raw-files", "imported-artifact-content",
            },
        });
    }

    internal static async Task<TestAgentBoundRunAccess> GetBoundRunAccessAsync(
        McpAgentSession session,
        MauiTestAgentRequestEnvelope? envelope,
        string? requestedRunId,
        string? requestedCapabilityToken,
        string operation)
    {
        var expectedRunId = envelope?.Correlation?.RunId;
        var runId = requestedRunId ?? expectedRunId;
        if (string.IsNullOrWhiteSpace(runId))
        {
            return TestAgentBoundRunAccess.Failure(TestAgentToolSupport.Error(
                MauiTestAgentErrorCodes.InvalidRequest,
                MauiTestAgentErrorCategories.Validation,
                $"{operation} requires envelope.correlation.runId.",
                retryable: false));
        }
        if (!string.Equals(expectedRunId, runId, StringComparison.Ordinal))
        {
            return TestAgentBoundRunAccess.Failure(TestAgentToolSupport.Error(
                MauiTestAgentErrorCodes.MutationGrantScopeDenied,
                MauiTestAgentErrorCategories.Authorization,
                $"{operation} must use the exact runId bound in the authoring-session envelope.",
                retryable: false));
        }
        var capabilityToken = requestedCapabilityToken;
        if (string.IsNullOrWhiteSpace(capabilityToken) &&
            envelope?.Correlation?.AuthoringSessionId is { Length: > 0 } sessionId)
        {
            session.TryGetTestRunCapability(sessionId, runId, out capabilityToken);
        }
        if (string.IsNullOrWhiteSpace(capabilityToken) ||
            envelope?.Correlation?.AuthoringSessionId is not { Length: > 0 } authoringSessionId)
        {
            return TestAgentBoundRunAccess.Failure(TestAgentToolSupport.Error(
                MauiTestAgentErrorCodes.ReadCapabilityRequired,
                MauiTestAgentErrorCategories.Authorization,
                "A target/run-bound capability token from run start is required.",
                retryable: false));
        }

        var brokerPort = await session.GetBrokerPortAsync().ConfigureAwait(false);
        var binding = await TestAgentBrokerClient.ValidateRunBindingAsync(
            brokerPort,
            new MauiTestAgentRunBindingRequest
            {
                SessionId = authoringSessionId,
                ReadCapabilityId = envelope.ReadCapabilityId,
                Envelope = envelope,
                RunId = runId,
                RunCapabilityToken = capabilityToken,
            }).ConfigureAwait(false);
        if (binding.Value?.Ok == true)
            return TestAgentBoundRunAccess.Success(runId, capabilityToken);

        return TestAgentBoundRunAccess.Failure(
            binding.Value?.Error ?? TestAgentToolSupport.Error(
                MauiTestAgentErrorCodes.TargetUnavailable,
                binding.TransportError is null
                    ? MauiTestAgentErrorCategories.Transport
                    : MauiTestAgentErrorCategories.Target,
                binding.TransportError ?? "The broker could not validate the bound run capability.",
                retryable: binding.StatusCode == 0));
    }
}

/// <summary>Deterministic, value-free failure classification for a broker-owned run.</summary>
[McpServerToolType]
public sealed class TestAgentFailureTool
{
    [McpServerTool(Name = "maui_test_failure"),
     System.ComponentModel.Description("Return the canonical classification, plain-language explanation, next safe action, and bounded failed-step facts for one explicitly named broker-owned run. Name the runId that maui_test_run start returned for this session, together with its runCapabilityToken when the MCP process has restarted; do not create or migrate an authoring draft to discover a run. It does not expose raw app logs, user text, network content, screenshots, source, or model prompts.")]
    public static async Task<string> Failure(
        [System.ComponentModel.Description("MCP session injected by the server and used for the local broker")] McpAgentSession session,
        [System.ComponentModel.Description("Typed trace-shaped request with envelope, runId, and target/run-bound capability token")] MauiTestAgentTraceRequest request)
    {
        if (request?.Envelope is null)
        {
            return TestAgentToolSupport.Failure(null, TestAgentToolSupport.Error(
                MauiTestAgentErrorCodes.InvalidRequest,
                MauiTestAgentErrorCategories.Validation,
                "A failure request with a protocol envelope is required.",
                retryable: false));
        }

        var sessionResult = await TestAgentToolSupport.SessionAsync(session, request.Envelope).ConfigureAwait(false);
        if (sessionResult.Value?.Ok != true)
            return TestAgentToolSupport.BrokerFailure(request.Envelope.RequestId, sessionResult);
        var access = await TestAgentTraceTool.GetBoundRunAccessAsync(
            session,
            request.Envelope,
            request.RunId,
            request.RunCapabilityToken,
            "failure").ConfigureAwait(false);
        if (access.Error is not null)
            return TestAgentToolSupport.Failure(request.Envelope.RequestId, access.Error);

        var brokerPort = await session.GetBrokerPortAsync().ConfigureAwait(false);
        var result = await TestAgentBrokerClient.PostWorkflowRunAsync(
            brokerPort,
            $"/api/workflow-runs/{Uri.EscapeDataString(access.RunId!)}/status",
            new WorkflowRunAccessRequest { CapabilityToken = access.CapabilityToken },
            DevFlowCliJsonContext.Default.WorkflowRunAccessRequest).ConfigureAwait(false);
        if (!result.HasValue)
        {
            return TestAgentToolSupport.Failure(request.Envelope.RequestId, TestAgentToolSupport.Error(
                MauiTestAgentErrorCodes.TargetUnavailable,
                MauiTestAgentErrorCategories.Transport,
                result.TransportError ?? "The broker did not return failure facts.",
                retryable: true));
        }

        var diagnostic = ReadDiagnostic(result.Value);
        return TestAgentToolSupport.Success(request.Envelope.RequestId, new
        {
            runId = access.RunId,
            diagnostic.Classification,
            diagnostic.Facts,
            diagnostic.FailedStep,
            diagnostic.PlainLanguage,
            diagnostic.NextSafeAction,
            diagnostic.SelectorRepair,
        });
    }

    internal static TestAgentFailureDiagnostic ReadDiagnostic(JsonElement response)
    {
        var run = Object(response, "run");
        var report = Object(run, "report");
        var failure = Object(report, "failure");
        var failedStepId = String(failure, "stepId");
        var failedStep = Array(report, "steps")
            .FirstOrDefault(step =>
                string.Equals(String(step, "stepId"), failedStepId, StringComparison.Ordinal));
        var facts = ReadFailureFacts(run, report, failure, failedStep);
        var classification = MauiFlowFailureClassifier.Classify(facts);
        var projection = ReadFailedStep(failedStep);
        var admission = Object(run, "admission");
        var nonReplayable = string.Equals(
            String(admission, "sideEffectPolicy"),
            MauiFlowSideEffectPolicies.NonReplayable,
            StringComparison.Ordinal);
        var replayAdviceAllowed = !nonReplayable &&
            Bool(admission, "ordinaryReplayAllowed") != false;
        var repairAllowed = !nonReplayable &&
            Bool(admission, "repairEligibility") != false &&
            classification.RepairEligible;
        // Two independent authorities can each withhold repair, and conflating them sends a reader
        // to the wrong place. The classifier judges the symptom; replay safety judges whether this
        // run is a sound basis to repair from. Naming which one refused, and the admission's own
        // reason codes, is the difference between an actionable answer and a dead end.
        var admissionRefused = !nonReplayable && Bool(admission, "repairEligibility") == false;
        var admissionReasons = Array(admission, "reasons")
            .Select(reason => String(reason, "code"))
            .OfType<string>()
            .Where(static code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.Ordinal)
            .Take(6)
            .ToList();
        var explanation = Explain(classification, projection);
        var nextAction = NextSafeAction(classification, projection, replayAdviceAllowed);

        // A passed run has no failure to classify. Classifying the empty failure facts anyway
        // produced "The failed step failed with canonical code 'infrastructure'" on a clean pass,
        // which reads as a defect report for a healthy run and would send a reader hunting for a
        // problem that does not exist.
        if (string.Equals(facts.TerminalOutcome, MauiFlowRunOutcomes.Passed, StringComparison.Ordinal) &&
            string.IsNullOrWhiteSpace(facts.FailureClass))
        {
            return new TestAgentFailureDiagnostic
            {
                Facts = new TestAgentFailureFactsProjection
                {
                    TerminalOutcome = facts.TerminalOutcome,
                    FailureClass = null,
                    CompletionCertain = facts.CompletionCertain,
                    BeforeDispatch = facts.BeforeDispatch,
                },
                FailedStep = null,
                PlainLanguage = "The run passed; there is no failure to diagnose.",
                NextSafeAction = nonReplayable
                    ? "No further action is authorized: the retained admission marks this run non-replayable."
                    : "No action is required.",
                SelectorRepair = new TestAgentSelectorRepairAdvice
                {
                    Status = "ineligible",
                    Eligible = false,
                    ProposalRecommended = false,
                    Reason = "The run passed, so there is nothing to repair.",
                },
            };
        }

        return new TestAgentFailureDiagnostic
        {
            Classification = new TestAgentFailureClassification
            {
                Class = classification.FailureClass,
                Code = classification.Code,
                Category = classification.Category,
                Phase = classification.Phase,
                Retryable = classification.Retryable,
                RepairEligible = repairAllowed,
            },
            Facts = new TestAgentFailureFactsProjection
            {
                TerminalOutcome = facts.TerminalOutcome,
                FailureClass = facts.FailureClass,
                CompletionCertain = facts.CompletionCertain,
                BeforeDispatch = facts.BeforeDispatch,
                CheckpointVerified = facts.CheckpointVerified,
                CheckpointMatches = facts.CheckpointMatches,
                RouteMatches = facts.RouteMatches,
            },
            FailedStep = projection,
            PlainLanguage = explanation,
            NextSafeAction = nextAction,
            SelectorRepair = new TestAgentSelectorRepairAdvice
            {
                Status = repairAllowed ? "eligible" : "ineligible",
                Eligible = repairAllowed,
                ProposalRecommended = repairAllowed,
                Reason = repairAllowed
                    ? "The failure is a verified pre-dispatch missing-selector failure, so one inert selector-only proposal may be prepared for human review."
                    : nonReplayable
                        ? "The retained admission marks this run non-replayable. Do not replay it or create a repair proposal."
                        : admissionRefused && classification.RepairEligible
                            ? "The failure shape is repairable, but this run is not an admissible basis to repair from" +
                                (admissionReasons.Count > 0
                                    ? $": {string.Join(", ", admissionReasons)}."
                                    : ".") +
                                " Repairing from it would assert a business outcome this run never established. " +
                                "Re-run the flow on a host that produces the missing admission evidence, then diagnose that run."
                            : "This failure is not a verified pre-dispatch missing-selector failure. Do not create a selector repair proposal.",
                ClassifierEligible = classification.RepairEligible,
                AdmissionEligible = !admissionRefused && !nonReplayable,
                AdmissionReasonCodes = admissionReasons,
            },
        };
    }

    private static MauiFlowFailureFacts ReadFailureFacts(
        JsonElement run,
        JsonElement report,
        JsonElement failure,
        JsonElement failedStep)
    {
        var terminalState = String(run, "state");
        var outcome = Object(report, "outcome");
        var dispatch = Object(failedStep, "dispatch");
        var expectedCheckpoint = Object(failedStep, "expectedCheckpoint");
        var observedCheckpoint = Object(failedStep, "observedCheckpoint");
        var failureClass = String(failure, "class");
        var completionCertainty = String(dispatch, "completionCertainty") ??
            String(failedStep, "completionCertainty");
        var phase = String(failure, "phase");
        bool? checkpointVerified = expectedCheckpoint.ValueKind == JsonValueKind.Object
            ? observedCheckpoint.ValueKind == JsonValueKind.Object
            : null;
        bool? checkpointMatches = checkpointVerified == true
            ? CheckpointMatches(expectedCheckpoint, observedCheckpoint)
            : checkpointVerified == false
                ? false
                : null;
        var expectedRoute = String(expectedCheckpoint, "route");
        bool? routeMatches = string.IsNullOrWhiteSpace(expectedRoute)
            ? null
            : observedCheckpoint.ValueKind == JsonValueKind.Object &&
              string.Equals(expectedRoute, String(observedCheckpoint, "route"), StringComparison.Ordinal);
        return new MauiFlowFailureFacts
        {
            TerminalOutcome = String(outcome, "status") ?? terminalState,
            FailureClass = failureClass,
            LegacyFailureKind = String(failure, "legacyKind"),
            FlowInvalid = failureClass == MauiFlowFailureClasses.FlowInvalid,
            SchemaUnsupported = failureClass == MauiFlowFailureClasses.SchemaUnsupported,
            CapabilityMissing = failureClass == MauiFlowFailureClasses.CapabilityMissing,
            ResetFailed = failureClass == MauiFlowFailureClasses.ResetFailed,
            ActionRejected = string.IsNullOrWhiteSpace(failureClass) &&
                string.Equals(String(dispatch, "acknowledgementState"), "rejected", StringComparison.Ordinal),
            AgentDisconnected = failureClass == MauiFlowFailureClasses.AgentDisconnected,
            TransportFailure = failureClass == MauiFlowFailureClasses.Transport,
            CompletionCertain = string.Equals(completionCertainty, "unknown", StringComparison.Ordinal)
                ? false
                : !string.IsNullOrWhiteSpace(completionCertainty) ||
                  IsTerminalState(terminalState),
            BeforeDispatch = phase is "validation" or "preflight" or "resolution" or "actionability",
            CheckpointVerified = checkpointVerified,
            CheckpointMatches = checkpointMatches,
            RouteMatches = routeMatches,
        };
    }

    private static TestAgentFailedStepProjection? ReadFailedStep(JsonElement step)
    {
        if (step.ValueKind != JsonValueKind.Object)
            return null;

        var resolution = Object(step, "targetResolution");
        var dispatch = Object(step, "dispatch");
        var actionability = Array(step, "actionability");
        bool? visible = null;
        bool? enabled = null;
        bool? hasBounds = null;
        bool? boundsStable = null;
        foreach (var observation in actionability)
        {
            visible = Bool(observation, "visible") ?? visible;
            enabled = Bool(observation, "enabled") ?? enabled;
            hasBounds = Bool(observation, "hasBounds") ?? hasBounds;
            boundsStable = Bool(observation, "boundsStable") ?? boundsStable;
        }

        return new TestAgentFailedStepProjection
        {
            Sequence = Number(step, "sequence"),
            StepId = String(step, "stepId"),
            Action = String(step, "action"),
            SelectorKind = SelectorKind(Object(step, "selector")),
            SelectorQuality = String(Object(step, "selector"), "quality"),
            SelectorType = String(Object(Object(step, "selector"), "typeIndex"), "type"),
            TargetResolutionStatus = String(resolution, "status"),
            MatchCount = Number(resolution, "matchCount"),
            Visible = visible,
            Enabled = enabled,
            HasBounds = hasBounds,
            BoundsStable = boundsStable,
            AcknowledgementState = String(dispatch, "acknowledgementState") ??
                String(step, "acknowledgementState"),
            CompletionCertainty = String(dispatch, "completionCertainty") ??
                String(step, "completionCertainty"),
        };
    }

    private static string Explain(
        MauiFlowFailureClassification classification,
        TestAgentFailedStepProjection? step)
    {
        var prefix = step?.Sequence is { } sequence
            ? $"Step {sequence} ({step.Action ?? "action"})"
            : "The failed step";
        return classification.Code switch
        {
            MauiFlowFailureClasses.LocatorNotFound =>
                $"{prefix} could not find its control before any app action was sent.",
            MauiFlowFailureClasses.LocatorAmbiguous =>
                $"{prefix} matched more than one control, so DevFlow refused to choose one.",
            MauiFlowFailureClasses.DriveFailed
                when step?.FragileSelector == true =>
                $"{prefix} resolved a fragile {step.SelectorKind ?? "selector"} to one {step.SelectorType ?? "target"}, but that target rejected the action. The recording likely captured the wrong control; automatic selector repair is not eligible after dispatch.",
            MauiFlowFailureClasses.DriveFailed
                when step?.MatchCount == 1 &&
                     step.Visible != false &&
                     step.Enabled != false &&
                     string.Equals(step.AcknowledgementState, "rejected", StringComparison.Ordinal) =>
                $"{prefix} found exactly one visible and enabled control, but the app rejected the command during dispatch. This is an action or driver failure, not a selector or route failure.",
            MauiFlowFailureClasses.ActionRejected =>
                $"{prefix} was rejected before the requested app action could complete.",
            MauiFlowFailureClasses.RouteStateDrift =>
                "The app route did not match the route required by the saved test.",
            MauiFlowFailureClasses.PreconditionUnsatisfied =>
                "The app state did not match the saved test preconditions.",
            MauiFlowFailureClasses.AssertionFailed =>
                $"{prefix} ran, but its expected result was not observed.",
            MauiFlowFailureClasses.Transport or MauiFlowFailureClasses.AgentDisconnected =>
                "DevFlow lost reliable communication with the app while the test was running.",
            MauiFlowFailureClasses.UnknownCompletion =>
                "DevFlow cannot prove whether the in-flight app action completed.",
            _ =>
                $"{prefix} failed with canonical code '{classification.Code}'.",
        };
    }

    private static string NextSafeAction(
        MauiFlowFailureClassification classification,
        TestAgentFailedStepProjection? step,
        bool replayAdviceAllowed)
    {
        if (!replayAdviceAllowed)
        {
            return "The retained replay admission does not allow another replay. Inspect the current app state manually; do not retry, continue, or create a selector repair.";
        }

        return classification.Code switch
        {
            MauiFlowFailureClasses.LocatorNotFound when classification.RepairEligible =>
                "Review one inert selector-only proposal. Validate it against the live app before any human approval or apply action.",
            MauiFlowFailureClasses.LocatorNotFound or MauiFlowFailureClasses.LocatorAmbiguous =>
                "Read the failed step from maui_test_failure, look at the live app, and have the user name the intended control. Do not let an agent guess.",
            MauiFlowFailureClasses.DriveFailed when step?.FragileSelector == true =>
                "Remove or replace the failed step in the committed flow and bind it to a durable app-owned selector. Have the user check the test before it is saved again. Do not create an automatic selector repair.",
            MauiFlowFailureClasses.DriveFailed
                when step?.MatchCount == 1 &&
                     string.Equals(step.AcknowledgementState, "rejected", StringComparison.Ordinal) =>
                "Read the failed step from maui_test_failure and verify the same action manually in the app. If manual interaction works, retry once after reconnecting DevFlow; if it fails again, investigate the platform action driver. Do not create a selector repair.",
            MauiFlowFailureClasses.RouteStateDrift or MauiFlowFailureClasses.PreconditionUnsatisfied =>
                "Restore the saved starting state, then run a fresh test. Do not repair the selector.",
            MauiFlowFailureClasses.Transport or MauiFlowFailureClasses.AgentDisconnected =>
                "Reconnect the exact app instance and run a fresh test. Do not reuse an uncertain result for repair.",
            MauiFlowFailureClasses.UnknownCompletion =>
                "Inspect the app state before doing anything else. Do not retry, repair, or continue until completion is known.",
            _ =>
                "Read the failed step from maui_test_failure and address the reported failure class. Do not create a selector repair unless selectorRepair.status is 'eligible'.",
        };
    }

    private static string? SelectorKind(JsonElement selector)
    {
        var declared = String(selector, "selectorKind");
        if (!string.IsNullOrWhiteSpace(declared))
            return declared;
        if (!string.IsNullOrWhiteSpace(String(selector, "automationId")))
            return "automationId";
        if (Object(selector, "typeIndex").ValueKind == JsonValueKind.Object)
            return "typeIndex";
        if (!string.IsNullOrWhiteSpace(String(selector, "text")))
            return "text";
        if (!string.IsNullOrWhiteSpace(String(selector, "id")))
            return "runtimeId";
        return null;
    }

    private static bool CheckpointMatches(JsonElement expected, JsonElement observed)
    {
        foreach (var name in new[]
                 {
                     "appBuildFingerprint", "agentInstanceId", "seedFingerprint",
                     "backendStateFingerprint", "route", "window", "modal",
                 })
        {
            var expectedValue = String(expected, name);
            if (!string.IsNullOrWhiteSpace(expectedValue) &&
                !string.Equals(expectedValue, String(observed, name), StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsTerminalState(string? state)
        => state is not null &&
           state is not "queued" and not "acquiring-lease" and not "preparing" and not "running";

    private static JsonElement Object(JsonElement source, string name)
        => source.ValueKind == JsonValueKind.Object &&
           source.TryGetProperty(name, out var value) &&
           value.ValueKind == JsonValueKind.Object
            ? value
            : default;

    private static IReadOnlyList<JsonElement> Array(JsonElement source, string name)
        => source.ValueKind == JsonValueKind.Object &&
           source.TryGetProperty(name, out var value) &&
           value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(static item => item.Clone()).ToArray()
            : [];

    private static string? String(JsonElement source, string name)
        => source.ValueKind == JsonValueKind.Object &&
           source.TryGetProperty(name, out var value) &&
           value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? Number(JsonElement source, string name)
        => source.ValueKind == JsonValueKind.Object &&
           source.TryGetProperty(name, out var value) &&
           value.TryGetInt32(out var number)
            ? number
            : null;

    private static bool? Bool(JsonElement source, string name)
        => source.ValueKind == JsonValueKind.Object &&
           source.TryGetProperty(name, out var value) &&
           value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
}

internal sealed class TestAgentBoundRunAccess
{
    public string? RunId { get; private init; }
    public string? CapabilityToken { get; private init; }
    public MauiTestAgentError? Error { get; private init; }

    public static TestAgentBoundRunAccess Success(string runId, string capabilityToken)
        => new() { RunId = runId, CapabilityToken = capabilityToken };

    public static TestAgentBoundRunAccess Failure(MauiTestAgentError error)
        => new() { Error = error };
}

internal sealed class TestAgentFailureDiagnostic
{
    public TestAgentFailureClassification Classification { get; init; } = new();
    public TestAgentFailureFactsProjection Facts { get; init; } = new();
    public TestAgentFailedStepProjection? FailedStep { get; init; }
    public string PlainLanguage { get; init; } = "";
    public string NextSafeAction { get; init; } = "";
    public TestAgentSelectorRepairAdvice SelectorRepair { get; init; } = new();
}

internal sealed class TestAgentFailureClassification
{
    public string Class { get; init; } = MauiFlowFailureClasses.Infrastructure;
    public string Code { get; init; } = MauiFlowFailureClasses.Infrastructure;
    public string Category { get; init; } = "infrastructure";
    public string Phase { get; init; } = "execution";
    public bool Retryable { get; init; }
    public bool RepairEligible { get; init; }
}

internal sealed class TestAgentFailureFactsProjection
{
    public string? TerminalOutcome { get; init; }
    public string? FailureClass { get; init; }
    public bool? CompletionCertain { get; init; }
    public bool? BeforeDispatch { get; init; }
    public bool? CheckpointVerified { get; init; }
    public bool? CheckpointMatches { get; init; }
    public bool? RouteMatches { get; init; }
}

internal sealed class TestAgentFailedStepProjection
{
    public int? Sequence { get; init; }
    public string? StepId { get; init; }
    public string? Action { get; init; }
    public string? SelectorKind { get; init; }
    public string? SelectorQuality { get; init; }
    public string? SelectorType { get; init; }
    public bool FragileSelector => string.Equals(SelectorQuality, "fragile", StringComparison.Ordinal) ||
        SelectorKind is "typeIndex" or "text" or "runtimeId";
    public string? TargetResolutionStatus { get; init; }
    public int? MatchCount { get; init; }
    public bool? Visible { get; init; }
    public bool? Enabled { get; init; }
    public bool? HasBounds { get; init; }
    public bool? BoundsStable { get; init; }
    public string? AcknowledgementState { get; init; }
    public string? CompletionCertainty { get; init; }
}

internal sealed class TestAgentSelectorRepairAdvice
{
    public string Status { get; init; } = "ineligible";
    public bool Eligible { get; init; }
    public bool ProposalRecommended { get; init; }
    public string Reason { get; init; } = "";

    /// <summary>Whether the failure classifier judged the symptom itself repairable.</summary>
    public bool ClassifierEligible { get; init; }

    /// <summary>Whether the run's retained admission permits repairing from this run.</summary>
    public bool AdmissionEligible { get; init; }

    /// <summary>The admission's own blocking reason codes, so a refusal names its cause.</summary>
    public IReadOnlyList<string> AdmissionReasonCodes { get; init; } = [];
}

/// <summary>Inert patch proposal storage only; no agent path can apply or approve a patch.</summary>
[McpServerToolType]
public sealed class TestAgentPatchTool
{
    [McpServerTool(Name = "maui_test_patch"),
     System.ComponentModel.Description("Store, preview, or reject an inert structured flow patch proposal. Apply and approve are forbidden in this profile; this tool never edits source, flow files, or an app.")]
    public static async Task<string> Patch(
        [System.ComponentModel.Description("MCP session injected by the server and used only for the local broker")] McpAgentSession session,
        [System.ComponentModel.Description("Typed inert patch request with operation proposal, preview, reject, apply, approve, or rollback. Apply, approve, and rollback are rejected. Call preview with only proposal.sourceStepId and proposal.proposedSelector to be told the canonical patchDigest, then send proposal with that digest.")] McpTestAgentPatchRequest request)
    {
        MauiTestAgentPatchRequest? typed = null;
        string? readError = null;
        if (request is null || !request.TryToTyped(out typed, out readError))
        {
            return TestAgentToolSupport.Failure(request?.Envelope?.RequestId, TestAgentToolSupport.Error(
                MauiTestAgentErrorCodes.InvalidRequest,
                MauiTestAgentErrorCategories.Validation,
                readError ?? "A typed inert patch request is required.",
                retryable: false));
        }

        if (typed?.Envelope is null)
        {
            return TestAgentToolSupport.Failure(null, TestAgentToolSupport.Error(
                MauiTestAgentErrorCodes.InvalidRequest,
                MauiTestAgentErrorCategories.Validation,
                "An inert patch request with a protocol envelope is required.",
                retryable: false));
        }

        var brokerPort = await session.GetBrokerPortAsync().ConfigureAwait(false);
        var result = await TestAgentBrokerClient.PatchAsync(brokerPort, typed).ConfigureAwait(false);
        return result.Value?.Ok == true
            ? TestAgentToolSupport.Success(typed.Envelope.RequestId, result.Value)
            : TestAgentToolSupport.BrokerFailure(typed.Envelope.RequestId, result);
    }
}

/// <summary>Deterministic selector-health findings without candidate application or source proposals.</summary>
[McpServerToolType]
public sealed class TestAgentImprovementsTool
{
    /// <summary>
    /// The element cap the Inspector's selector-health route uses. Sharing it keeps the two hosts
    /// from disagreeing about whether the same app was fully observed.
    /// </summary>
    private const int LiveTreeElementLimit = 2_000;

    [McpServerTool(Name = "maui_test_improvements"),
     System.ComponentModel.Description("Return deterministic selector-health findings and a bounded value-free live selector projection for the current broker draft. Read-only selector discovery needs no mutation grant; this tool never returns UI text/values or generates, applies, or approves changes.")]
    public static async Task<string> Improvements(
        [System.ComponentModel.Description("MCP session injected by the server and used for the local broker and exact target")] McpAgentSession session,
        [System.ComponentModel.Description("Complete protocol envelope with explicit target and read capability")] MauiTestAgentRequestEnvelope envelope)
    {
        var snapshotResult = await TestAgentToolSupport.SessionAsync(session, envelope).ConfigureAwait(false);
        if (snapshotResult.Value?.Ok != true || snapshotResult.Value.Snapshot is null)
            return TestAgentToolSupport.BrokerFailure(envelope?.RequestId, snapshotResult);

        var target = await TestAgentToolSupport.ResolveTargetAsync(
            session,
            snapshotResult.Value.Snapshot.Target).ConfigureAwait(false);
        if (target.Error is not null)
            return TestAgentToolSupport.Failure(envelope.RequestId, target.Error);

        using var agent = await session.GetTestAgentClientAsync(snapshotResult.Value.Snapshot.Target).ConfigureAwait(false);
        // The whole tree, not a shallow slice. A depth cap of 8 sat below the depth of a templated
        // collection row — the exact place an AutomationId is most likely to be repeated — so the
        // duplicate-id diagnostic could not see the duplicates and the coverage summary then
        // described a selector matching three controls as a durable target.
        var tree = await agent.GetTreeAsync().ConfigureAwait(false);
        var observation = tree.Count > 0
            ? MauiSelectorObservationFactory.Create(tree[0], tree, new MauiSelectorObservationContext
            {
                AppBuild = target.State?.AppBuildFingerprint,
                Platform = target.Registration?.Platform,
                Route = target.State?.Route,
                Window = target.State?.Window,
            })
            : null;
        // Completeness is a claim about coverage, so it has to mean "nothing was dropped" rather
        // than "something came back"; a truncated observation must not license a coverage verdict.
        var liveTreeTruncated = observation is not null && observation.Elements.Count > LiveTreeElementLimit;
        var input = new MauiSelectorHealthAnalysisInput
        {
            Flow = snapshotResult.Value.Snapshot.Flow,
            Plan = snapshotResult.Value.Snapshot.Plan,
            LiveTreeComplete = observation is not null && !liveTreeTruncated,
            Context = new MauiSelectorObservationContext
            {
                AppBuild = target.State?.AppBuildFingerprint,
                Platform = target.Registration?.Platform,
                Route = target.State?.Route,
                Window = target.State?.Window,
            },
        };
        IReadOnlyList<MauiSelectorObservationElement> observedElements = [];
        if (observation is not null)
        {
            // The factory intentionally drops ElementInfo.Text and Value before the analyzer sees
            // anything, so UI content cannot become a policy input or tool output.
            observedElements = observation.Elements.Take(LiveTreeElementLimit).ToList();
            input.LiveElements = observedElements.ToList();
        }

        var analysis = MauiSelectorHealthAnalyzer.Analyze(input);
        var selectorProjection = BuildSelectorProjection(observedElements);
        return TestAgentToolSupport.Success(
            envelope.RequestId,
            new
            {
                ruleVersion = analysis.RuleVersion,
                findings = analysis.Findings,
                coverage = analysis.Coverage,
                liveSelectors = new
                {
                    available = observation is not null,
                    complete = input.LiveTreeComplete,
                    observedElementCount = observedElements.Count,
                    returned = selectorProjection.Count,
                    truncated = observedElements.Count > selectorProjection.Count,
                    identities = selectorProjection,
                    omissions = new[] { "text", "value", "runtime-id", "source", "bounds", "screenshots" },
                },
                repairApply = false,
                sourceProposal = false,
            },
            tree.Count > 0 ? [TestAgentToolSupport.Untrusted("ui-tree")] : []);
    }

    internal static IReadOnlyList<TestAgentSelectorIdentityProjection> BuildSelectorProjection(
        IReadOnlyList<MauiSelectorObservationElement> elements,
        int maximum = 128)
    {
        if (elements.Count == 0 || maximum <= 0)
            return [];

        var automationIdCounts = elements
            .Where(static element => !string.IsNullOrWhiteSpace(element.AutomationId))
            .GroupBy(static element => element.AutomationId!, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);
        var scopedCounts = elements
            .Where(static element =>
                !string.IsNullOrWhiteSpace(element.AutomationId) &&
                IsOpaqueStableItemKey(element.StableItemKey) &&
                !string.IsNullOrWhiteSpace(element.CollectionScope))
            .Select(static element => MauiTestAgentSelectorScopeKey.ScopedItem(
                element.CollectionScope!,
                element.StableItemKey!,
                element.AutomationId!))
            .GroupBy(static key => key, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);
        var typeIndices = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new List<TestAgentSelectorIdentityProjection>(Math.Min(maximum, elements.Count));

        foreach (var element in elements.Where(static element => element.IsVisible != false))
        {
            var type = string.IsNullOrWhiteSpace(element.Type) ? element.FullType : element.Type;
            if (string.IsNullOrWhiteSpace(type))
                continue;
            typeIndices.TryGetValue(type, out var typeIndex);
            typeIndices[type] = typeIndex + 1;

            var automationId = string.IsNullOrWhiteSpace(element.AutomationId) ? null : element.AutomationId;
            var scoped = automationId is not null &&
                IsOpaqueStableItemKey(element.StableItemKey) &&
                !string.IsNullOrWhiteSpace(element.CollectionScope);
            var scopedKey = scoped
                ? MauiTestAgentSelectorScopeKey.ScopedItem(
                    element.CollectionScope!,
                    element.StableItemKey!,
                    automationId!)
                : null;
            int? matchCount = automationId is null
                ? null
                : scoped
                    ? scopedCounts[scopedKey!]
                    : automationIdCounts[automationId];
            result.Add(new TestAgentSelectorIdentityProjection
            {
                Type = type,
                Role = element.Role,
                AutomationId = automationId,
                NativeAutomationIdentity = element.NativeAutomationIdentity,
                NativeAutomationIdentityKind = element.NativeAutomationIdentityKind,
                Selector = scoped
                    ? scopedKey
                    : automationId is not null
                        ? $"automationId:{automationId}"
                    : $"typeIndex:{type}:{typeIndex}",
                SelectorKind = scoped ? "scopedItem" : automationId is not null ? "automationId" : "typeIndex",
                MatchCount = matchCount ?? 1,
                Quality = automationId is null
                    ? "fragile"
                    : matchCount == 1
                        ? "durable"
                        : "ambiguous",
                Enabled = element.IsEnabled,
                Focused = element.IsFocused,
                CollectionScope = element.CollectionScope,
                StableItemKeyDigest = scoped ? element.StableItemKey : null,
                Virtualized = element.IsVirtualized,
            });
            if (result.Count >= maximum)
                break;
        }

        return result;
    }

    private static bool IsOpaqueStableItemKey(string? value)
        => value is { Length: 71 } &&
           value.StartsWith("sha256:", StringComparison.Ordinal) &&
           value[7..].All(Uri.IsHexDigit);
}

internal sealed class TestAgentSelectorIdentityProjection
{
    public string? Type { get; init; }
    public string? Role { get; init; }
    public string? AutomationId { get; init; }
    public string? NativeAutomationIdentity { get; init; }
    public string? NativeAutomationIdentityKind { get; init; }
    public string? Selector { get; init; }
    public string? SelectorKind { get; init; }
    public int MatchCount { get; init; }
    public string? Quality { get; init; }
    public bool? Enabled { get; init; }
    public bool? Focused { get; init; }
    public string? CollectionScope { get; init; }
    public string? StableItemKeyDigest { get; init; }
    public bool? Virtualized { get; init; }
}
