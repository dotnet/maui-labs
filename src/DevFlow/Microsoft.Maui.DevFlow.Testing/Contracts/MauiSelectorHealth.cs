using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Testing;

/// <summary>App, route, window, locale, and display facts observed with a fingerprint.</summary>
public sealed class MauiElementFingerprintContext
{
    [JsonPropertyName("appId")] public string? AppId { get; set; }
    [JsonPropertyName("appBuild")] public string? AppBuild { get; set; }
    [JsonPropertyName("platform")] public string? Platform { get; set; }
    [JsonPropertyName("route")] public string? Route { get; set; }
    [JsonPropertyName("window")] public string? Window { get; set; }
    [JsonPropertyName("modal")] public string? Modal { get; set; }
    [JsonPropertyName("locale")] public string? Locale { get; set; }
    [JsonPropertyName("theme")] public string? Theme { get; set; }
    [JsonPropertyName("orientation")] public string? Orientation { get; set; }
    [JsonPropertyName("displayProfile")] public string? DisplayProfile { get; set; }
}

/// <summary>Managed MAUI identity facts. It intentionally has no Text or Value member.</summary>
public sealed class MauiManagedElementIdentity
{
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("fullType")] public string? FullType { get; set; }
    [JsonPropertyName("framework")] public string? Framework { get; set; }
    [JsonPropertyName("role")] public string? Role { get; set; }
    [JsonPropertyName("traits")] public List<string> Traits { get; set; } = [];
    [JsonPropertyName("automationId")] public string? AutomationId { get; set; }
}

/// <summary>An authoritative platform automation identity when one was observed.</summary>
public sealed class MauiNativeAutomationIdentity
{
    [JsonPropertyName("identity")] public string? Identity { get; set; }
    [JsonPropertyName("kind")] public string? Kind { get; set; }
    [JsonPropertyName("authoritative")] public bool Authoritative { get; set; }
}

/// <summary>A conservative source-map anchor. <c>state</c> is current, stale, ambiguous, or missing.</summary>
public sealed class MauiSourceAnchor
{
    [JsonPropertyName("file")] public string? File { get; set; }
    [JsonPropertyName("line")] public int? Line { get; set; }
    [JsonPropertyName("column")] public int? Column { get; set; }
    [JsonPropertyName("buildHash")] public string? BuildHash { get; set; }
    [JsonPropertyName("currentHash")] public string? CurrentHash { get; set; }
    [JsonPropertyName("state")] public string? State { get; set; }
    [JsonPropertyName("confidence")] public string? Confidence { get; set; }
}

/// <summary>Hash-based ancestor, sibling, and child signatures; no runtime ids are persisted.</summary>
public sealed class MauiTopologySignature
{
    [JsonPropertyName("ancestorHash")] public string? AncestorHash { get; set; }
    [JsonPropertyName("siblingHash")] public string? SiblingHash { get; set; }
    [JsonPropertyName("childHash")] public string? ChildHash { get; set; }
    [JsonPropertyName("stableAncestorAutomationId")] public string? StableAncestorAutomationId { get; set; }
}

/// <summary>Collection/template facts used to avoid promoting virtualized or indexed rows.</summary>
public sealed class MauiCollectionIdentity
{
    [JsonPropertyName("scope")] public string? Scope { get; set; }
    [JsonPropertyName("itemKey")] public string? ItemKey { get; set; }
    [JsonPropertyName("templateKind")] public string? TemplateKind { get; set; }
    [JsonPropertyName("virtualized")] public bool? Virtualized { get; set; }
}

/// <summary>A candidate selector representation separate from the one active flow selector.</summary>
public sealed class MauiSelectorCandidateSelector
{
    [JsonPropertyName("kind")] public string? Kind { get; set; }
    [JsonPropertyName("automationId")] public string? AutomationId { get; set; }
    [JsonPropertyName("stableItemKey")] public string? StableItemKey { get; set; }
    [JsonPropertyName("nativeAutomationIdentity")] public string? NativeAutomationIdentity { get; set; }
    [JsonPropertyName("role")] public string? Role { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("ancestorAutomationId")] public string? AncestorAutomationId { get; set; }
    [JsonPropertyName("sourceAnchor")] public string? SourceAnchor { get; set; }
    /// <summary>
    /// Present only when a caller explicitly supplied both exact text and a locale assumption.
    /// It is never populated from a default fingerprint.
    /// </summary>
    [JsonPropertyName("exactText")] public string? ExactText { get; set; }
}

/// <summary>Route/window/collection constraints that qualify a candidate.</summary>
public sealed class MauiSelectorCandidateScope
{
    [JsonPropertyName("route")] public string? Route { get; set; }
    [JsonPropertyName("window")] public string? Window { get; set; }
    [JsonPropertyName("modal")] public string? Modal { get; set; }
    [JsonPropertyName("collectionScope")] public string? CollectionScope { get; set; }
    [JsonPropertyName("localeAssumption")] public string? LocaleAssumption { get; set; }
}

/// <summary>
/// Transparent deterministic score contributions. <see cref="DeterministicRankScore"/> is a rule
/// score, not a probability or calibrated confidence.
/// </summary>
public sealed class MauiSelectorCandidateScores
{
    [JsonPropertyName("ruleVersion")] public string RuleVersion { get; set; } = MauiSelectorHealthRules.RankerRuleVersion;
    [JsonPropertyName("appOwnedIdentifier")] public double AppOwnedIdentifier { get; set; }
    [JsonPropertyName("scopeMatch")] public double ScopeMatch { get; set; }
    [JsonPropertyName("managedNativeAgreement")] public double ManagedNativeAgreement { get; set; }
    [JsonPropertyName("sourceAnchorMatch")] public double SourceAnchorMatch { get; set; }
    [JsonPropertyName("topologySimilarity")] public double TopologySimilarity { get; set; }
    [JsonPropertyName("normalizedGeometryCorroboration")] public double NormalizedGeometryCorroboration { get; set; }
    [JsonPropertyName("localizationPenalty")] public double LocalizationPenalty { get; set; }
    [JsonPropertyName("virtualizationPenalty")] public double VirtualizationPenalty { get; set; }
    [JsonPropertyName("staleSourcePenalty")] public double StaleSourcePenalty { get; set; }
    [JsonPropertyName("platformDivergencePenalty")] public double PlatformDivergencePenalty { get; set; }
    [JsonPropertyName("ambiguityPenalty")] public double AmbiguityPenalty { get; set; }
    [JsonPropertyName("deterministicRankScore")] public double DeterministicRankScore { get; set; }
}

/// <summary>Current uniqueness and platform/source validation facts for a candidate.</summary>
public sealed class MauiSelectorCandidateValidation
{
    [JsonPropertyName("unique")] public bool? Unique { get; set; }
    [JsonPropertyName("matchCount")] public int? MatchCount { get; set; }
    [JsonPropertyName("platformState")] public string? PlatformState { get; set; }
    [JsonPropertyName("sourceState")] public string? SourceState { get; set; }
    [JsonPropertyName("accepted")] public bool Accepted { get; set; }
    [JsonPropertyName("rejectionReason")] public string? RejectionReason { get; set; }
}

/// <summary>Calibration is deliberately unavailable until later benchmark gates.</summary>
public sealed class MauiSelectorCandidateCalibration
{
    [JsonPropertyName("state")] public string State { get; set; } = MauiSelectorHealthRules.Uncalibrated;
    [JsonPropertyName("ruleVersion")] public string RuleVersion { get; set; } = MauiSelectorHealthRules.RankerRuleVersion;
}

/// <summary>Evidence attached to a recorded flow step without changing its active selector.</summary>
public sealed class MauiSelectorEvidence
{
    [JsonPropertyName("schema")] public int Schema { get; set; } = 1;
    [JsonPropertyName("fingerprint")] public MauiElementFingerprint? Fingerprint { get; set; }
    [JsonPropertyName("candidates")] public List<MauiSelectorCandidate> Candidates { get; set; } = [];
    [JsonPropertyName("omissions")] public List<MauiSelectorEvidenceOmission> Omissions { get; set; } = [];
}

/// <summary>An explicit reason selector evidence omitted a candidate or live fact.</summary>
public sealed class MauiSelectorEvidenceOmission
{
    [JsonPropertyName("kind")] public string? Kind { get; set; }
    [JsonPropertyName("reason")] public string? Reason { get; set; }
    [JsonPropertyName("count")] public int? Count { get; set; }
}

/// <summary>Options for deterministic candidate generation. Text is opt-in and locale-bound.</summary>
public sealed class MauiSelectorCandidateGenerationOptions
{
    public int MaxCandidates { get; set; } = 8;
    public string? LocaleAssumption { get; set; }
    public string? ExactText { get; set; }
    /// <summary>Authoritative exact-text match count supplied by an explicit live validation.</summary>
    public int? ExactTextMatchCount { get; set; }
    public string? CurrentSourceHash { get; set; }
    public bool PlatformDivergent { get; set; }
}

/// <summary>The bounded result of candidate generation, including explicit rejected forms.</summary>
public sealed class MauiSelectorCandidateGenerationResult
{
    public MauiElementFingerprint? Fingerprint { get; init; }
    public List<MauiSelectorCandidate> Candidates { get; init; } = [];
    public List<MauiSelectorEvidenceOmission> Omissions { get; init; } = [];
}

/// <summary>Stable diagnostic identifiers emitted by <see cref="MauiSelectorHealthAnalyzer"/>.</summary>
public static class MauiSelectorHealthDiagnosticIds
{
    public const string DuplicateAutomationId = "DFSH001";
    public const string MissingDurableId = "DFSH002";
    public const string RuntimeIdOrTypeIndex = "DFSH003";
    public const string LocalizedOrDynamicText = "DFSH004";
    public const string TemplateOrVirtualization = "DFSH005";
    public const string SourceAnchor = "DFSH006";
    public const string ManagedNativeDivergence = "DFSH007";
    public const string RequiredPlatform = "DFSH008";
    public const string MissingHardPostcondition = "DFSH009";
    public const string AcceptanceCriterionUncovered = "DFSH010";
    public const string CoverageSummary = "DFSH011";
}

/// <summary>Pure input to the deterministic selector-health analyzer.</summary>
public sealed class MauiSelectorHealthAnalysisInput
{
    public MauiFlow? Flow { get; set; }
    public MauiTestPlan? Plan { get; set; }
    public List<MauiSelectorObservationElement> LiveElements { get; set; } = [];
    public MauiSelectorObservationContext? Context { get; set; }
    public List<MauiSelectorHealthPlatformSnapshot> PlatformSnapshots { get; set; } = [];
    public List<MauiFlowRunReport> RunHistory { get; set; } = [];
    public bool LiveTreeComplete { get; set; } = true;
}

/// <summary>Candidate/fingerprint facts captured for one platform without executing a replay.</summary>
public sealed class MauiSelectorHealthPlatformSnapshot
{
    public string? Platform { get; set; }
    public List<MauiElementFingerprint> Fingerprints { get; set; } = [];
    public List<MauiSelectorCandidate> Candidates { get; set; } = [];
}

/// <summary>Deterministic selector-health analysis result.</summary>
public sealed class MauiSelectorHealthAnalysis
{
    public string RuleVersion { get; set; } = MauiSelectorHealthRules.RuleVersion;
    public List<MauiSelectorHealthFinding> Findings { get; set; } = [];
    public List<MauiSelectorCoverageSummary> Coverage { get; set; } = [];
}

/// <summary>A deterministic diagnostic with source, step, platform, and evidence links.</summary>
public sealed class MauiSelectorHealthFinding
{
    public string DiagnosticId { get; set; } = "";
    public string FindingId { get; set; } = "";
    public string Severity { get; set; } = "info";
    public string Category { get; set; } = "";
    public string? StepId { get; set; }
    public string? Source { get; set; }
    public List<string> Platforms { get; set; } = [];
    public string Message { get; set; } = "";
    public List<string> RationaleCodes { get; set; } = [];
    public List<string> EvidenceRefs { get; set; } = [];
}

/// <summary>Selector coverage grouped by observed route and platform.</summary>
public sealed class MauiSelectorCoverageSummary
{
    public string? Platform { get; set; }
    public string? Route { get; set; }
    public int TotalTargets { get; set; }
    public int DurableTargets { get; set; }
    public int FragileTargets { get; set; }
    public int MissingTargets { get; set; }
}

/// <summary>Versions and fixed non-probabilistic policy constants used by selector health.</summary>
public static class MauiSelectorHealthRules
{
    public const string RuleVersion = "selector-health-v1";
    public const string RankerRuleVersion = "selector-ranker-v1";
    public const string Uncalibrated = "uncalibrated";
}
