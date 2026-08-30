using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Maui.DevFlow.Testing;

/// <summary>Terminal states for an engineering-preview qualification gate.</summary>
public static class MauiPreviewQualificationStates
{
    public const string Pass = "pass";
    public const string Fail = "fail";
    public const string NotQualified = "not-qualified";
}

/// <summary>Known origins for a qualification sample.</summary>
public static class MauiQualificationSampleSources
{
    public const string Curated = "curated";

    /// <summary>
    /// A curated case adapted from another curated case. Kept separate from <see cref="Curated"/>
    /// because N derivations of one seed are one piece of evidence repeated, not N trials, and a
    /// Wilson interval computed over them would claim a confidence the sample cannot support.
    /// </summary>
    public const string CuratedDerived = "curated-derived";

    public const string Generated = "generated";
    public const string DeviceBacked = "device-backed";

    public static bool IsKnown(string? value) =>
        value is Curated or CuratedDerived or Generated or DeviceBacked;

    /// <summary>
    /// True when a sample from this source counts toward a gate's minimum-evaluation requirement.
    /// Derived and generated samples are reported but never counted as independent trials.
    /// </summary>
    public static bool IsIndependent(string? value) =>
        value is Curated or DeviceBacked;

    /// <summary>
    /// True when this source's evidence is a property of the corpus files rather than of a run.
    /// Every accumulated run is required to share a corpus fingerprint, so static evidence is
    /// byte-identical across runs: re-evaluating it is a repeat, never a fresh trial, and the
    /// accumulator must count it exactly once no matter how many runs report it.
    /// </summary>
    public static bool IsStatic(string? value) =>
        value is Curated or CuratedDerived or Generated;
}

/// <summary>
/// A versioned, redacted qualification report for the DevFlow engineering preview. The Android
/// gate is authoritative only for its declared Android scope; Apple manifests are advisory
/// evidence projections and never relabel platform coverage.
/// It is evidence accounting only; it neither replays a flow nor applies a repair or source patch.
/// </summary>
public sealed class MauiPreviewQualificationReport
{
    [JsonPropertyName("schema")] public int Schema { get; set; } = 1;
    [JsonPropertyName("kind")] public string Kind { get; set; } = "maui-preview-qualification";
    [JsonPropertyName("contractVersion")] public string ContractVersion { get; set; } = "preview-qualification-v1";
    [JsonPropertyName("generatedAt")] public DateTimeOffset GeneratedAt { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = MauiPreviewQualificationStates.NotQualified;
    [JsonPropertyName("platform")] public string? Platform { get; set; }
    [JsonPropertyName("fingerprints")] public MauiQualificationFingerprints Fingerprints { get; set; } = new();
    [JsonPropertyName("profiles")] public List<MauiQualificationPlatformProfile> Profiles { get; set; } = [];
    [JsonPropertyName("appleQa")] public MauiQualificationAppleQaEvidence? AppleQa { get; set; }
    [JsonPropertyName("featureFlags")] public MauiPreviewFeatureFlags FeatureFlags { get; set; } = MauiPreviewFeatureFlags.CreateDefault();
    [JsonPropertyName("review")] public MauiQualificationReviewEvidence Review { get; set; } = new();
    [JsonPropertyName("corpus")] public MauiQualificationCorpusSummary Corpus { get; set; } = new();
    [JsonPropertyName("metrics")] public MauiQualificationMetrics Metrics { get; set; } = new();
    [JsonPropertyName("thresholds")] public MauiQualificationGateThresholds Thresholds { get; set; } = new();
    [JsonPropertyName("gates")] public List<MauiQualificationGateResult> Gates { get; set; } = [];
    [JsonPropertyName("reasons")] public List<MauiQualificationReason> Reasons { get; set; } = [];
    [JsonPropertyName("artifactRefs")] public List<MauiQualificationArtifactReference> ArtifactRefs { get; set; } = [];
    [JsonPropertyName("exclusions")] public List<MauiQualificationExclusion> Exclusions { get; set; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Input supplied to the pure qualification evaluator or its read-only CLI adapter.</summary>
public sealed class MauiPreviewQualificationInput
{
    [JsonPropertyName("schema")] public int Schema { get; set; } = 1;
    [JsonPropertyName("platform")] public string? Platform { get; set; }
    [JsonPropertyName("fingerprints")] public MauiQualificationFingerprints Fingerprints { get; set; } = new();
    [JsonPropertyName("profiles")] public List<MauiQualificationPlatformProfile> Profiles { get; set; } = [];
    [JsonPropertyName("appleQa")] public MauiQualificationAppleQaEvidence? AppleQa { get; set; }
    [JsonPropertyName("featureFlags")] public MauiPreviewFeatureFlags? FeatureFlags { get; set; }
    [JsonPropertyName("review")] public MauiQualificationReviewEvidence? Review { get; set; }
    [JsonPropertyName("corpus")] public MauiQualificationCorpusSummary? Corpus { get; set; }
    [JsonPropertyName("samples")] public List<MauiQualificationExecutionSample> Samples { get; set; } = [];
    [JsonPropertyName("evidence")] public MauiQualificationRequiredEvidence? Evidence { get; set; }
    [JsonPropertyName("runtimeOverhead")] public MauiQualificationRuntimeOverheadMetric? RuntimeOverhead { get; set; }
    [JsonPropertyName("privacySecurity")] public MauiQualificationPrivacySecurityMetric? PrivacySecurity { get; set; }
    [JsonPropertyName("artifactRefs")] public List<MauiQualificationArtifactReference> ArtifactRefs { get; set; } = [];
    [JsonPropertyName("exclusions")] public List<MauiQualificationExclusion> Exclusions { get; set; } = [];
    [JsonPropertyName("tier1Flows")] public List<string> Tier1Flows { get; set; } = [];
    [JsonPropertyName("thresholds")] public MauiQualificationGateThresholds? Thresholds { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Immutable identity facts used to bind a report to a corpus, build, and policy.</summary>
public sealed class MauiQualificationFingerprints
{
    [JsonPropertyName("corpusVersion")] public string? CorpusVersion { get; set; }
    [JsonPropertyName("corpusFingerprint")] public string? CorpusFingerprint { get; set; }
    [JsonPropertyName("repositoryCommit")] public string? RepositoryCommit { get; set; }
    [JsonPropertyName("testingPackageVersion")] public string? TestingPackageVersion { get; set; }
    [JsonPropertyName("packageId")] public string? PackageId { get; set; }
    [JsonPropertyName("packageFingerprint")] public string? PackageFingerprint { get; set; }
    [JsonPropertyName("toolVersion")] public string? ToolVersion { get; set; }
    [JsonPropertyName("toolFingerprint")] public string? ToolFingerprint { get; set; }
    [JsonPropertyName("policyVersion")] public string? PolicyVersion { get; set; } = "preview-qualification-policy-v1";
    [JsonPropertyName("policyFingerprint")] public string? PolicyFingerprint { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Declared platform, build, device, seed, and execution-mode facts for a sample scope.</summary>
public sealed class MauiQualificationPlatformProfile
{
    [JsonPropertyName("platform")] public string? Platform { get; set; }
    [JsonPropertyName("scope")] public string? Scope { get; set; }
    [JsonPropertyName("deviceEvidenceKind")] public string? DeviceEvidenceKind { get; set; }
    [JsonPropertyName("realDevice")] public bool? RealDevice { get; set; }
    [JsonPropertyName("deviceFingerprint")] public string? DeviceFingerprint { get; set; }
    [JsonPropertyName("runtimeFingerprint")] public string? RuntimeFingerprint { get; set; }
    [JsonPropertyName("buildFingerprint")] public string? BuildFingerprint { get; set; }
    [JsonPropertyName("packageFingerprint")] public string? PackageFingerprint { get; set; }
    [JsonPropertyName("seedFingerprint")] public string? SeedFingerprint { get; set; }
    [JsonPropertyName("backendStateFingerprint")] public string? BackendStateFingerprint { get; set; }
    [JsonPropertyName("firstAttemptMode")] public string? FirstAttemptMode { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>
/// Redacted Apple XCTest flow-QA facts adapted from a versioned <c>devflow-flow-qa</c> manifest.
/// The adapter retains source field meanings; it never converts simulator evidence into physical
/// device evidence or turns an experimental AppKit lane into Mac Catalyst coverage.
/// </summary>
public sealed class MauiQualificationAppleQaEvidence
{
    [JsonPropertyName("contractVersion")] public string ContractVersion { get; set; } = "apple-flow-qa-adapter-v1";
    [JsonPropertyName("platform")] public string? Platform { get; set; }
    [JsonPropertyName("experimental")] public bool? Experimental { get; set; }
    [JsonPropertyName("backend")] public string? Backend { get; set; }
    [JsonPropertyName("officialCoverage")] public bool? OfficialCoverage { get; set; }
    [JsonPropertyName("macCatalystEquivalent")] public bool? MacCatalystEquivalent { get; set; }
    [JsonPropertyName("spikeStatus")] public string? SpikeStatus { get; set; }
    [JsonPropertyName("foregroundProof")] public bool? ForegroundProof { get; set; }
    [JsonPropertyName("authenticatedTransport")] public bool? AuthenticatedTransport { get; set; }
    [JsonPropertyName("receipt")] public bool? Receipt { get; set; }
    [JsonPropertyName("cancellation")] public bool? Cancellation { get; set; }
    [JsonPropertyName("parity")] public bool? Parity { get; set; }
    [JsonPropertyName("appProject")] public string? AppProject { get; set; }
    [JsonPropertyName("appSourceDigest")] public string? AppSourceDigest { get; set; }
    [JsonPropertyName("packageDigest")] public string? PackageDigest { get; set; }
    [JsonPropertyName("flowDigests")] public List<string> FlowDigests { get; set; } = [];
    [JsonPropertyName("firstAttemptCount")] public int FirstAttemptCount { get; set; }
    [JsonPropertyName("cleanAttemptCount")] public int CleanAttemptCount { get; set; }
    [JsonPropertyName("artifactCount")] public int ArtifactCount { get; set; }
    [JsonPropertyName("omissionCount")] public int OmissionCount { get; set; }
    [JsonPropertyName("xcodeVersion")] public string? XcodeVersion { get; set; }
    [JsonPropertyName("simulatorRuntime")] public string? SimulatorRuntime { get; set; }
    [JsonPropertyName("deviceIdFingerprint")] public string? DeviceIdFingerprint { get; set; }
    [JsonPropertyName("deviceProfile")] public string? DeviceProfile { get; set; }
    [JsonPropertyName("resetFingerprint")] public string? ResetFingerprint { get; set; }
    [JsonPropertyName("seedFingerprint")] public string? SeedFingerprint { get; set; }
    [JsonPropertyName("backendStateFingerprint")] public string? BackendStateFingerprint { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>
/// Preview controls are explicit configuration only. They never introduce source or repair apply
/// authority and are safe to serialize into a qualification report.
/// </summary>
public sealed class MauiPreviewFeatureFlags
{
    [JsonPropertyName("schema")] public int Schema { get; set; } = 1;
    [JsonPropertyName("policyVersion")] public string PolicyVersion { get; set; } = "preview-flags-v1";
    [JsonPropertyName("workbenchEnabled")] public bool WorkbenchEnabled { get; set; }
    [JsonPropertyName("agentAuthoringEnabled")] public bool AgentAuthoringEnabled { get; set; }
    [JsonPropertyName("repairProposalsEnabled")] public bool RepairProposalsEnabled { get; set; }
    [JsonPropertyName("sourceProposalsEnabled")] public bool SourceProposalsEnabled { get; set; }
    [JsonPropertyName("traceImportExportEnabled")] public bool TraceImportExportEnabled { get; set; }

    /// <summary>
    /// Exposes the optional Mobile Canvas device layer to model-facing surfaces. Off by default:
    /// the companion is a separately installed, experimental binary that DevFlow does not ship, so
    /// advertising its tools unasked would offer a surface that is usually not installed.
    /// </summary>
    [JsonPropertyName("mobileCanvasEnabled")] public bool MobileCanvasEnabled { get; set; }
    [JsonPropertyName("autoApplyRepair")] public bool AutoApplyRepair { get; set; }
    [JsonPropertyName("autoApplySource")] public bool AutoApplySource { get; set; }
    [JsonPropertyName("modelProviderEnabled")] public bool ModelProviderEnabled { get; set; }
    [JsonPropertyName("telemetryEgressEnabled")] public bool TelemetryEgressEnabled { get; set; }
    [JsonPropertyName("requiredPullRequestGate")] public bool RequiredPullRequestGate { get; set; }
    [JsonPropertyName("killSwitches")] public List<string> KillSwitches { get; set; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    public static MauiPreviewFeatureFlags CreateDefault() => new();

    /// <summary>Returns whether a feature is enabled after applying its named kill switch.</summary>
    public bool IsEnabled(string feature)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feature);
        if (KillSwitches.Contains(feature, StringComparer.OrdinalIgnoreCase))
            return false;

        return feature switch
        {
            "workbench" => WorkbenchEnabled,
            "agent-authoring" => AgentAuthoringEnabled,
            "repair-proposals" => RepairProposalsEnabled,
            "source-proposals" => SourceProposalsEnabled,
            "trace-import-export" => TraceImportExportEnabled,
            "mobile-canvas" => MobileCanvasEnabled,
            _ => false,
        };
    }
}

/// <summary>
/// Bounded preview-flag configuration. It recognizes only proposal/read-only feature switches and
/// deliberately ignores any attempt to enable model providers, telemetry, auto-apply, or a required
/// PR gate.
/// </summary>
public static class MauiPreviewFeatureFlagConfiguration
{
    /// <summary>Reads optional local feature flags without creating any external connection or side effect.</summary>
    public static MauiPreviewFeatureFlags FromEnvironment(Func<string, string?>? readEnvironment = null)
    {
        readEnvironment ??= Environment.GetEnvironmentVariable;
        var killSwitches = (readEnvironment("DEVFLOW_PREVIEW_KILL_SWITCHES") ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static value => value.ToLowerInvariant())
            .Where(static value => value is "workbench" or "agent-authoring" or "repair-proposals" or "source-proposals" or "trace-import-export" or "mobile-canvas")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToList();
        return new MauiPreviewFeatureFlags
        {
            WorkbenchEnabled = IsEnabled(readEnvironment("DEVFLOW_PREVIEW_WORKBENCH")),
            AgentAuthoringEnabled = IsEnabled(readEnvironment("DEVFLOW_PREVIEW_AGENT_AUTHORING")),
            RepairProposalsEnabled = IsEnabled(readEnvironment("DEVFLOW_PREVIEW_REPAIR_PROPOSALS")),
            SourceProposalsEnabled = IsEnabled(readEnvironment("DEVFLOW_PREVIEW_SOURCE_PROPOSALS")),
            TraceImportExportEnabled = IsEnabled(readEnvironment("DEVFLOW_PREVIEW_TRACE_IMPORT_EXPORT")),
            MobileCanvasEnabled = IsEnabled(readEnvironment("DEVFLOW_PREVIEW_MOBILE_CANVAS")),
            KillSwitches = killSwitches,
            AutoApplyRepair = false,
            AutoApplySource = false,
            ModelProviderEnabled = false,
            TelemetryEgressEnabled = false,
            RequiredPullRequestGate = false,
        };
    }

    private static bool IsEnabled(string? value) =>
        string.Equals(value, "1", StringComparison.Ordinal) ||
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Recorded independent review evidence required before a preview can qualify.</summary>
public sealed class MauiQualificationReviewEvidence
{
    [JsonPropertyName("planId")] public string? PlanId { get; set; }
    [JsonPropertyName("planRevision")] public int? PlanRevision { get; set; }
    [JsonPropertyName("planReviewStatus")] public string? PlanReviewStatus { get; set; }
    [JsonPropertyName("rubberDuckReviewStatus")] public string? RubberDuckReviewStatus { get; set; }
    [JsonPropertyName("independentReviewStatus")] public string? IndependentReviewStatus { get; set; }
    [JsonPropertyName("reviewedAt")] public DateTimeOffset? ReviewedAt { get; set; }
    [JsonPropertyName("reviewerFingerprints")] public List<string> ReviewerFingerprints { get; set; } = [];
    [JsonPropertyName("artifactRefs")] public List<string> ArtifactRefs { get; set; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Manifest-level corpus accounting. Generated cases are explicitly not device runs.</summary>
public sealed class MauiQualificationCorpusSummary
{
    [JsonPropertyName("version")] public string? Version { get; set; }
    [JsonPropertyName("manifestFingerprint")] public string? ManifestFingerprint { get; set; }
    [JsonPropertyName("staticOnly")] public bool? StaticOnly { get; set; }
    [JsonPropertyName("manifestValid")] public bool? ManifestValid { get; set; }
    [JsonPropertyName("caseSchemaValid")] public bool? CaseSchemaValid { get; set; }
    [JsonPropertyName("curatedCases")] public int CuratedCases { get; set; }
    [JsonPropertyName("generatedCases")] public int GeneratedCases { get; set; }
    [JsonPropertyName("deviceBackedCases")] public int DeviceBackedCases { get; set; }
    [JsonPropertyName("curatedRepairPositiveCases")] public int CuratedRepairPositiveCases { get; set; }

    /// <summary>
    /// How many curated cases were adapted from another curated case. Read
    /// <see cref="CuratedRepairPositiveCases"/> without this and 31 restatements of one seed look
    /// like 31 curated cases.
    /// </summary>
    [JsonPropertyName("curatedDerivedCases")] public int CuratedDerivedCases { get; set; }

    /// <summary>
    /// Curated cases that do not declare a seed yet evaluate to exactly the same evidence as
    /// another case. The curated-versus-derived split is self-declared, so this is the only signal
    /// that an undeclared clone is being counted as an independent trial. Disclosure, not a
    /// rejection — genuinely distinct cases can coincide.
    /// </summary>
    [JsonPropertyName("undeclaredProjectionCollisions")] public int UndeclaredProjectionCollisions { get; set; }

    /// <summary>
    /// Curated cases that do not declare a seed yet share a fixture *shape* — the set of key
    /// paths, values ignored — with another case of the same kind. Catches the clone that evades
    /// <see cref="UndeclaredProjectionCollisions"/> by perturbing values until its diagnostics
    /// differ. Also a disclosure, not a rejection.
    /// </summary>
    [JsonPropertyName("undeclaredShapeCollisions")] public int UndeclaredShapeCollisions { get; set; }

    [JsonPropertyName("curatedNoRepairCases")] public int CuratedNoRepairCases { get; set; }
    [JsonPropertyName("generatedNoRepairCases")] public int GeneratedNoRepairCases { get; set; }
    [JsonPropertyName("curatedClassificationLabeledCases")] public int CuratedClassificationLabeledCases { get; set; }
    [JsonPropertyName("provenanceComplete")] public bool? ProvenanceComplete { get; set; }
    [JsonPropertyName("provenanceSourceCounts")] public List<MauiQualificationCorpusProvenanceCount> ProvenanceSourceCounts { get; set; } = [];
    [JsonPropertyName("mutationSeed")] public int? MutationSeed { get; set; }
    [JsonPropertyName("generatorVersion")] public string? GeneratorVersion { get; set; }

    /// <summary>
    /// How many distinct curated fixtures the generated mutants were actually drawn from. The
    /// generated denominator is a resampling of this many originals, so this — not the mutant
    /// count — bounds the statistical power the generated share can contribute.
    /// </summary>
    [JsonPropertyName("generatedBaseFixtures")] public int? GeneratedBaseFixtures { get; set; }

    /// <summary>
    /// How many distinct mutation seeds produced the generated share. One seed means the whole
    /// generated denominator is a single deterministic draw, repeatable but not repeated.
    /// </summary>
    [JsonPropertyName("generatedSeedCount")] public int? GeneratedSeedCount { get; set; }

    /// <summary>
    /// Whether every diagnostic and repair decision the corpus scored came from the shipped
    /// analyzer. False means the harness re-implements those rules and the corpus is scoring
    /// itself; see <see cref="MauiQualificationRateMetric.Exercises"/> for which metrics.
    /// </summary>
    [JsonPropertyName("exercisesShippedAnalyzer")] public bool? ExercisesShippedAnalyzer { get; set; }
    [JsonPropertyName("errors")] public List<string> Errors { get; set; } = [];
    [JsonPropertyName("securityCorpus")] public MauiQualificationSecurityCorpusSummary? SecurityCorpus { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>
/// How many curated cases carry each provenance source kind. Reported so a curated denominator
/// is never read as a set of independent observed trials.
/// </summary>
public sealed class MauiQualificationCorpusProvenanceCount
{
    [JsonPropertyName("sourceKind")] public string SourceKind { get; set; } = MauiQualificationCorpusProvenanceSourceKinds.Unknown;
    [JsonPropertyName("count")] public int Count { get; set; }
}

/// <summary>The closed set of corpus provenance source kinds.</summary>
public static class MauiQualificationCorpusProvenanceSourceKinds
{
    public const string Synthetic = "synthetic";
    public const string ObservedLocalRun = "observed-local-run";
    public const string ObservedCiRun = "observed-ci-run";
    public const string ReportedIssue = "reported-issue";
    public const string Unknown = "unknown";

    /// <summary>Returns the supplied value when it is a recognized source kind, otherwise <c>unknown</c>.</summary>
    public static string Normalize(string? value) => value switch
    {
        Synthetic or ObservedLocalRun or ObservedCiRun or ReportedIssue => value,
        _ => Unknown,
    };

    /// <summary>Returns whether the source kind describes an observed run rather than an authored fixture.</summary>
    public static bool IsObserved(string? value) =>
        value is ObservedLocalRun or ObservedCiRun;
}

/// <summary>The closed set of corpus labeling methods.</summary>
public static class MauiQualificationCorpusProvenanceMethods
{
    public const string HandAuthored = "hand-authored";
    public const string AdaptedFromCase = "adapted-from-case";
    public const string DerivedFromReplay = "derived-from-replay";
    public const string DerivedFromIncident = "derived-from-incident";

    /// <summary>Returns whether the supplied labeling method is recognized.</summary>
    public static bool IsKnown(string? value) =>
        value is HandAuthored or AdaptedFromCase or DerivedFromReplay or DerivedFromIncident;
}

/// <summary>
/// One normalized evaluation record. It contains only safe IDs, classifications, booleans, timing,
/// and digests; text, source, prompt, network, and artifact content are intentionally absent.
/// </summary>
public sealed class MauiQualificationExecutionSample
{
    [JsonPropertyName("sampleId")] public string? SampleId { get; set; }
    [JsonPropertyName("source")] public string? Source { get; set; }
    [JsonPropertyName("category")] public string? Category { get; set; }
    [JsonPropertyName("platform")] public string? Platform { get; set; }
    [JsonPropertyName("flowId")] public string? FlowId { get; set; }
    [JsonPropertyName("tier")] public string? Tier { get; set; }
    [JsonPropertyName("deviceEvidenceKind")] public string? DeviceEvidenceKind { get; set; }
    [JsonPropertyName("realDevice")] public bool? RealDevice { get; set; }
    [JsonPropertyName("cleanState")] public bool? CleanState { get; set; }
    [JsonPropertyName("firstAttempt")] public bool? FirstAttempt { get; set; }
    [JsonPropertyName("diagnosticRerun")] public bool? DiagnosticRerun { get; set; }
    [JsonPropertyName("outcome")] public string? Outcome { get; set; }
    [JsonPropertyName("infrastructureExclusionReason")] public string? InfrastructureExclusionReason { get; set; }
    [JsonPropertyName("recordingValid")] public bool? RecordingValid { get; set; }
    [JsonPropertyName("reportPresent")] public bool? ReportPresent { get; set; }
    [JsonPropertyName("reportSchemaValid")] public bool? ReportSchemaValid { get; set; }
    [JsonPropertyName("reportComplete")] public bool? ReportComplete { get; set; }
    [JsonPropertyName("selectorStable")] public bool? SelectorStable { get; set; }
    [JsonPropertyName("repairProposed")] public bool? RepairProposed { get; set; }
    [JsonPropertyName("repairExpected")] public bool? RepairExpected { get; set; }
    [JsonPropertyName("repairCorrect")] public bool? RepairCorrect { get; set; }
    [JsonPropertyName("noRepairExpected")] public bool? NoRepairExpected { get; set; }
    [JsonPropertyName("expectedFailureClass")] public string? ExpectedFailureClass { get; set; }
    [JsonPropertyName("observedFailureClass")] public string? ObservedFailureClass { get; set; }
    /// <summary>
    /// False when the classifier merely honoured a failure class the evidence already carried.
    /// Such a case is correct by construction and says nothing about inference quality, so it is
    /// reported in its own bucket rather than pooled into a single accuracy headline.
    /// </summary>
    [JsonPropertyName("failureClassInferred")] public bool? FailureClassInferred { get; set; }
    /// <summary>
    /// The product entry point that produced <see cref="ObservedFailureClass"/>, stamped by
    /// whatever actually called it. A sample without this stamp carries a label nothing in this
    /// process was told the origin of, so the gate evaluator refuses to describe it as product
    /// evidence. The stamp is still only as trustworthy as the writer of the sample — it moves the
    /// claim from "inferred from the source name" to "asserted by the component that ran", which is
    /// weaker than an observation and stronger than a guess.
    /// </summary>
    [JsonPropertyName("observedFailureClassProducer")] public string? ObservedFailureClassProducer { get; set; }
    [JsonPropertyName("falseHeal")] public bool? FalseHeal { get; set; }
    [JsonPropertyName("abstained")] public bool? Abstained { get; set; }
    [JsonPropertyName("humanDecision")] public string? HumanDecision { get; set; }
    [JsonPropertyName("probabilityLikeConfidence")] public double? ProbabilityLikeConfidence { get; set; }
    [JsonPropertyName("expectedOutcome")] public bool? ExpectedOutcome { get; set; }
    [JsonPropertyName("timeToDiagnosisMs")] public double? TimeToDiagnosisMs { get; set; }
    [JsonPropertyName("traceBytes")] public long? TraceBytes { get; set; }
    [JsonPropertyName("reportBytes")] public long? ReportBytes { get; set; }
    [JsonPropertyName("runtimeOverheadMs")] public double? RuntimeOverheadMs { get; set; }
    [JsonPropertyName("privacySecurityEscape")] public bool? PrivacySecurityEscape { get; set; }
    [JsonPropertyName("excluded")] public bool? Excluded { get; set; }
    [JsonPropertyName("exclusionReason")] public string? ExclusionReason { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>All evidence flags that must be present before an Android preview can qualify.</summary>
public sealed class MauiQualificationRequiredEvidence
{
    [JsonPropertyName("corpusManifestValid")] public bool? CorpusManifestValid { get; set; }
    [JsonPropertyName("caseSchemaValid")] public bool? CaseSchemaValid { get; set; }
    [JsonPropertyName("reportSchemaValid")] public bool? ReportSchemaValid { get; set; }
    [JsonPropertyName("recordingValid")] public bool? RecordingValid { get; set; }
    [JsonPropertyName("firstAttemptEvidencePresent")] public bool? FirstAttemptEvidencePresent { get; set; }
    [JsonPropertyName("artifactManifestValid")] public bool? ArtifactManifestValid { get; set; }
    [JsonPropertyName("artifactReferencesComplete")] public bool? ArtifactReferencesComplete { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Explicit policy thresholds for the Android engineering-preview gate.</summary>
public sealed class MauiQualificationGateThresholds
{
    [JsonPropertyName("policyVersion")] public string PolicyVersion { get; set; } = "preview-qualification-policy-v1";
    [JsonPropertyName("confidenceLevel")] public double ConfidenceLevel { get; set; } = 0.95;
    [JsonPropertyName("minimumRepairPrecision")] public double MinimumRepairPrecision { get; set; } = 0.95;
    [JsonPropertyName("minimumRepairEvaluations")] public int MinimumRepairEvaluations { get; set; } = 100;
    [JsonPropertyName("minimumNoRepairEvaluations")] public int MinimumNoRepairEvaluations { get; set; } = 300;
    [JsonPropertyName("maximumFalseHeals")] public int MaximumFalseHeals { get; set; }
    [JsonPropertyName("minimumSelectorStability")] public double MinimumSelectorStability { get; set; } = 0.99;
    [JsonPropertyName("minimumSelectorObservations")] public int MinimumSelectorObservations { get; set; } = 100;
    [JsonPropertyName("minimumClassificationAccuracy")] public double MinimumClassificationAccuracy { get; set; } = 0.90;
    [JsonPropertyName("minimumClassificationEvaluations")] public int MinimumClassificationEvaluations { get; set; } = 100;
    [JsonPropertyName("maximumCalibrationEce")] public double MaximumCalibrationEce { get; set; } = 0.05;
    [JsonPropertyName("minimumCleanFirstAttemptsPerTier1Flow")] public int MinimumCleanFirstAttemptsPerTier1Flow { get; set; } = 100;
    [JsonPropertyName("minimumFirstAttemptStability")] public double MinimumFirstAttemptStability { get; set; } = 0.99;
    [JsonPropertyName("hostOperationP95BudgetMs")] public double HostOperationP95BudgetMs { get; set; } = 250;
    [JsonPropertyName("requireRealAndroidDeviceEvidence")] public bool RequireRealAndroidDeviceEvidence { get; set; } = true;
    [JsonPropertyName("requireRecordedReviews")] public bool RequireRecordedReviews { get; set; } = true;
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Aggregated qualification metrics. All denominators are serialized for review.</summary>
public sealed class MauiQualificationMetrics
{
    [JsonPropertyName("recordingValidity")] public MauiQualificationRateMetric RecordingValidity { get; set; } = new();
    [JsonPropertyName("selectorStability")] public MauiQualificationRateMetric SelectorStability { get; set; } = new();
    [JsonPropertyName("repairPrecision")] public MauiQualificationRateMetric RepairPrecision { get; set; } = new();
    [JsonPropertyName("repairRecall")] public MauiQualificationRateMetric RepairRecall { get; set; } = new();
    [JsonPropertyName("classificationAccuracy")] public MauiQualificationRateMetric ClassificationAccuracy { get; set; } = new();
    [JsonPropertyName("classificationMatrix")] public MauiQualificationClassificationMatrix ClassificationMatrix { get; set; } = new();
    [JsonPropertyName("falseHeals")] public MauiQualificationRateMetric FalseHeals { get; set; } = new();
    [JsonPropertyName("abstention")] public MauiQualificationRateMetric Abstention { get; set; } = new();
    [JsonPropertyName("humanDecisionOutcomes")] public MauiQualificationHumanDecisionOutcomes HumanDecisionOutcomes { get; set; } = new();
    [JsonPropertyName("calibration")] public MauiQualificationCalibrationMetric Calibration { get; set; } = new();
    [JsonPropertyName("timeToDiagnosis")] public MauiQualificationDurationMetric TimeToDiagnosis { get; set; } = new();
    [JsonPropertyName("traceReportSize")] public MauiQualificationTraceReportMetric TraceReportSize { get; set; } = new();
    [JsonPropertyName("runtimeOverhead")] public MauiQualificationRuntimeOverheadMetric RuntimeOverhead { get; set; } = new();
    [JsonPropertyName("flakeFirstAttemptStability")] public MauiQualificationFirstAttemptMetric FlakeFirstAttemptStability { get; set; } = new();
    [JsonPropertyName("privacySecurityEscapes")] public MauiQualificationPrivacySecurityMetric PrivacySecurityEscapes { get; set; } = new();
}

/// <summary>A numerator/denominator metric with a conservative Wilson interval when measured.</summary>
public sealed class MauiQualificationRateMetric
{
    [JsonPropertyName("state")] public string State { get; set; } = "missing";
    [JsonPropertyName("numerator")] public int Numerator { get; set; }
    [JsonPropertyName("denominator")] public int Denominator { get; set; }
    [JsonPropertyName("value")] public double? Value { get; set; }
    [JsonPropertyName("confidenceInterval")] public MauiQualificationConfidenceInterval? ConfidenceInterval { get; set; }
    [JsonPropertyName("sampleSources")] public List<string> SampleSources { get; set; } = [];
    [JsonPropertyName("sourceCounts")] public List<MauiQualificationRateSourceCount> SourceCounts { get; set; } = [];

    /// <summary>
    /// The share of <see cref="Denominator"/> that is independent evidence — curated originals and
    /// device-backed runs, excluding derived cases and generated mutants. Gates count this, not the
    /// pooled denominator, so a metric cannot reach its minimum by repeating one seed.
    /// </summary>
    [JsonPropertyName("independentEvaluations")] public int IndependentEvaluations { get; set; }

    /// <summary>The successes among <see cref="IndependentEvaluations"/>.</summary>
    [JsonPropertyName("independentNumerator")] public int IndependentNumerator { get; set; }

    /// <summary>
    /// The Wilson interval over the independent subset alone. <see cref="ConfidenceInterval"/> is
    /// computed on the pooled denominator and therefore narrows when derived clones or generated
    /// mutants are added; that interval is disclosure only. Gates read this one.
    /// </summary>
    [JsonPropertyName("independentConfidenceInterval")] public MauiQualificationConfidenceInterval? IndependentConfidenceInterval { get; set; }

    [JsonPropertyName("independentDeviceRuns")] public bool? IndependentDeviceRuns { get; set; }
    [JsonPropertyName("exclusions")] public List<MauiQualificationExclusion> Exclusions { get; set; } = [];

    /// <summary>
    /// Which code decided each observation this metric counted. A large denominator says nothing
    /// about the product if the harness produced the observation itself, so the component and the
    /// kind are published next to the number rather than left to be inferred from the metric name.
    /// </summary>
    [JsonPropertyName("exercises")] public MauiQualificationMetricProvenance? Exercises { get; set; }
}

/// <summary>Names the code that produced a metric's observations.</summary>
public static class MauiQualificationMetricProvenanceKinds
{
    /// <summary>The metric called the same entry point the product calls at runtime.</summary>
    public const string ShippedAnalyzer = "shipped-analyzer";

    /// <summary>
    /// The metric called rules re-implemented inside the qualification harness. The number is a
    /// self-consistency check between those rules and the expectations authored beside them, and
    /// it is not evidence about the shipped analyzer's behaviour.
    /// </summary>
    public const string HarnessLocalRules = "harness-local-rules";

    /// <summary>
    /// The observation was supplied by the run that submitted the sample. Nothing in this report
    /// checked which code the submitting run used, so this kind is a claim, not a verification.
    /// </summary>
    public const string SampleSupplied = "sample-supplied";

    /// <summary>
    /// Nothing can be said about what produced the observation — typically a merged total whose
    /// contributors disagreed or did not all declare. Never product evidence.
    /// </summary>
    public const string Unknown = "unknown";

    /// <summary>
    /// True when the observation is claimed to come from product code rather than from rules
    /// re-implemented in the qualification harness. <see cref="SampleSupplied"/> qualifies because a
    /// submitting run observed the product itself — subject to the standing caveat that a
    /// self-reported run file is trusted as written, which is why the product-analyzer-coverage
    /// gate reports it separately from <see cref="ShippedAnalyzer"/> instead of merging the two
    /// into one pass. <c>unknown</c> and an absent declaration never qualify.
    /// </summary>
    public static bool IsProductEvidence(string? kind) =>
        string.Equals(kind, ShippedAnalyzer, StringComparison.Ordinal) ||
        string.Equals(kind, SampleSupplied, StringComparison.Ordinal);

    /// <summary>
    /// Orders the kinds weakest first so callers can take a minimum rather than letting whichever
    /// kind they happened to check first decide. Both merging across runs and mixing within a run
    /// must take the weaker claim, so the ranking lives here rather than being restated at each
    /// site: two copies that drift would make the within-run and across-run answers disagree
    /// silently. An unrecognised kind ranks level with <see cref="Unknown"/> rather than above any
    /// named kind, so a label this build does not model can never win a minimum.
    /// </summary>
    public static int Strength(string? kind) => kind switch
    {
        Unknown => 0,
        HarnessLocalRules => 1,
        SampleSupplied => 2,
        ShippedAnalyzer => 3,
        _ => 0,
    };
}

/// <summary>
/// Which component produced a metric's observations, and whether that component is the shipped one.
/// </summary>
public sealed class MauiQualificationMetricProvenance
{
    /// <summary>
    /// The value <see cref="Component"/> holds when nothing set it. Named so the coverage gate can
    /// recognise the contract default without coupling a component to a kind constant that merely
    /// happens to share its spelling.
    /// </summary>
    public const string UndeclaredComponent = "unknown";

    /// <summary>The type and member that decided the observation.</summary>
    [JsonPropertyName("component")] public string Component { get; set; } = UndeclaredComponent;

    /// <summary>One of <see cref="MauiQualificationMetricProvenanceKinds"/>.</summary>
    [JsonPropertyName("kind")] public string Kind { get; set; } = MauiQualificationMetricProvenanceKinds.Unknown;

    /// <summary>What a reader must not conclude from this metric.</summary>
    [JsonPropertyName("note")] public string? Note { get; set; }
}

/// <summary>
/// The share of a rate metric contributed by one sample source. Reported so a pooled
/// denominator such as 0/316 cannot be read as 316 independent trials.
/// </summary>
public sealed class MauiQualificationRateSourceCount
{
    [JsonPropertyName("source")] public string Source { get; set; } = "unknown";
    [JsonPropertyName("numerator")] public int Numerator { get; set; }
    [JsonPropertyName("denominator")] public int Denominator { get; set; }

    /// <summary>
    /// The share of <see cref="Denominator"/> this source contributed to the metric's
    /// <see cref="MauiQualificationRateMetric.IndependentEvaluations"/>. Carried per source so the
    /// accumulator can merge independence exactly rather than re-deriving it from the source name,
    /// which is wrong for classification accuracy (a curated case with a stamped label is not an
    /// independent classification evaluation).
    /// </summary>
    [JsonPropertyName("independentEvaluations")] public int IndependentEvaluations { get; set; }
}

/// <summary>
/// A bounded failure-class confusion matrix. Labels are normalized to the closed
/// <see cref="MauiFlowFailureClasses"/> set so the matrix can never carry free text.
/// </summary>
public sealed class MauiQualificationClassificationMatrix
{
    [JsonPropertyName("state")] public string State { get; set; } = "missing";
    [JsonPropertyName("method")] public string Method { get; set; } = "expected-versus-observed-failure-class";
    [JsonPropertyName("sampleCount")] public int SampleCount { get; set; }
    [JsonPropertyName("correct")] public int Correct { get; set; }
    [JsonPropertyName("labelCount")] public int LabelCount { get; set; }
    /// <summary>Cases where the classifier had to infer the class from replay facts.</summary>
    [JsonPropertyName("inferredSampleCount")] public int InferredSampleCount { get; set; }
    [JsonPropertyName("inferredCorrect")] public int InferredCorrect { get; set; }
    /// <summary>Cases whose evidence already stamped a known class; correct by construction.</summary>
    [JsonPropertyName("stampHonouredSampleCount")] public int StampHonouredSampleCount { get; set; }
    [JsonPropertyName("stampHonouredCorrect")] public int StampHonouredCorrect { get; set; }
    [JsonPropertyName("cells")] public List<MauiQualificationClassificationCell> Cells { get; set; } = [];
    [JsonPropertyName("perClass")] public List<MauiQualificationClassificationClassResult> PerClass { get; set; } = [];
    [JsonPropertyName("missingReason")] public string? MissingReason { get; set; }
}

/// <summary>One confusion-matrix cell: how often an expected class was reported as an observed class.</summary>
public sealed class MauiQualificationClassificationCell
{
    [JsonPropertyName("expected")] public string Expected { get; set; } = "unknown";
    [JsonPropertyName("observed")] public string Observed { get; set; } = "unknown";
    [JsonPropertyName("count")] public int Count { get; set; }
}

/// <summary>Per-class support, precision, and recall derived from the confusion matrix.</summary>
public sealed class MauiQualificationClassificationClassResult
{
    [JsonPropertyName("failureClass")] public string FailureClass { get; set; } = "unknown";
    [JsonPropertyName("support")] public int Support { get; set; }
    [JsonPropertyName("predicted")] public int Predicted { get; set; }
    [JsonPropertyName("correct")] public int Correct { get; set; }
    [JsonPropertyName("precision")] public double? Precision { get; set; }
    [JsonPropertyName("recall")] public double? Recall { get; set; }
}

/// <summary>Wilson score interval. The lower bound is used for conservative release decisions.</summary>
public sealed class MauiQualificationConfidenceInterval
{
    [JsonPropertyName("method")] public string Method { get; set; } = "wilson-95";
    [JsonPropertyName("confidenceLevel")] public double ConfidenceLevel { get; set; } = 0.95;
    [JsonPropertyName("lower")] public double Lower { get; set; }
    [JsonPropertyName("upper")] public double Upper { get; set; }
}

/// <summary>Calibration metrics for a value displayed as a probability-like confidence.</summary>
public sealed class MauiQualificationCalibrationMetric
{
    [JsonPropertyName("state")] public string State { get; set; } = "not-applicable";
    [JsonPropertyName("probabilityLikeConfidenceDisplayed")] public bool ProbabilityLikeConfidenceDisplayed { get; set; }
    [JsonPropertyName("sampleCount")] public int SampleCount { get; set; }
    [JsonPropertyName("ece")] public double? Ece { get; set; }
    [JsonPropertyName("brier")] public double? Brier { get; set; }
    [JsonPropertyName("buckets")] public List<MauiQualificationCalibrationBucket> Buckets { get; set; } = [];
}

/// <summary>One equal-width calibration bucket.</summary>
public sealed class MauiQualificationCalibrationBucket
{
    [JsonPropertyName("lowerInclusive")] public double LowerInclusive { get; set; }
    [JsonPropertyName("upperInclusive")] public double UpperInclusive { get; set; }
    [JsonPropertyName("sampleCount")] public int SampleCount { get; set; }
    [JsonPropertyName("meanConfidence")] public double? MeanConfidence { get; set; }
    [JsonPropertyName("empiricalRate")] public double? EmpiricalRate { get; set; }
}

/// <summary>A p50/p95 duration metric measured by a bounded deterministic host operation.</summary>
public sealed class MauiQualificationDurationMetric
{
    [JsonPropertyName("state")] public string State { get; set; } = "missing";
    [JsonPropertyName("operation")] public string? Operation { get; set; }
    [JsonPropertyName("sampleCount")] public int SampleCount { get; set; }
    [JsonPropertyName("p50Ms")] public double? P50Ms { get; set; }
    [JsonPropertyName("p95Ms")] public double? P95Ms { get; set; }
    [JsonPropertyName("maxMs")] public double? MaxMs { get; set; }
    [JsonPropertyName("missingReason")] public string? MissingReason { get; set; }
}

/// <summary>Completeness and bounded-size accounting for flow reports and traces.</summary>
public sealed class MauiQualificationTraceReportMetric
{
    [JsonPropertyName("state")] public string State { get; set; } = "missing";
    [JsonPropertyName("expectedReportCount")] public int ExpectedReportCount { get; set; }
    [JsonPropertyName("reportPresent")] public int ReportPresent { get; set; }
    [JsonPropertyName("reportSchemaValid")] public int ReportSchemaValid { get; set; }
    [JsonPropertyName("reportComplete")] public int ReportComplete { get; set; }
    [JsonPropertyName("reportCompleteness")] public double? ReportCompleteness { get; set; }
    [JsonPropertyName("traceSampleCount")] public int TraceSampleCount { get; set; }
    [JsonPropertyName("reportP50Bytes")] public double? ReportP50Bytes { get; set; }
    [JsonPropertyName("reportP95Bytes")] public double? ReportP95Bytes { get; set; }
    [JsonPropertyName("traceP50Bytes")] public double? TraceP50Bytes { get; set; }
    [JsonPropertyName("traceP95Bytes")] public double? TraceP95Bytes { get; set; }
    [JsonPropertyName("missingReason")] public string? MissingReason { get; set; }
}

/// <summary>Host micro-measurements and separately declared device overhead evidence.</summary>
public sealed class MauiQualificationRuntimeOverheadMetric
{
    [JsonPropertyName("hostOperations")] public List<MauiQualificationDurationMetric> HostOperations { get; set; } = [];
    [JsonPropertyName("deviceOverhead")] public MauiQualificationDurationMetric DeviceOverhead { get; set; } = new();
}

/// <summary>First-attempt-only stability accounting and per-flow device evidence.</summary>
public sealed class MauiQualificationFirstAttemptMetric
{
    [JsonPropertyName("state")] public string State { get; set; } = "missing";
    [JsonPropertyName("stability")] public MauiQualificationRateMetric Stability { get; set; } = new();
    [JsonPropertyName("flows")] public List<MauiQualificationFlowAttemptSummary> Flows { get; set; } = [];
    [JsonPropertyName("diagnosticRerunsIgnored")] public int DiagnosticRerunsIgnored { get; set; }
    [JsonPropertyName("infrastructureExclusions")] public List<MauiQualificationExclusion> InfrastructureExclusions { get; set; } = [];
}

/// <summary>Per-Tier-1 flow clean first-attempt evidence. It never treats generated samples as device runs.</summary>
public sealed class MauiQualificationFlowAttemptSummary
{
    [JsonPropertyName("flowId")] public string? FlowId { get; set; }
    [JsonPropertyName("cleanFirstAttempts")] public int CleanFirstAttempts { get; set; }
    [JsonPropertyName("passedFirstAttempts")] public int PassedFirstAttempts { get; set; }
    [JsonPropertyName("stability")] public double? Stability { get; set; }
    [JsonPropertyName("realDeviceEvidence")] public bool RealDeviceEvidence { get; set; }
    /// <summary>Number of accepted runs that contributed to <see cref="CleanFirstAttempts"/>. Only set when accumulating.</summary>
    [JsonPropertyName("contributingRuns")] public int ContributingRuns { get; set; }
    /// <summary>Distinct declared real-device fingerprints behind those runs. Self-reported; the merge cannot verify it.</summary>
    [JsonPropertyName("contributingDevices")] public int ContributingDevices { get; set; }
}

/// <summary>Counts review outcomes without retaining reviewer text or grant content.</summary>
public sealed class MauiQualificationHumanDecisionOutcomes
{
    [JsonPropertyName("approved")] public int Approved { get; set; }
    [JsonPropertyName("rejected")] public int Rejected { get; set; }
    [JsonPropertyName("expired")] public int Expired { get; set; }
    [JsonPropertyName("abstained")] public int Abstained { get; set; }
    [JsonPropertyName("unresolved")] public int Unresolved { get; set; }
}

/// <summary>Privacy/security corpus and escape totals. Escape values must be zero to qualify.</summary>
public sealed class MauiQualificationPrivacySecurityMetric
{
    [JsonPropertyName("state")] public string State { get; set; } = "missing";
    [JsonPropertyName("testCount")] public int TestCount { get; set; }
    [JsonPropertyName("escapeCount")] public int EscapeCount { get; set; }
    [JsonPropertyName("canaryScanPassed")] public bool? CanaryScanPassed { get; set; }
    [JsonPropertyName("caseIds")] public List<string> CaseIds { get; set; } = [];
    [JsonPropertyName("missingReason")] public string? MissingReason { get; set; }
}

/// <summary>Result of one individual gate, including an explicit status and safe reason codes.</summary>
public sealed class MauiQualificationGateResult
{
    [JsonPropertyName("gateId")] public string GateId { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; } = MauiPreviewQualificationStates.NotQualified;
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("reasonCodes")] public List<string> ReasonCodes { get; set; } = [];
    [JsonPropertyName("artifactRefs")] public List<string> ArtifactRefs { get; set; } = [];
}

/// <summary>A safe, fixed-text qualification reason. It intentionally cannot carry raw evidence.</summary>
public sealed class MauiQualificationReason
{
    [JsonPropertyName("code")] public string Code { get; set; } = "";
    [JsonPropertyName("severity")] public string Severity { get; set; } = "warning";
    [JsonPropertyName("message")] public string Message { get; set; } = "";
}

/// <summary>A bounded artifact reference represented by a digest rather than content.</summary>
public sealed class MauiQualificationArtifactReference
{
    [JsonPropertyName("kind")] public string? Kind { get; set; }
    [JsonPropertyName("digest")] public string? Digest { get; set; }
    [JsonPropertyName("reference")] public string? Reference { get; set; }
    [JsonPropertyName("redacted")] public bool? Redacted { get; set; }
}

/// <summary>A declared denominator exclusion, including the rule that allowed it.</summary>
public sealed class MauiQualificationExclusion
{
    [JsonPropertyName("kind")] public string? Kind { get; set; }
    [JsonPropertyName("count")] public int Count { get; set; }
    [JsonPropertyName("reason")] public string? Reason { get; set; }
}

/// <summary>Security corpus accounting that never serializes the canary strings themselves.</summary>
public sealed class MauiQualificationSecurityCorpusSummary
{
    [JsonPropertyName("version")] public string? Version { get; set; }
    [JsonPropertyName("manifestFingerprint")] public string? ManifestFingerprint { get; set; }
    [JsonPropertyName("valid")] public bool? Valid { get; set; }
    [JsonPropertyName("caseCount")] public int CaseCount { get; set; }
    [JsonPropertyName("passedCount")] public int PassedCount { get; set; }
    [JsonPropertyName("caseIds")] public List<string> CaseIds { get; set; } = [];
    [JsonPropertyName("errors")] public List<string> Errors { get; set; } = [];
}

/// <summary>Input to the proposal transition policy used by bounded fuzz and security tests.</summary>
public sealed class MauiQualificationProposalTransition
{
    [JsonPropertyName("state")] public string? State { get; set; }
    [JsonPropertyName("humanApprovalRecorded")] public bool? HumanApprovalRecorded { get; set; }
    [JsonPropertyName("grantValid")] public bool? GrantValid { get; set; }
    [JsonPropertyName("applyRequested")] public bool? ApplyRequested { get; set; }
}

/// <summary>Fail-closed transition result. The preview never grants automatic application.</summary>
public sealed class MauiQualificationProposalTransitionResult
{
    [JsonPropertyName("allowed")] public bool Allowed { get; set; }
    [JsonPropertyName("automaticApplyAllowed")] public bool AutomaticApplyAllowed { get; set; }
    [JsonPropertyName("reasonCode")] public string? ReasonCode { get; set; }
}
