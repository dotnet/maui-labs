using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Maui.DevFlow.Testing;

/// <summary>
/// Describes whether a flow can safely be replayed without creating an unaccounted-for external
/// side effect. The wire representation remains the lower-case values in
/// <see cref="MauiFlowSideEffectPolicies"/>.
/// </summary>
[JsonConverter(typeof(MauiFlowSideEffectPolicyJsonConverter))]
public enum MauiFlowSideEffectPolicy
{
    Unspecified,
    None,
    /// <summary>
    /// The flow mutates only app-local state, and a reset owner can restore that state without
    /// touching a backend. Admission proves the app-state reset and its seed, and deliberately does
    /// not ask for backend proof, because no backend was changed to prove anything about.
    /// </summary>
    AppStateResettable,
    TestTenantResettable,
    Compensated,
    NonReplayable,
}

/// <summary>Serializes <see cref="MauiFlowSideEffectPolicy"/> with its stable lower-case wire values.</summary>
public sealed class MauiFlowSideEffectPolicyJsonConverter : JsonConverter<MauiFlowSideEffectPolicy>
{
    /// <inheritdoc />
    public override MauiFlowSideEffectPolicy Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("A side-effect policy must be a string.");

        var value = reader.GetString();
        if (value == MauiFlowSideEffectPolicies.Unspecified)
            return MauiFlowSideEffectPolicy.Unspecified;
        if (!MauiFlowSideEffectPolicies.IsKnown(value))
            throw new JsonException($"Unknown side-effect policy '{value}'.");
        return MauiFlowSideEffectPolicies.Parse(value);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, MauiFlowSideEffectPolicy value, JsonSerializerOptions options)
        => writer.WriteStringValue(MauiFlowSideEffectPolicies.ToWireValue(value));
}

/// <summary>Known replay intents used by hosts when requesting a bounded flow run.</summary>
public static class MauiFlowReplayIntents
{
    public const string OrdinaryReplay = "ordinary-replay";
    public const string DiagnosticReplay = "diagnostic-replay";
    public const string RepairValidation = "repair-validation";
    public const string DownstreamContinuation = "downstream-continuation";

    public static bool IsKnown(string? value) =>
        value is OrdinaryReplay or DiagnosticReplay or RepairValidation or DownstreamContinuation;
}

/// <summary>
/// Identifies a host-owned reset implementation. This is evidence only; it deliberately does not
/// expose a reset operation or device lifecycle interface.
/// </summary>
public sealed class MauiFlowResetReference
{
    [JsonPropertyName("strategy")] public string? Strategy { get; set; }
    [JsonPropertyName("resetId")] public string? ResetId { get; set; }
    [JsonPropertyName("scope")] public string? Scope { get; set; }
    [JsonPropertyName("version")] public string? Version { get; set; }
    [JsonPropertyName("evidenceReference")] public string? EvidenceReference { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>A privacy-safe identity for an app-state seed established by a host.</summary>
public sealed class MauiFlowAppStateSeedFingerprint
{
    [JsonPropertyName("seedId")] public string? SeedId { get; set; }
    [JsonPropertyName("fingerprint")] public string? Fingerprint { get; set; }
    [JsonPropertyName("version")] public string? Version { get; set; }
    [JsonPropertyName("source")] public string? Source { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>A privacy-safe identity for backend or test-data state established by a host.</summary>
public sealed class MauiFlowBackendTestDataSeedFingerprint
{
    [JsonPropertyName("seedId")] public string? SeedId { get; set; }
    [JsonPropertyName("fingerprint")] public string? Fingerprint { get; set; }
    [JsonPropertyName("dataset")] public string? Dataset { get; set; }
    [JsonPropertyName("version")] public string? Version { get; set; }
    [JsonPropertyName("source")] public string? Source { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Observed success facts for a reset performed outside this package.</summary>
public sealed class MauiFlowResetOutcome
{
    [JsonPropertyName("requested")] public bool? Requested { get; set; }
    [JsonPropertyName("succeeded")] public bool? Succeeded { get; set; }
    [JsonPropertyName("appStateSucceeded")] public bool? AppStateSucceeded { get; set; }
    [JsonPropertyName("backendTestDataSucceeded")] public bool? BackendTestDataSucceeded { get; set; }
    [JsonPropertyName("completedAt")] public DateTimeOffset? CompletedAt { get; set; }
    [JsonPropertyName("evidenceReference")] public string? EvidenceReference { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>
/// Expected and observed preconditions. The checkpoints deliberately carry stable identities and
/// display state, not user-entered text or values.
/// </summary>
public sealed class MauiFlowReplayPreconditions
{
    [JsonPropertyName("expected")] public MauiFlowCheckpoint? Expected { get; set; }
    [JsonPropertyName("observed")] public MauiFlowCheckpoint? Observed { get; set; }

    /// <summary>
    /// Set by a host that cannot read the app's clean state until the app is launched, to declare
    /// that the observation is owed rather than absent.
    /// </summary>
    /// <remarks>
    /// Admission runs before the app is deployed, so a host evaluating at that point has nothing to
    /// observe. Fabricating an all-blank observed checkpoint would be a false attestation, and
    /// omitting it silently would be indistinguishable from a host that simply never checked. This
    /// flag makes the gap explicit: admission proceeds, but every downstream trust claim
    /// (verification and repair eligibility) stays closed until a real observation is supplied and
    /// the evaluation is repeated.
    /// </remarks>
    [JsonPropertyName("observationDeferredUntilLaunch")] public bool? ObservationDeferredUntilLaunch { get; set; }
    [JsonPropertyName("checkedAt")] public DateTimeOffset? CheckedAt { get; set; }
    [JsonPropertyName("evidenceReference")] public string? EvidenceReference { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Declares an independent business fact required to verify a run or repair.</summary>
public sealed class MauiIndependentBusinessOracleDeclaration
{
    [JsonPropertyName("oracleId")] public string? OracleId { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("required")] public bool Required { get; set; } = true;
    [JsonPropertyName("independent")] public bool Independent { get; set; } = true;
    [JsonPropertyName("evidenceKind")] public string? EvidenceKind { get; set; }
    [JsonPropertyName("reference")] public string? Reference { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Observed result from an independent business oracle supplied by a host.</summary>
public sealed class MauiIndependentBusinessOracleResult
{
    [JsonPropertyName("oracleId")] public string? OracleId { get; set; }
    [JsonPropertyName("succeeded")] public bool? Succeeded { get; set; }
    [JsonPropertyName("independent")] public bool? Independent { get; set; }
    [JsonPropertyName("observedAt")] public DateTimeOffset? ObservedAt { get; set; }
    [JsonPropertyName("evidenceReference")] public string? EvidenceReference { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Identifies a declared compensator for a side effect.</summary>
public sealed class MauiFlowCompensatorReference
{
    [JsonPropertyName("compensatorId")] public string? CompensatorId { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("scope")] public string? Scope { get; set; }
    [JsonPropertyName("evidenceKind")] public string? EvidenceKind { get; set; }
    [JsonPropertyName("reference")] public string? Reference { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Observed evidence that a declared compensator completed successfully.</summary>
public sealed class MauiFlowCompensatorOutcome
{
    [JsonPropertyName("compensator")] public MauiFlowCompensatorReference? Compensator { get; set; }
    [JsonPropertyName("succeeded")] public bool? Succeeded { get; set; }
    [JsonPropertyName("completedAt")] public DateTimeOffset? CompletedAt { get; set; }
    [JsonPropertyName("evidenceReference")] public string? EvidenceReference { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>
/// Host-supplied facts used to evaluate safety before a runner acquires a lease or sends a
/// mutating command.
/// </summary>
public sealed class MauiFlowRunContext
{
    [JsonPropertyName("intent")] public string? Intent { get; set; }
    [JsonPropertyName("manualOneShotAuthorization")] public bool? ManualOneShotAuthorization { get; set; }
    [JsonPropertyName("preconditions")] public MauiFlowReplayPreconditions? Preconditions { get; set; }
    [JsonPropertyName("reset")] public MauiFlowResetResult? Reset { get; set; }
    [JsonPropertyName("compensator")] public MauiFlowCompensatorOutcome? Compensator { get; set; }
    [JsonPropertyName("businessOracles")] public List<MauiIndependentBusinessOracleResult> BusinessOracles { get; set; } = [];
    [JsonPropertyName("priorMutationCompletionCertain")] public bool? PriorMutationCompletionCertain { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    /// <summary>
    /// Compatibility-friendly alias for callers that use a shorter name. The JSON contract always
    /// uses <c>manualOneShotAuthorization</c> so it cannot be confused with routine approval.
    /// </summary>
    [JsonIgnore]
    public bool? ManualAuthorization
    {
        get => ManualOneShotAuthorization;
        set => ManualOneShotAuthorization = value;
    }
}

/// <summary>
/// Additive safety data accompanying a flow run. It is a data contract only and never grants
/// reset, device, broker, or compensator execution authority.
/// </summary>
public sealed class MauiFlowRunRequest
{
    [JsonPropertyName("plan")] public MauiTestPlan? Plan { get; set; }
    [JsonPropertyName("context")] public MauiFlowRunContext? Context { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>One deterministic explanation for an admission or verification decision.</summary>
public sealed class MauiFlowReplayEligibilityReason
{
    [JsonPropertyName("code")] public string? Code { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("blocking")] public bool? Blocking { get; set; }
    [JsonPropertyName("scope")] public string? Scope { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>
/// The pure result of applying the side-effect/reset/oracle contract to a run request. Hosts use
/// this result before mutation and retain it in reports instead of inferring safety later.
/// </summary>
public sealed class MauiFlowReplayEligibilityDecision
{
    [JsonPropertyName("sideEffectPolicy")] public string SideEffectPolicy { get; set; } = MauiFlowSideEffectPolicies.Unspecified;
    [JsonPropertyName("ordinaryReplayAllowed")] public bool OrdinaryReplayAllowed { get; set; }
    [JsonPropertyName("diagnosticReplayAllowed")] public bool DiagnosticReplayAllowed { get; set; }
    [JsonPropertyName("repairValidationAllowed")] public bool RepairValidationAllowed { get; set; }
    [JsonPropertyName("downstreamContinuationAllowed")] public bool DownstreamContinuationAllowed { get; set; }
    [JsonPropertyName("repairEligibility")] public bool RepairEligibility { get; set; }
    [JsonPropertyName("runVerificationAllowed")] public bool RunVerificationAllowed { get; set; }
    [JsonPropertyName("requiresManualOneShotAuthorization")] public bool RequiresManualOneShotAuthorization { get; set; }
    [JsonPropertyName("reasons")] public List<MauiFlowReplayEligibilityReason> Reasons { get; set; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    [JsonIgnore]
    public MauiFlowSideEffectPolicy Policy => MauiFlowSideEffectPolicies.Parse(SideEffectPolicy);

    /// <summary>Returns whether the declared intent is admitted by this decision.</summary>
    public bool IsAllowedForIntent(string? intent) => intent switch
    {
        null or MauiFlowReplayIntents.OrdinaryReplay => OrdinaryReplayAllowed,
        MauiFlowReplayIntents.DiagnosticReplay => DiagnosticReplayAllowed,
        MauiFlowReplayIntents.RepairValidation => RepairValidationAllowed,
        MauiFlowReplayIntents.DownstreamContinuation => DownstreamContinuationAllowed,
        _ => false,
    };
}

/// <summary>
/// Pure deterministic side-effect admission logic. It performs no I/O, invokes no reset or
/// oracle, and is safe to use in a broker before a mutation lease is acquired.
/// </summary>
public static class MauiFlowReplaySafetyEvaluator
{
    /// <summary>Evaluates replay, repair, continuation, and verification eligibility.</summary>
    public static MauiFlowReplayEligibilityDecision Evaluate(MauiFlowRunRequest request)
        => EvaluateCore(request, flow: null);

    /// <summary>
    /// Compatibility overload. New callers should use <see cref="EvaluateWithFlow"/> so a null
    /// second argument cannot be confused with the request-only API.
    /// </summary>
    [Obsolete("Use EvaluateWithFlow(request, flow) for executable-flow validation.")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static MauiFlowReplayEligibilityDecision Evaluate(MauiFlowRunRequest request, MauiFlow? flow)
        => EvaluateWithFlow(request, flow);

    /// <summary>
    /// Evaluates replay safety and verifies that a supplied executable flow covers the validated
    /// plan's declared scenarios and required acceptance criteria.
    /// </summary>
    public static MauiFlowReplayEligibilityDecision EvaluateWithFlow(
        MauiFlowRunRequest request,
        MauiFlow? flow)
        => EvaluateCore(request, flow);

    private static MauiFlowReplayEligibilityDecision EvaluateCore(
        MauiFlowRunRequest request,
        MauiFlow? flow)
    {
        ArgumentNullException.ThrowIfNull(request);

        var plan = request.Plan;
        var context = request.Context;
        var rawPolicy = plan?.SideEffectPolicy;
        var hasDeclaredPolicy = plan is not null && !string.IsNullOrWhiteSpace(rawPolicy);
        var policy = MauiFlowSideEffectPolicies.Parse(rawPolicy);
        var decision = new MauiFlowReplayEligibilityDecision
        {
            SideEffectPolicy = hasDeclaredPolicy
                ? MauiFlowSideEffectPolicies.ToWireValue(policy)
                : MauiFlowSideEffectPolicies.Unspecified,
        };
        if (plan is not null && !MauiTestPlanValidator.Validate(plan).IsValid)
        {
            AddReason(
                decision,
                "test-plan-invalid",
                "The supplied test plan failed deterministic contract validation.",
                blocking: true,
                scope: "admission");
            return decision;
        }

        if (hasDeclaredPolicy && !MauiFlowSideEffectPolicies.IsKnown(rawPolicy))
        {
            AddReason(
                decision,
                "side-effect-policy-unknown",
                "The declared side-effect policy is not recognized and cannot authorize replay.",
                blocking: true,
                scope: "admission");
            return decision;
        }
        if (!string.IsNullOrWhiteSpace(context?.Intent) && !MauiFlowReplayIntents.IsKnown(context.Intent))
        {
            AddReason(
                decision,
                "replay-intent-unknown",
                "The requested replay intent is not recognized and cannot authorize mutation.",
                blocking: true,
                scope: "admission");
            return decision;
        }

        var preconditionsMatch = EvaluatePreconditions(plan, context, hasDeclaredPolicy, decision, out var preconditionsDeferred);
        if (!hasDeclaredPolicy)
        {
            AddReason(
                decision,
                "side-effect-policy-unspecified",
                "No plan side-effect policy was supplied. Legacy manual replay remains available, but repair is not eligible.",
                blocking: false,
                scope: "compatibility");
            SetCompatibilityDecision(decision, preconditionsMatch);
            return decision;
        }

        var requiresResetProof = policy is MauiFlowSideEffectPolicy.AppStateResettable
            or MauiFlowSideEffectPolicy.TestTenantResettable
            or MauiFlowSideEffectPolicy.Compensated;
        // App-state proof is demanded by both resettable policies. Backend proof is demanded only by
        // the tenant policy, because that is the one claiming a backend tenant was reset. Asking an
        // app-state-only flow for backend proof would make the claim unprovable rather than safer.
        var resetProven = requiresResetProof &&
            HasSuccessfulReset(
                plan,
                context,
                decision,
                requireAppStateProof: policy is MauiFlowSideEffectPolicy.AppStateResettable
                    or MauiFlowSideEffectPolicy.TestTenantResettable,
                requireBackendProof: policy == MauiFlowSideEffectPolicy.TestTenantResettable);
        var compensatorProven = policy == MauiFlowSideEffectPolicy.Compensated &&
            HasSuccessfulCompensator(plan, context, decision);
        var admitted = policy switch
        {
            MauiFlowSideEffectPolicy.None => preconditionsMatch,
            MauiFlowSideEffectPolicy.AppStateResettable => preconditionsMatch && resetProven,
            MauiFlowSideEffectPolicy.TestTenantResettable => preconditionsMatch && resetProven,
            MauiFlowSideEffectPolicy.Compensated => preconditionsMatch && (resetProven || compensatorProven),
            MauiFlowSideEffectPolicy.NonReplayable => preconditionsMatch && context?.ManualOneShotAuthorization == true,
            _ => false,
        };

        if (policy == MauiFlowSideEffectPolicy.NonReplayable)
        {
            decision.RequiresManualOneShotAuthorization = true;
            if (context?.ManualOneShotAuthorization != true)
            {
                AddReason(
                    decision,
                    "manual-one-shot-authorization-required",
                    "A non-replayable flow may run only with explicit one-shot human authorization.",
                    blocking: true,
                    scope: "admission");
            }
            else
            {
                AddReason(
                    decision,
                    "manual-one-shot-authorized",
                    "The non-replayable flow is admitted as one explicitly authorized human run only.",
                    blocking: false,
                    scope: "admission");
            }
        }

        decision.OrdinaryReplayAllowed = admitted;
        decision.DiagnosticReplayAllowed = admitted && policy != MauiFlowSideEffectPolicy.NonReplayable;
        decision.DownstreamContinuationAllowed =
            admitted &&
            policy != MauiFlowSideEffectPolicy.NonReplayable &&
            context?.PriorMutationCompletionCertain != false;

        if (context?.PriorMutationCompletionCertain == false)
        {
            AddReason(
                decision,
                "prior-mutation-completion-unknown",
                "A prior mutation has unknown completion, so automatic continuation and repair validation are not safe.",
                blocking: true,
                scope: "continuation");
        }

        var oracle = EvaluateIndependentOracles(plan, context, decision);
        var coverage = EvaluateVerificationCoverage(plan, flow, decision);
        decision.RunVerificationAllowed =
            admitted &&
            oracle.AllRequiredSucceeded &&
            coverage.AllRequiredCovered;
        if (oracle.HasFailure)
            decision.DownstreamContinuationAllowed = false;
        decision.RepairValidationAllowed =
            admitted &&
            policy != MauiFlowSideEffectPolicy.NonReplayable &&
            context?.PriorMutationCompletionCertain != false &&
            oracle.HasRequiredDeclaration &&
            !oracle.HasFailure;
        decision.RepairEligibility =
            decision.RepairValidationAllowed &&
            oracle.AllRequiredSucceeded &&
            coverage.AllRequiredCovered;

        if (preconditionsDeferred)
        {
            decision.RepairValidationAllowed = false;
            decision.RepairEligibility = false;
            decision.RunVerificationAllowed = false;
            // Continuing a dependent flow is a claim that this run started from the clean state it
            // declared, which is exactly the observation that has not been made yet.
            decision.DownstreamContinuationAllowed = false;
        }

        if (policy == MauiFlowSideEffectPolicy.NonReplayable)
        {
            decision.RepairValidationAllowed = false;
            decision.RepairEligibility = false;
            decision.DownstreamContinuationAllowed = false;
            AddReason(
                decision,
                "non-replayable-repair-prohibited",
                "Non-replayable flows are never eligible for automatic repair validation or downstream continuation.",
                blocking: true,
                scope: "repair");
        }

        if (!preconditionsMatch)
            AddReason(decision, "preconditions-unsatisfied", "Required clean-state preconditions do not match.", true, "admission");
        if (policy is MauiFlowSideEffectPolicy.AppStateResettable or MauiFlowSideEffectPolicy.TestTenantResettable && !resetProven)
        {
            AddReason(
                decision,
                "reset-proof-required",
                policy == MauiFlowSideEffectPolicy.AppStateResettable
                    ? "A successful app-state reset with a matching seed fingerprint is required."
                    : "A successful app and backend reset with matching seed fingerprints is required.",
                true,
                "admission");
        }
        if (policy == MauiFlowSideEffectPolicy.Compensated && !resetProven && !compensatorProven)
            AddReason(decision, "reset-or-compensator-required", "A successful reset or declared compensator outcome is required.", true, "admission");

        return decision;
    }

    /// <summary>Returns whether a completed run may be represented as independently verified.</summary>
    public static bool CanMarkRunVerified(MauiFlowReplayEligibilityDecision? decision)
        => decision?.RunVerificationAllowed == true;

    /// <summary>Returns whether a repair outcome may be represented as independently verified.</summary>
    public static bool CanMarkRepairVerified(MauiFlowReplayEligibilityDecision? decision)
        => decision?.RepairEligibility == true;

    private static void SetCompatibilityDecision(MauiFlowReplayEligibilityDecision decision, bool preconditionsMatch)
    {
        decision.OrdinaryReplayAllowed = preconditionsMatch;
        decision.DiagnosticReplayAllowed = preconditionsMatch;
        decision.DownstreamContinuationAllowed = preconditionsMatch;
        decision.RepairValidationAllowed = false;
        decision.RepairEligibility = false;
        decision.RunVerificationAllowed = false;
    }

    private static bool EvaluatePreconditions(
        MauiTestPlan? plan,
        MauiFlowRunContext? context,
        bool required,
        MauiFlowReplayEligibilityDecision decision,
        out bool observationDeferred)
    {
        observationDeferred = false;
        var before = decision.Reasons.Count;
        var declared = ToCheckpoint(plan?.Checkpoint);
        var supplied = context?.Preconditions?.Expected;
        if (declared is not null && supplied is not null)
            CompareDeclaredCheckpoint(declared, supplied, "precondition-declaration", decision);

        var expected = MergeCheckpoints(declared, supplied);
        var observed = context?.Preconditions?.Observed;
        if (expected is null)
        {
            if (required)
            {
                AddReason(
                    decision,
                    "preconditions-missing",
                    "A side-effect plan must declare expected preconditions before replay.",
                    blocking: true,
                    scope: "admission");
                return false;
            }

            return true;
        }

        if (observed is null)
        {
            if (context?.Preconditions?.ObservationDeferredUntilLaunch == true)
            {
                observationDeferred = true;
                AddReason(
                    decision,
                    "preconditions-observation-deferred",
                    "The host cannot observe clean state before launch and owes the observation. Replay is admitted provisionally; verification and repair stay ineligible until the observation is supplied.",
                    blocking: false,
                    scope: "admission");
                // The deferral only excuses the missing observation. A conflict between what the
                // plan declared and what the host supplied is already decided and stays decided.
                return !decision.Reasons.Skip(before).Any(static reason => reason.Blocking == true);
            }

            AddReason(
                decision,
                "preconditions-observation-missing",
                "Expected preconditions were supplied but no observed clean-state checkpoint was supplied.",
                blocking: true,
                scope: "admission");
            return false;
        }

        CompareCheckpointField(decision, "app-build", expected.AppBuildFingerprint, observed.AppBuildFingerprint);
        CompareCheckpointField(decision, "app-state-seed", expected.SeedFingerprint, observed.SeedFingerprint);
        CompareCheckpointField(decision, "backend-test-data-seed", expected.BackendStateFingerprint, observed.BackendStateFingerprint);
        CompareCheckpointField(decision, "route", expected.Route, observed.Route);
        CompareCheckpointField(decision, "window", expected.Window, observed.Window);
        CompareCheckpointField(decision, "modal", expected.Modal, observed.Modal);
        CompareCheckpointField(decision, "locale", expected.Locale, observed.Locale);
        CompareCheckpointField(decision, "theme", expected.Theme, observed.Theme);
        CompareCheckpointField(decision, "orientation", expected.Orientation, observed.Orientation);
        CompareCheckpointField(decision, "display", expected.DisplayProfile, observed.DisplayProfile);
        CompareCheckpointField(decision, "collection-item-key", expected.CollectionItemKey, observed.CollectionItemKey);
        return !decision.Reasons.Skip(before).Any(static reason => reason.Blocking == true);
    }

    private static void CompareDeclaredCheckpoint(
        MauiFlowCheckpoint declared,
        MauiFlowCheckpoint supplied,
        string prefix,
        MauiFlowReplayEligibilityDecision decision)
    {
        CompareConflictingField(decision, prefix, "app-build", declared.AppBuildFingerprint, supplied.AppBuildFingerprint);
        CompareConflictingField(decision, prefix, "app-state-seed", declared.SeedFingerprint, supplied.SeedFingerprint);
        CompareConflictingField(decision, prefix, "backend-test-data-seed", declared.BackendStateFingerprint, supplied.BackendStateFingerprint);
        CompareConflictingField(decision, prefix, "route", declared.Route, supplied.Route);
        CompareConflictingField(decision, prefix, "window", declared.Window, supplied.Window);
        CompareConflictingField(decision, prefix, "modal", declared.Modal, supplied.Modal);
        CompareConflictingField(decision, prefix, "locale", declared.Locale, supplied.Locale);
        CompareConflictingField(decision, prefix, "theme", declared.Theme, supplied.Theme);
        CompareConflictingField(decision, prefix, "orientation", declared.Orientation, supplied.Orientation);
        CompareConflictingField(decision, prefix, "display", declared.DisplayProfile, supplied.DisplayProfile);
        CompareConflictingField(decision, prefix, "collection-item-key", declared.CollectionItemKey, supplied.CollectionItemKey);
    }

    private static void CompareConflictingField(
        MauiFlowReplayEligibilityDecision decision,
        string prefix,
        string name,
        string? declared,
        string? supplied)
    {
        if (!string.IsNullOrWhiteSpace(declared) &&
            !string.IsNullOrWhiteSpace(supplied) &&
            !string.Equals(declared, supplied, StringComparison.Ordinal))
        {
            AddReason(
                decision,
                $"{prefix}-{name}-mismatch",
                $"The supplied expected {name} does not match the plan declaration.",
                blocking: true,
                scope: "admission");
        }
    }

    private static void CompareCheckpointField(
        MauiFlowReplayEligibilityDecision decision,
        string name,
        string? expected,
        string? observed)
    {
        if (string.IsNullOrWhiteSpace(expected))
            return;
        if (string.IsNullOrWhiteSpace(observed))
        {
            AddReason(
                decision,
                $"precondition-{name}-missing",
                $"The observed {name} precondition is missing.",
                blocking: true,
                scope: "admission");
            return;
        }
        if (!string.Equals(expected, observed, StringComparison.Ordinal))
        {
            AddReason(
                decision,
                $"precondition-{name}-mismatch",
                $"The observed {name} precondition does not match the expected value.",
                blocking: true,
                scope: "admission");
        }
    }

    private static bool HasSuccessfulReset(
        MauiTestPlan? plan,
        MauiFlowRunContext? context,
        MauiFlowReplayEligibilityDecision decision,
        bool requireAppStateProof,
        bool requireBackendProof)
    {
        var reset = context?.Reset;
        if (!IsResetSucceeded(reset))
            return false;

        var expectedAppSeed = plan?.Reset?.AppStateSeed?.Fingerprint ??
            plan?.Reset?.SeedFingerprint ??
            plan?.Checkpoint?.AppStateSeed?.Fingerprint ??
            plan?.Checkpoint?.SeedFingerprint;
        var expectedBackendSeed = plan?.Reset?.BackendTestDataSeed?.Fingerprint ??
            plan?.Reset?.BackendStateFingerprint ??
            plan?.Checkpoint?.BackendTestDataSeed?.Fingerprint ??
            plan?.Checkpoint?.BackendStateFingerprint;
        var actualAppSeed = reset?.AppStateSeed?.Fingerprint ?? reset?.SeedFingerprint;
        var actualBackendSeed = reset?.BackendTestDataSeed?.Fingerprint ?? reset?.BackendStateFingerprint;
        var appSucceeded = reset?.Outcome?.AppStateSucceeded ?? reset?.AppStateSucceeded ?? reset?.Succeeded;
        var backendSucceeded = reset?.Outcome?.BackendTestDataSucceeded ?? reset?.BackendTestDataSucceeded ?? reset?.Succeeded;
        var valid = true;

        if (requireAppStateProof && appSucceeded != true)
        {
            AddReason(decision, "app-state-reset-not-proven", "The app-state reset was not proven successful.", true, "admission");
            valid = false;
        }
        if (requireBackendProof && backendSucceeded != true)
        {
            AddReason(decision, "backend-test-data-reset-not-proven", "The backend/test-data reset was not proven successful.", true, "admission");
            valid = false;
        }

        valid &= CompareResetFingerprint(
            decision,
            "app-state-seed",
            expectedAppSeed,
            actualAppSeed,
            required: requireAppStateProof);
        valid &= CompareResetFingerprint(
            decision,
            "backend-test-data-seed",
            expectedBackendSeed,
            actualBackendSeed,
            required: requireBackendProof);
        return valid;
    }

    private static bool IsResetSucceeded(MauiFlowResetResult? reset)
        => reset?.Outcome?.Succeeded ?? reset?.Succeeded == true;

    private static bool CompareResetFingerprint(
        MauiFlowReplayEligibilityDecision decision,
        string name,
        string? expected,
        string? observed,
        bool required)
    {
        if (string.IsNullOrWhiteSpace(expected))
        {
            if (required)
            {
                AddReason(
                    decision,
                    $"{name}-not-declared",
                    $"A {name} fingerprint must be declared for a flow whose policy requires this reset proof.",
                    true,
                    "admission");
                return false;
            }
            return true;
        }

        if (string.IsNullOrWhiteSpace(observed))
        {
            AddReason(
                decision,
                $"{name}-not-proven",
                $"The reset outcome did not include the observed {name} fingerprint.",
                true,
                "admission");
            return false;
        }
        if (!string.Equals(expected, observed, StringComparison.Ordinal))
        {
            AddReason(
                decision,
                $"{name}-mismatch",
                $"The reset outcome {name} fingerprint does not match the declared fingerprint.",
                true,
                "admission");
            return false;
        }
        return true;
    }

    private static bool HasSuccessfulCompensator(
        MauiTestPlan? plan,
        MauiFlowRunContext? context,
        MauiFlowReplayEligibilityDecision decision)
    {
        var declared = plan?.Compensator;
        var outcome = context?.Compensator;
        if (declared is null || outcome?.Succeeded != true)
            return false;

        var declaredId = declared.CompensatorId;
        var observedId = outcome.Compensator?.CompensatorId;
        if (!string.IsNullOrWhiteSpace(declaredId) &&
            !string.Equals(declaredId, observedId, StringComparison.Ordinal))
        {
            AddReason(
                decision,
                "compensator-mismatch",
                "The successful compensator outcome does not identify the compensator declared by the plan.",
                true,
                "admission");
            return false;
        }

        return true;
    }

    private static OracleEvaluation EvaluateIndependentOracles(
        MauiTestPlan? plan,
        MauiFlowRunContext? context,
        MauiFlowReplayEligibilityDecision decision)
    {
        var declarations = new List<MauiIndependentBusinessOracleDeclaration>();
        if (plan is not null)
        {
            declarations.AddRange(plan.IndependentBusinessOracles.Where(static oracle => oracle is not null));
            declarations.AddRange(plan.BusinessOracles
                .Where(static oracle => oracle is not null)
                .Select(static oracle => new MauiIndependentBusinessOracleDeclaration
                {
                    OracleId = oracle.OracleId,
                    Description = oracle.Description,
                    Required = oracle.Required,
                    Independent = oracle.Independent,
                    EvidenceKind = oracle.EvidenceKind,
                    Reference = oracle.Reference,
                }));
        }

        var required = declarations
            .Where(static oracle => oracle is not null && oracle.Required && oracle.Independent)
            .GroupBy(static oracle => oracle.OracleId ?? string.Empty, StringComparer.Ordinal)
            .Select(static group => group.First())
            .ToList();
        if (required.Count == 0)
        {
            AddReason(
                decision,
                "independent-oracle-absent",
                "No required independent business oracle is declared, so the run or repair cannot be verified.",
                false,
                "verification");
            return new OracleEvaluation(false, false, false);
        }
        if ((context?.BusinessOracles ?? []).Any(static result => result is null))
        {
            AddReason(
                decision,
                "independent-oracle-result-invalid",
                "Independent business-oracle results cannot contain null entries.",
                true,
                "verification");
            return new OracleEvaluation(true, false, true);
        }

        var allSucceeded = true;
        var hasFailure = false;
        foreach (var declaration in required)
        {
            if (string.IsNullOrWhiteSpace(declaration.OracleId))
            {
                AddReason(
                    decision,
                    "independent-oracle-id-missing",
                    "A required independent business oracle has no oracleId.",
                    true,
                    "verification");
                allSucceeded = false;
                hasFailure = true;
                continue;
            }

            var results = (context?.BusinessOracles ?? [])
                .Where(candidate =>
                    string.Equals(candidate.OracleId, declaration.OracleId, StringComparison.Ordinal))
                .ToList();
            if (results.Count == 0 ||
                results.Any(static result => result.Independent != true || result.Succeeded is null))
            {
                AddReason(
                    decision,
                    "independent-oracle-missing",
                    $"Independent business oracle '{declaration.OracleId}' has not produced verification evidence.",
                    false,
                    "verification");
                allSucceeded = false;
                continue;
            }

            if (results.Select(static result => result.Succeeded).Distinct().Count() != 1)
            {
                AddReason(
                    decision,
                    "independent-oracle-outcome-conflict",
                    $"Independent business oracle '{declaration.OracleId}' produced contradictory outcomes.",
                    true,
                    "verification");
                allSucceeded = false;
                hasFailure = true;
                continue;
            }

            if (results[0].Succeeded != true)
            {
                AddReason(
                    decision,
                    "independent-oracle-failed",
                    $"Independent business oracle '{declaration.OracleId}' did not verify the expected business result.",
                    true,
                    "verification");
                allSucceeded = false;
                hasFailure = true;
            }
        }

        return new OracleEvaluation(true, allSucceeded, hasFailure);
    }

    private static CoverageEvaluation EvaluateVerificationCoverage(
        MauiTestPlan? plan,
        MauiFlow? flow,
        MauiFlowReplayEligibilityDecision decision)
    {
        var scenarios = (plan?.Scenarios ?? [])
            .Where(static scenario => scenario is not null)
            .ToList();
        var requiredCriteria = (plan?.AcceptanceCriteria ?? [])
            .Where(static criterion => criterion is not null && criterion.Required)
            .ToList();
        if (scenarios.Count == 0 && requiredCriteria.Count == 0)
            return new CoverageEvaluation(true);

        if (flow is null)
        {
            AddReason(
                decision,
                "verification-flow-missing",
                "The executable flow is required to verify scenario and acceptance-criterion coverage.",
                false,
                "verification");
            return new CoverageEvaluation(false);
        }

        var coveredCriteria = (flow.Steps ?? [])
            .Where(HasHardAssertion)
            .SelectMany(static step => step.AcceptanceCriterionIds ?? [])
            .Where(static criterionId => !string.IsNullOrWhiteSpace(criterionId))
            .ToHashSet(StringComparer.Ordinal);
        var allCovered = true;
        foreach (var criterion in requiredCriteria)
        {
            if (string.IsNullOrWhiteSpace(criterion.CriterionId) ||
                !coveredCriteria.Contains(criterion.CriterionId))
            {
                AddReason(
                    decision,
                    "required-acceptance-criterion-uncovered",
                    "A required acceptance criterion is not linked to a hard assertion in the executable flow.",
                    false,
                    "verification");
                allCovered = false;
            }
        }

        foreach (var scenario in scenarios)
        {
            var criterionIds = (scenario.AcceptanceCriterionIds ?? [])
                .Where(static criterionId => !string.IsNullOrWhiteSpace(criterionId))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (criterionIds.Count == 0 ||
                criterionIds.Any(criterionId => !coveredCriteria.Contains(criterionId)))
            {
                AddReason(
                    decision,
                    "required-scenario-uncovered",
                    "A declared scenario does not have complete hard-assertion coverage for its acceptance criteria.",
                    false,
                    "verification");
                allCovered = false;
            }
        }

        var requiredOracleIds = (plan?.IndependentBusinessOracles ?? [])
            .Where(static oracle =>
                oracle is not null &&
                oracle.Required &&
                oracle.Independent &&
                !string.IsNullOrWhiteSpace(oracle.OracleId))
            .Select(static oracle => oracle.OracleId!)
            .Concat((plan?.BusinessOracles ?? [])
                .Where(static oracle =>
                    oracle is not null &&
                    oracle.Required &&
                    oracle.Independent &&
                    !string.IsNullOrWhiteSpace(oracle.OracleId))
                .Select(static oracle => oracle.OracleId!))
            .ToHashSet(StringComparer.Ordinal);
        if (requiredCriteria.Any(criterion =>
                !string.IsNullOrWhiteSpace(criterion.BusinessOracleId) &&
                !requiredOracleIds.Contains(criterion.BusinessOracleId)))
        {
            AddReason(
                decision,
                "acceptance-criterion-oracle-undeclared",
                "A required acceptance criterion references a business oracle that is not declared as required and independent.",
                false,
                "verification");
            allCovered = false;
        }

        return new CoverageEvaluation(allCovered);
    }

    private static bool HasHardAssertion(FlowStep step)
        => (step.Asserts ?? []).Any(static assertion => assertion?.Verify == true);

    private static MauiFlowCheckpoint? ToCheckpoint(MauiFlowCheckpointRequirements? requirements)
    {
        if (requirements is null)
            return null;

        return new MauiFlowCheckpoint
        {
            AppBuildFingerprint = requirements.AppBuildFingerprint,
            SeedFingerprint = requirements.AppStateSeed?.Fingerprint ?? requirements.SeedFingerprint,
            BackendStateFingerprint = requirements.BackendTestDataSeed?.Fingerprint ?? requirements.BackendStateFingerprint,
            Route = requirements.Route,
            Window = requirements.Window,
            Modal = requirements.Modal,
            Locale = requirements.Locale,
            Theme = requirements.Theme,
            Orientation = requirements.Orientation,
            DisplayProfile = requirements.DisplayProfile,
            CollectionItemKey = requirements.CollectionItemKey,
        };
    }

    private static MauiFlowCheckpoint? MergeCheckpoints(MauiFlowCheckpoint? declared, MauiFlowCheckpoint? supplied)
    {
        if (declared is null && supplied is null)
            return null;

        return new MauiFlowCheckpoint
        {
            AppBuildFingerprint = declared?.AppBuildFingerprint ?? supplied?.AppBuildFingerprint,
            AgentInstanceId = declared?.AgentInstanceId ?? supplied?.AgentInstanceId,
            SeedFingerprint = declared?.SeedFingerprint ?? supplied?.SeedFingerprint,
            BackendStateFingerprint = declared?.BackendStateFingerprint ?? supplied?.BackendStateFingerprint,
            Route = declared?.Route ?? supplied?.Route,
            Window = declared?.Window ?? supplied?.Window,
            Modal = declared?.Modal ?? supplied?.Modal,
            Locale = declared?.Locale ?? supplied?.Locale,
            Theme = declared?.Theme ?? supplied?.Theme,
            Orientation = declared?.Orientation ?? supplied?.Orientation,
            DisplayProfile = declared?.DisplayProfile ?? supplied?.DisplayProfile,
            CollectionItemKey = declared?.CollectionItemKey ?? supplied?.CollectionItemKey,
        };
    }

    private static void AddReason(
        MauiFlowReplayEligibilityDecision decision,
        string code,
        string message,
        bool blocking,
        string scope)
    {
        if (decision.Reasons.Any(reason => string.Equals(reason.Code, code, StringComparison.Ordinal)))
            return;
        decision.Reasons.Add(new MauiFlowReplayEligibilityReason
        {
            Code = code,
            Message = message,
            Blocking = blocking,
            Scope = scope,
        });
    }

    private readonly record struct OracleEvaluation(
        bool HasRequiredDeclaration,
        bool AllRequiredSucceeded,
        bool HasFailure);

    private readonly record struct CoverageEvaluation(bool AllRequiredCovered);
}
