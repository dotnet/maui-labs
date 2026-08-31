using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Maui.DevFlow.Testing;

/// <summary>
/// Bounded, redacted result of comparing one imported diagnostic artifact with one newly executed
/// local run. This report records no proposal, approval, apply, validation, or rollback authority.
/// </summary>
public sealed class MauiLocalReproductionReport
{
    [JsonPropertyName("schema")] public int Schema { get; set; } = 1;
    [JsonPropertyName("importedArtifact")] public MauiImportedArtifactIdentity? ImportedArtifact { get; set; }
    [JsonPropertyName("importedArtifactKind")] public string? ImportedArtifactKind { get; set; }
    [JsonPropertyName("importedArtifactDigest")] public string? ImportedArtifactDigest { get; set; }
    [JsonPropertyName("importTrustState")] public string ImportTrustState { get; set; } = MauiArtifactTrustStates.Untrusted;
    [JsonPropertyName("localRunId")] public string? LocalRunId { get; set; }
    [JsonPropertyName("localExitCategory")] public string? LocalExitCategory { get; set; }
    [JsonPropertyName("localManifestDigest")] public string? LocalManifestDigest { get; set; }
    [JsonPropertyName("localReportDigest")] public string? LocalReportDigest { get; set; }
    [JsonPropertyName("matched")] public bool Matched { get; set; }
    [JsonPropertyName("trustState")] public string TrustState { get; set; } = MauiArtifactTrustStates.Untrusted;
    [JsonPropertyName("failureCorrespondence")] public string FailureCorrespondence { get; set; } = "indeterminate";
    [JsonPropertyName("reasonCodes")] public List<string> ReasonCodes { get; set; } = [];
    [JsonPropertyName("missingFacts")] public List<string> MissingFacts { get; set; } = [];
    [JsonPropertyName("importedFailureFingerprint")] public string? ImportedFailureFingerprint { get; set; }
    [JsonPropertyName("importedStepFingerprint")] public string? ImportedStepFingerprint { get; set; }
    [JsonPropertyName("importedRuntimeProfileFingerprint")] public string? ImportedRuntimeProfileFingerprint { get; set; }
    [JsonPropertyName("localFailureFingerprint")] public string? LocalFailureFingerprint { get; set; }
    [JsonPropertyName("localStepFingerprint")] public string? LocalStepFingerprint { get; set; }
    [JsonPropertyName("localRuntimeProfileFingerprint")] public string? LocalRuntimeProfileFingerprint { get; set; }
    [JsonPropertyName("localExpectedCheckpointFingerprint")] public string? LocalExpectedCheckpointFingerprint { get; set; }
    [JsonPropertyName("localObservedCheckpointFingerprint")] public string? LocalObservedCheckpointFingerprint { get; set; }
    [JsonPropertyName("localArtifacts")] public List<MauiTestExecutionArtifact> LocalArtifacts { get; set; } = [];
    [JsonPropertyName("brokerBindingPersisted")] public bool BrokerBindingPersisted { get; set; }
    [JsonPropertyName("approvalGranted")] public bool ApprovalGranted { get; set; }
    [JsonPropertyName("proposalCreated")] public bool ProposalCreated { get; set; }
    [JsonPropertyName("reviewAction")] public string? ReviewAction { get; set; }
    [JsonPropertyName("reviewCommand")] public string? ReviewCommand { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Creates byte-stable, redacted local-reproduction JSON.</summary>
public static class MauiLocalReproductionReportSerializer
{
    public const string FileName = "local-reproduction.json";

    private const string MatchedReviewAction = "open-inspector-workbench-repair-review";
    private const string DiagnosticReviewAction = "inspect-local-reproduction-diagnostics";
    private const string InspectorReviewCommand =
        "maui devflow inspect --test <committed-flow.md> --trace <imported-artifact>";

    public static byte[] SerializeToUtf8Bytes(MauiLocalReproductionReport? report)
        => JsonSerializer.SerializeToUtf8Bytes(
            CreateSafeProjection(report),
            MauiTestingJsonContext.Default.MauiLocalReproductionReport);

    public static MauiLocalReproductionReport CreateSafeProjection(MauiLocalReproductionReport? report)
    {
        report ??= new MauiLocalReproductionReport();
        var importTrust = MauiArtifactTrustStates.IsKnown(report.ImportTrustState)
            ? report.ImportTrustState
            : MauiArtifactTrustStates.Untrusted;
        var requestedTrust = MauiArtifactTrustStates.IsKnown(report.TrustState)
            ? report.TrustState
            : importTrust;
        var matched = report.Matched &&
            string.Equals(requestedTrust, MauiArtifactTrustStates.LocallyReproduced, StringComparison.Ordinal);
        var trust = matched
            ? MauiArtifactTrustStates.LocallyReproduced
            : string.Equals(requestedTrust, MauiArtifactTrustStates.LocallyReproduced, StringComparison.Ordinal)
                ? importTrust
                : requestedTrust;
        var artifacts = MauiTestExecutionManifestSerializer.CreateSafeProjection(
            new MauiTestExecutionManifest { Artifacts = report.LocalArtifacts ?? [] }).Artifacts;
        var reasonCodes = SafeCodes(report.ReasonCodes);
        var missingFacts = SafeCodes(report.MissingFacts);

        return new MauiLocalReproductionReport
        {
            Schema = 1,
            ImportedArtifact = report.ImportedArtifact?.IsValid == true
                ? new MauiImportedArtifactIdentity
                {
                    Namespace = MauiArtifactIdentityNamespaces.ImportedArtifact,
                    Id = report.ImportedArtifact.Id,
                }
                : null,
            ImportedArtifactKind = report.ImportedArtifactKind is "flow-run" or "mauitrace"
                ? report.ImportedArtifactKind
                : null,
            ImportedArtifactDigest = MauiTestingContractRedactor.Fingerprint(
                report.ImportedArtifactDigest,
                "imported-artifact"),
            ImportTrustState = importTrust,
            LocalRunId = MauiTestingContractRedactor.SafeIdentifierOrFingerprint(
                report.LocalRunId,
                "local-run-id"),
            LocalExitCategory = MauiTestingContractRedactor.SafeCode(report.LocalExitCategory),
            LocalManifestDigest = MauiTestingContractRedactor.Fingerprint(
                report.LocalManifestDigest,
                "local-manifest"),
            LocalReportDigest = MauiTestingContractRedactor.Fingerprint(
                report.LocalReportDigest,
                "local-report"),
            Matched = matched,
            TrustState = trust,
            FailureCorrespondence = GetFailureCorrespondence(reasonCodes, missingFacts),
            ReasonCodes = reasonCodes,
            MissingFacts = missingFacts,
            ImportedFailureFingerprint = SafeFingerprint(
                report.ImportedFailureFingerprint,
                "imported-failure"),
            ImportedStepFingerprint = SafeFingerprint(
                report.ImportedStepFingerprint,
                "imported-step"),
            ImportedRuntimeProfileFingerprint = SafeFingerprint(
                report.ImportedRuntimeProfileFingerprint,
                "imported-runtime-profile"),
            LocalFailureFingerprint = SafeFingerprint(
                report.LocalFailureFingerprint,
                "local-failure"),
            LocalStepFingerprint = SafeFingerprint(
                report.LocalStepFingerprint,
                "local-step"),
            LocalRuntimeProfileFingerprint = SafeFingerprint(
                report.LocalRuntimeProfileFingerprint,
                "local-runtime-profile"),
            LocalExpectedCheckpointFingerprint = SafeFingerprint(
                report.LocalExpectedCheckpointFingerprint,
                "local-expected-checkpoint"),
            LocalObservedCheckpointFingerprint = SafeFingerprint(
                report.LocalObservedCheckpointFingerprint,
                "local-observed-checkpoint"),
            LocalArtifacts = artifacts,
            BrokerBindingPersisted = false,
            ApprovalGranted = false,
            ProposalCreated = false,
            ReviewAction = matched ? MatchedReviewAction : DiagnosticReviewAction,
            ReviewCommand = matched ? InspectorReviewCommand : null,
        };
    }

    private static List<string> SafeCodes(IEnumerable<string>? values)
        => (values ?? [])
            .Select(SafeDiagnosticCode)
            .Where(static value => value is not null)
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .Take(64)
            .ToList();

    private static string? SafeDiagnosticCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        if (trimmed.Length > 128 ||
            trimmed.Any(char.IsWhiteSpace) ||
            trimmed.Any(static character =>
                !char.IsAsciiLetterOrDigit(character) &&
                character is not '-' and not '_' and not '.' and not ':' and not '/'))
        {
            return null;
        }
        return trimmed.StartsWith("/", StringComparison.Ordinal) ||
               trimmed.Contains("://", StringComparison.Ordinal)
            ? null
            : trimmed;
    }

    private static string? SafeFingerprint(string? value, string domain)
        => MauiTestingContractRedactor.Fingerprint(value, domain);

    private static string GetFailureCorrespondence(
        IReadOnlyCollection<string> reasonCodes,
        IReadOnlyCollection<string> missingFacts)
    {
        var same = reasonCodes.Contains("locally-reproduced", StringComparer.Ordinal) ||
            reasonCodes.Contains("failure-correspondence-same", StringComparer.Ordinal);
        var different = reasonCodes.Any(static code => code is
            "failureCode-mismatch" or
            "failureClass-mismatch" or
            "failureStep-mismatch" or
            "expectedCheckpoint-mismatch" or
            "observedCheckpoint-mismatch");
        var noLocalFailure = missingFacts.Contains("localFailure", StringComparer.Ordinal);
        var identityMismatch = reasonCodes.Any(static code => code is
            "flowDigest-mismatch" or
            "appSourceFingerprint-mismatch" or
            "platform-mismatch" or
            "runtimeProfile-mismatch");
        var missingFailureFact = missingFacts.Any(static fact =>
            fact.Equals("failure", StringComparison.Ordinal) ||
            fact.StartsWith("imported.failure", StringComparison.Ordinal) ||
            fact.StartsWith("local.failure", StringComparison.Ordinal) ||
            fact.StartsWith("imported.expectedCheckpoint", StringComparison.Ordinal) ||
            fact.StartsWith("local.expectedCheckpoint", StringComparison.Ordinal) ||
            fact.StartsWith("imported.observedCheckpoint", StringComparison.Ordinal) ||
            fact.StartsWith("local.observedCheckpoint", StringComparison.Ordinal));
        var missingIdentityFact = missingFacts.Any(static fact =>
            fact.EndsWith("flowDigest", StringComparison.Ordinal) ||
            fact.EndsWith("appSourceFingerprint", StringComparison.Ordinal) ||
            fact.EndsWith("platform", StringComparison.Ordinal) ||
            fact.EndsWith("runtimeProfile", StringComparison.Ordinal));

        var states = (same ? 1 : 0) + (different ? 1 : 0) + (noLocalFailure ? 1 : 0);
        if (states != 1 || missingFailureFact || identityMismatch || missingIdentityFact)
            return "indeterminate";
        if (different)
            return "different-failure";
        if (noLocalFailure)
            return "no-local-failure";
        return "same-failure";
    }
}
