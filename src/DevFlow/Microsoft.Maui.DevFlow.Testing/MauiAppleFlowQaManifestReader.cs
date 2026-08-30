using System.Text;
using System.Text.Json;

namespace Microsoft.Maui.DevFlow.Testing;

/// <summary>
/// Adapts the versioned Apple <c>devflow-flow-qa</c> manifest into read-only qualification
/// evidence. It reads metadata only; referenced reports, traces, archives, and host paths are
/// never opened by this adapter.
/// </summary>
public static class MauiAppleFlowQaManifestReader
{
    /// <summary>The supported outer manifest schema.</summary>
    public const int Schema = 1;

    /// <summary>The outer manifest kind emitted by the flow-QA scripts.</summary>
    public const string ManifestKind = "devflow-flow-qa";

    /// <summary>The adapter contract recorded in qualification evidence.</summary>
    public const string AdapterContractVersion = "apple-flow-qa-adapter-v1";

    private const int MaxManifestBytes = 1_048_576;
    private const int MaxFlows = 128;
    private const int MaxAttemptsPerFlow = 32;
    private const int MaxArtifacts = 512;
    private const int MaxOmissions = 256;

    /// <summary>
    /// Parses an iOS, Mac Catalyst, or experimental AppKit flow-QA manifest. Invalid or incomplete
    /// evidence is represented as fixed error codes and never promoted to a stronger fact.
    /// </summary>
    public static MauiPreviewQualificationArtifactManifestParseResult ParseJson(string? json)
    {
        var result = new MauiPreviewQualificationArtifactManifestParseResult();
        if (string.IsNullOrWhiteSpace(json) || Encoding.UTF8.GetByteCount(json) > MaxManifestBytes)
        {
            AddError(result, "apple-qa-manifest-missing-or-too-large");
            return result;
        }

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                AddError(result, "apple-qa-manifest-not-object");
                return result;
            }

            if (!HasInt(root, "schema", Schema))
                AddError(result, "apple-qa-manifest-schema-invalid");
            if (!HasString(root, "kind", ManifestKind))
                AddError(result, "apple-qa-manifest-kind-invalid");

            var platformInfo = RequireObject(root, "platform", result, "apple-qa-platform-missing");
            var platform = ReadRequiredString(platformInfo, "name", result, "apple-qa-platform-name-missing");
            if (!IsApplePlatform(platform))
                AddError(result, "apple-qa-platform-unsupported");

            var experimental = ReadRequiredBoolean(root, "experimental", result, "apple-qa-experimental-missing");
            var backend = ReadRequiredNullableString(root, "backend", result, "apple-qa-backend-missing");
            var officialCoverage = ReadRequiredBoolean(root, "officialCoverage", result, "apple-qa-official-coverage-missing");
            var macCatalystEquivalent = ReadRequiredNullableBoolean(
                root,
                "macCatalystEquivalent",
                result,
                "apple-qa-maccatalyst-equivalent-missing");
            ValidatePlatformSeparation(
                platform,
                experimental,
                backend,
                officialCoverage,
                macCatalystEquivalent,
                result,
                "apple-qa");

            var app = RequireObject(root, "app", result, "apple-qa-app-missing");
            var appProject = ReadRequiredString(app, "project", result, "apple-qa-app-project-missing");
            var appSourceDigest = ReadRequiredNullableString(app, "sourceDigest", result, "apple-qa-app-source-digest-missing");
            var packageDigest = ReadRequiredNullableString(app, "packageDigest", result, "apple-qa-package-digest-missing");
            var buildFingerprint = ReadOptionalString(app, "buildFingerprint");
            ValidateDigestIfPresent(appSourceDigest, result, "apple-qa-app-source-digest-invalid");
            ValidateDigestIfPresent(packageDigest, result, "apple-qa-package-digest-invalid");

            var repository = RequireObject(root, "repository", result, "apple-qa-repository-missing");
            var testing = RequireObject(root, "testing", result, "apple-qa-testing-missing");
            var host = RequireObject(platformInfo, "host", result, "apple-qa-host-missing");
            var deviceEvidence = RequireObject(host, "deviceEvidence", result, "apple-qa-device-evidence-missing");
            var deviceEvidenceKind = ReadRequiredString(
                deviceEvidence,
                "kind",
                result,
                "apple-qa-device-evidence-kind-missing");
            var realDevice = ReadRequiredBoolean(
                deviceEvidence,
                "realDevice",
                result,
                "apple-qa-device-evidence-real-device-missing");
            var hostDeviceId = ReadRequiredNullableString(
                deviceEvidence,
                "deviceIdFingerprint",
                result,
                "apple-qa-device-id-fingerprint-missing");
            var hostDeviceProfile = ReadRequiredString(
                deviceEvidence,
                "profile",
                result,
                "apple-qa-device-profile-missing");
            var hostXcode = ReadRequiredString(host, "xcode", result, "apple-qa-xcode-missing");
            var hostRuntime = ReadRequiredString(host, "runtime", result, "apple-qa-runtime-missing");

            var appleQa = RequireObject(root, "appleQa", result, "apple-qa-section-missing");
            var adapted = new MauiQualificationAppleQaEvidence
            {
                ContractVersion = AdapterContractVersion,
                Platform = platform,
                Experimental = experimental,
                Backend = backend,
                OfficialCoverage = officialCoverage,
                MacCatalystEquivalent = macCatalystEquivalent,
                AppProject = appProject,
                AppSourceDigest = appSourceDigest,
                PackageDigest = packageDigest,
                XcodeVersion = hostXcode,
                SimulatorRuntime = hostRuntime,
                DeviceIdFingerprint = hostDeviceId,
                DeviceProfile = hostDeviceProfile,
            };
            result.Input.AppleQa = adapted;
            result.Input.Platform = platform;
            result.Input.Fingerprints = new MauiQualificationFingerprints
            {
                RepositoryCommit = ReadOptionalString(repository, "commit"),
                TestingPackageVersion = ReadOptionalString(testing, "packageVersion"),
                PackageFingerprint = packageDigest,
                PolicyVersion = AdapterContractVersion,
            };
            result.Input.Profiles.Add(new MauiQualificationPlatformProfile
            {
                Platform = platform,
                Scope = ManifestKind,
                DeviceEvidenceKind = deviceEvidenceKind,
                RealDevice = realDevice,
                DeviceFingerprint = hostDeviceId,
                RuntimeFingerprint = hostRuntime,
                BuildFingerprint = buildFingerprint,
                PackageFingerprint = packageDigest,
            });

            ReadAppleQaSection(
                appleQa,
                platform,
                experimental,
                backend,
                officialCoverage,
                macCatalystEquivalent,
                hostXcode,
                hostRuntime,
                hostDeviceId,
                adapted,
                result);
            var flowDigests = ReadFlowDigests(root, result);
            foreach (var flow in flowDigests)
            {
                adapted.FlowDigests.Add(flow.Digest);
                result.Input.ArtifactRefs.Add(new MauiQualificationArtifactReference
                {
                    Kind = "flow-digest",
                    Digest = flow.Digest,
                    Redacted = true,
                });
            }

            ReadAttempts(appleQa, platform, deviceEvidenceKind, realDevice, flowDigests, adapted, result);
            var artifactsComplete = ReadArtifacts(root, adapted, result);
            ReadOmissions(root, adapted, result);

            result.Input.ArtifactRefs.Add(new MauiQualificationArtifactReference
            {
                Kind = "apple-flow-qa-manifest",
                Digest = MauiQualificationSanitizer.Fingerprint(json),
                Redacted = true,
            });
            result.Input.Evidence = new MauiQualificationRequiredEvidence
            {
                ArtifactManifestValid = result.Errors.Count == 0,
                ArtifactReferencesComplete = artifactsComplete,
                FirstAttemptEvidencePresent = adapted.FlowDigests.Count > 0 &&
                    adapted.FirstAttemptCount == adapted.FlowDigests.Count,
                // A manifest reference never proves that the referenced report or trace was parsed.
                ReportSchemaValid = null,
                RecordingValid = null,
            };

            if (!artifactsComplete)
                AddError(result, "apple-qa-artifact-references-invalid");
            if (adapted.FirstAttemptCount == 0)
                AddError(result, "apple-qa-first-attempts-missing");
            else if (adapted.FirstAttemptCount != adapted.FlowDigests.Count)
                AddError(result, "apple-qa-first-attempts-incomplete");
            if (result.Errors.Count > 0)
                result.Input.Evidence.ArtifactManifestValid = false;
        }
        catch (JsonException)
        {
            AddError(result, "apple-qa-manifest-invalid-json");
        }

        return result;
    }

    internal static bool IsAppleQaManifest(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || Encoding.UTF8.GetByteCount(json) > MaxManifestBytes)
            return false;

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                HasString(document.RootElement, "kind", ManifestKind);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void ReadAppleQaSection(
        JsonElement? appleQa,
        string? platform,
        bool? experimental,
        string? backend,
        bool? officialCoverage,
        bool? macCatalystEquivalent,
        string? hostXcode,
        string? hostRuntime,
        string? hostDeviceId,
        MauiQualificationAppleQaEvidence adapted,
        MauiPreviewQualificationArtifactManifestParseResult result)
    {
        if (appleQa is not { ValueKind: JsonValueKind.Object } section)
            return;

        if (!HasInt(section, "schema", Schema))
            AddError(result, "apple-qa-section-schema-invalid");
        if (!HasString(section, "kind", "devflow-apple-flow-qa"))
            AddError(result, "apple-qa-section-kind-invalid");

        var sectionPlatform = ReadRequiredString(section, "platform", result, "apple-qa-section-platform-missing");
        if (!string.Equals(sectionPlatform, platform, StringComparison.Ordinal))
            AddError(result, "apple-qa-section-platform-mismatch");
        var sectionExperimental = ReadRequiredBoolean(section, "experimental", result, "apple-qa-section-experimental-missing");
        var sectionBackend = ReadRequiredNullableString(section, "backend", result, "apple-qa-section-backend-missing");
        var sectionOfficialCoverage = ReadRequiredBoolean(section, "officialCoverage", result, "apple-qa-section-official-coverage-missing");
        var sectionMacCatalystEquivalent = ReadRequiredNullableBoolean(
            section,
            "macCatalystEquivalent",
            result,
            "apple-qa-section-maccatalyst-equivalent-missing");
        if (sectionExperimental != experimental ||
            !string.Equals(sectionBackend, backend, StringComparison.Ordinal) ||
            sectionOfficialCoverage != officialCoverage ||
            sectionMacCatalystEquivalent != macCatalystEquivalent)
        {
            AddError(result, "apple-qa-section-platform-label-mismatch");
        }
        ValidatePlatformSeparation(
            sectionPlatform,
            sectionExperimental,
            sectionBackend,
            sectionOfficialCoverage,
            sectionMacCatalystEquivalent,
            result,
            "apple-qa-section");

        var spike = RequireObject(section, "spike", result, "apple-qa-spike-missing");
        adapted.SpikeStatus = ReadRequiredString(spike, "status", result, "apple-qa-spike-status-missing");
        adapted.ForegroundProof = ReadRequiredBoolean(spike, "foregroundProof", result, "apple-qa-foreground-proof-missing");
        adapted.AuthenticatedTransport = ReadRequiredBoolean(
            spike,
            "authenticatedTransport",
            result,
            "apple-qa-authenticated-transport-missing");
        adapted.Receipt = ReadRequiredBoolean(spike, "receipt", result, "apple-qa-receipt-missing");
        adapted.Cancellation = ReadRequiredBoolean(spike, "cancellation", result, "apple-qa-cancellation-missing");
        adapted.Parity = ReadRequiredBoolean(spike, "parity", result, "apple-qa-parity-missing");

        var checkpoint = RequireObject(section, "checkpoint", result, "apple-qa-checkpoint-missing");
        adapted.ResetFingerprint = ReadRequiredString(checkpoint, "resetFingerprint", result, "apple-qa-reset-fingerprint-missing");
        adapted.SeedFingerprint = ReadRequiredString(checkpoint, "seedFingerprint", result, "apple-qa-seed-fingerprint-missing");
        adapted.BackendStateFingerprint = ReadRequiredString(
            checkpoint,
            "backendStateFingerprint",
            result,
            "apple-qa-backend-state-fingerprint-missing");

        var apple = RequireObject(section, "apple", result, "apple-qa-apple-metadata-missing");
        var appleXcode = ReadRequiredString(apple, "xcodeVersion", result, "apple-qa-apple-xcode-missing");
        var appleRuntime = ReadRequiredString(apple, "simulatorRuntime", result, "apple-qa-simulator-runtime-missing");
        var appleDevice = ReadRequiredNullableString(
            apple,
            "simulatorDeviceFingerprint",
            result,
            "apple-qa-simulator-device-fingerprint-missing");
        var appleProfile = ReadRequiredString(
            apple,
            "simulatorDeviceProfile",
            result,
            "apple-qa-simulator-device-profile-missing");

        if (!string.Equals(appleXcode, hostXcode, StringComparison.Ordinal))
            AddError(result, "apple-qa-xcode-mismatch");
        if (!string.Equals(appleRuntime, hostRuntime, StringComparison.Ordinal))
            AddError(result, "apple-qa-runtime-mismatch");
        if (appleDevice is not null && hostDeviceId is not null &&
            !string.Equals(appleDevice, hostDeviceId, StringComparison.Ordinal))
        {
            AddError(result, "apple-qa-device-fingerprint-mismatch");
        }
        adapted.XcodeVersion = appleXcode;
        adapted.SimulatorRuntime = appleRuntime;
        adapted.DeviceIdFingerprint = appleDevice ?? hostDeviceId;
        adapted.DeviceProfile = appleProfile;
        ValidateDigestIfPresent(adapted.DeviceIdFingerprint, result, "apple-qa-device-fingerprint-invalid");
    }

    private static List<FlowDigest> ReadFlowDigests(
        JsonElement root,
        MauiPreviewQualificationArtifactManifestParseResult result)
    {
        var values = new List<FlowDigest>();
        if (!root.TryGetProperty("flows", out var flows) || flows.ValueKind != JsonValueKind.Array)
        {
            AddError(result, "apple-qa-flow-digests-missing");
            return values;
        }
        if (flows.GetArrayLength() is 0 or > MaxFlows)
        {
            AddError(result, "apple-qa-flow-digest-count-invalid");
            return values;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var flow in flows.EnumerateArray())
        {
            var path = ReadRequiredString(flow, "path", result, "apple-qa-flow-path-missing");
            var digest = ReadRequiredString(flow, "sha256", result, "apple-qa-flow-digest-missing");
            if (path is null || digest is null)
                continue;
            if (!IsDigest(digest))
            {
                AddError(result, "apple-qa-flow-digest-invalid");
                continue;
            }

            var name = Path.GetFileNameWithoutExtension(path.Replace('/', Path.DirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(name) || !names.Add(name))
            {
                AddError(result, "apple-qa-flow-name-duplicate-or-invalid");
                continue;
            }
            values.Add(new FlowDigest(name, digest));
        }
        return values;
    }

    private static void ReadAttempts(
        JsonElement? appleQa,
        string? platform,
        string? deviceEvidenceKind,
        bool? realDevice,
        IReadOnlyList<FlowDigest> flowDigests,
        MauiQualificationAppleQaEvidence adapted,
        MauiPreviewQualificationArtifactManifestParseResult result)
    {
        if (appleQa is not { ValueKind: JsonValueKind.Object } section ||
            !section.TryGetProperty("flows", out var flows) ||
            flows.ValueKind != JsonValueKind.Array)
        {
            AddError(result, "apple-qa-attempt-flows-missing");
            return;
        }
        if (flows.GetArrayLength() is 0 or > MaxFlows)
        {
            AddError(result, "apple-qa-attempt-flow-count-invalid");
            return;
        }

        var digestByName = flowDigests.ToDictionary(static flow => flow.Name, static flow => flow.Digest, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var flow in flows.EnumerateArray())
        {
            var name = ReadRequiredString(flow, "name", result, "apple-qa-attempt-flow-name-missing");
            if (name is null || !seen.Add(name))
            {
                AddError(result, "apple-qa-attempt-flow-name-duplicate");
                continue;
            }
            if (!digestByName.TryGetValue(name, out var digest))
            {
                AddError(result, "apple-qa-attempt-flow-digest-mismatch");
                continue;
            }

            if (!flow.TryGetProperty("cleanAttempts", out var cleanAttempts) ||
                cleanAttempts.ValueKind != JsonValueKind.Array ||
                cleanAttempts.GetArrayLength() is 0 or > MaxAttemptsPerFlow)
            {
                AddError(result, "apple-qa-clean-attempts-invalid");
                continue;
            }

            var parsedAttempts = new List<AppleAttempt>();
            foreach (var attempt in cleanAttempts.EnumerateArray())
            {
                var parsed = ReadAttempt(attempt, result);
                if (parsed is not null)
                    parsedAttempts.Add(parsed);
            }
            if (parsedAttempts.Count != cleanAttempts.GetArrayLength())
                continue;

            var hasValidFirstAttempt = false;
            if (!flow.TryGetProperty("firstAttempt", out var firstAttempt) ||
                firstAttempt.ValueKind != JsonValueKind.Object)
            {
                AddError(result, "apple-qa-first-attempt-missing");
            }
            else
            {
                var first = ReadAttempt(firstAttempt, result);
                if (first is not null)
                {
                    if (!Equivalent(first, parsedAttempts[0]))
                        AddError(result, "apple-qa-first-attempt-mismatch");
                    else
                    {
                        adapted.FirstAttemptCount++;
                        hasValidFirstAttempt = true;
                    }
                }
            }

            for (var index = 0; index < parsedAttempts.Count; index++)
            {
                var attempt = parsedAttempts[index];
                result.Input.Samples.Add(new MauiQualificationExecutionSample
                {
                    SampleId = MauiQualificationSanitizer.Fingerprint($"{digest}:{index + 1}"),
                    Source = MauiQualificationSampleSources.DeviceBacked,
                    Category = "apple-flow-qa",
                    Platform = platform,
                    FlowId = digest,
                    DeviceEvidenceKind = deviceEvidenceKind,
                    RealDevice = realDevice,
                    CleanState = true,
                    FirstAttempt = index == 0 && hasValidFirstAttempt,
                    Outcome = attempt.Status,
                    ReportPresent = !string.IsNullOrWhiteSpace(attempt.Report) ||
                        !string.IsNullOrWhiteSpace(attempt.ReportDigest),
                    // The manifest does not assert report schema or recording validity.
                    ReportSchemaValid = null,
                    ReportComplete = null,
                    RecordingValid = null,
                });
                adapted.CleanAttemptCount++;
            }
        }
    }

    private static AppleAttempt? ReadAttempt(
        JsonElement attempt,
        MauiPreviewQualificationArtifactManifestParseResult result)
    {
        if (attempt.ValueKind != JsonValueKind.Object)
        {
            AddError(result, "apple-qa-attempt-invalid");
            return null;
        }
        var status = ReadRequiredString(attempt, "status", result, "apple-qa-attempt-status-missing");
        if (!IsOutcome(status))
        {
            AddError(result, "apple-qa-attempt-status-invalid");
            return null;
        }
        var report = ReadOptionalString(attempt, "report");
        var reportDigest = ReadOptionalString(attempt, "reportDigest");
        ValidateDigestIfPresent(reportDigest, result, "apple-qa-attempt-report-digest-invalid");
        return new AppleAttempt(status!, report, reportDigest);
    }

    private static bool ReadArtifacts(
        JsonElement root,
        MauiQualificationAppleQaEvidence adapted,
        MauiPreviewQualificationArtifactManifestParseResult result)
    {
        if (!root.TryGetProperty("artifacts", out var artifacts) || artifacts.ValueKind != JsonValueKind.Array)
        {
            AddError(result, "apple-qa-artifacts-missing");
            return false;
        }
        if (artifacts.GetArrayLength() is 0 or > MaxArtifacts)
            return false;

        var paths = new HashSet<string>(StringComparer.Ordinal);
        var complete = true;
        foreach (var artifact in artifacts.EnumerateArray())
        {
            var kind = ReadRequiredString(artifact, "kind", result, "apple-qa-artifact-kind-missing");
            var path = ReadRequiredString(artifact, "path", result, "apple-qa-artifact-path-missing");
            var digest = ReadRequiredString(artifact, "sha256", result, "apple-qa-artifact-digest-missing");
            if (artifact.ValueKind != JsonValueKind.Object ||
                !artifact.TryGetProperty("sizeBytes", out var size) ||
                size.ValueKind != JsonValueKind.Number ||
                !size.TryGetInt64(out var sizeBytes) ||
                sizeBytes < 0 ||
                kind is null ||
                path is null ||
                digest is null ||
                !IsDigest(digest) ||
                !paths.Add(path))
            {
                complete = false;
                AddError(result, "apple-qa-artifact-invalid");
                continue;
            }

            result.Input.ArtifactRefs.Add(new MauiQualificationArtifactReference
            {
                Kind = kind,
                Digest = digest,
                Reference = path,
                Redacted = true,
            });
            adapted.ArtifactCount++;
        }
        return complete;
    }

    private static void ReadOmissions(
        JsonElement root,
        MauiQualificationAppleQaEvidence adapted,
        MauiPreviewQualificationArtifactManifestParseResult result)
    {
        if (!root.TryGetProperty("omissions", out var omissions) || omissions.ValueKind != JsonValueKind.Array)
        {
            AddError(result, "apple-qa-omissions-missing");
            return;
        }
        if (omissions.GetArrayLength() > MaxOmissions)
        {
            AddError(result, "apple-qa-omissions-too-many");
            return;
        }

        foreach (var omission in omissions.EnumerateArray())
        {
            var kind = ReadRequiredString(omission, "kind", result, "apple-qa-omission-kind-missing");
            _ = ReadRequiredString(omission, "reason", result, "apple-qa-omission-reason-missing");
            if (kind is null)
                continue;

            // The source reason can contain host-specific text. Preserve the declared omission kind
            // while retaining only the fact that an omission was declared.
            result.Input.Exclusions.Add(new MauiQualificationExclusion
            {
                Kind = kind,
                Count = 1,
                Reason = "source-declared-omission",
            });
            adapted.OmissionCount++;
        }
    }

    private static void ValidatePlatformSeparation(
        string? platform,
        bool? experimental,
        string? backend,
        bool? officialCoverage,
        bool? macCatalystEquivalent,
        MauiPreviewQualificationArtifactManifestParseResult result,
        string prefix)
    {
        if (string.Equals(platform, "macos", StringComparison.Ordinal))
        {
            if (experimental != true ||
                !string.Equals(backend, "appkit", StringComparison.Ordinal) ||
                officialCoverage != false ||
                macCatalystEquivalent != false)
            {
                AddError(result, $"{prefix}-appkit-separation-invalid");
            }
            return;
        }

        if (platform is "ios" or "maccatalyst")
        {
            if (experimental != false ||
                backend is not null ||
                officialCoverage != true ||
                macCatalystEquivalent is not null)
            {
                AddError(result, $"{prefix}-official-platform-labels-invalid");
            }
        }
    }

    private static JsonElement? RequireObject(
        JsonElement? element,
        string property,
        MauiPreviewQualificationArtifactManifestParseResult result,
        string error)
    {
        if (element is { ValueKind: JsonValueKind.Object } source &&
            source.TryGetProperty(property, out var child) &&
            child.ValueKind == JsonValueKind.Object)
        {
            return child;
        }

        AddError(result, error);
        return null;
    }

    private static JsonElement? RequireObject(
        JsonElement element,
        string property,
        MauiPreviewQualificationArtifactManifestParseResult result,
        string error)
        => RequireObject((JsonElement?)element, property, result, error);

    private static string? ReadRequiredString(
        JsonElement? element,
        string property,
        MauiPreviewQualificationArtifactManifestParseResult result,
        string error)
    {
        if (element is { ValueKind: JsonValueKind.Object } source &&
            source.TryGetProperty(property, out var value) &&
            value.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(value.GetString()))
        {
            return value.GetString();
        }

        AddError(result, error);
        return null;
    }

    private static string? ReadRequiredString(
        JsonElement element,
        string property,
        MauiPreviewQualificationArtifactManifestParseResult result,
        string error)
        => ReadRequiredString((JsonElement?)element, property, result, error);

    private static string? ReadRequiredNullableString(
        JsonElement? element,
        string property,
        MauiPreviewQualificationArtifactManifestParseResult result,
        string error)
    {
        if (element is not { ValueKind: JsonValueKind.Object } source ||
            !source.TryGetProperty(property, out var value) ||
            value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
        {
            AddError(result, error);
            return null;
        }

        return value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;
    }

    private static string? ReadRequiredNullableString(
        JsonElement element,
        string property,
        MauiPreviewQualificationArtifactManifestParseResult result,
        string error)
        => ReadRequiredNullableString((JsonElement?)element, property, result, error);

    private static string? ReadOptionalString(JsonElement? element, string property) =>
        element is { ValueKind: JsonValueKind.Object } source &&
        source.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    private static string? ReadOptionalString(JsonElement element, string property)
        => ReadOptionalString((JsonElement?)element, property);

    private static bool? ReadRequiredBoolean(
        JsonElement? element,
        string property,
        MauiPreviewQualificationArtifactManifestParseResult result,
        string error)
    {
        if (element is { ValueKind: JsonValueKind.Object } source &&
            source.TryGetProperty(property, out var value) &&
            value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return value.GetBoolean();
        }

        AddError(result, error);
        return null;
    }

    private static bool? ReadRequiredBoolean(
        JsonElement element,
        string property,
        MauiPreviewQualificationArtifactManifestParseResult result,
        string error)
        => ReadRequiredBoolean((JsonElement?)element, property, result, error);

    private static bool? ReadRequiredNullableBoolean(
        JsonElement? element,
        string property,
        MauiPreviewQualificationArtifactManifestParseResult result,
        string error)
    {
        if (element is not { ValueKind: JsonValueKind.Object } source ||
            !source.TryGetProperty(property, out var value))
        {
            AddError(result, error);
            return null;
        }
        if (value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return value.GetBoolean();

        AddError(result, error);
        return null;
    }

    private static bool? ReadRequiredNullableBoolean(
        JsonElement element,
        string property,
        MauiPreviewQualificationArtifactManifestParseResult result,
        string error)
        => ReadRequiredNullableBoolean((JsonElement?)element, property, result, error);

    private static bool HasInt(JsonElement element, string property, int expected) =>
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var actual) &&
        actual == expected;

    private static bool HasString(JsonElement element, string property, string expected) =>
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String &&
        string.Equals(value.GetString(), expected, StringComparison.Ordinal);

    private static bool IsApplePlatform(string? value) => value is "ios" or "maccatalyst" or "macos";

    private static bool IsOutcome(string? value) => value is
        MauiFlowRunOutcomes.Passed or
        MauiFlowRunOutcomes.Failed or
        MauiFlowRunOutcomes.InfrastructureError or
        MauiFlowRunOutcomes.UnknownCompletion or
        MauiFlowRunOutcomes.Cancelled or
        MauiFlowRunOutcomes.TimedOut;

    private static bool IsDigest(string? value) =>
        value is { Length: 71 } &&
        value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) &&
        value.AsSpan(7).ToString().All(Uri.IsHexDigit);

    private static void ValidateDigestIfPresent(
        string? value,
        MauiPreviewQualificationArtifactManifestParseResult result,
        string error)
    {
        if (value is not null && !IsDigest(value))
            AddError(result, error);
    }

    private static bool Equivalent(AppleAttempt first, AppleAttempt second) =>
        string.Equals(first.Status, second.Status, StringComparison.Ordinal) &&
        string.Equals(first.Report, second.Report, StringComparison.Ordinal) &&
        string.Equals(first.ReportDigest, second.ReportDigest, StringComparison.Ordinal);

    private static void AddError(MauiPreviewQualificationArtifactManifestParseResult result, string error)
    {
        if (!result.Errors.Contains(error, StringComparer.Ordinal))
            result.Errors.Add(error);
    }

    private sealed record FlowDigest(string Name, string Digest);

    private sealed record AppleAttempt(string Status, string? Report, string? ReportDigest);
}
