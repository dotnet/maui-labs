using System.Text;
using System.Text.Json;

namespace Microsoft.Maui.DevFlow.Testing;

/// <summary>Safe result of parsing a flow-pilot artifact manifest for qualification accounting.</summary>
public sealed class MauiPreviewQualificationArtifactManifestParseResult
{
    public MauiPreviewQualificationInput Input { get; init; } = new();
    public List<string> Errors { get; } = [];
    public bool Ok => Errors.Count == 0;
}

/// <summary>
/// Reads the bounded, redacted flow-pilot manifest shape into qualification evidence. It never
/// reads referenced reports, extracts ZIPs, trusts embedded provenance, or treats an emulator as a
/// real device.
/// </summary>
public static class MauiPreviewQualificationArtifactManifestReader
{
    /// <summary>Parses a manifest JSON document. Caller-provided strings are converted to fingerprints downstream.</summary>
    public static MauiPreviewQualificationArtifactManifestParseResult ParseJson(string? json)
    {
        if (MauiAppleFlowQaManifestReader.IsAppleQaManifest(json))
            return MauiAppleFlowQaManifestReader.ParseJson(json);

        var result = new MauiPreviewQualificationArtifactManifestParseResult();
        if (string.IsNullOrWhiteSpace(json) || Encoding.UTF8.GetByteCount(json) > 1_048_576)
        {
            result.Errors.Add("artifact-manifest-missing-or-too-large");
            return result;
        }

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !HasInt(root, "schema", 1) ||
                !HasString(root, "kind", "devflow-flow-pilot"))
            {
                result.Errors.Add("artifact-manifest-schema-invalid");
                return result;
            }

            var input = result.Input;
            input.Evidence = new MauiQualificationRequiredEvidence();
            input.Fingerprints = new MauiQualificationFingerprints
            {
                RepositoryCommit = ReadPath(root, "repository", "commit"),
                TestingPackageVersion = ReadPath(root, "testing", "packageVersion"),
                PackageId = ReadPath(root, "app", "packageId"),
                PackageFingerprint = ReadPath(root, "app", "packageDigest"),
            };

            var platform = ReadPath(root, "platform", "name");
            input.Platform = platform;
            var deviceEvidence = ReadObjectPath(root, "platform", "deviceEvidence");
            var deviceKind = ReadString(deviceEvidence, "kind");
            var realDevice = ReadBoolean(deviceEvidence, "realDevice");
            var avdName = ReadPath(root, "platform", "androidSdk", "avdName");
            if (string.IsNullOrWhiteSpace(deviceKind))
                deviceKind = string.IsNullOrWhiteSpace(avdName) ? "unknown" : "emulator";
            if (realDevice is null)
                realDevice = false;

            input.Profiles.Add(new MauiQualificationPlatformProfile
            {
                Platform = platform,
                DeviceEvidenceKind = deviceKind,
                RealDevice = realDevice,
                DeviceFingerprint = ReadPath(root, "platform", "deviceId"),
                RuntimeFingerprint = ReadPath(root, "platform", "androidSdk", "apiLevel"),
                BuildFingerprint = ReadPath(root, "app", "buildFingerprint"),
                PackageFingerprint = ReadPath(root, "app", "packageDigest"),
                FirstAttemptMode = "manifest-first-attempt",
            });

            var manifestValid = !HasNonEmptyArray(root, "validationErrors");
            input.Evidence.ArtifactManifestValid = manifestValid;
            input.Evidence.ArtifactReferencesComplete = ReadArtifactReferences(root, input, result.Errors);
            var fixtureInitializationDiagnostic = ReadFixtureInitializationDiagnostic(root, input, result.Errors);
            ReadFlows(
                root,
                input,
                platform,
                deviceKind,
                realDevice,
                fixtureInitializationDiagnostic,
                result.Errors);
            input.Evidence.FirstAttemptEvidencePresent = input.Samples.Any(static sample => sample.FirstAttempt == true);
            input.ArtifactRefs.Add(new MauiQualificationArtifactReference
            {
                Kind = "flow-pilot-manifest",
                Digest = MauiQualificationSanitizer.Fingerprint(json),
                Redacted = true,
            });

            if (!manifestValid)
                result.Errors.Add("artifact-manifest-reported-validation-errors");
            if (input.Samples.Count == 0)
                result.Errors.Add("artifact-manifest-first-attempts-missing");
            if (result.Errors.Count > 0)
                input.Evidence.ArtifactManifestValid = false;
        }
        catch (JsonException)
        {
            result.Errors.Add("artifact-manifest-invalid-json");
        }
        return result;
    }

    private static void ReadFlows(
        JsonElement root,
        MauiPreviewQualificationInput input,
        string? platform,
        string? deviceKind,
        bool? realDevice,
        bool fixtureInitializationDiagnostic,
        List<string> errors)
    {
        if (!root.TryGetProperty("flows", out var flows) || flows.ValueKind != JsonValueKind.Array)
        {
            errors.Add("artifact-manifest-flows-missing");
            return;
        }

        if (flows.GetArrayLength() > 1_024)
        {
            errors.Add("artifact-manifest-flows-too-many");
            return;
        }

        foreach (var flow in flows.EnumerateArray())
        {
            if (flow.ValueKind != JsonValueKind.Object ||
                !TryGetString(flow, "digest", out var digest) ||
                !flow.TryGetProperty("firstAttempt", out var firstAttempt) ||
                firstAttempt.ValueKind != JsonValueKind.Object)
            {
                errors.Add("artifact-manifest-flow-invalid");
                continue;
            }

            var runKind = ReadString(firstAttempt, "runKind");
            var outcome = ReadString(firstAttempt, "outcome");
            var tier = ReadString(flow, "tier");
            var reportPresent = !string.IsNullOrWhiteSpace(ReadString(firstAttempt, "reportDigest")) ||
                !string.IsNullOrWhiteSpace(ReadString(firstAttempt, "reportPath"));
            var reportDigest = ReadString(firstAttempt, "reportDigest");
            var reportPath = ReadString(firstAttempt, "reportPath");
            var normalizedReportDigest = NormalizeSha256(reportDigest);
            if (!string.IsNullOrWhiteSpace(normalizedReportDigest) &&
                !string.IsNullOrWhiteSpace(reportPath))
            {
                input.ArtifactRefs.Add(new MauiQualificationArtifactReference
                {
                    Kind = "report",
                    Digest = normalizedReportDigest,
                    Reference = reportPath,
                    Redacted = true,
                });
            }
            input.Samples.Add(new MauiQualificationExecutionSample
            {
                SampleId = MauiQualificationSanitizer.Fingerprint(ReadString(firstAttempt, "runId") ?? digest),
                Source = MauiQualificationSampleSources.DeviceBacked,
                Category = "flow-pilot-manifest",
                Platform = platform,
                FlowId = digest,
                Tier = string.Equals(tier, "tier-1", StringComparison.Ordinal) ? "tier-1" : null,
                DeviceEvidenceKind = deviceKind,
                RealDevice = realDevice,
                CleanState = string.Equals(runKind, "clean", StringComparison.Ordinal),
                FirstAttempt = true,
                Outcome = NormalizeOutcome(outcome),
                InfrastructureExclusionReason =
                    fixtureInitializationDiagnostic &&
                    string.Equals(
                        NormalizeOutcome(outcome),
                        MauiFlowRunOutcomes.InfrastructureError,
                        StringComparison.Ordinal)
                        ? "recorded-fixture-initialization-diagnostic"
                        : null,
                ReportPresent = reportPresent,
                // A manifest reference does not prove that a referenced report was parsed or schema-valid.
                ReportSchemaValid = null,
                ReportComplete = null,
                RecordingValid = null,
            });
            if (string.Equals(tier, "tier-1", StringComparison.Ordinal))
                input.Tier1Flows.Add(digest);
        }
    }

    private static bool ReadFixtureInitializationDiagnostic(
        JsonElement root,
        MauiPreviewQualificationInput input,
        List<string> errors)
    {
        if (!HasOmissionKind(root, "fixture-initialization-diagnostic"))
            return false;

        input.Exclusions.Add(new MauiQualificationExclusion
        {
            Kind = "android-fixture-initialization",
            Count = 1,
            Reason = "recorded-fixture-initialization-diagnostic",
        });

        if (!root.TryGetProperty("artifacts", out var artifacts) || artifacts.ValueKind != JsonValueKind.Array)
        {
            input.Evidence!.ArtifactReferencesComplete = false;
            errors.Add("artifact-manifest-fixture-initialization-diagnostic-missing");
            return true;
        }

        foreach (var artifact in artifacts.EnumerateArray())
        {
            if (artifact.ValueKind != JsonValueKind.Object ||
                !string.Equals(
                    ReadString(artifact, "kind"),
                    "fixture-initialization-diagnostic",
                    StringComparison.Ordinal))
            {
                continue;
            }

            var digest = ReadString(artifact, "sha256");
            var redacted = ReadBoolean(artifact, "redacted");
            if (string.IsNullOrWhiteSpace(digest) || redacted != true)
            {
                input.Evidence!.ArtifactReferencesComplete = false;
                errors.Add("artifact-manifest-fixture-initialization-diagnostic-invalid");
                return true;
            }

            input.ArtifactRefs.Add(new MauiQualificationArtifactReference
            {
                Kind = "fixture-initialization-diagnostic",
                Digest = digest,
                Reference = ReadString(artifact, "path"),
                Redacted = true,
            });
            return true;
        }

        input.Evidence!.ArtifactReferencesComplete = false;
        errors.Add("artifact-manifest-fixture-initialization-diagnostic-missing");
        return true;
    }

    private static bool? ReadArtifactReferences(
        JsonElement root,
        MauiPreviewQualificationInput input,
        List<string> errors)
    {
        if (!root.TryGetProperty("artifacts", out var artifacts) || artifacts.ValueKind != JsonValueKind.Array)
            return null;
        if (artifacts.GetArrayLength() == 0)
            return false;
        var complete = true;
        foreach (var artifact in artifacts.EnumerateArray())
        {
            if (artifact.ValueKind != JsonValueKind.Object ||
                !TryGetString(artifact, "kind", out var kind) ||
                !TryGetString(artifact, "sha256", out var digest) ||
                !artifact.TryGetProperty("sizeBytes", out var size) ||
                size.ValueKind != JsonValueKind.Number)
            {
                complete = false;
                continue;
            }

            if (kind is not ("flow-run-report" or "mauitrace"))
                continue;

            var path = ReadString(artifact, "path");
            var redacted = ReadBoolean(artifact, "redacted");
            if (string.IsNullOrWhiteSpace(path) ||
                string.IsNullOrWhiteSpace(digest) ||
                redacted != true)
            {
                complete = false;
                errors.Add("artifact-manifest-evidence-reference-invalid");
                continue;
            }

            input.ArtifactRefs.Add(new MauiQualificationArtifactReference
            {
                Kind = kind,
                Digest = digest,
                Reference = path,
                Redacted = true,
            });
        }
        return complete;
    }

    private static string? NormalizeSha256(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        if (normalized?.StartsWith("sha256:", StringComparison.Ordinal) == true)
            normalized = normalized[7..];
        return normalized is { Length: 64 } && normalized.All(Uri.IsHexDigit)
            ? $"sha256:{normalized}"
            : null;
    }

    private static string? NormalizeOutcome(string? value) => value switch
    {
        MauiFlowRunOutcomes.Passed => MauiFlowRunOutcomes.Passed,
        MauiFlowRunOutcomes.Failed => MauiFlowRunOutcomes.Failed,
        MauiFlowRunOutcomes.InfrastructureError => MauiFlowRunOutcomes.InfrastructureError,
        MauiFlowRunOutcomes.UnknownCompletion => MauiFlowRunOutcomes.UnknownCompletion,
        MauiFlowRunOutcomes.Cancelled => MauiFlowRunOutcomes.Cancelled,
        MauiFlowRunOutcomes.TimedOut => MauiFlowRunOutcomes.TimedOut,
        _ => "unknown",
    };

    private static JsonElement? ReadObjectPath(JsonElement root, params string[] path)
    {
        var current = root;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
                return null;
        }
        return current.ValueKind == JsonValueKind.Object ? current : null;
    }

    private static string? ReadPath(JsonElement root, params string[] path)
    {
        var current = root;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
                return null;
        }
        return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
    }

    private static string? ReadString(JsonElement? element, string property) =>
        element.HasValue ? ReadString(element.Value, property) : null;

    private static string? ReadString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool? ReadBoolean(JsonElement? element, string property) =>
        element.HasValue && element.Value.ValueKind == JsonValueKind.Object &&
        element.Value.TryGetProperty(property, out var value)
            ? value.ValueKind == JsonValueKind.True ? true :
                value.ValueKind == JsonValueKind.False ? false : null
            : null;

    private static bool HasNonEmptyArray(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array && value.GetArrayLength() > 0;

    private static bool HasOmissionKind(JsonElement root, string expectedKind)
    {
        if (!root.TryGetProperty("omissions", out var omissions) || omissions.ValueKind != JsonValueKind.Array)
            return false;

        foreach (var omission in omissions.EnumerateArray())
        {
            if (omission.ValueKind == JsonValueKind.Object &&
                string.Equals(ReadString(omission, "kind"), expectedKind, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasInt(JsonElement element, string property, int expected) =>
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var actual) &&
        actual == expected;

    private static bool HasString(JsonElement element, string property, string expected) =>
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String &&
        string.Equals(value.GetString(), expected, StringComparison.Ordinal);

    private static bool TryGetString(JsonElement element, string property, out string value)
    {
        value = string.Empty;
        return element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(property, out var item) &&
            item.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(value = item.GetString() ?? string.Empty);
    }
}
