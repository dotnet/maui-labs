using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Maui.DevFlow.Testing;

/// <summary>
/// A bounded, provider-neutral account of one flow run. It contains observed facts only and is
/// safe to persist or attach to a redacted evidence bundle.
/// </summary>
public sealed class MauiFlowRunReport
{
    [JsonPropertyName("schema")] public int Schema { get; set; } = 1;
    [JsonPropertyName("runId")] public string? RunId { get; set; }
    [JsonPropertyName("flowId")] public string? FlowId { get; set; }
    [JsonPropertyName("flowRevision")] public int? FlowRevision { get; set; }
    [JsonPropertyName("flowDigest")] public string? FlowDigest { get; set; }
    [JsonPropertyName("legacyFlowIdentity")] public string? LegacyFlowIdentity { get; set; }
    [JsonPropertyName("target")] public MauiFlowRunTarget? Target { get; set; }
    [JsonPropertyName("reset")] public MauiFlowResetResult? Reset { get; set; }
    [JsonPropertyName("preconditions")] public MauiFlowReplayPreconditions? Preconditions { get; set; }
    [JsonPropertyName("sideEffectPolicy")] public string? SideEffectPolicy { get; set; }
    [JsonPropertyName("compensator")] public MauiFlowCompensatorOutcome? Compensator { get; set; }
    [JsonPropertyName("businessOracles")] public List<MauiIndependentBusinessOracleResult> BusinessOracles { get; set; } = [];
    [JsonPropertyName("replayEligibility")] public MauiFlowReplayEligibilityDecision? ReplayEligibility { get; set; }
    [JsonPropertyName("verification")] public MauiFlowRunVerification? Verification { get; set; }
    [JsonPropertyName("startedAt")] public DateTimeOffset? StartedAt { get; set; }
    [JsonPropertyName("endedAt")] public DateTimeOffset? EndedAt { get; set; }
    [JsonPropertyName("outcome")] public MauiFlowRunOutcome? Outcome { get; set; }
    [JsonPropertyName("divergenceStepId")] public string? DivergenceStepId { get; set; }
    [JsonPropertyName("events")] public List<MauiFlowRunEvent> Events { get; set; } = [];
    [JsonPropertyName("steps")] public List<MauiFlowStepAttempt> Steps { get; set; } = [];
    [JsonPropertyName("failure")] public MauiFlowFailure? Failure { get; set; }

    /// <summary>
    /// Host-owned failures that happened around the run rather than in it, such as owned platform
    /// or build-artifact cleanup. They never displace <see cref="Failure"/>, <see cref="Outcome"/>,
    /// or <see cref="Verification"/>: a run that passed and then failed to tear down is a pass with
    /// a cleanup problem, not a test failure, and a run that failed and then failed to tear down
    /// keeps the symptom an author needs. Both may be present at once.
    /// </summary>
    [JsonPropertyName("secondaryFailures")] public List<MauiFlowSecondaryFailure> SecondaryFailures { get; set; } = [];
    [JsonPropertyName("appProcess")] public MauiFlowAppProcessEvidence? AppProcess { get; set; }
    [JsonPropertyName("expectedEvidence")] public MauiFlowExpectedEvidenceReport? ExpectedEvidence { get; set; }
    [JsonPropertyName("artifacts")] public List<MauiFlowArtifactReference> Artifacts { get; set; } = [];
    [JsonPropertyName("selectorHealth")] public MauiFlowSelectorHealthSummary? SelectorHealth { get; set; }
    [JsonPropertyName("reportDigest")] public string? ReportDigest { get; set; }
    [JsonPropertyName("reportPath")] public string? ReportPath { get; set; }
    [JsonPropertyName("truncated")] public bool? Truncated { get; set; }
    [JsonPropertyName("truncationReason")] public string? TruncationReason { get; set; }
    [JsonPropertyName("omissions")] public List<MauiFlowReportOmission> Omissions { get; set; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>The selected application, device, agent, and display facts for a run.</summary>
public sealed class MauiFlowRunTarget
{
    [JsonPropertyName("targetId")] public string? TargetId { get; set; }
    [JsonPropertyName("platform")] public string? Platform { get; set; }
    [JsonPropertyName("deviceId")] public string? DeviceId { get; set; }
    [JsonPropertyName("deviceProfile")] public string? DeviceProfile { get; set; }
    [JsonPropertyName("appId")] public string? AppId { get; set; }
    [JsonPropertyName("appBuildFingerprint")] public string? AppBuildFingerprint { get; set; }
    [JsonPropertyName("appSourceFingerprint")] public string? AppSourceFingerprint { get; set; }
    [JsonPropertyName("packageDigest")] public string? PackageDigest { get; set; }

    /// <summary>
    /// A signing-insensitive digest of the deployed package payload, published as a diagnostic
    /// fact. It has not been established as a cross-occurrence identity on any platform and does
    /// not rescue a <c>packageDigest</c> mismatch. Optional: a producer that cannot compute one
    /// omits it and consumers keep refusing.
    /// </summary>
    [JsonPropertyName("normalizedPayloadDigest")] public string? NormalizedPayloadDigest { get; set; }
    [JsonPropertyName("agentId")] public string? AgentId { get; set; }
    [JsonPropertyName("agentInstanceId")] public string? AgentInstanceId { get; set; }
    [JsonPropertyName("locale")] public string? Locale { get; set; }
    [JsonPropertyName("theme")] public string? Theme { get; set; }
    [JsonPropertyName("orientation")] public string? Orientation { get; set; }
    [JsonPropertyName("displayProfile")] public string? DisplayProfile { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Observed reset and backend/test-data state for a run.</summary>
public sealed class MauiFlowResetResult
{
    [JsonPropertyName("requested")] public bool? Requested { get; set; }
    [JsonPropertyName("succeeded")] public bool? Succeeded { get; set; }
    [JsonPropertyName("appStateSucceeded")] public bool? AppStateSucceeded { get; set; }
    [JsonPropertyName("backendTestDataSucceeded")] public bool? BackendTestDataSucceeded { get; set; }
    [JsonPropertyName("strategy")] public string? Strategy { get; set; }
    [JsonPropertyName("resetIdentity")] public string? ResetIdentity { get; set; }
    [JsonPropertyName("seedFingerprint")] public string? SeedFingerprint { get; set; }
    [JsonPropertyName("backendStateFingerprint")] public string? BackendStateFingerprint { get; set; }
    [JsonPropertyName("reference")] public MauiFlowResetReference? Reference { get; set; }
    [JsonPropertyName("appStateSeed")] public MauiFlowAppStateSeedFingerprint? AppStateSeed { get; set; }
    [JsonPropertyName("backendTestDataSeed")] public MauiFlowBackendTestDataSeedFingerprint? BackendTestDataSeed { get; set; }
    [JsonPropertyName("outcome")] public MauiFlowResetOutcome? Outcome { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>The terminal or in-progress state reported for a flow run.</summary>
public sealed class MauiFlowRunOutcome
{
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("summary")] public string? Summary { get; set; }
    [JsonPropertyName("terminal")] public bool? Terminal { get; set; }
    [JsonPropertyName("verified")] public bool? Verified { get; set; }
    [JsonPropertyName("verificationReason")] public string? VerificationReason { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>
/// Records whether a terminal run can be marked verified independently of whether its replay
/// completed. A passing UI replay without its required business oracle is deliberately not
/// verified.
/// </summary>
public sealed class MauiFlowRunVerification
{
    [JsonPropertyName("verified")] public bool? Verified { get; set; }
    [JsonPropertyName("reason")] public string? Reason { get; set; }
    [JsonPropertyName("checkedAt")] public DateTimeOffset? CheckedAt { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Known run outcome values.</summary>
public static class MauiFlowRunOutcomes
{
    public const string Passed = "passed";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
    public const string TimedOut = "timed-out";
    public const string LeaseLost = "lease-lost";
    public const string InfrastructureError = "infrastructure-error";
    public const string UnknownCompletion = "unknown-completion";
    public const string Orphaned = "orphaned";
}

/// <summary>An ordered, bounded event emitted while a run was prepared or executed.</summary>
public sealed class MauiFlowRunEvent
{
    [JsonPropertyName("sequence")] public int? Sequence { get; set; }
    [JsonPropertyName("at")] public DateTimeOffset? At { get; set; }
    [JsonPropertyName("kind")] public string? Kind { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("stepId")] public string? StepId { get; set; }
    [JsonPropertyName("data")] public JsonElement? Data { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>The observed attempt for one flow step.</summary>
public sealed class MauiFlowStepAttempt
{
    [JsonPropertyName("stepId")] public string? StepId { get; set; }
    [JsonPropertyName("sequence")] public int? Sequence { get; set; }
    [JsonPropertyName("action")] public string? Action { get; set; }
    [JsonPropertyName("intent")] public string? Intent { get; set; }
    [JsonPropertyName("startedAt")] public DateTimeOffset? StartedAt { get; set; }
    [JsonPropertyName("endedAt")] public DateTimeOffset? EndedAt { get; set; }
    [JsonPropertyName("durationMs")] public long? DurationMs { get; set; }
    [JsonPropertyName("selector")] public FlowSelector? Selector { get; set; }
    [JsonPropertyName("selectorRequest")] public MauiFlowSelectorRequest? SelectorRequest { get; set; }
    [JsonPropertyName("candidateCount")] public int? CandidateCount { get; set; }
    [JsonPropertyName("candidateSummary")] public MauiFlowCandidateSummary? CandidateSummary { get; set; }
    [JsonPropertyName("targetResolution")] public MauiFlowTargetResolution? TargetResolution { get; set; }
    [JsonPropertyName("actionability")] public List<MauiFlowActionabilityAttempt> Actionability { get; set; } = [];
    [JsonPropertyName("dispatch")] public MauiFlowDispatchReceipt? Dispatch { get; set; }
    [JsonPropertyName("assertions")] public List<MauiFlowAssertionResult> Assertions { get; set; } = [];
    [JsonPropertyName("expectedCheckpoint")] public MauiFlowCheckpoint? ExpectedCheckpoint { get; set; }
    [JsonPropertyName("observedCheckpoint")] public MauiFlowCheckpoint? ObservedCheckpoint { get; set; }
    [JsonPropertyName("seedFingerprint")] public string? SeedFingerprint { get; set; }
    [JsonPropertyName("backendStateFingerprint")] public string? BackendStateFingerprint { get; set; }
    [JsonPropertyName("commandId")] public string? CommandId { get; set; }
    [JsonPropertyName("commandSequence")] public long? CommandSequence { get; set; }
    [JsonPropertyName("actionDigest")] public string? ActionDigest { get; set; }
    [JsonPropertyName("authorityEpoch")] public long? AuthorityEpoch { get; set; }
    [JsonPropertyName("acknowledgementState")] public string? AcknowledgementState { get; set; }
    [JsonPropertyName("completionCertainty")] public string? CompletionCertainty { get; set; }
    [JsonPropertyName("failureClass")] public string? FailureClass { get; set; }
    [JsonPropertyName("fingerprint")] public MauiElementFingerprint? Fingerprint { get; set; }
    [JsonPropertyName("selectorCandidates")] public List<MauiSelectorCandidate> SelectorCandidates { get; set; } = [];
    [JsonPropertyName("selectorCandidateOmissions")] public List<MauiSelectorEvidenceOmission> SelectorCandidateOmissions { get; set; } = [];
    [JsonPropertyName("artifacts")] public List<MauiFlowArtifactReference> Artifacts { get; set; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Bounded summary of value-free selector evidence captured during a run.</summary>
public sealed class MauiFlowSelectorHealthSummary
{
    [JsonPropertyName("ruleVersion")] public string RuleVersion { get; set; } = MauiSelectorHealthRules.RuleVersion;
    [JsonPropertyName("rankerRuleVersion")] public string RankerRuleVersion { get; set; } = MauiSelectorHealthRules.RankerRuleVersion;
    [JsonPropertyName("calibrationState")] public string CalibrationState { get; set; } = MauiSelectorHealthRules.Uncalibrated;
    [JsonPropertyName("capturedSteps")] public int CapturedSteps { get; set; }
    [JsonPropertyName("candidateCount")] public int CandidateCount { get; set; }
    [JsonPropertyName("omissionCount")] public int OmissionCount { get; set; }
}

/// <summary>A selector request projected without retaining sensitive typed text.</summary>
public sealed class MauiFlowSelectorRequest
{
    [JsonPropertyName("kind")] public string? Kind { get; set; }
    [JsonPropertyName("value")] public MauiFlowValueDisclosure? Value { get; set; }
    [JsonPropertyName("scope")] public string? Scope { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>A privacy-safe candidate and final-target summary.</summary>
public sealed class MauiFlowCandidateSummary
{
    [JsonPropertyName("count")] public int? Count { get; set; }
    [JsonPropertyName("types")] public List<string> Types { get; set; } = [];
    [JsonPropertyName("final")] public string? Final { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>The result of resolving a selector for a specific step attempt.</summary>
public sealed class MauiFlowTargetResolution
{
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("matchCount")] public int? MatchCount { get; set; }
    [JsonPropertyName("elementId")] public string? ElementId { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("finalResolution")] public string? FinalResolution { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>One capability-honest actionability poll or check before dispatch.</summary>
public sealed class MauiFlowActionabilityAttempt
{
    [JsonPropertyName("sequence")] public int? Sequence { get; set; }
    [JsonPropertyName("attempt")] public int? Attempt { get; set; }
    [JsonPropertyName("at")] public DateTimeOffset? At { get; set; }
    [JsonPropertyName("kind")] public string? Kind { get; set; }
    [JsonPropertyName("passed")] public bool? Passed { get; set; }
    [JsonPropertyName("resolved")] public bool? Resolved { get; set; }
    [JsonPropertyName("visible")] public bool? Visible { get; set; }
    [JsonPropertyName("enabled")] public bool? Enabled { get; set; }
    [JsonPropertyName("hasBounds")] public bool? HasBounds { get; set; }
    [JsonPropertyName("boundsStable")] public bool? BoundsStable { get; set; }
    [JsonPropertyName("waitDurationMs")] public long? WaitDurationMs { get; set; }
    [JsonPropertyName("outcome")] public string? Outcome { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>A command receipt and completion certainty captured for a mutation.</summary>
public sealed class MauiFlowDispatchReceipt
{
    [JsonPropertyName("commandId")] public string? CommandId { get; set; }
    [JsonPropertyName("sequence")] public long? Sequence { get; set; }
    [JsonPropertyName("actionDigest")] public string? ActionDigest { get; set; }
    [JsonPropertyName("authorityEpoch")] public long? AuthorityEpoch { get; set; }
    [JsonPropertyName("acknowledgementState")] public string? AcknowledgementState { get; set; }
    [JsonPropertyName("completionCertainty")] public string? CompletionCertainty { get; set; }
    [JsonPropertyName("receivedAt")] public DateTimeOffset? ReceivedAt { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>The observed result of a flow assertion.</summary>
public sealed class MauiFlowAssertionResult
{
    [JsonPropertyName("kind")] public string? Kind { get; set; }
    [JsonPropertyName("passed")] public bool? Passed { get; set; }
    [JsonPropertyName("skipped")] public bool? Skipped { get; set; }
    [JsonPropertyName("expected")] public string? Expected { get; set; }
    [JsonPropertyName("actual")] public string? Actual { get; set; }
    [JsonPropertyName("expectedDisclosure")] public MauiFlowValueDisclosure? ExpectedDisclosure { get; set; }
    [JsonPropertyName("actualDisclosure")] public MauiFlowValueDisclosure? ActualDisclosure { get; set; }

    /// <summary>
    /// How the assertion's own selector resolved, recorded only for the kinds that must resolve a
    /// selector to reach a verdict. A failed assertion whose selector did not resolve never read a
    /// value from the app, so triage attributes it to the test rather than to the app.
    /// </summary>
    [JsonPropertyName("targetResolution")] public MauiFlowTargetResolution? TargetResolution { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>How a value was represented in a report without storing sensitive typed text.</summary>
public sealed class MauiFlowValueDisclosure
{
    [JsonPropertyName("state")] public string? State { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("length")] public int? Length { get; set; }
    [JsonPropertyName("digest")] public string? Digest { get; set; }
    [JsonPropertyName("value")] public string? Value { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Checkpoint facts used to distinguish locator drift from state drift.</summary>
public sealed class MauiFlowCheckpoint
{
    [JsonPropertyName("appBuildFingerprint")] public string? AppBuildFingerprint { get; set; }
    [JsonPropertyName("agentInstanceId")] public string? AgentInstanceId { get; set; }
    [JsonPropertyName("seedFingerprint")] public string? SeedFingerprint { get; set; }
    [JsonPropertyName("backendStateFingerprint")] public string? BackendStateFingerprint { get; set; }
    [JsonPropertyName("route")] public string? Route { get; set; }
    [JsonPropertyName("window")] public string? Window { get; set; }
    [JsonPropertyName("modal")] public string? Modal { get; set; }
    [JsonPropertyName("locale")] public string? Locale { get; set; }
    [JsonPropertyName("theme")] public string? Theme { get; set; }
    [JsonPropertyName("orientation")] public string? Orientation { get; set; }
    [JsonPropertyName("displayProfile")] public string? DisplayProfile { get; set; }
    [JsonPropertyName("collectionItemKey")] public string? CollectionItemKey { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>A deterministic primary failure finding for the run or a step attempt.</summary>
public sealed class MauiFlowFailure
{
    [JsonPropertyName("failureId")] public string? FailureId { get; set; }
    [JsonPropertyName("class")] public string? Class { get; set; }
    [JsonPropertyName("code")] public string? Code { get; set; }
    [JsonPropertyName("category")] public string? Category { get; set; }
    [JsonPropertyName("phase")] public string? Phase { get; set; }
    [JsonPropertyName("retryable")] public bool? Retryable { get; set; }
    [JsonPropertyName("repairEligible")] public bool? RepairEligible { get; set; }
    /// <summary>
    /// What the failure classifier concluded on its own, before replay-safety was consulted.
    /// <see cref="RepairEligible"/> is the conjunction of this and the run's replay eligibility, so
    /// the two differ exactly when the symptom is repairable but the run was not safe to repair
    /// from. Keeping both visible stops that distinction from being read as a classifier defect.
    /// </summary>
    [JsonPropertyName("classifierRepairEligible")] public bool? ClassifierRepairEligible { get; set; }
    [JsonPropertyName("legacyKind")] public string? LegacyKind { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("stepId")] public string? StepId { get; set; }
    [JsonPropertyName("at")] public DateTimeOffset? At { get; set; }
    [JsonPropertyName("artifacts")] public List<MauiFlowArtifactReference> Artifacts { get; set; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>
/// A bounded, identifier-only account of a host-owned failure that happened around a run rather
/// than in it. Every field is an enum-shaped code so the record can be attached to a redacted
/// evidence bundle without carrying a message, a path, or a device value.
/// </summary>
public sealed class MauiFlowSecondaryFailure
{
    /// <summary>One of <see cref="MauiFlowSecondaryFailurePhases"/>.</summary>
    [JsonPropertyName("phase")] public string? Phase { get; set; }

    /// <summary>The host's detail code for the phase, for example <c>cleanup-exception</c>.</summary>
    [JsonPropertyName("code")] public string? Code { get; set; }

    /// <summary>
    /// Always <see cref="MauiFlowFailureClasses.Infrastructure"/>. A cleanup failure is the host's
    /// problem by construction: it is observed after the run reached its own terminal state, so it
    /// can never be evidence about the app under test. Producers and readers both restate it, so a
    /// malformed artifact cannot claim otherwise.
    /// </summary>
    [JsonPropertyName("class")] public string? Class { get; set; }

    /// <summary>
    /// Whether the cleanup operation itself may succeed if it is attempted again. It says nothing
    /// about the test: a cleanup failure never makes a run retryable, and never makes a failure
    /// repairable.
    /// </summary>
    [JsonPropertyName("retryable")] public bool? Retryable { get; set; }
}

/// <summary>Known phases that can produce a <see cref="MauiFlowSecondaryFailure"/>.</summary>
public static class MauiFlowSecondaryFailurePhases
{
    /// <summary>Owned platform teardown: stopping, uninstalling, or releasing the device session.</summary>
    public const string Cleanup = "cleanup";

    /// <summary>Removal of the build output directory this invocation created and owns.</summary>
    public const string ArtifactCleanup = "artifact-cleanup";

    /// <summary>
    /// The bounded number of secondary failures a report or manifest retains. There are only two
    /// owned cleanup phases, so the cap exists to keep a malformed or hostile artifact bounded
    /// rather than to truncate a real run.
    /// </summary>
    public const int MaxRetained = 4;
}

/// <summary>
/// Values for the <c>displacedBy</c> member of the <c>primaryExecutionOutcome</c> report extension.
/// </summary>
/// <remarks>
/// The extension object predates the two-axis outcome contract, where it held the verdict an owned
/// cleanup failure had overwritten. It now holds the verdict a restatement displaced, which is a
/// different claim, and a reader cannot tell the two apart from <c>secondaryFailures</c> because
/// absent and empty are required to read identically. <c>displacedBy</c> is the discriminator:
/// present means this contract, absent means the older one. It is a closed set, and a value outside
/// it is dropped rather than published, so an imported artifact cannot invent a third meaning.
/// </remarks>
public static class MauiFlowPrimaryOutcomeDisplacements
{
    /// <summary>
    /// The verdict was displaced by a restatement — a later, non-cleanup stage of the invocation
    /// failed, and the report was restated to the category the command exited with.
    /// </summary>
    public const string Restatement = "restatement";
}

/// <summary>Known terminal classifications for a flow run.</summary>
public static class MauiFlowFailureClasses
{
    public const string FlowInvalid = "flow-invalid";
    public const string SchemaUnsupported = "schema-unsupported";
    public const string CapabilityMissing = "capability-missing";
    public const string LeaseConflict = "lease-conflict";
    public const string LeaseLost = "lease-lost";
    public const string Cancelled = "cancelled";
    public const string Timeout = "timeout";
    public const string ResetFailed = "reset-failed";
    public const string PreconditionUnsatisfied = "precondition-unsatisfied";
    public const string RouteStateDrift = "route-state-drift";
    public const string LocatorNotFound = "locator-not-found";
    public const string LocatorAmbiguous = "locator-ambiguous";
    public const string NotVisible = "not-visible";
    public const string Disabled = "disabled";
    public const string UnstableBounds = "unstable-bounds";
    public const string ActionRejected = "action-rejected";
    public const string DriveFailed = "drive-failed";
    public const string UnknownCompletion = "unknown-completion";
    public const string WorkflowCommandConflict = "workflow-command-conflict";
    public const string SecretUnavailable = "secret-unavailable";
    public const string UnsafeValue = "unsafe-value";
    public const string AssertionFailed = "assertion-failed";
    public const string Transport = "transport";
    public const string AgentDisconnected = "agent-disconnected";

    /// <summary>
    /// The application process under test died during the run and the host collected evidence
    /// that it died abnormally. This is only ever emitted from
    /// <see cref="MauiFlowAppProcessEvidence"/> that proves an abnormal exit; an agent that
    /// merely stopped answering stays <see cref="AgentDisconnected"/>.
    /// </summary>
    public const string AppCrash = "app-crash";
    public const string Infrastructure = "infrastructure";
}

/// <summary>
/// Why the application process under test stopped running. Only the reasons listed by
/// <see cref="MauiFlowAppProcessEvidence.ProvesAbnormalExit"/> are treated as crash evidence; the
/// rest exist so a deliberate teardown is recorded as what it is rather than being read as a
/// crash.
/// </summary>
public static class MauiFlowAppExitReasons
{
    /// <summary>An unhandled managed or Java exception terminated the process.</summary>
    public const string Crash = "crash";

    /// <summary>A native fault (for example a fatal signal handled by the platform) terminated the process.</summary>
    public const string CrashNative = "crash-native";

    /// <summary>The platform declared the application not responding and killed it.</summary>
    public const string Anr = "anr";

    /// <summary>A user, operator, or harness explicitly stopped the application.</summary>
    public const string UserRequested = "user-requested";

    /// <summary>An external signal stopped the process without a platform crash record.</summary>
    public const string Signaled = "signaled";

    /// <summary>The application terminated itself normally.</summary>
    public const string ExitSelf = "exit-self";

    /// <summary>The platform reclaimed the process under memory pressure.</summary>
    public const string LowMemory = "low-memory";

    /// <summary>The process is gone but the platform did not name a reason.</summary>
    public const string Unknown = "unknown";
}

/// <summary>
/// What the host observed about the application process after a run failed. Every field is an
/// observation: <see langword="null"/> means the host did not look or could not tell, and is never
/// interpreted as a crash.
/// </summary>
public sealed class MauiFlowAppProcessEvidence
{
    /// <summary>Whether the host attempted the probe at all.</summary>
    [JsonPropertyName("probed")] public bool? Probed { get; set; }

    /// <summary>Where the observation came from, for example <c>android-adb</c> or <c>process-handle</c>.</summary>
    [JsonPropertyName("source")] public string? Source { get; set; }

    /// <summary>Whether the application process was gone when the host looked.</summary>
    [JsonPropertyName("processExited")] public bool? ProcessExited { get; set; }

    /// <summary>The process exit code when the host owns the process handle.</summary>
    [JsonPropertyName("exitCode")] public int? ExitCode { get; set; }

    /// <summary>One of <see cref="MauiFlowAppExitReasons"/>.</summary>
    [JsonPropertyName("exitReason")] public string? ExitReason { get; set; }

    /// <summary>Whether the platform held a crash record for this application.</summary>
    [JsonPropertyName("crashLogPresent")] public bool? CrashLogPresent { get; set; }

    /// <summary>A redacted one-line summary of the crash record, when one was found.</summary>
    [JsonPropertyName("crashSignature")] public string? CrashSignature { get; set; }

    /// <summary>A bounded, redacted excerpt of the crash record.</summary>
    [JsonPropertyName("crashExcerpt")] public List<string>? CrashExcerpt { get; set; }

    /// <summary>Why the probe could not answer, when it could not.</summary>
    [JsonPropertyName("probeError")] public string? ProbeError { get; set; }

    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    /// <summary>
    /// Whether this evidence proves the application died abnormally. The bar is deliberately high:
    /// the process must be observed gone <em>and</em> the platform must independently name an
    /// abnormal reason or hold a crash record. A missing process on its own, a non-zero exit code
    /// on its own, and an operator-requested stop are all explicitly not proof, because the honest
    /// answer for those is that the host does not know why the application went away.
    /// </summary>
    public bool ProvesAbnormalExit() => MauiFlowFailureClassifier.ProvesAppCrash(new MauiFlowFailureFacts
    {
        AppProcessExited = ProcessExited,
        AppExitCode = ExitCode,
        AppExitReason = ExitReason,
        CrashLogPresent = CrashLogPresent,
    });
}

/// <summary>A redaction-aware reference to a bounded artifact associated with a run.</summary>
public sealed class MauiFlowArtifactReference
{
    [JsonPropertyName("artifactId")] public string? ArtifactId { get; set; }
    [JsonPropertyName("kind")] public string? Kind { get; set; }
    [JsonPropertyName("path")] public string? Path { get; set; }
    [JsonPropertyName("digest")] public string? Digest { get; set; }
    [JsonPropertyName("mediaType")] public string? MediaType { get; set; }
    [JsonPropertyName("redacted")] public bool? Redacted { get; set; }
    [JsonPropertyName("createdAt")] public DateTimeOffset? CreatedAt { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>An explicit reason a bounded report omitted otherwise available detail.</summary>
public sealed class MauiFlowReportOmission
{
    [JsonPropertyName("kind")] public string? Kind { get; set; }
    [JsonPropertyName("reason")] public string? Reason { get; set; }
    [JsonPropertyName("count")] public int? Count { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
