using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Maui.DevFlow.Testing;

/// <summary>Stable trust-state values for imported diagnostic artifacts.</summary>
public static class MauiArtifactTrustStates
{
    public const string Untrusted = "untrusted";
    public const string Attested = "attested";
    public const string LocallyReproduced = "locally-reproduced";

    public static bool IsKnown(string? value)
        => value is Untrusted or Attested or LocallyReproduced;
}

/// <summary>Namespaces that prevent imported identities from impersonating local workflow identities.</summary>
public static class MauiArtifactIdentityNamespaces
{
    public const string ImportedArtifact = "imported-artifact";
    public const string LocalRun = "local-run";
    public const string Flow = "flow";
    public const string Proposal = "proposal";
}

/// <summary>
/// A broker-generated opaque identity for an imported artifact. It is intentionally not a local
/// run, flow, or proposal identifier, regardless of identifiers embedded in hostile input.
/// </summary>
public sealed class MauiImportedArtifactIdentity
{
    public const string OpaqueIdPrefix = "iat_";

    [JsonPropertyName("namespace")]
    public string Namespace { get; set; } = MauiArtifactIdentityNamespaces.ImportedArtifact;

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonIgnore]
    public bool IsValid =>
        string.Equals(Namespace, MauiArtifactIdentityNamespaces.ImportedArtifact, StringComparison.Ordinal) &&
        IsImportedArtifactId(Id);

    public static MauiImportedArtifactIdentity Create()
        => new()
        {
            Id = OpaqueIdPrefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(),
        };

    public static bool IsImportedArtifactId(string? value)
    {
        if (value is null || value.Length != OpaqueIdPrefix.Length + 32 ||
            !value.StartsWith(OpaqueIdPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var character in value.AsSpan(OpaqueIdPrefix.Length))
        {
            if (!(character is >= '0' and <= '9' or >= 'a' and <= 'f'))
                return false;
        }

        return true;
    }
}

/// <summary>
/// Producer facts that a verifier can attest independently of artifact content. The core library
/// never contacts an issuer; callers must supply facts only after their own verification.
/// </summary>
public sealed class MauiArtifactProvenanceSubject
{
    [JsonPropertyName("repository")]
    public string? Repository { get; set; }

    [JsonPropertyName("workflow")]
    public string? Workflow { get; set; }

    [JsonPropertyName("commit")]
    public string? Commit { get; set; }

    [JsonPropertyName("issuer")]
    public string? Issuer { get; set; }

    [JsonPropertyName("artifactDigest")]
    public string? ArtifactDigest { get; set; }

    [JsonPropertyName("appDigest")]
    public string? AppDigest { get; set; }

    [JsonPropertyName("packageDigest")]
    public string? PackageDigest { get; set; }

    [JsonPropertyName("flowDigest")]
    public string? FlowDigest { get; set; }

    [JsonPropertyName("platform")]
    public string? Platform { get; set; }

    [JsonPropertyName("deviceProfile")]
    public string? DeviceProfile { get; set; }

    [JsonPropertyName("capturedAt")]
    public DateTimeOffset? CapturedAt { get; set; }

    [JsonPropertyName("policyVersion")]
    public string? PolicyVersion { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Caller-supplied facts that were verified outside this provider-neutral core.</summary>
public sealed class MauiArtifactVerifiedProvenanceFacts
{
    [JsonPropertyName("verified")]
    public bool? Verified { get; set; }

    [JsonPropertyName("verifiedBy")]
    public string? VerifiedBy { get; set; }

    [JsonPropertyName("verifiedAt")]
    public DateTimeOffset? VerifiedAt { get; set; }

    [JsonPropertyName("subject")]
    public MauiArtifactProvenanceSubject? Subject { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>
/// Policy configured by a trusted host. Attestation is disabled by default and requires expected
/// repository, workflow, commit, and artifact-digest facts before it can be enabled.
/// </summary>
public sealed class MauiArtifactTrustPolicy
{
    [JsonPropertyName("allowAttestation")]
    public bool AllowAttestation { get; set; }

    [JsonPropertyName("expectedProvenance")]
    public MauiArtifactProvenanceSubject? ExpectedProvenance { get; set; }

    [JsonPropertyName("policyVersion")]
    public string? PolicyVersion { get; set; }

    [JsonPropertyName("requireVerifiedIssuer")]
    public bool RequireVerifiedIssuer { get; set; } = true;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>
/// Integrity facts calculated by the import boundary. A matching ZIP or report-internal hash can
/// prove bytes were not altered, but is never a provenance assertion.
/// </summary>
public sealed class MauiArtifactIntegrityVerification
{
    [JsonPropertyName("algorithm")]
    public string Algorithm { get; set; } = "sha256";

    [JsonPropertyName("artifactDigest")]
    public string? ArtifactDigest { get; set; }

    [JsonPropertyName("verified")]
    public bool? Verified { get; set; }

    [JsonPropertyName("internalHashesPresent")]
    public bool? InternalHashesPresent { get; set; }

    [JsonPropertyName("internalHashesVerified")]
    public bool? InternalHashesVerified { get; set; }

    [JsonPropertyName("integrityOnly")]
    public bool IntegrityOnly { get; set; } = true;

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>A machine-readable reason for a trust decision.</summary>
public sealed class MauiArtifactTrustReason
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("blocking")]
    public bool? Blocking { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>An explicitly omitted fact that prevented a stronger trust decision.</summary>
public sealed class MauiArtifactTrustOmission
{
    [JsonPropertyName("field")]
    public string? Field { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>The bounded, explainable outcome of integrity, attestation, or reproduction checks.</summary>
public sealed class MauiArtifactTrustVerificationResult
{
    [JsonPropertyName("state")]
    public string State { get; set; } = MauiArtifactTrustStates.Untrusted;

    [JsonIgnore]
    public string TrustState
    {
        get => State;
        set => State = value;
    }

    [JsonPropertyName("integrity")]
    public MauiArtifactIntegrityVerification? Integrity { get; set; }

    [JsonPropertyName("policyVersion")]
    public string? PolicyVersion { get; set; }

    [JsonPropertyName("reasons")]
    public List<MauiArtifactTrustReason> Reasons { get; set; } = [];

    [JsonPropertyName("omissions")]
    public List<MauiArtifactTrustOmission> Omissions { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>
/// A redacted diagnostic projection retained from imported data. Fingerprints are one-way hashes;
/// embedded IDs, messages, workflow Markdown, screenshots, and raw ZIP/report content are absent.
/// </summary>
public sealed class MauiImportedArtifactSafeProjection
{
    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    [JsonPropertyName("sourceSchema")]
    public string? SourceSchema { get; set; }

    [JsonPropertyName("flowFingerprint")]
    public string? FlowFingerprint { get; set; }

    [JsonPropertyName("appBuildFingerprint")]
    public string? AppBuildFingerprint { get; set; }

    [JsonPropertyName("appSourceFingerprint")]
    public string? AppSourceFingerprint { get; set; }

    [JsonPropertyName("packageFingerprint")]
    public string? PackageFingerprint { get; set; }

    /// <summary>
    /// A one-way fingerprint of the imported occurrence's signing-insensitive normalized payload
    /// digest, when the producer published one.
    /// </summary>
    [JsonPropertyName("normalizedPayloadFingerprint")]
    public string? NormalizedPayloadFingerprint { get; set; }

    [JsonPropertyName("platformFingerprint")]
    public string? PlatformFingerprint { get; set; }

    [JsonPropertyName("deviceProfileFingerprint")]
    public string? DeviceProfileFingerprint { get; set; }

    [JsonPropertyName("runtimeProfileFingerprint")]
    public string? RuntimeProfileFingerprint { get; set; }

    [JsonPropertyName("capturedAt")]
    public DateTimeOffset? CapturedAt { get; set; }

    [JsonPropertyName("outcome")]
    public string? Outcome { get; set; }

    [JsonPropertyName("failure")]
    public MauiImportedFailureProjection? Failure { get; set; }

    /// <summary>
    /// Whether the producing host recorded a failure of its own owned cleanup for this occurrence.
    /// The imported outcome is now the run's preserved verdict, so a cleanup failure no longer
    /// shows up there; without this flag a run whose source host never tore itself down would be
    /// imported as an ordinary reproducible failure. <see langword="null"/> means the artifact does
    /// not say — a producer that predates the two-axis contract records no cleanup facts at all,
    /// and reading that silence as <see langword="false"/> would assert a clean teardown the
    /// artifact never claimed. Only <see langword="true"/> refuses a reproduction.
    /// </summary>
    [JsonPropertyName("ownedCleanupIncomplete")]
    public bool? OwnedCleanupIncomplete { get; set; }

    [JsonPropertyName("truncated")]
    public bool? Truncated { get; set; }

    [JsonPropertyName("omissions")]
    public List<MauiArtifactTrustOmission> Omissions { get; set; } = [];

    [JsonPropertyName("embeddedIdentifierDigests")]
    public List<string> EmbeddedIdentifierDigests { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Redacted failure facts used to compare an imported failure with a local run.</summary>
public sealed class MauiImportedFailureProjection
{
    [JsonPropertyName("failureKey")]
    public string? FailureKey { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("class")]
    public string? Class { get; set; }

    [JsonPropertyName("stepFingerprint")]
    public string? StepFingerprint { get; set; }

    [JsonPropertyName("expectedCheckpointFingerprint")]
    public string? ExpectedCheckpointFingerprint { get; set; }

    [JsonPropertyName("observedCheckpointFingerprint")]
    public string? ObservedCheckpointFingerprint { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>
/// Stored imported-artifact metadata. The raw input is intentionally not represented by this
/// contract and must not be exposed from broad broker endpoints.
/// </summary>
public sealed class MauiArtifactTrustRecord
{
    [JsonPropertyName("identity")]
    public MauiImportedArtifactIdentity? Identity { get; set; }

    [JsonPropertyName("artifactKind")]
    public string? ArtifactKind { get; set; }

    [JsonPropertyName("importedAt")]
    public DateTimeOffset? ImportedAt { get; set; }

    [JsonPropertyName("integrity")]
    public MauiArtifactIntegrityVerification? Integrity { get; set; }

    [JsonPropertyName("verification")]
    public MauiArtifactTrustVerificationResult Verification { get; set; } = new();

    [JsonPropertyName("projection")]
    public MauiImportedArtifactSafeProjection? Projection { get; set; }

    [JsonPropertyName("localReproduction")]
    public MauiLocalReproductionBinding? LocalReproduction { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Token-gated summary safe to return from an imported-artifact status endpoint.</summary>
public sealed class MauiArtifactTrustStatus
{
    [JsonPropertyName("identity")]
    public MauiImportedArtifactIdentity? Identity { get; set; }

    [JsonPropertyName("artifactKind")]
    public string? ArtifactKind { get; set; }

    [JsonPropertyName("importedAt")]
    public DateTimeOffset? ImportedAt { get; set; }

    [JsonPropertyName("expiresAt")]
    public DateTimeOffset? ExpiresAt { get; set; }

    [JsonPropertyName("verification")]
    public MauiArtifactTrustVerificationResult? Verification { get; set; }

    [JsonPropertyName("rawContentRetained")]
    public bool RawContentRetained { get; set; }

    [JsonPropertyName("hasSafeProjection")]
    public bool HasSafeProjection { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Locally observed facts that may establish a new reproduction of an imported failure.</summary>
public sealed class MauiLocalReproductionFacts
{
    [JsonPropertyName("localRunId")]
    public string? LocalRunId { get; set; }

    [JsonPropertyName("isNewLocalRun")]
    public bool? IsNewLocalRun { get; set; }

    [JsonPropertyName("startedAt")]
    public DateTimeOffset? StartedAt { get; set; }

    [JsonPropertyName("flowDigest")]
    public string? FlowDigest { get; set; }

    [JsonPropertyName("appBuildFingerprint")]
    public string? AppBuildFingerprint { get; set; }

    [JsonPropertyName("appSourceFingerprint")]
    public string? AppSourceFingerprint { get; set; }

    [JsonPropertyName("packageDigest")]
    public string? PackageDigest { get; set; }

    /// <summary>
    /// The local run's signing-insensitive normalized payload digest, when available.
    /// </summary>
    /// <remarks>
    /// This is published as a diagnostic fact. It is deliberately not used to rescue a
    /// <c>packageDigest</c> mismatch between two occurrences, because no platform has yet been
    /// shown to produce a byte-stable normalized payload across two builds. Two <c>flow run</c>
    /// invocations of one flow, run back to back on one Android device against one clean commit
    /// with no edit in between, produced two distinct normalized payload digests. The
    /// normalization already excludes signature material and neutralizes DevFlow's injected agent
    /// session id, so by construction those differences are in payload bytes that are neither.
    /// Treating this digest as a cross-occurrence identity would therefore assert something
    /// untrue.
    /// </remarks>
    [JsonPropertyName("normalizedPayloadDigest")]
    public string? NormalizedPayloadDigest { get; set; }

    [JsonPropertyName("platform")]
    public string? Platform { get; set; }

    [JsonPropertyName("deviceProfile")]
    public string? DeviceProfile { get; set; }

    [JsonPropertyName("runtimeProfileFingerprint")]
    public string? RuntimeProfileFingerprint { get; set; }

    [JsonPropertyName("failure")]
    public MauiLocalFailureFacts? Failure { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Relevant facts from a local run's failure.</summary>
public sealed class MauiLocalFailureFacts
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("class")]
    public string? Class { get; set; }

    [JsonPropertyName("stepId")]
    public string? StepId { get; set; }

    [JsonPropertyName("expectedCheckpoint")]
    public MauiFlowCheckpoint? ExpectedCheckpoint { get; set; }

    [JsonPropertyName("observedCheckpoint")]
    public MauiFlowCheckpoint? ObservedCheckpoint { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Trusted current-workspace facts that a local reproduction must match.</summary>
public sealed class MauiLocalReproductionExpectation
{
    [JsonPropertyName("flowDigest")]
    public string? FlowDigest { get; set; }

    [JsonPropertyName("appBuildFingerprint")]
    public string? AppBuildFingerprint { get; set; }

    [JsonPropertyName("appSourceFingerprint")]
    public string? AppSourceFingerprint { get; set; }

    [JsonPropertyName("packageDigest")]
    public string? PackageDigest { get; set; }

    [JsonPropertyName("platform")]
    public string? Platform { get; set; }

    [JsonPropertyName("deviceProfile")]
    public string? DeviceProfile { get; set; }

    [JsonPropertyName("runtimeProfileFingerprint")]
    public string? RuntimeProfileFingerprint { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>The immutable link from an imported failure to a newly executed local run.</summary>
public sealed class MauiLocalReproductionBinding
{
    [JsonPropertyName("importedArtifact")]
    public MauiImportedArtifactIdentity? ImportedArtifact { get; set; }

    [JsonPropertyName("importedFailureKey")]
    public string? ImportedFailureKey { get; set; }

    [JsonPropertyName("localRunId")]
    public string? LocalRunId { get; set; }

    [JsonPropertyName("boundAt")]
    public DateTimeOffset? BoundAt { get; set; }

    [JsonPropertyName("matched")]
    public bool? Matched { get; set; }

    [JsonPropertyName("verification")]
    public MauiArtifactTrustVerificationResult? Verification { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Result of checking whether a local run can establish reproduction trust.</summary>
public sealed class MauiLocalReproductionEvaluation
{
    [JsonPropertyName("verification")]
    public MauiArtifactTrustVerificationResult Verification { get; set; } = new();

    [JsonPropertyName("binding")]
    public MauiLocalReproductionBinding Binding { get; set; } = new();
}

/// <summary>Result returned to a future repair or source-proposal service.</summary>
public sealed class MauiArtifactProposalDecision
{
    [JsonPropertyName("allowed")]
    public bool Allowed { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

/// <summary>
/// Mandatory trust hook for future repair and source-proposal services. This is deliberately a
/// policy decision only; it never creates, applies, or validates a proposal.
/// </summary>
public static class MauiArtifactProposalPolicy
{
    public static MauiArtifactProposalDecision CanCreateProposal(MauiArtifactTrustRecord? artifact)
    {
        if (artifact?.Identity?.IsValid != true)
        {
            return Denied("imported-artifact-required",
                "A valid imported-artifact identity is required for an imported-evidence proposal.");
        }

        if (!string.Equals(
                artifact.Verification?.State,
                MauiArtifactTrustStates.LocallyReproduced,
                StringComparison.Ordinal))
        {
            return Denied("local-reproduction-required",
                "Imported evidence can create diagnostics only until the failure is reproduced by a new local run.");
        }

        var binding = artifact.LocalReproduction;
        if (binding?.Matched != true ||
            binding.ImportedArtifact?.IsValid != true ||
            !string.Equals(binding.ImportedArtifact.Id, artifact.Identity.Id, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(binding.LocalRunId) ||
            MauiImportedArtifactIdentity.IsImportedArtifactId(binding.LocalRunId) ||
            !string.Equals(
                binding.Verification?.State,
                MauiArtifactTrustStates.LocallyReproduced,
                StringComparison.Ordinal))
        {
            return Denied("local-reproduction-binding-required",
                "A matching binding to a distinct local run is required before creating a proposal.");
        }

        return new MauiArtifactProposalDecision { Allowed = true, Reason = "locally-reproduced" };
    }

    private static MauiArtifactProposalDecision Denied(string reason, string message)
        => new() { Allowed = false, Reason = reason, Message = message };
}

/// <summary>
/// Repair engines must call this gate before creating any repair candidate. It does not generate,
/// rank, apply, or persist a repair proposal.
/// </summary>
public static class MauiFlowRepairProposalTrustPolicy
{
    public static MauiArtifactProposalDecision CanCreateProposal(MauiArtifactTrustRecord? artifact)
        => MauiArtifactProposalPolicy.CanCreateProposal(artifact);
}

/// <summary>
/// Source-proposal engines must call this gate before creating any source candidate. It does not
/// inspect source, generate a patch, or apply a workspace change.
/// </summary>
public static class MauiSourceProposalTrustPolicy
{
    public static MauiArtifactProposalDecision CanCreateProposal(MauiArtifactTrustRecord? artifact)
        => MauiArtifactProposalPolicy.CanCreateProposal(artifact);
}

/// <summary>
/// Pure trust evaluator. It consumes only caller-supplied verification facts and local-run facts;
/// embedded report or ZIP content has no path to upgrade trust by itself.
/// </summary>
public static class MauiArtifactTrustEvaluator
{
    public static MauiArtifactTrustVerificationResult EvaluateImport(
        MauiArtifactTrustPolicy? policy,
        MauiArtifactIntegrityVerification? integrity,
        MauiArtifactVerifiedProvenanceFacts? verifiedProvenance)
    {
        var result = CreateResult(MauiArtifactTrustStates.Untrusted, integrity, policy?.PolicyVersion);
        result.Reasons.Add(Reason(
            "default-untrusted",
            "Imported artifacts begin untrusted and are diagnostic-only until independently verified or reproduced.",
            blocking: false));

        if (integrity?.InternalHashesVerified == true)
        {
            result.Reasons.Add(Reason(
                "internal-hashes-integrity-only",
                "Artifact-internal hashes establish integrity only and are not provenance facts.",
                blocking: false));
        }

        if (integrity?.Verified != true || string.IsNullOrWhiteSpace(integrity.ArtifactDigest))
        {
            result.Reasons.Add(Reason(
                "integrity-unverified",
                "The import boundary did not calculate a bounded artifact digest.",
                blocking: true));
            return result;
        }

        if (policy?.AllowAttestation != true)
        {
            result.Reasons.Add(Reason(
                "attestation-not-configured",
                "No trusted attestation policy is configured for this import.",
                blocking: false));
            return result;
        }

        var expected = policy.ExpectedProvenance;
        if (!HasRequiredExpectedPolicy(expected, result))
            return result;

        if (verifiedProvenance?.Verified != true)
        {
            result.Reasons.Add(Reason(
                "provenance-not-verified",
                "Attestation requires caller-supplied facts verified outside the artifact.",
                blocking: true));
            return result;
        }

        if (string.IsNullOrWhiteSpace(verifiedProvenance.VerifiedBy))
        {
            AddOmission(result, "verifiedBy", "The independent verifier identity is required.");
            return result;
        }

        if (verifiedProvenance.VerifiedAt is null)
        {
            AddOmission(result, "verifiedAt", "The independent verification time is required.");
            return result;
        }

        var subject = verifiedProvenance.Subject;
        if (subject is null)
        {
            AddOmission(result, "subject", "The verified provenance subject is required.");
            return result;
        }

        if (policy.RequireVerifiedIssuer && string.IsNullOrWhiteSpace(subject.Issuer))
        {
            AddOmission(result, "issuer", "The verified provenance issuer is required.");
            return result;
        }

        var configured = expected!;
        if (!Matches("repository", configured.Repository, subject.Repository, result) ||
            !Matches("workflow", configured.Workflow, subject.Workflow, result) ||
            !Matches("commit", configured.Commit, subject.Commit, result) ||
            !Matches("artifactDigest", configured.ArtifactDigest, subject.ArtifactDigest, result) ||
            !Matches("artifactDigest", integrity.ArtifactDigest, subject.ArtifactDigest, result) ||
            !MatchesOptional("issuer", configured.Issuer, subject.Issuer, result) ||
            !MatchesOptional("appDigest", configured.AppDigest, subject.AppDigest, result) ||
            !MatchesOptional("packageDigest", configured.PackageDigest, subject.PackageDigest, result) ||
            !MatchesOptional("flowDigest", configured.FlowDigest, subject.FlowDigest, result) ||
            !MatchesOptional("platform", configured.Platform, subject.Platform, result) ||
            !MatchesOptional("deviceProfile", configured.DeviceProfile, subject.DeviceProfile, result) ||
            !MatchesOptional("policyVersion", configured.PolicyVersion, subject.PolicyVersion, result))
        {
            return result;
        }

        result.State = MauiArtifactTrustStates.Attested;
        result.Reasons.Add(Reason(
            "attested",
            "Caller-supplied verified provenance facts match the configured policy.",
            blocking: false));
        return result;
    }

    public static MauiLocalReproductionEvaluation EvaluateLocalReproduction(
        MauiArtifactTrustRecord? artifact,
        MauiLocalReproductionFacts? localRun,
        MauiLocalReproductionExpectation? current,
        DateTimeOffset? evaluatedAt = null)
    {
        var priorState = MauiArtifactTrustStates.Untrusted;
        if (MauiArtifactTrustStates.IsKnown(artifact?.Verification?.State))
            priorState = artifact!.Verification.State;

        var result = CreateResult(priorState, artifact?.Integrity, artifact?.Verification?.PolicyVersion);
        result.Reasons.Add(Reason(
            "local-reproduction-not-established",
            "A new local run must match the current flow, application, target, and failure facts.",
            blocking: true));

        var binding = new MauiLocalReproductionBinding
        {
            ImportedArtifact = artifact?.Identity,
            ImportedFailureKey = artifact?.Projection?.Failure?.FailureKey,
            LocalRunId = localRun?.LocalRunId,
            BoundAt = evaluatedAt ?? DateTimeOffset.UtcNow,
            Matched = false,
        };

        if (artifact?.Identity?.IsValid != true || artifact.Projection is null)
        {
            result.Reasons.Add(Reason(
                "imported-artifact-invalid",
                "A valid imported diagnostic projection is required.",
                blocking: true));
            binding.Verification = result;
            return new MauiLocalReproductionEvaluation { Verification = result, Binding = binding };
        }
        if (!HasStrictImportedIdentifiers(artifact.Projection))
        {
            result.Reasons.Add(Reason(
                "imported-projection-identifiers-invalid",
                "The imported diagnostic projection contains an identifier or fingerprint with an invalid format.",
                blocking: true));
            binding.Verification = result;
            return new MauiLocalReproductionEvaluation { Verification = result, Binding = binding };
        }

        if (localRun?.IsNewLocalRun != true)
        {
            AddOmission(result, "isNewLocalRun", "A newly executed local run is required.");
            binding.Verification = result;
            return new MauiLocalReproductionEvaluation { Verification = result, Binding = binding };
        }

        if (string.IsNullOrWhiteSpace(localRun.LocalRunId) ||
            MauiImportedArtifactIdentity.IsImportedArtifactId(localRun.LocalRunId))
        {
            result.Reasons.Add(Reason(
                "local-run-identity-invalid",
                "The reproduction must reference a distinct local run identity.",
                blocking: true));
            binding.Verification = result;
            return new MauiLocalReproductionEvaluation { Verification = result, Binding = binding };
        }

        if (localRun.StartedAt is null || artifact.ImportedAt is null || localRun.StartedAt <= artifact.ImportedAt)
        {
            result.Reasons.Add(Reason(
                "local-run-not-newer",
                "The local run must start after the imported artifact was recorded.",
                blocking: true));
            binding.Verification = result;
            return new MauiLocalReproductionEvaluation { Verification = result, Binding = binding };
        }

        if (!HasCurrentFacts(current, result))
        {
            binding.Verification = result;
            return new MauiLocalReproductionEvaluation { Verification = result, Binding = binding };
        }

        var projection = artifact.Projection;
        var isMauiTrace = string.Equals(
            projection.Kind,
            "mauitrace",
            StringComparison.Ordinal);
        var importedAppBuild = isMauiTrace ? null : projection.AppBuildFingerprint;
        var importedAppSource = isMauiTrace ? null : projection.AppSourceFingerprint;
        var importedPackage = isMauiTrace ? null : projection.PackageFingerprint;
        var matched = true;
        matched &= MatchesFingerprint("flowDigest", projection.FlowFingerprint, current!.FlowDigest, localRun.FlowDigest, result);
        matched &= MatchesFingerprint("appBuildFingerprint", importedAppBuild, current.AppBuildFingerprint, localRun.AppBuildFingerprint, result);
        matched &= MatchesFingerprint("appSourceFingerprint", importedAppSource, current.AppSourceFingerprint, localRun.AppSourceFingerprint, result);
        matched &= MatchesFingerprint("packageDigest", importedPackage, current.PackageDigest, localRun.PackageDigest, result);
        matched &= MatchesFingerprint("platform", projection.PlatformFingerprint, current.Platform, localRun.Platform, result);
        matched &= MatchesPrecomputedFingerprint(
            "runtimeProfile",
            projection.RuntimeProfileFingerprint,
            current.RuntimeProfileFingerprint,
            localRun.RuntimeProfileFingerprint,
            result);
        matched &= MatchesFailure(projection.Failure, localRun.Failure, result);
        if (!matched)
        {
            binding.Verification = result;
            return new MauiLocalReproductionEvaluation { Verification = result, Binding = binding };
        }

        result.State = MauiArtifactTrustStates.LocallyReproduced;
        result.Reasons.Clear();
        result.Reasons.Add(Reason(
            "locally-reproduced",
            "A newly executed local run matched the current flow, app fingerprints, target, and failure checkpoints.",
            blocking: false));
        binding.Matched = true;
        binding.Verification = result;
        return new MauiLocalReproductionEvaluation { Verification = result, Binding = binding };
    }

    /// <summary>Forwards to the mandatory proposal gate used by future repair/source services.</summary>
    public static MauiArtifactProposalDecision CanCreateProposal(MauiArtifactTrustRecord? artifact)
        => MauiArtifactProposalPolicy.CanCreateProposal(artifact);

    private static MauiArtifactTrustVerificationResult CreateResult(
        string state,
        MauiArtifactIntegrityVerification? integrity,
        string? policyVersion)
        => new()
        {
            State = state,
            Integrity = integrity,
            PolicyVersion = policyVersion,
        };

    private static bool HasStrictImportedIdentifiers(MauiImportedArtifactSafeProjection projection)
    {
        if (projection.Kind is not ("flow-run" or "mauitrace"))
            return false;
        if (projection.SourceSchema is not null &&
            MauiArtifactTrustRedactor.SafeFailureCode(projection.SourceSchema) != projection.SourceSchema)
        {
            return false;
        }
        if (projection.Outcome is not null &&
            MauiArtifactTrustRedactor.SafeFailureCode(projection.Outcome) != projection.Outcome)
        {
            return false;
        }
        var fingerprints = new[]
        {
            projection.FlowFingerprint,
            projection.AppBuildFingerprint,
            projection.AppSourceFingerprint,
            projection.PackageFingerprint,
            projection.NormalizedPayloadFingerprint,
            projection.PlatformFingerprint,
            projection.DeviceProfileFingerprint,
            projection.RuntimeProfileFingerprint,
            projection.Failure?.StepFingerprint,
            projection.Failure?.ExpectedCheckpointFingerprint,
            projection.Failure?.ObservedCheckpointFingerprint,
        };
        if (fingerprints.Any(value =>
                value is not null &&
                !string.Equals(
                    MauiArtifactTrustRedactor.NormalizeFingerprint(value),
                    value,
                    StringComparison.Ordinal)))
        {
            return false;
        }
        if ((projection.EmbeddedIdentifierDigests ?? []).Any(value =>
                !string.Equals(
                    MauiArtifactTrustRedactor.NormalizeFingerprint(value),
                    value,
                    StringComparison.Ordinal)))
        {
            return false;
        }
        var failure = projection.Failure;
        if (failure is null)
            return true;
        if (failure.FailureKey is not null &&
            !IsStrictFailureKey(failure.FailureKey))
        {
            return false;
        }
        return (failure.Code is null ||
                MauiArtifactTrustRedactor.SafeFailureCode(failure.Code) == failure.Code) &&
            (failure.Class is null ||
             MauiArtifactTrustRedactor.SafeFailureCode(failure.Class) == failure.Class);
    }

    private static bool IsStrictFailureKey(string value)
        => value.Length == 67 &&
           value.StartsWith("if_", StringComparison.Ordinal) &&
           value.AsSpan(3).ToArray().All(static character =>
               character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool HasRequiredExpectedPolicy(
        MauiArtifactProvenanceSubject? expected,
        MauiArtifactTrustVerificationResult result)
    {
        if (expected is null)
        {
            AddOmission(result, "expectedProvenance", "Attestation requires a configured expected provenance subject.");
            return false;
        }

        var valid = true;
        valid &= RequiredExpected("repository", expected.Repository, result);
        valid &= RequiredExpected("workflow", expected.Workflow, result);
        valid &= RequiredExpected("commit", expected.Commit, result);
        valid &= RequiredExpected("artifactDigest", expected.ArtifactDigest, result);
        return valid;
    }

    private static bool RequiredExpected(string field, string? value, MauiArtifactTrustVerificationResult result)
    {
        if (!string.IsNullOrWhiteSpace(value))
            return true;

        AddOmission(result, "expectedProvenance." + field, "The configured attestation policy must require this fact.");
        return false;
    }

    private static bool HasCurrentFacts(
        MauiLocalReproductionExpectation? current,
        MauiArtifactTrustVerificationResult result)
    {
        if (current is null)
        {
            AddOmission(result, "current", "Trusted current-workspace reproduction facts are required.");
            return false;
        }

        var valid = true;
        valid &= RequiredCurrent("flowDigest", current.FlowDigest, result);
        valid &= RequiredCurrent("appBuildFingerprint", current.AppBuildFingerprint, result);
        valid &= RequiredCurrent("appSourceFingerprint", current.AppSourceFingerprint, result);
        valid &= RequiredCurrent("packageDigest", current.PackageDigest, result);
        valid &= RequiredCurrent("platform", current.Platform, result);
        valid &= RequiredCurrent("runtimeProfileFingerprint", current.RuntimeProfileFingerprint, result);
        return valid;
    }

    private static bool RequiredCurrent(string field, string? value, MauiArtifactTrustVerificationResult result)
    {
        if (!string.IsNullOrWhiteSpace(value))
            return true;

        AddOmission(result, field, "The current local value is required for reproduction matching.");
        return false;
    }

    private static bool Matches(
        string field,
        string? expected,
        string? actual,
        MauiArtifactTrustVerificationResult result)
    {
        if (ValuesEqual(field, expected, actual))
            return true;

        result.Reasons.Add(Reason(
            field + "-mismatch",
            $"Verified provenance did not match the configured {field} policy.",
            blocking: true));
        return false;
    }

    private static bool MatchesOptional(
        string field,
        string? expected,
        string? actual,
        MauiArtifactTrustVerificationResult result)
        => string.IsNullOrWhiteSpace(expected) || Matches(field, expected, actual, result);

    private static bool MatchesFingerprint(
        string field,
        string? imported,
        string? current,
        string? local,
        MauiArtifactTrustVerificationResult result)
    {
        if (string.IsNullOrWhiteSpace(imported))
        {
            AddOmission(
                result,
                "imported." + field,
                $"The imported diagnostic projection omitted {field}.");
            return false;
        }
        if (string.IsNullOrWhiteSpace(current))
        {
            AddOmission(
                result,
                "current." + field,
                $"The trusted current workspace omitted {field}.");
            return false;
        }
        if (string.IsNullOrWhiteSpace(local))
        {
            AddOmission(
                result,
                "local." + field,
                $"The newly executed local run omitted {field}.");
            return false;
        }

        var importedFingerprint = MauiArtifactTrustRedactor.NormalizeFingerprint(imported);
        var currentFingerprint = MauiArtifactTrustRedactor.Fingerprint(current);
        var localFingerprint = MauiArtifactTrustRedactor.Fingerprint(local);
        if (importedFingerprint is null)
        {
            AddOmission(
                result,
                "imported." + field,
                $"The imported diagnostic projection contained an invalid {field} fingerprint.");
            return false;
        }
        if (string.Equals(importedFingerprint, currentFingerprint, StringComparison.Ordinal) &&
            string.Equals(importedFingerprint, localFingerprint, StringComparison.Ordinal))
        {
            return true;
        }

        result.Reasons.Add(Reason(
            field + "-mismatch",
            $"The imported, current, and local {field} facts did not all match.",
            blocking: true));
        return false;
    }

    private static bool MatchesPrecomputedFingerprint(
        string field,
        string? imported,
        string? current,
        string? local,
        MauiArtifactTrustVerificationResult result)
    {
        var importedFingerprint = MauiArtifactTrustRedactor.NormalizeFingerprint(imported);
        var currentFingerprint = MauiArtifactTrustRedactor.NormalizeFingerprint(current);
        var localFingerprint = MauiArtifactTrustRedactor.NormalizeFingerprint(local);
        if (importedFingerprint is null)
        {
            AddOmission(
                result,
                "imported." + field,
                $"The imported diagnostic projection omitted a valid {field} fingerprint.");
            return false;
        }
        if (currentFingerprint is null)
        {
            AddOmission(
                result,
                "current." + field,
                $"The trusted current workspace omitted a valid {field} fingerprint.");
            return false;
        }
        if (localFingerprint is null)
        {
            AddOmission(
                result,
                "local." + field,
                $"The newly executed local run omitted a valid {field} fingerprint.");
            return false;
        }
        if (string.Equals(importedFingerprint, currentFingerprint, StringComparison.Ordinal) &&
            string.Equals(importedFingerprint, localFingerprint, StringComparison.Ordinal))
        {
            return true;
        }

        result.Reasons.Add(Reason(
            field + "-mismatch",
            $"The imported, current, and local {field} facts did not all match.",
            blocking: true));
        return false;
    }

    private static bool MatchesFailure(
        MauiImportedFailureProjection? imported,
        MauiLocalFailureFacts? local,
        MauiArtifactTrustVerificationResult result)
    {
        if (imported is null)
        {
            AddOmission(result, "failure", "Imported evidence has no bounded failure projection to reproduce.");
            return false;
        }

        if (local is null)
        {
            AddOmission(result, "localFailure", "The local run has no observed failure facts.");
            return false;
        }

        var expectedCheckpoint = MauiArtifactTrustRedactor.CheckpointFingerprint(local.ExpectedCheckpoint);
        var observedCheckpoint = MauiArtifactTrustRedactor.CheckpointFingerprint(local.ObservedCheckpoint);
        var matched = true;
        matched &= MatchesFailureValue("failureCode", imported.Code, MauiArtifactTrustRedactor.SafeFailureCode(local.Code), result);
        matched &= MatchesFailureValue("failureClass", imported.Class, MauiArtifactTrustRedactor.SafeFailureCode(local.Class), result);
        matched &= MatchesFailureValue("failureStep", imported.StepFingerprint, MauiArtifactTrustRedactor.Fingerprint(local.StepId), result);
        matched &= MatchesFailureValue("expectedCheckpoint", imported.ExpectedCheckpointFingerprint, expectedCheckpoint, result);
        matched &= MatchesFailureValue("observedCheckpoint", imported.ObservedCheckpointFingerprint, observedCheckpoint, result);
        return matched;
    }

    private static bool MatchesFailureValue(
        string field,
        string? imported,
        string? local,
        MauiArtifactTrustVerificationResult result)
    {
        if (string.IsNullOrWhiteSpace(imported))
        {
            AddOmission(
                result,
                "imported." + field,
                $"The imported diagnostic projection omitted {field}.");
            return false;
        }
        if (string.IsNullOrWhiteSpace(local))
        {
            AddOmission(
                result,
                "local." + field,
                $"The newly executed local run omitted {field}.");
            return false;
        }
        var codeField = field is "failureCode" or "failureClass";
        var normalizedImported = codeField
            ? MauiArtifactTrustRedactor.SafeFailureCode(imported)
            : MauiArtifactTrustRedactor.NormalizeFingerprint(imported);
        var normalizedLocal = codeField
            ? MauiArtifactTrustRedactor.SafeFailureCode(local)
            : MauiArtifactTrustRedactor.NormalizeFingerprint(local);
        if (normalizedImported is null ||
            normalizedLocal is null)
        {
            AddOmission(
                result,
                "imported." + field,
                $"The imported diagnostic projection contained an invalid {field}.");
            return false;
        }
        if (string.Equals(normalizedImported, normalizedLocal, StringComparison.Ordinal))
            return true;

        result.Reasons.Add(Reason(
            field + "-mismatch",
            "The local failure does not match the imported failure facts.",
            blocking: true));
        return false;
    }

    private static bool ValuesEqual(string field, string? left, string? right)
        => !string.IsNullOrWhiteSpace(left) &&
           !string.IsNullOrWhiteSpace(right) &&
           string.Equals(
               left.Trim(),
               right.Trim(),
               field is "commit" or "artifactDigest" or "appDigest" or "packageDigest" or "flowDigest"
                   ? StringComparison.OrdinalIgnoreCase
                   : StringComparison.Ordinal);

    private static void AddOmission(MauiArtifactTrustVerificationResult result, string field, string reason)
    {
        result.Omissions.Add(new MauiArtifactTrustOmission { Field = field, Reason = reason });
        result.Reasons.Add(Reason("missing-" + field, reason, blocking: true));
    }

    private static MauiArtifactTrustReason Reason(string code, string message, bool blocking)
        => new() { Code = code, Message = message, Blocking = blocking };
}

/// <summary>Shared one-way redaction helpers for imported artifact projections and matching.</summary>
public static class MauiArtifactTrustRedactor
{
    private const int MaxFingerprintInputLength = 4_096;

    /// <summary>Returns a one-way SHA-256 fingerprint without retaining the supplied value.</summary>
    public static string? Fingerprint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxFingerprintInputLength || HasControlCharacter(value))
            return null;

        return "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    /// <summary>Normalizes only strict SHA-256 fingerprints and rejects malformed digest claims.</summary>
    public static string? NormalizeFingerprint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        if (trimmed.Length != 71 ||
            !trimmed.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        var hex = trimmed[7..];
        if (!hex.All(Uri.IsHexDigit))
            return null;
        return "sha256:" + hex.ToLowerInvariant();
    }

    /// <summary>Returns a fingerprint only when a checkpoint contains meaningful comparison facts.</summary>
    public static string? CheckpointFingerprint(MauiFlowCheckpoint? checkpoint)
    {
        if (checkpoint is null)
            return null;

        var values = new[]
        {
            checkpoint.AppBuildFingerprint,
            checkpoint.SeedFingerprint,
            checkpoint.BackendStateFingerprint,
            checkpoint.Route,
            checkpoint.Window,
            checkpoint.Modal,
            checkpoint.Locale,
            checkpoint.Theme,
            checkpoint.Orientation,
            checkpoint.DisplayProfile,
            checkpoint.CollectionItemKey,
        };

        if (values.All(string.IsNullOrWhiteSpace) ||
            values.Any(value => value is { Length: > MaxFingerprintInputLength } ||
                                (value is not null && HasControlCharacter(value))))
            return null;

        var material = string.Join(
            "\u001f",
            values.Select(static value => value is null ? "\u0000" : value.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + value));
        return "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
    }

    /// <summary>Allows only short, identifier-shaped failure codes in a safe diagnostic projection.</summary>
    public static string? SafeFailureCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
            return null;

        var trimmed = value.Trim().ToLowerInvariant();
        foreach (var character in trimmed)
        {
            if (!(character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_' or '.'))
                return null;
        }

        return trimmed;
    }

    private static bool HasControlCharacter(string value)
        => value.Any(char.IsControl);
}
