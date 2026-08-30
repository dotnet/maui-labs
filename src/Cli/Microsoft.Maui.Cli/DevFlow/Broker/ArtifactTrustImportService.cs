using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow.Evidence;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.Cli.DevFlow.Broker;

/// <summary>Supported, non-executable import formats.</summary>
internal static class ArtifactTrustImportKinds
{
    public const string FlowRun = "flow-run";
    public const string Evidence = "mauitrace";

    public static bool IsKnown(string? kind)
        => string.Equals(kind, FlowRun, StringComparison.Ordinal) ||
           string.Equals(kind, Evidence, StringComparison.Ordinal);
}

/// <summary>Result of a bounded import. It intentionally contains no raw input bytes.</summary>
internal sealed class ArtifactTrustImportResult
{
    public bool Ok { get; init; }
    public string? Error { get; init; }
    public MauiArtifactTrustRecord? Artifact { get; init; }
    public long BytesRead { get; init; }

    public static ArtifactTrustImportResult Failure(string error)
        => new() { Error = error };
}

/// <summary>
/// Reads foreign flow reports and evidence bundles as hostile input. This service produces only a
/// bounded, redacted diagnostic projection; it never replays, executes, writes a workspace file,
/// or retains raw imported bytes.
/// </summary>
internal interface IArtifactTrustImporter
{
    ArtifactTrustImportResult Import(
        Stream input,
        string artifactKind,
        MauiArtifactTrustPolicy? policy = null,
        MauiArtifactVerifiedProvenanceFacts? verifiedProvenance = null,
        CancellationToken cancellationToken = default);
}

internal sealed class ArtifactTrustImportService : IArtifactTrustImporter
{
    internal const int MaxFlowRunBytes = 1_048_576;
    internal const int MaxJsonDepth = 64;
    internal const int MaxJsonTokens = 20_000;
    internal const int MaxJsonStringBytes = 4_096;

    private readonly TimeProvider _clock;

    public ArtifactTrustImportService(TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
    }

    public ArtifactTrustImportResult Import(
        Stream input,
        string artifactKind,
        MauiArtifactTrustPolicy? policy = null,
        MauiArtifactVerifiedProvenanceFacts? verifiedProvenance = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();

        var maximum = MaximumBytes(artifactKind);
        if (maximum <= 0)
            return ArtifactTrustImportResult.Failure("Unsupported artifact import kind.");

        byte[] bytes;
        try
        {
            bytes = ReadBounded(input, maximum, cancellationToken);
        }
        catch (ArtifactTrustImportException exception)
        {
            return ArtifactTrustImportResult.Failure(exception.Message);
        }
        catch (IOException)
        {
            return ArtifactTrustImportResult.Failure("The artifact could not be read.");
        }

        return Import(bytes, artifactKind, policy, verifiedProvenance, cancellationToken);
    }

    public ArtifactTrustImportResult Import(
        ReadOnlyMemory<byte> bytes,
        string artifactKind,
        MauiArtifactTrustPolicy? policy = null,
        MauiArtifactVerifiedProvenanceFacts? verifiedProvenance = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var maximum = MaximumBytes(artifactKind);
        if (maximum <= 0)
            return ArtifactTrustImportResult.Failure("Unsupported artifact import kind.");
        if (bytes.IsEmpty)
            return ArtifactTrustImportResult.Failure("The imported artifact is empty.");
        if (bytes.Length > maximum)
            return ArtifactTrustImportResult.Failure("The imported artifact exceeds the supported size limit.");

        var digest = Convert.ToHexString(SHA256.HashData(bytes.Span)).ToLowerInvariant();
        return string.Equals(artifactKind, ArtifactTrustImportKinds.FlowRun, StringComparison.Ordinal)
            ? ImportFlowRun(bytes, digest, policy, verifiedProvenance, cancellationToken)
            : ImportEvidence(bytes, digest, policy, verifiedProvenance, cancellationToken);
    }

    private ArtifactTrustImportResult ImportFlowRun(
        ReadOnlyMemory<byte> bytes,
        string digest,
        MauiArtifactTrustPolicy? policy,
        MauiArtifactVerifiedProvenanceFacts? verifiedProvenance,
        CancellationToken cancellationToken)
    {
        JsonDocument document;
        try
        {
            ValidateJsonBudget(bytes.Span);
            document = JsonDocument.Parse(bytes, new JsonDocumentOptions
            {
                MaxDepth = MaxJsonDepth,
                CommentHandling = JsonCommentHandling.Disallow,
                AllowTrailingCommas = false,
            });
        }
        catch (JsonException)
        {
            return ArtifactTrustImportResult.Failure("flow-run.json is not a supported bounded JSON document.");
        }
        catch (ArtifactTrustImportException exception)
        {
            return ArtifactTrustImportResult.Failure(exception.Message);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !TryGetInt32(root, "schema", out var schema) ||
                schema != 1)
            {
                return ArtifactTrustImportResult.Failure("flow-run.json must use supported schema 1.");
            }

            var projection = CreateBaseProjection(ArtifactTrustImportKinds.FlowRun, "flow-run-report-v1");
            projection.FlowFingerprint = MauiArtifactTrustRedactor.Fingerprint(GetString(root, "flowDigest"));
            projection.Outcome = MauiArtifactTrustRedactor.SafeFailureCode(
                GetObject(root, "outcome") is { } outcome ? GetString(outcome, "status") : null);
            projection.CapturedAt = GetDateTimeOffset(root, "endedAt") ?? GetDateTimeOffset(root, "startedAt");
            projection.Truncated = GetBoolean(root, "truncated");
            // The imported outcome is the producing host's preserved run verdict, so a failure of
            // its own owned cleanup is no longer visible there and has to be carried separately.
            // Three states, not two. An artifact that omits the field entirely — or writes null —
            // was produced before this contract existed and simply does not say whether its host
            // tore itself down, so it stays unknown: answering `false` would assert a clean teardown
            // on the artifact's behalf. An explicit empty array is the current producer stating
            // there was nothing to report. Anything else present is treated as a cleanup failure,
            // because this is the untrusted read path and a malformed shape must not read as clean.
            projection.OwnedCleanupIncomplete =
                !root.TryGetProperty("secondaryFailures", out var secondaryFailures) ||
                secondaryFailures.ValueKind == JsonValueKind.Null
                    ? null
                    : secondaryFailures.ValueKind != JsonValueKind.Array ||
                      secondaryFailures.GetArrayLength() != 0;

            var target = GetObject(root, "target");
            projection.AppBuildFingerprint = MauiArtifactTrustRedactor.Fingerprint(GetString(target, "appBuildFingerprint"));
            projection.AppSourceFingerprint = MauiArtifactTrustRedactor.Fingerprint(GetString(target, "appSourceFingerprint"));
            projection.PackageFingerprint = MauiArtifactTrustRedactor.Fingerprint(GetString(target, "packageDigest"));
            projection.NormalizedPayloadFingerprint = MauiArtifactTrustRedactor.Fingerprint(
                GetString(target, "normalizedPayloadDigest"));
            projection.PlatformFingerprint = MauiArtifactTrustRedactor.Fingerprint(GetString(target, "platform"));
            projection.DeviceProfileFingerprint = MauiArtifactTrustRedactor.Fingerprint(GetString(target, "deviceProfile"));
            projection.RuntimeProfileFingerprint = MauiArtifactTrustRedactor.NormalizeFingerprint(
                GetString(target, "runtimeProfileFingerprint"));

            AddIdentifierDigest(projection, GetString(root, "runId"));
            PopulateFailureProjection(root, projection, digest);
            AddReportOmissions(root, projection);

            return CreateImportedResult(
                projection,
                digest,
                internalHashesPresent: false,
                internalHashesVerified: false,
                policy,
                verifiedProvenance,
                bytes.Length);
        }
    }

    private ArtifactTrustImportResult ImportEvidence(
        ReadOnlyMemory<byte> bytes,
        string digest,
        MauiArtifactTrustPolicy? policy,
        MauiArtifactVerifiedProvenanceFacts? verifiedProvenance,
        CancellationToken cancellationToken)
    {
        EvidenceReadResult read;
        try
        {
            using var stream = new MemoryStream(bytes.ToArray(), writable: false);
            read = EvidenceBundleReader.Read(stream, path: null, cancellationToken);
        }
        catch (IOException)
        {
            return ArtifactTrustImportResult.Failure("The evidence bundle could not be read.");
        }

        if (!read.Ok || read.Manifest is null || read.Manifest.FormatVersion != EvidenceFormat.Version)
            return ArtifactTrustImportResult.Failure("The evidence bundle is not a supported .mauitrace v1 artifact.");

        var manifest = read.Manifest;
        var projection = CreateBaseProjection(ArtifactTrustImportKinds.Evidence, "mauitrace-v1");
        projection.CapturedAt = ParseUtc(manifest.CapturedUtc);
        projection.AppBuildFingerprint = MauiArtifactTrustRedactor.Fingerprint(manifest.App?.Build);
        projection.PackageFingerprint = MauiArtifactTrustRedactor.Fingerprint(manifest.App?.PackageId);
        projection.PlatformFingerprint = MauiArtifactTrustRedactor.Fingerprint(manifest.Platform?.Name);
        projection.DeviceProfileFingerprint = MauiArtifactTrustRedactor.Fingerprint(
            string.Join(
                "|",
                manifest.Platform?.DeviceType ?? string.Empty,
                manifest.Platform?.Idiom ?? string.Empty,
                manifest.Platform?.Framework ?? string.Empty));

        var link = manifest.FlowRun;
        if (link is not null)
        {
            AddIdentifierDigest(projection, link.RunId);
            AddIdentifierDigest(projection, link.FailedStepId);
            projection.Failure = new MauiImportedFailureProjection
            {
                Code = MauiArtifactTrustRedactor.SafeFailureCode(link.FailureCode),
                StepFingerprint = MauiArtifactTrustRedactor.Fingerprint(link.FailedStepId),
            };
            projection.Failure.FailureKey = FailureKey(digest, projection.Failure);
            projection.Omissions.Add(new MauiArtifactTrustOmission
            {
                Field = "failure.checkpoints",
                Reason = "Evidence v1 does not retain flow failure checkpoints.",
            });
        }
        else
        {
            projection.Omissions.Add(new MauiArtifactTrustOmission
            {
                Field = "flowRun",
                Reason = "The evidence bundle has no flow-run linkage.",
            });
        }

        if (read.Warnings.Count > 0 || manifest.Excluded.Count > 0)
        {
            projection.Omissions.Add(new MauiArtifactTrustOmission
            {
                Field = "bundle-content",
                Reason = "The evidence reader omitted unsupported or bounded entries.",
            });
        }

        return CreateImportedResult(
            projection,
            digest,
            internalHashesPresent: manifest.Entries.Count > 0,
            internalHashesVerified: true,
            policy,
            verifiedProvenance,
            bytes.Length);
    }

    private ArtifactTrustImportResult CreateImportedResult(
        MauiImportedArtifactSafeProjection projection,
        string digest,
        bool internalHashesPresent,
        bool internalHashesVerified,
        MauiArtifactTrustPolicy? policy,
        MauiArtifactVerifiedProvenanceFacts? verifiedProvenance,
        int bytesRead)
    {
        projection.Omissions.Add(new MauiArtifactTrustOmission
        {
            Field = "raw-content",
            Reason = "Raw imported bytes are not retained.",
        });
        projection.Omissions.Add(new MauiArtifactTrustOmission
        {
            Field = "embedded-identifiers",
            Reason = "Embedded IDs are confined to one-way metadata digests.",
        });

        var integrity = new MauiArtifactIntegrityVerification
        {
            ArtifactDigest = digest,
            Verified = true,
            InternalHashesPresent = internalHashesPresent,
            InternalHashesVerified = internalHashesVerified,
            IntegrityOnly = true,
            Reason = internalHashesVerified
                ? "Validated archive/report-internal hashes are integrity-only."
                : "The import boundary calculated the artifact SHA-256 digest.",
        };
        var verification = MauiArtifactTrustEvaluator.EvaluateImport(policy, integrity, verifiedProvenance);
        var record = new MauiArtifactTrustRecord
        {
            Identity = MauiImportedArtifactIdentity.Create(),
            ArtifactKind = projection.Kind,
            ImportedAt = _clock.GetUtcNow(),
            Integrity = integrity,
            Verification = verification,
            Projection = projection,
        };
        return new ArtifactTrustImportResult
        {
            Ok = true,
            Artifact = record,
            BytesRead = bytesRead,
        };
    }

    private static MauiImportedArtifactSafeProjection CreateBaseProjection(string kind, string schema)
        => new()
        {
            Kind = kind,
            SourceSchema = schema,
        };

    private static void PopulateFailureProjection(
        JsonElement root,
        MauiImportedArtifactSafeProjection projection,
        string artifactDigest)
    {
        var failure = GetObject(root, "failure");
        if (failure is null)
        {
            projection.Omissions.Add(new MauiArtifactTrustOmission
            {
                Field = "failure",
                Reason = "The report contains no terminal failure facts.",
            });
            return;
        }

        var stepId = GetString(failure.Value, "stepId");
        var imported = new MauiImportedFailureProjection
        {
            Code = MauiArtifactTrustRedactor.SafeFailureCode(GetString(failure.Value, "code")),
            Class = MauiArtifactTrustRedactor.SafeFailureCode(GetString(failure.Value, "class")),
            StepFingerprint = MauiArtifactTrustRedactor.Fingerprint(stepId),
        };
        AddIdentifierDigest(projection, GetString(failure.Value, "failureId"));
        AddIdentifierDigest(projection, stepId);

        if (!string.IsNullOrWhiteSpace(stepId) &&
            TryFindStep(root, stepId, out var step))
        {
            imported.ExpectedCheckpointFingerprint = MauiArtifactTrustRedactor.CheckpointFingerprint(
                ReadCheckpoint(GetObject(step, "expectedCheckpoint")));
            imported.ObservedCheckpointFingerprint = MauiArtifactTrustRedactor.CheckpointFingerprint(
                ReadCheckpoint(GetObject(step, "observedCheckpoint")));
        }
        else
        {
            projection.Omissions.Add(new MauiArtifactTrustOmission
            {
                Field = "failure.step",
                Reason = "The failed step could not be projected from the bounded report.",
            });
        }

        if (string.IsNullOrWhiteSpace(imported.Code) ||
            string.IsNullOrWhiteSpace(imported.StepFingerprint) ||
            string.IsNullOrWhiteSpace(imported.ExpectedCheckpointFingerprint) ||
            string.IsNullOrWhiteSpace(imported.ObservedCheckpointFingerprint))
        {
            projection.Omissions.Add(new MauiArtifactTrustOmission
            {
                Field = "failure.reproduction-facts",
                Reason = "Code, step, and both checkpoints are required for local reproduction trust.",
            });
        }

        imported.FailureKey = FailureKey(artifactDigest, imported);
        projection.Failure = imported;
    }

    private static MauiFlowCheckpoint? ReadCheckpoint(JsonElement? value)
    {
        if (value is not { ValueKind: JsonValueKind.Object } checkpoint)
            return null;

        return new MauiFlowCheckpoint
        {
            AppBuildFingerprint = GetString(checkpoint, "appBuildFingerprint"),
            AgentInstanceId = GetString(checkpoint, "agentInstanceId"),
            SeedFingerprint = GetString(checkpoint, "seedFingerprint"),
            BackendStateFingerprint = GetString(checkpoint, "backendStateFingerprint"),
            Route = GetString(checkpoint, "route"),
            Window = GetString(checkpoint, "window"),
            Modal = GetString(checkpoint, "modal"),
            Locale = GetString(checkpoint, "locale"),
            Theme = GetString(checkpoint, "theme"),
            Orientation = GetString(checkpoint, "orientation"),
            DisplayProfile = GetString(checkpoint, "displayProfile"),
            CollectionItemKey = GetString(checkpoint, "collectionItemKey"),
        };
    }

    private static bool TryFindStep(JsonElement root, string stepId, out JsonElement step)
    {
        step = default;
        if (!root.TryGetProperty("steps", out var steps) || steps.ValueKind != JsonValueKind.Array)
            return false;

        var count = 0;
        foreach (var candidate in steps.EnumerateArray())
        {
            if (++count > 2_000)
                return false;
            if (candidate.ValueKind == JsonValueKind.Object &&
                string.Equals(GetString(candidate, "stepId"), stepId, StringComparison.Ordinal))
            {
                step = candidate;
                return true;
            }
        }

        return false;
    }

    private static string FailureKey(string artifactDigest, MauiImportedFailureProjection failure)
    {
        var material = string.Join(
            "\u001f",
            artifactDigest,
            failure.Code ?? string.Empty,
            failure.Class ?? string.Empty,
            failure.StepFingerprint ?? string.Empty,
            failure.ExpectedCheckpointFingerprint ?? string.Empty,
            failure.ObservedCheckpointFingerprint ?? string.Empty);
        return "if_" + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
    }

    private static void AddReportOmissions(JsonElement root, MauiImportedArtifactSafeProjection projection)
    {
        if (GetBoolean(root, "truncated") == true)
        {
            projection.Omissions.Add(new MauiArtifactTrustOmission
            {
                Field = "report",
                Reason = "The source report explicitly declared truncation.",
            });
        }

        if (root.TryGetProperty("omissions", out var omissions) &&
            omissions.ValueKind == JsonValueKind.Array &&
            omissions.GetArrayLength() > 0)
        {
            projection.Omissions.Add(new MauiArtifactTrustOmission
            {
                Field = "source-omissions",
                Reason = "The source report explicitly declared omitted detail.",
            });
        }
    }

    private static void AddIdentifierDigest(MauiImportedArtifactSafeProjection projection, string? identifier)
    {
        var digest = MauiArtifactTrustRedactor.Fingerprint(identifier);
        if (!string.IsNullOrWhiteSpace(digest) && projection.EmbeddedIdentifierDigests.Count < 8)
            projection.EmbeddedIdentifierDigests.Add(digest);
    }

    private static int MaximumBytes(string? artifactKind)
        => artifactKind switch
        {
            ArtifactTrustImportKinds.FlowRun => MaxFlowRunBytes,
            ArtifactTrustImportKinds.Evidence => (int)EvidenceFormat.MaxBundleFileBytes,
            _ => 0,
        };

    private static byte[] ReadBounded(Stream input, int maximum, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream(Math.Min(maximum, 64 * 1024));
        var buffer = new byte[16 * 1024];
        var total = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = input.Read(buffer, 0, buffer.Length);
            if (read == 0)
                break;

            total += read;
            if (total > maximum)
                throw new ArtifactTrustImportException("The imported artifact exceeds the supported size limit.");
            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    private static void ValidateJsonBudget(ReadOnlySpan<byte> bytes)
    {
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions
        {
            MaxDepth = MaxJsonDepth,
            CommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false,
        });
        var tokens = 0;
        while (reader.Read())
        {
            if (++tokens > MaxJsonTokens)
                throw new ArtifactTrustImportException("flow-run.json exceeds the supported JSON complexity limit.");

            if (reader.TokenType == JsonTokenType.PropertyName && reader.ValueSpan.Length > 128)
                throw new ArtifactTrustImportException("flow-run.json contains an oversized property name.");
            if (reader.TokenType == JsonTokenType.String && reader.ValueSpan.Length > MaxJsonStringBytes)
                throw new ArtifactTrustImportException("flow-run.json contains an oversized string.");
        }
    }

    private static JsonElement? GetObject(JsonElement? value, string property)
    {
        if (value is { ValueKind: JsonValueKind.Object } root &&
            root.TryGetProperty(property, out var child) &&
            child.ValueKind == JsonValueKind.Object)
        {
            return child;
        }

        return null;
    }

    private static JsonElement? GetObject(JsonElement value, string property)
        => GetObject((JsonElement?)value, property);

    private static string? GetString(JsonElement? value, string property)
    {
        if (value is { ValueKind: JsonValueKind.Object } root &&
            root.TryGetProperty(property, out var child) &&
            child.ValueKind == JsonValueKind.String)
        {
            var text = child.GetString();
            return text is { Length: <= MaxJsonStringBytes } ? text : null;
        }

        return null;
    }

    private static string? GetString(JsonElement value, string property)
        => GetString((JsonElement?)value, property);

    private static bool? GetBoolean(JsonElement value, string property)
        => value.TryGetProperty(property, out var child) && child.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? child.GetBoolean()
            : null;

    private static bool TryGetInt32(JsonElement value, string property, out int number)
    {
        number = default;
        return value.TryGetProperty(property, out var child) &&
               child.ValueKind == JsonValueKind.Number &&
               child.TryGetInt32(out number);
    }

    private static DateTimeOffset? GetDateTimeOffset(JsonElement value, string property)
        => value.TryGetProperty(property, out var child) && child.ValueKind == JsonValueKind.String
            ? ParseUtc(child.GetString())
            : null;

    private static DateTimeOffset? ParseUtc(string? value)
        => DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;

    private sealed class ArtifactTrustImportException(string message) : Exception(message);
}
