using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Maui.DevFlow.Testing;

/// <summary>
/// A non-executable description of the intent, safety constraints, and approval history for a
/// DevFlow test. Hosts own all device and backend lifecycle operations.
/// </summary>
public sealed class MauiTestPlan
{
    [JsonPropertyName("schema")] public int Schema { get; init; } = 1;
    [JsonPropertyName("planId")] public string? PlanId { get; init; }
    [JsonPropertyName("revision")] public int? Revision { get; init; }
    [JsonPropertyName("flow")] public MauiFlowReference? Flow { get; init; }
    [JsonPropertyName("title")] public string? Title { get; init; }
    [JsonPropertyName("goal")] public string? Goal { get; init; }

    // Every list below coalesces null on assignment. A hand-authored plan may spell an optional
    // list as an explicit JSON null, which System.Text.Json writes straight over the initializer;
    // consumers then dereference a null List and the operator sees an unhandled
    // ArgumentNullException reported as an infrastructure error rather than a plan they can fix.
    // An absent list and a null list mean the same thing - nothing declared - so they are stored
    // the same way.
    [JsonPropertyName("scenarios")] public List<MauiTestScenario> Scenarios { get => field; init => field = value ?? []; } = [];
    [JsonPropertyName("assumptions")] public List<string> Assumptions { get => field; init => field = value ?? []; } = [];
    [JsonPropertyName("risks")] public List<string> Risks { get => field; init => field = value ?? []; } = [];
    [JsonPropertyName("preconditions")] public List<MauiTestPrecondition> Preconditions { get => field; init => field = value ?? []; } = [];
    [JsonPropertyName("reset")] public MauiTestResetRequirement? Reset { get; init; }
    [JsonPropertyName("acceptanceCriteria")] public List<MauiAcceptanceCriterion> AcceptanceCriteria { get => field; init => field = value ?? []; } = [];
    [JsonPropertyName("requiredPlatforms")] public List<string> RequiredPlatforms { get => field; init => field = value ?? []; } = [];
    [JsonPropertyName("requirements")] public MauiFlowRequirements? Requirements { get; init; }
    [JsonPropertyName("explorationBudget")] public MauiExplorationBudget? ExplorationBudget { get; init; }
    [JsonPropertyName("prohibitedActionClasses")] public List<string> ProhibitedActionClasses { get => field; init => field = value ?? []; } = [];
    [JsonPropertyName("provenance")] public MauiActorProvenance? Provenance { get; init; }
    [JsonPropertyName("reviews")] public List<MauiPlanReview> Reviews { get => field; init => field = value ?? []; } = [];
    [JsonPropertyName("approvals")] public List<MauiPlanApproval> Approvals { get => field; init => field = value ?? []; } = [];
    [JsonPropertyName("sideEffectPolicy")] public string? SideEffectPolicy { get; init; }
    [JsonPropertyName("repairPolicy")] public MauiFlowRepairPolicy? RepairPolicy { get; init; }
    [JsonPropertyName("businessOracles")] public List<MauiBusinessOracleRequirement> BusinessOracles { get => field; init => field = value ?? []; } = [];
    [JsonPropertyName("independentBusinessOracles")] public List<MauiIndependentBusinessOracleDeclaration> IndependentBusinessOracles { get => field; init => field = value ?? []; } = [];
    [JsonPropertyName("compensator")] public MauiFlowCompensatorReference? Compensator { get; set; }
    [JsonPropertyName("checkpoint")] public MauiFlowCheckpointRequirements? Checkpoint { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    /// <summary>Parses the wire value without changing the additive string JSON contract.</summary>
    [JsonIgnore]
    public MauiFlowSideEffectPolicy ParsedSideEffectPolicy => MauiFlowSideEffectPolicies.Parse(SideEffectPolicy);
}

/// <summary>
/// Explicit human-authored selector-repair gates. The policy can narrow the fixed safe defaults
/// but cannot enable non-executable, ambiguous, virtualized, stale-source, or divergent forms.
/// </summary>
public sealed class MauiFlowRepairPolicy
{
    [JsonPropertyName("allowedCandidateKinds")] public List<string> AllowedCandidateKinds { get => field; init => field = value ?? []; } = [];
    [JsonPropertyName("allowedRiskFlags")] public List<string> AllowedRiskFlags { get => field; init => field = value ?? []; } = [];
    [JsonPropertyName("maxCandidates")] public int? MaxCandidates { get; init; }
    [JsonPropertyName("minimumScore")] public double? MinimumScore { get; init; }
    [JsonPropertyName("minimumScoreGap")] public double? MinimumScoreGap { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Identifies a flow without asserting that a legacy content-addressed flow has a stable ID.</summary>
public sealed class MauiFlowReference
{
    [JsonPropertyName("path")] public string? Path { get; init; }
    [JsonPropertyName("flowId")] public string? FlowId { get; init; }
    [JsonPropertyName("revision")] public int? Revision { get; init; }
    [JsonPropertyName("digest")] public string? Digest { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>A scenario the plan intends to prove; it is not an executable step list.</summary>
public sealed class MauiTestScenario
{
    [JsonPropertyName("scenarioId")] public string? ScenarioId { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("acceptanceCriterionIds")] public List<string> AcceptanceCriterionIds { get => field; init => field = value ?? []; } = [];
    [JsonPropertyName("risks")] public List<string> Risks { get => field; init => field = value ?? []; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>A condition that a host must establish or verify before a flow is run.</summary>
public sealed class MauiTestPrecondition
{
    [JsonPropertyName("preconditionId")] public string? PreconditionId { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("required")] public bool Required { get; init; } = true;
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>
/// Data describing the reset state expected by a plan. This contract deliberately does not expose
/// an operation for performing a reset.
/// </summary>
public sealed class MauiTestResetRequirement
{
    [JsonPropertyName("required")] public bool Required { get; init; } = true;
    [JsonPropertyName("strategy")] public string? Strategy { get; init; }
    [JsonPropertyName("resetIdentity")] public string? ResetIdentity { get; init; }
    [JsonPropertyName("seedFingerprint")] public string? SeedFingerprint { get; init; }
    [JsonPropertyName("backendStateFingerprint")] public string? BackendStateFingerprint { get; init; }
    [JsonPropertyName("reference")] public MauiFlowResetReference? Reference { get; init; }
    [JsonPropertyName("appStateSeed")] public MauiFlowAppStateSeedFingerprint? AppStateSeed { get; init; }
    [JsonPropertyName("backendTestDataSeed")] public MauiFlowBackendTestDataSeedFingerprint? BackendTestDataSeed { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>An externally observable condition that a completed test must satisfy.</summary>
public sealed class MauiAcceptanceCriterion
{
    [JsonPropertyName("criterionId")] public string? CriterionId { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("required")] public bool Required { get; init; } = true;
    [JsonPropertyName("businessOracleId")] public string? BusinessOracleId { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Declares capabilities and semantics that must be available before execution.</summary>
public sealed class MauiFlowRequirements
{
    [JsonPropertyName("requiredCapabilities")] public List<MauiCapabilityRequirement> RequiredCapabilities { get => field; init => field = value ?? []; } = [];
    [JsonPropertyName("requiredSemantics")] public List<MauiRequiredSemantic> RequiredSemantics { get => field; init => field = value ?? []; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>A minimum version and feature set required from a named capability.</summary>
public sealed class MauiCapabilityRequirement
{
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("minimumVersion")] public int? MinimumVersion { get; init; }
    [JsonPropertyName("features")] public List<string> Features { get => field; init => field = value ?? []; } = [];
    [JsonPropertyName("required")] public bool Required { get; init; } = true;
    [JsonPropertyName("reason")] public string? Reason { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>A semantic contract that a runner must explicitly support rather than infer.</summary>
public sealed class MauiRequiredSemantic
{
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("minimumVersion")] public int? MinimumVersion { get; init; }
    [JsonPropertyName("required")] public bool Required { get; init; } = true;
    [JsonPropertyName("reason")] public string? Reason { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>The capabilities and semantics that a host or driver reports as available.</summary>
public sealed class MauiFlowCapabilitySet
{
    [JsonPropertyName("capabilities")] public List<MauiFlowCapability> Capabilities { get => field; init => field = value ?? []; } = [];
    [JsonPropertyName("semantics")] public List<MauiSupportedSemantic> Semantics { get => field; init => field = value ?? []; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>A capability advertised by a host or driver.</summary>
public sealed class MauiFlowCapability
{
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("version")] public int? Version { get; init; }
    [JsonPropertyName("features")] public List<string> Features { get => field; init => field = value ?? []; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>A semantic contract explicitly supported by a host or runner.</summary>
public sealed class MauiSupportedSemantic
{
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("version")] public int? Version { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>The result of checking plan requirements before any mutation is attempted.</summary>
public sealed class MauiFlowRequirementValidation
{
    public List<MauiFlowRequirementViolation> Errors { get; } = [];
    public List<MauiFlowRequirementViolation> Warnings { get; } = [];
    public bool IsValid => Errors.Count == 0;
}

/// <summary>A deterministic capability or semantic validation finding.</summary>
public sealed record MauiFlowRequirementViolation(string Code, string Requirement, string Message);

/// <summary>
/// Validates declared capability and required-semantic contracts. Unknown required semantics fail
/// closed so future semantics cannot silently acquire a mutation path.
/// </summary>
public static class MauiFlowRequirementValidator
{
    public static MauiFlowRequirementValidation Validate(
        MauiFlowRequirements? requirements,
        MauiFlowCapabilitySet? available)
    {
        var validation = new MauiFlowRequirementValidation();
        if (requirements is null)
            return validation;

        available ??= new MauiFlowCapabilitySet();

        foreach (var requirement in requirements.RequiredCapabilities)
            ValidateCapability(validation, requirement, available.Capabilities);

        foreach (var requirement in requirements.RequiredSemantics)
            ValidateSemantic(validation, requirement, available.Semantics);

        return validation;
    }

    private static void ValidateCapability(
        MauiFlowRequirementValidation validation,
        MauiCapabilityRequirement requirement,
        IReadOnlyList<MauiFlowCapability> available)
    {
        if (string.IsNullOrWhiteSpace(requirement.Name))
        {
            validation.Errors.Add(new(
                "capability-invalid",
                "capability",
                "A required capability must have a name."));
            return;
        }

        var capability = available.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, requirement.Name, StringComparison.Ordinal));
        if (capability is null)
        {
            AddCapabilityFinding(
                validation,
                requirement,
                $"Required capability '{requirement.Name}' is not available.");
            return;
        }

        if (requirement.MinimumVersion is { } minimumVersion &&
            (capability.Version is null || capability.Version < minimumVersion))
        {
            AddCapabilityFinding(
                validation,
                requirement,
                $"Required capability '{requirement.Name}' needs version {minimumVersion} or later.");
        }

        foreach (var feature in requirement.Features.Where(static feature => !string.IsNullOrWhiteSpace(feature)))
        {
            if (!capability.Features.Contains(feature, StringComparer.Ordinal))
            {
                AddCapabilityFinding(
                    validation,
                    requirement,
                    $"Required capability '{requirement.Name}' does not provide feature '{feature}'.");
            }
        }
    }

    private static void ValidateSemantic(
        MauiFlowRequirementValidation validation,
        MauiRequiredSemantic requirement,
        IReadOnlyList<MauiSupportedSemantic> available)
    {
        if (string.IsNullOrWhiteSpace(requirement.Name))
        {
            validation.Errors.Add(new(
                "required-semantics-invalid",
                "semantic",
                "A required semantic must have a name."));
            return;
        }

        var semantic = available.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, requirement.Name, StringComparison.Ordinal));
        if (semantic is null)
        {
            AddSemanticFinding(
                validation,
                requirement,
                $"Required semantic '{requirement.Name}' is not supported.");
            return;
        }

        if (requirement.MinimumVersion is { } minimumVersion &&
            (semantic.Version is null || semantic.Version < minimumVersion))
        {
            AddSemanticFinding(
                validation,
                requirement,
                $"Required semantic '{requirement.Name}' needs version {minimumVersion} or later.");
        }
    }

    private static void AddCapabilityFinding(
        MauiFlowRequirementValidation validation,
        MauiCapabilityRequirement requirement,
        string message)
    {
        var finding = new MauiFlowRequirementViolation("capability-missing", requirement.Name!, message);
        if (requirement.Required)
            validation.Errors.Add(finding);
        else
            validation.Warnings.Add(finding);
    }

    private static void AddSemanticFinding(
        MauiFlowRequirementValidation validation,
        MauiRequiredSemantic requirement,
        string message)
    {
        var finding = new MauiFlowRequirementViolation("required-semantics-unsupported", requirement.Name!, message);
        if (requirement.Required)
            validation.Errors.Add(finding);
        else
            validation.Warnings.Add(finding);
    }
}

/// <summary>A bounded approval for exploration during authoring.</summary>
public sealed class MauiExplorationBudget
{
    [JsonPropertyName("maxActions")] public int? MaxActions { get; init; }
    [JsonPropertyName("maxDurationSeconds")] public int? MaxDurationSeconds { get; init; }
    [JsonPropertyName("allowedScopes")] public List<string> AllowedScopes { get => field; init => field = value ?? []; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Records the actor, channel, and provider that supplied plan data or approval.</summary>
public sealed class MauiActorProvenance
{
    [JsonPropertyName("actorKind")] public string? ActorKind { get; init; }
    [JsonPropertyName("actorId")] public string? ActorId { get; init; }
    [JsonPropertyName("channel")] public string? Channel { get; init; }
    [JsonPropertyName("provider")] public string? Provider { get; init; }
    [JsonPropertyName("intent")] public string? Intent { get; init; }
    [JsonPropertyName("recordedAt")] public DateTimeOffset? RecordedAt { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>A review of a plan revision.</summary>
public sealed class MauiPlanReview
{
    [JsonPropertyName("reviewer")] public string? Reviewer { get; init; }
    [JsonPropertyName("status")] public string? Status { get; init; }
    [JsonPropertyName("reviewedAt")] public DateTimeOffset? ReviewedAt { get; init; }
    [JsonPropertyName("note")] public string? Note { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>An approval that may be scoped and time-bound.</summary>
public sealed class MauiPlanApproval
{
    [JsonPropertyName("approver")] public string? Approver { get; init; }
    [JsonPropertyName("scope")] public string? Scope { get; init; }
    [JsonPropertyName("approvedAt")] public DateTimeOffset? ApprovedAt { get; init; }
    [JsonPropertyName("expiresAt")] public DateTimeOffset? ExpiresAt { get; init; }
    [JsonPropertyName("digest")] public string? Digest { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>An independent business fact required to accept a run.</summary>
public sealed class MauiBusinessOracleRequirement
{
    [JsonPropertyName("oracleId")] public string? OracleId { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("required")] public bool Required { get; init; } = true;
    [JsonPropertyName("independent")] public bool Independent { get; init; } = true;
    [JsonPropertyName("evidenceKind")] public string? EvidenceKind { get; init; }
    [JsonPropertyName("reference")] public string? Reference { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>State that must be checked before a flow is considered safe to replay.</summary>
public sealed class MauiFlowCheckpointRequirements
{
    [JsonPropertyName("appBuildFingerprint")] public string? AppBuildFingerprint { get; init; }
    [JsonPropertyName("seedFingerprint")] public string? SeedFingerprint { get; init; }
    [JsonPropertyName("backendStateFingerprint")] public string? BackendStateFingerprint { get; init; }
    [JsonPropertyName("appStateSeed")] public MauiFlowAppStateSeedFingerprint? AppStateSeed { get; init; }
    [JsonPropertyName("backendTestDataSeed")] public MauiFlowBackendTestDataSeedFingerprint? BackendTestDataSeed { get; init; }
    [JsonPropertyName("locale")] public string? Locale { get; init; }
    [JsonPropertyName("theme")] public string? Theme { get; init; }
    [JsonPropertyName("orientation")] public string? Orientation { get; init; }
    [JsonPropertyName("route")] public string? Route { get; init; }
    [JsonPropertyName("window")] public string? Window { get; init; }
    [JsonPropertyName("modal")] public string? Modal { get; init; }
    [JsonPropertyName("collectionItemKey")] public string? CollectionItemKey { get; init; }
    [JsonPropertyName("displayProfile")] public string? DisplayProfile { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Known side-effect policies for a non-executable test plan.</summary>
public static class MauiFlowSideEffectPolicies
{
    public const string Unspecified = "unspecified";
    public const string None = "none";

    /// <summary>
    /// The flow mutates app-local state only and a reset owner restores it in place. Distinct from
    /// <see cref="TestTenantResettable"/>, which asserts that a backend test tenant was also reset.
    /// </summary>
    public const string AppStateResettable = "app-state-resettable";
    public const string TestTenantResettable = "test-tenant-resettable";
    public const string Compensated = "compensated";
    public const string NonReplayable = "non-replayable";

    public static bool IsKnown(string? value) =>
        value is None or AppStateResettable or TestTenantResettable or Compensated or NonReplayable;

    public static MauiFlowSideEffectPolicy Parse(string? value) => value switch
    {
        None => MauiFlowSideEffectPolicy.None,
        AppStateResettable => MauiFlowSideEffectPolicy.AppStateResettable,
        TestTenantResettable => MauiFlowSideEffectPolicy.TestTenantResettable,
        Compensated => MauiFlowSideEffectPolicy.Compensated,
        NonReplayable => MauiFlowSideEffectPolicy.NonReplayable,
        _ => MauiFlowSideEffectPolicy.Unspecified,
    };

    public static string ToWireValue(MauiFlowSideEffectPolicy value) => value switch
    {
        MauiFlowSideEffectPolicy.None => None,
        MauiFlowSideEffectPolicy.AppStateResettable => AppStateResettable,
        MauiFlowSideEffectPolicy.TestTenantResettable => TestTenantResettable,
        MauiFlowSideEffectPolicy.Compensated => Compensated,
        MauiFlowSideEffectPolicy.NonReplayable => NonReplayable,
        _ => Unspecified,
    };
}
