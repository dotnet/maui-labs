using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Maui.DevFlow.Testing;

/// <summary>
/// Safe deterministic diagnosis of one execution occurrence. It contains fixed-code explanations
/// and redacted execution facts, never raw logs, app text, prompts, secrets, or device serials.
/// </summary>
public sealed class MauiFlowTriage
{
    [JsonPropertyName("schema")] public int Schema { get; set; } = 1;
    [JsonPropertyName("fingerprintVersion")] public string FingerprintVersion { get; set; } = MauiFlowIncidentFingerprint.RuleVersion;
    [JsonPropertyName("testIdentityFingerprint")] public string? TestIdentityFingerprint { get; set; }
    [JsonPropertyName("incidentFingerprint")] public string? IncidentFingerprint { get; set; }
    [JsonPropertyName("occurrenceFingerprint")] public string? OccurrenceFingerprint { get; set; }
    [JsonPropertyName("classification")] public MauiFlowTriageClassification Classification { get; set; } = new();
    [JsonPropertyName("evidence")] public MauiFlowTriageEvidenceSufficiency Evidence { get; set; } = new();
    [JsonPropertyName("retryable")] public bool Retryable { get; set; }
    [JsonPropertyName("repairEligible")] public bool RepairEligible { get; set; }
    [JsonPropertyName("allowedNextActions")] public List<string> AllowedNextActions { get; set; } = [];
    [JsonPropertyName("localReproductionRequired")] public bool LocalReproductionRequired { get; set; }
    [JsonPropertyName("localReproductionReason")] public string? LocalReproductionReason { get; set; }
    [JsonPropertyName("importedEvidence")] public bool ImportedEvidence { get; set; }
    [JsonPropertyName("summaryCode")] public string? SummaryCode { get; set; }
    [JsonPropertyName("summary")] public string? Summary { get; set; }
    [JsonPropertyName("repairEligibilityCodes")] public List<string> RepairEligibilityCodes { get; set; } = [];
    [JsonPropertyName("execution")] public MauiFlowTriageExecutionFacts Execution { get; set; } = new();
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Classifier output projected without arbitrary exception text.</summary>
public sealed class MauiFlowTriageClassification
{
    [JsonPropertyName("class")] public string? FailureClass { get; set; }
    [JsonPropertyName("code")] public string? Code { get; set; }
    [JsonPropertyName("category")] public string? Category { get; set; }
    [JsonPropertyName("phase")] public string? Phase { get; set; }

    /// <summary>
    /// The four-value ownership axis projected from <see cref="FailureClass"/>. It answers "who
    /// should look at this" and never replaces the 25 stable wire failure classes. It is null when
    /// there is no failure to project, that is when the run passed or carries no classification.
    /// </summary>
    [JsonPropertyName("disposition")] public string? Disposition { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Whether the supplied manifest and report facts support the diagnosis.</summary>
public sealed class MauiFlowTriageEvidenceSufficiency
{
    [JsonPropertyName("state")] public string State { get; set; } = MauiFlowTriageEvidenceStates.Insufficient;
    [JsonPropertyName("missingFacts")] public List<string> MissingFacts { get; set; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Redacted execution facts retained to support human and agent handoff.</summary>
public sealed class MauiFlowTriageExecutionFacts
{
    [JsonPropertyName("manifestId")] public string? ManifestId { get; set; }
    [JsonPropertyName("runId")] public string? RunId { get; set; }
    [JsonPropertyName("flowId")] public string? FlowId { get; set; }
    [JsonPropertyName("host")] public MauiTestExecutionHostFacts? Host { get; set; }
    [JsonPropertyName("build")] public MauiTestExecutionBuildFacts? Build { get; set; }
    [JsonPropertyName("device")] public MauiTestExecutionDeviceFacts? Device { get; set; }
    [JsonPropertyName("lifecycle")] public MauiTestExecutionLifecycleFacts? Lifecycle { get; set; }
    [JsonPropertyName("outcome")] public MauiTestExecutionOutcomeFacts? Outcome { get; set; }
    [JsonPropertyName("artifacts")] public List<MauiTestExecutionArtifact> Artifacts { get; set; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Pure input for <see cref="MauiFlowTriageAnalyzer"/>.</summary>
public sealed class MauiFlowTriageInput
{
    public MauiFlow? Flow { get; set; }
    public MauiFlowRunReport? Report { get; set; }
    public MauiTestExecutionManifest? Manifest { get; set; }
    public MauiTestPlan? Plan { get; set; }
    public MauiFlowReplayEligibilityDecision? ReplayEligibility { get; set; }
    public MauiFlowCheckpoint? ExpectedCheckpoint { get; set; }
    public MauiFlowCheckpoint? CurrentCheckpoint { get; set; }
    public bool? BeforeDispatch { get; set; }
    public bool IsCurrentLocalRun { get; set; }
    public bool ImportedEvidence { get; set; }
    public string? ArtifactTrust { get; set; }
    public MauiArtifactTrustRecord? ImportedArtifact { get; set; }
    public MauiRepairPriorSelectorResolution? PriorActiveSelectorResolution { get; set; }
    public MauiElementFingerprint? TargetFingerprint { get; set; }
    public List<string> AdditionalFailureCodes { get; set; } = [];
}

/// <summary>Known evidence-sufficiency values.</summary>
public static class MauiFlowTriageEvidenceStates
{
    public const string Sufficient = "sufficient";
    public const string Insufficient = "insufficient";
}

/// <summary>
/// The four dispositions a failure class projects onto. This axis is deliberately coarse: it
/// states which owner a failure belongs to, and refuses to guess when the recorded signals cannot
/// separate an app fault from a harness or environment fault.
/// </summary>
public static class MauiFlowTriageDispositions
{
    /// <summary>The app under test behaved differently than the flow recorded.</summary>
    public const string AppRegression = "app-regression";

    /// <summary>The committed flow, selector, or precondition no longer matches the app.</summary>
    public const string TestDrift = "test-drift";

    /// <summary>The failure happened outside the app and the flow, in tooling or the environment.</summary>
    public const string Infrastructure = "infrastructure";

    /// <summary>The recorded signals do not support any of the other three dispositions.</summary>
    public const string Inconclusive = "inconclusive";
}

/// <summary>Known, inert next actions emitted by deterministic triage.</summary>
public static class MauiFlowTriageNextActions
{
    public const string CollectEvidence = "collect-evidence";
    public const string RetryRun = "retry-run";
    public const string ReproduceLocally = "reproduce-locally";
    public const string ReviewSelectorRepair = "review-selector-repair";
    public const string InspectSelectorEvidence = "inspect-selector-evidence";
    public const string RestorePreconditions = "restore-preconditions";
    public const string InvestigateAppRegression = "investigate-app-regression";
    public const string InspectInfrastructure = "inspect-infrastructure";
    public const string SelectSupportedTarget = "select-supported-target";
    public const string FenceUnknownCompletion = "fence-unknown-completion";
    public const string CorrectFlow = "correct-flow";
    public const string InspectActionability = "inspect-actionability";
    public const string ProvideSensitiveInput = "provide-sensitive-input";
    public const string InvestigateFailure = "investigate-failure";
}

/// <summary>Creates byte-stable, redacted triage JSON from the public contract.</summary>
public static class MauiFlowTriageSerializer
{
    private static readonly HashSet<string> KnownNextActions = new(StringComparer.Ordinal)
    {
        MauiFlowTriageNextActions.CollectEvidence,
        MauiFlowTriageNextActions.RetryRun,
        MauiFlowTriageNextActions.ReproduceLocally,
        MauiFlowTriageNextActions.ReviewSelectorRepair,
        MauiFlowTriageNextActions.InspectSelectorEvidence,
        MauiFlowTriageNextActions.RestorePreconditions,
        MauiFlowTriageNextActions.InvestigateAppRegression,
        MauiFlowTriageNextActions.InspectInfrastructure,
        MauiFlowTriageNextActions.SelectSupportedTarget,
        MauiFlowTriageNextActions.FenceUnknownCompletion,
        MauiFlowTriageNextActions.CorrectFlow,
        MauiFlowTriageNextActions.InspectActionability,
        MauiFlowTriageNextActions.ProvideSensitiveInput,
        MauiFlowTriageNextActions.InvestigateFailure,
    };

    public static byte[] SerializeToUtf8Bytes(MauiFlowTriage? triage)
        => JsonSerializer.SerializeToUtf8Bytes(
            CreateSafeProjection(triage),
            MauiTestingJsonContext.Default.MauiFlowTriage);

    public static MauiFlowTriage CreateSafeProjection(MauiFlowTriage? triage)
    {
        triage ??= new MauiFlowTriage();
        var execution = triage.Execution ?? new MauiFlowTriageExecutionFacts();
        var classification = triage.Classification ?? new MauiFlowTriageClassification();
        var evidence = triage.Evidence ?? new MauiFlowTriageEvidenceSufficiency();
        var manifest = MauiTestExecutionManifestSerializer.CreateSafeProjection(new MauiTestExecutionManifest
        {
            ManifestId = execution.ManifestId,
            RunId = execution.RunId,
            FlowId = execution.FlowId,
            Host = execution.Host,
            Build = execution.Build,
            Device = execution.Device,
            Lifecycle = execution.Lifecycle,
            Outcome = execution.Outcome,
            Artifacts = execution.Artifacts ?? [],
        });
        var failureClass = MauiTestingContractRedactor.SafeCode(classification.FailureClass);
        var code = MauiTestingContractRedactor.SafeCode(classification.Code) ?? failureClass;
        var summaryCode = MauiTestingContractRedactor.SafeCode(triage.SummaryCode) ?? code;
        var missingFacts = (evidence.MissingFacts ?? [])
            .Select(MauiTestingContractRedactor.SafeCode)
            .Where(static value => value is not null)
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToList();
        var evidenceSufficient =
            evidence.State == MauiFlowTriageEvidenceStates.Sufficient &&
            missingFacts.Count == 0;
        var canonicalRetryable = failureClass is not null &&
            !string.Equals(failureClass, "passed", StringComparison.Ordinal) &&
            MauiFlowFailureClassifier.Classify(new MauiFlowFailureFacts
            {
                FailureClass = failureClass,
            }).Retryable;
        return new MauiFlowTriage
        {
            Schema = 1,
            FingerprintVersion = MauiFlowIncidentFingerprint.RuleVersion,
            TestIdentityFingerprint = MauiTestingContractRedactor.Fingerprint(
                triage.TestIdentityFingerprint,
                "test"),
            IncidentFingerprint = MauiTestingContractRedactor.Fingerprint(
                triage.IncidentFingerprint,
                "incident"),
            OccurrenceFingerprint = MauiTestingContractRedactor.Fingerprint(
                triage.OccurrenceFingerprint,
                "occurrence"),
            Classification = new MauiFlowTriageClassification
            {
                FailureClass = failureClass,
                Code = code,
                Category = MauiTestingContractRedactor.SafeCode(classification.Category),
                Phase = MauiTestingContractRedactor.SafeCode(classification.Phase),
                // Derived, never copied: an imported triage cannot assert a disposition its own
                // failure class does not support.
                Disposition = MauiFlowFailureClassifier.Project(failureClass),
            },
            Evidence = new MauiFlowTriageEvidenceSufficiency
            {
                State = evidenceSufficient
                    ? MauiFlowTriageEvidenceStates.Sufficient
                    : MauiFlowTriageEvidenceStates.Insufficient,
                MissingFacts = missingFacts,
            },
            Retryable = canonicalRetryable,
            RepairEligible = triage.RepairEligible &&
                evidenceSufficient &&
                !triage.ImportedEvidence,
            AllowedNextActions = (triage.AllowedNextActions ?? [])
                .Where(KnownNextActions.Contains)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static value => value, StringComparer.Ordinal)
                .ToList(),
            LocalReproductionRequired = triage.LocalReproductionRequired || triage.ImportedEvidence,
            LocalReproductionReason = MauiTestingContractRedactor.SafeCode(triage.LocalReproductionReason),
            ImportedEvidence = triage.ImportedEvidence,
            SummaryCode = summaryCode,
            Summary = MauiFlowTriageText.Summary(failureClass ?? code),
            RepairEligibilityCodes = (triage.RepairEligibilityCodes ?? [])
                .Select(MauiTestingContractRedactor.SafeCode)
                .Where(static value => value is not null)
                .Cast<string>()
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static value => value, StringComparer.Ordinal)
                .ToList(),
            Execution = new MauiFlowTriageExecutionFacts
            {
                ManifestId = MauiTestingContractRedactor.Fingerprint(
                    manifest.ManifestId,
                    "triage-manifest-id"),
                RunId = MauiTestingContractRedactor.Fingerprint(
                    manifest.RunId,
                    "triage-run-id"),
                FlowId = MauiTestingContractRedactor.Fingerprint(
                    manifest.FlowId,
                    "triage-flow-id"),
                Host = manifest.Host,
                Build = manifest.Build,
                Device = manifest.Device,
                Lifecycle = manifest.Lifecycle,
                Outcome = manifest.Outcome,
                Artifacts = manifest.Artifacts,
            },
        };
    }
}

internal static class MauiFlowTriageText
{
    public static string Summary(string? code) => code switch
    {
        "passed" => "The flow completed without a classified failure.",
        MauiFlowFailureClasses.FlowInvalid => "The executable flow did not pass deterministic validation.",
        MauiFlowFailureClasses.SchemaUnsupported => "The executable flow uses an unsupported schema.",
        MauiFlowFailureClasses.CapabilityMissing => "The selected target did not advertise a required capability.",
        MauiFlowFailureClasses.PreconditionUnsatisfied => "The recorded preconditions were not satisfied.",
        MauiFlowFailureClasses.RouteStateDrift => "The observed route or state differed from the expected checkpoint.",
        MauiFlowFailureClasses.LocatorNotFound => "The committed selector did not resolve on the recorded target.",
        MauiFlowFailureClasses.LocatorAmbiguous => "The committed selector resolved more than one target.",
        MauiFlowFailureClasses.AssertionFailed => "A required independent assertion did not match.",
        MauiFlowFailureClasses.Transport => "The execution transport failed before a reliable result was recorded.",
        MauiFlowFailureClasses.AgentDisconnected => "The bound app agent disconnected during execution.",
        MauiFlowFailureClasses.AppCrash => "The app under test exited abnormally during execution.",
        MauiFlowFailureClasses.UnknownCompletion => "The mutation completion state is unknown and must be fenced.",
        MauiFlowFailureClasses.NotVisible or
        MauiFlowFailureClasses.Disabled or
        MauiFlowFailureClasses.UnstableBounds => "The resolved target was not safely actionable.",
        MauiFlowFailureClasses.SecretUnavailable or
        MauiFlowFailureClasses.UnsafeValue => "A required input was unavailable or unsafe to replay.",
        MauiFlowFailureClasses.Infrastructure => "The execution failed outside the semantic flow.",
        null or "" => "The supplied evidence is insufficient for a failure classification.",
        _ => "The flow failed with a deterministic DevFlow classification.",
    };
}
