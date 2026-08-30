using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Maui.DevFlow.Testing;

/// <summary>
/// A human-reviewable proposal to replace a selector after a pre-dispatch locator-not-found
/// failure. This contract does not apply a patch or execute a validation replay.
/// </summary>
public sealed class MauiFlowRepairProposal
{
    [JsonPropertyName("schema")] public int Schema { get; init; } = 1;
    [JsonPropertyName("proposalId")] public string? ProposalId { get; init; }
    [JsonPropertyName("revision")] public int? Revision { get; init; }
    [JsonPropertyName("state")] public string? State { get; init; }
    [JsonPropertyName("createdAt")] public DateTimeOffset? CreatedAt { get; init; }
    [JsonPropertyName("sourceRunId")] public string? SourceRunId { get; init; }
    [JsonPropertyName("sourceStepId")] public string? SourceStepId { get; init; }
    [JsonPropertyName("sourceFailureId")] public string? SourceFailureId { get; init; }
    [JsonPropertyName("sourceFailureCode")] public string? SourceFailureCode { get; init; }
    [JsonPropertyName("preDispatch")] public bool? PreDispatch { get; init; }
    [JsonPropertyName("baseFlow")] public MauiFlowReference? BaseFlow { get; init; }
    [JsonPropertyName("oldSelector")] public FlowSelector? OldSelector { get; init; }
    [JsonPropertyName("proposedSelector")] public FlowSelector? ProposedSelector { get; init; }
    [JsonPropertyName("candidate")] public MauiSelectorCandidate? Candidate { get; init; }
    [JsonPropertyName("uniquenessProof")] public MauiRepairUniquenessProof? UniquenessProof { get; init; }
    [JsonPropertyName("validationRunIds")] public List<string> ValidationRunIds { get; init; } = [];
    [JsonPropertyName("patch")] public MauiFlowPatch? Patch { get; init; }
    [JsonPropertyName("patchDigest")] public string? PatchDigest { get; init; }
    [JsonPropertyName("diff")] public MauiRepairSelectorDiff? Diff { get; init; }
    [JsonPropertyName("unchangedAssertionsProof")] public MauiRepairAssertionProof? UnchangedAssertionsProof { get; init; }
    [JsonPropertyName("approval")] public MauiRepairApproval? Approval { get; init; }
    [JsonPropertyName("trust")] public string? Trust { get; init; }
    [JsonPropertyName("expiresAt")] public DateTimeOffset? ExpiresAt { get; init; }
    [JsonPropertyName("reviewer")] public string? Reviewer { get; init; }
    [JsonPropertyName("grantDigest")] public string? GrantDigest { get; init; }
    [JsonPropertyName("verificationRunIds")] public List<string> VerificationRunIds { get; init; } = [];
    [JsonPropertyName("riskFlags")] public List<string> RiskFlags { get; init; } = [];
    [JsonPropertyName("provenance")] public MauiActorProvenance? Provenance { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Evidence about an element without retaining user-entered text or values by default.</summary>
public sealed class MauiElementFingerprint
{
    [JsonPropertyName("schema")] public int Schema { get; set; } = 1;
    [JsonPropertyName("fingerprintId")] public string? FingerprintId { get; set; }
    [JsonPropertyName("appId")] public string? AppId { get; init; }
    [JsonPropertyName("buildFingerprint")] public string? BuildFingerprint { get; init; }
    [JsonPropertyName("platform")] public string? Platform { get; init; }
    [JsonPropertyName("route")] public string? Route { get; init; }
    [JsonPropertyName("window")] public string? Window { get; init; }
    [JsonPropertyName("modal")] public string? Modal { get; init; }
    [JsonPropertyName("managedType")] public string? ManagedType { get; init; }
    [JsonPropertyName("fullType")] public string? FullType { get; init; }
    [JsonPropertyName("role")] public string? Role { get; init; }
    [JsonPropertyName("traits")] public List<string> Traits { get; init; } = [];
    [JsonPropertyName("automationId")] public string? AutomationId { get; init; }
    [JsonPropertyName("nativeAutomationId")] public string? NativeAutomationId { get; init; }
    [JsonPropertyName("sourceAnchor")] public string? SourceAnchor { get; init; }
    [JsonPropertyName("sourceHash")] public string? SourceHash { get; init; }
    [JsonPropertyName("sourceConfidence")] public string? SourceConfidence { get; init; }
    [JsonPropertyName("ancestorTopologyHash")] public string? AncestorTopologyHash { get; init; }
    [JsonPropertyName("siblingTopologyHash")] public string? SiblingTopologyHash { get; init; }
    [JsonPropertyName("collectionKey")] public string? CollectionKey { get; init; }
    [JsonPropertyName("itemKey")] public string? ItemKey { get; init; }
    [JsonPropertyName("normalizedBounds")] public MauiNormalizedBounds? NormalizedBounds { get; init; }
    [JsonPropertyName("observedAt")] public DateTimeOffset? ObservedAt { get; init; }
    [JsonPropertyName("capabilityVersion")] public string? CapabilityVersion { get; init; }
    [JsonPropertyName("locale")] public string? Locale { get; init; }
    [JsonPropertyName("theme")] public string? Theme { get; init; }
    [JsonPropertyName("orientation")] public string? Orientation { get; init; }
    [JsonPropertyName("displayProfile")] public string? DisplayProfile { get; init; }
    [JsonPropertyName("context")] public MauiElementFingerprintContext Context { get; set; } = new();
    [JsonPropertyName("managed")] public MauiManagedElementIdentity Managed { get; set; } = new();
    [JsonPropertyName("native")] public MauiNativeAutomationIdentity? Native { get; set; }
    [JsonPropertyName("source")] public MauiSourceAnchor? Source { get; set; }
    [JsonPropertyName("topology")] public MauiTopologySignature Topology { get; set; } = new();
    [JsonPropertyName("collection")] public MauiCollectionIdentity? Collection { get; set; }
    [JsonPropertyName("evidenceRefs")] public List<string> EvidenceRefs { get; set; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Bounds normalized to the current window or display profile.</summary>
public sealed class MauiNormalizedBounds
{
    [JsonPropertyName("x")] public double? X { get; init; }
    [JsonPropertyName("y")] public double? Y { get; init; }
    [JsonPropertyName("width")] public double? Width { get; init; }
    [JsonPropertyName("height")] public double? Height { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>A scored selector candidate that is not automatically activated or applied.</summary>
public sealed class MauiSelectorCandidate
{
    [JsonPropertyName("schema")] public int Schema { get; set; } = 1;
    [JsonPropertyName("candidateId")] public string? CandidateId { get; set; }
    [JsonPropertyName("rank")] public int? Rank { get; set; }
    [JsonPropertyName("priority")] public int? Priority { get; set; }
    [JsonPropertyName("selector")] public FlowSelector? Selector { get; set; }
    [JsonPropertyName("selectorDescriptor")] public MauiSelectorCandidateSelector SelectorDescriptor { get; set; } = new();
    [JsonPropertyName("scope")] public MauiFlowCheckpoint? Scope { get; init; }
    [JsonPropertyName("scopeDescriptor")] public MauiSelectorCandidateScope ScopeDescriptor { get; set; } = new();
    [JsonPropertyName("origin")] public string? Origin { get; set; }
    [JsonPropertyName("originCodes")] public List<string> OriginCodes { get; init; } = [];
    [JsonPropertyName("rationaleCodes")] public List<string> RationaleCodes { get; init; } = [];
    [JsonPropertyName("riskFlags")] public List<string> RiskFlags { get; init; } = [];
    [JsonPropertyName("score")] public double? Score { get; init; }
    [JsonPropertyName("scoreComponents")] public Dictionary<string, double> ScoreComponents { get; init; } = new(StringComparer.Ordinal);
    [JsonPropertyName("unique")] public bool? Unique { get; init; }
    [JsonPropertyName("platformValidated")] public bool? PlatformValidated { get; init; }
    [JsonPropertyName("calibrationStatus")] public string? CalibrationStatus { get; init; }
    [JsonPropertyName("scores")] public MauiSelectorCandidateScores Scores { get; set; } = new();
    [JsonPropertyName("validation")] public MauiSelectorCandidateValidation Validation { get; set; } = new();
    [JsonPropertyName("calibration")] public MauiSelectorCandidateCalibration Calibration { get; set; } = new();
    [JsonPropertyName("fingerprint")] public MauiElementFingerprint? Fingerprint { get; init; }
    [JsonPropertyName("evidenceRefs")] public List<string> EvidenceRefs { get; set; } = [];
    [JsonPropertyName("evidenceArtifacts")] public List<MauiFlowArtifactReference> EvidenceArtifacts { get; init; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Evidence that a proposed selector resolved to exactly one target.</summary>
public sealed class MauiRepairUniquenessProof
{
    [JsonPropertyName("matchCount")] public int? MatchCount { get; init; }
    [JsonPropertyName("validatedAt")] public DateTimeOffset? ValidatedAt { get; init; }
    [JsonPropertyName("platform")] public string? Platform { get; init; }
    [JsonPropertyName("evidenceArtifacts")] public List<MauiFlowArtifactReference> EvidenceArtifacts { get; init; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>A minimal, descriptive patch for a future host to apply after approval.</summary>
public sealed class MauiFlowPatch
{
    [JsonPropertyName("format")] public string? Format { get; init; }
    [JsonPropertyName("selectorOnly")] public bool? SelectorOnly { get; init; }
    [JsonPropertyName("beforeDigest")] public string? BeforeDigest { get; init; }
    [JsonPropertyName("afterDigest")] public string? AfterDigest { get; init; }
    [JsonPropertyName("operations")] public List<MauiFlowPatchOperation> Operations { get; init; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>One declarative operation in a proposed flow patch.</summary>
public sealed class MauiFlowPatchOperation
{
    [JsonPropertyName("op")] public string? Op { get; init; }
    [JsonPropertyName("path")] public string? Path { get; init; }
    [JsonPropertyName("value")] public JsonElement? Value { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Evidence that the proposal did not change the flow's hard assertions.</summary>
public sealed class MauiRepairAssertionProof
{
    [JsonPropertyName("unchanged")] public bool? Unchanged { get; init; }
    [JsonPropertyName("assertionDigest")] public string? AssertionDigest { get; init; }
    [JsonPropertyName("actionsUnchanged")] public bool? ActionsUnchanged { get; init; }
    [JsonPropertyName("actionDigest")] public string? ActionDigest { get; init; }
    [JsonPropertyName("valuesUnchanged")] public bool? ValuesUnchanged { get; init; }
    [JsonPropertyName("valueDigest")] public string? ValueDigest { get; init; }
    [JsonPropertyName("orderUnchanged")] public bool? OrderUnchanged { get; init; }
    [JsonPropertyName("orderDigest")] public string? OrderDigest { get; init; }
    [JsonPropertyName("method")] public string? Method { get; init; }
    [JsonPropertyName("verificationRunIds")] public List<string> VerificationRunIds { get; init; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>
/// A minimal selector-only review projection. The JSON and Markdown forms are deterministic and
/// intentionally exclude source excerpts, prompts, screenshots, secret values, and app changes.
/// </summary>
public sealed class MauiRepairSelectorDiff
{
    [JsonPropertyName("json")] public string? Json { get; init; }
    [JsonPropertyName("markdown")] public string? Markdown { get; init; }
    [JsonPropertyName("stepId")] public string? StepId { get; init; }
    [JsonPropertyName("selectorPath")] public string? SelectorPath { get; init; }
    [JsonPropertyName("assertionsUnchanged")] public bool? AssertionsUnchanged { get; init; }
    [JsonPropertyName("actionsUnchanged")] public bool? ActionsUnchanged { get; init; }
    [JsonPropertyName("valuesUnchanged")] public bool? ValuesUnchanged { get; init; }
    [JsonPropertyName("orderUnchanged")] public bool? OrderUnchanged { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>The approval state and expiration for a repair proposal.</summary>
public sealed class MauiRepairApproval
{
    [JsonPropertyName("state")] public string? State { get; init; }
    [JsonPropertyName("reviewer")] public string? Reviewer { get; init; }
    [JsonPropertyName("approvedAt")] public DateTimeOffset? ApprovedAt { get; init; }
    [JsonPropertyName("expiresAt")] public DateTimeOffset? ExpiresAt { get; init; }
    [JsonPropertyName("reason")] public string? Reason { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Records the immutable result of a repair proposal after review or application.</summary>
public sealed class MauiFlowRepairOutcome
{
    [JsonPropertyName("schema")] public int Schema { get; init; } = 1;
    [JsonPropertyName("proposalId")] public string? ProposalId { get; init; }
    [JsonPropertyName("state")] public string? State { get; init; }
    [JsonPropertyName("newFlowRevision")] public int? NewFlowRevision { get; init; }
    [JsonPropertyName("verificationRunIds")] public List<string> VerificationRunIds { get; init; } = [];
    [JsonPropertyName("rollbackRevision")] public int? RollbackRevision { get; init; }
    [JsonPropertyName("reviewer")] public string? Reviewer { get; init; }
    [JsonPropertyName("recordedAt")] public DateTimeOffset? RecordedAt { get; init; }
    [JsonPropertyName("safeAuditDigest")] public string? SafeAuditDigest { get; init; }
    [JsonPropertyName("reason")] public string? Reason { get; init; }
    [JsonPropertyName("replayEligibility")] public MauiFlowReplayEligibilityDecision? ReplayEligibility { get; init; }
    [JsonPropertyName("verification")] public MauiFlowRunVerification? Verification { get; init; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Known repair outcome values.</summary>
public static class MauiFlowRepairOutcomeStates
{
    public const string Proposed = "proposed";
    public const string Previewed = "previewed";
    public const string Approved = "approved";
    public const string Applying = "applying";
    public const string Rejected = "rejected";
    public const string Stale = "stale";
    public const string Applied = "applied";
    public const string Verified = "verified";
    public const string ApprovalExpired = "approval-expired";
    public const string VerificationFailed = "verification-failed";
    public const string RollbackRequired = "rollback-required";
    public const string Reverted = "reverted";
    public const string RollbackFailed = "rollback-failed";
}
