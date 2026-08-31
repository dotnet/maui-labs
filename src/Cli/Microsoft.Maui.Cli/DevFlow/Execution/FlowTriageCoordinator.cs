using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.Cli.DevFlow.Execution;

internal static class FlowTriageOutputFormats
{
    public const string Json = "json";
    public const string Markdown = "markdown";
}

internal sealed record FlowTriageRequest
{
    public required string ManifestPath { get; init; }
    public required string ReportPath { get; init; }
    public string Format { get; init; } = FlowTriageOutputFormats.Json;
    public string? OutputPath { get; init; }
}

internal sealed record FlowTriageResult
{
    public required MauiFlowTriage Triage { get; init; }
    public required byte[] Content { get; init; }
    public string? OutputPath { get; init; }
}

internal interface IFlowTriageCoordinator
{
    Task<FlowTriageResult> AnalyzeAsync(
        FlowTriageRequest request,
        CancellationToken cancellationToken = default);
}

internal sealed class FlowTriageCoordinator : IFlowTriageCoordinator
{
    public async Task<FlowTriageResult> AnalyzeAsync(
        FlowTriageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Format is not (FlowTriageOutputFormats.Json or FlowTriageOutputFormats.Markdown))
        {
            throw FlowExecutionException.Invalid(
                "triage-format-invalid",
                "Flow triage format must be json or markdown.");
        }

        var manifestBytes = await BoundedExecutionJsonReader.ReadAsync(
            request.ManifestPath,
            "execution manifest",
            cancellationToken).ConfigureAwait(false);
        var reportBytes = await BoundedExecutionJsonReader.ReadAsync(
            request.ReportPath,
            "flow run report",
            cancellationToken).ConfigureAwait(false);

        MauiTestExecutionManifest? manifest;
        MauiFlowRunReport? report;
        try
        {
            manifest = JsonSerializer.Deserialize(
                manifestBytes,
                MauiTestingJsonContext.Default.MauiTestExecutionManifest);
            report = JsonSerializer.Deserialize(
                reportBytes,
                MauiTestingJsonContext.Default.MauiFlowRunReport);
        }
        catch (JsonException)
        {
            throw FlowExecutionException.Invalid(
                "triage-json-invalid",
                "The execution manifest or flow run report is not supported bounded JSON.");
        }

        if (manifest?.Schema != 1 || report?.Schema != 1)
        {
            throw FlowExecutionException.Unsupported(
                "triage-schema-unsupported",
                "Flow triage supports execution-manifest and flow-run schema 1 only.");
        }

        MauiFlowRunReportSerializer.ApplyLimits(report, new MauiFlowRunReportLimits());
        ValidateEvidenceBinding(manifest, report, reportBytes);
        var triage = MauiFlowTriageAnalyzer.Analyze(new MauiFlowTriageInput
        {
            Report = report,
            Manifest = manifest,
            ImportedEvidence = true,
            IsCurrentLocalRun = false,
            ArtifactTrust = MauiArtifactTrustStates.Untrusted,
        });
        var content = request.Format == FlowTriageOutputFormats.Json
            ? MauiFlowTriageSerializer.SerializeToUtf8Bytes(triage)
            : Encoding.UTF8.GetBytes(FlowTriageMarkdownWriter.Format(triage));

        string? outputPath = null;
        if (!string.IsNullOrWhiteSpace(request.OutputPath))
        {
            outputPath = await ImmutableExecutionFileWriter.WriteAsync(
                request.OutputPath,
                content,
                cancellationToken).ConfigureAwait(false);
        }

        return new FlowTriageResult
        {
            Triage = triage,
            Content = content,
            OutputPath = outputPath,
        };
    }

    internal static void ValidateEvidenceBinding(
        MauiTestExecutionManifest manifest,
        MauiFlowRunReport report,
        ReadOnlySpan<byte> reportBytes)
    {
        if (string.IsNullOrWhiteSpace(manifest.RunId) ||
            string.IsNullOrWhiteSpace(report.RunId) ||
            !string.Equals(manifest.RunId, report.RunId, StringComparison.Ordinal))
        {
            throw FlowExecutionException.Invalid(
                "triage-run-id-mismatch",
                "The execution manifest and flow run report do not identify the same run.");
        }
        var requiresFlowDigest = RequiresFlowDigest(report);
        var manifestHasFlowDigest = !string.IsNullOrWhiteSpace(manifest.FlowDigest);
        var reportHasFlowDigest = !string.IsNullOrWhiteSpace(report.FlowDigest);
        if ((requiresFlowDigest && (!manifestHasFlowDigest || !reportHasFlowDigest)) ||
            manifestHasFlowDigest != reportHasFlowDigest ||
            (manifestHasFlowDigest &&
             !string.Equals(
                 manifest.FlowDigest,
                 Fingerprint(report.FlowDigest, "flow"),
                 StringComparison.Ordinal)))
        {
            throw FlowExecutionException.Invalid(
                "triage-flow-digest-mismatch",
                "The execution manifest and flow run report do not identify the same flow digest.");
        }
        RequireExactMatch(
            manifest.FlowId,
            report.FlowId,
            "triage-flow-id-mismatch",
            "The execution manifest and flow run report do not identify the same flow.");
        if (manifest.FlowRevision is { } manifestRevision &&
            report.FlowRevision is { } reportRevision &&
            manifestRevision != reportRevision)
        {
            throw FlowExecutionException.Invalid(
                "triage-flow-revision-mismatch",
                "The execution manifest and flow run report do not identify the same flow revision.");
        }
        RequireFingerprintMatch(
            manifest.Build?.AppBuildFingerprint,
            report.Target?.AppBuildFingerprint,
            "app-build",
            "triage-app-build-mismatch",
            "The execution manifest and flow run report do not identify the same app build.");
        RequireFingerprintMatch(
            manifest.Build?.AppSourceFingerprint,
            report.Target?.AppSourceFingerprint,
            "app-source",
            "triage-app-source-mismatch",
            "The execution manifest and flow run report do not identify the same app source.");
        RequireFingerprintMatch(
            manifest.Build?.PackageDigest,
            report.Target?.PackageDigest,
            "package",
            "triage-package-mismatch",
            "The execution manifest and flow run report do not identify the same app package.");
        RequireExactMatch(
            manifest.Build?.AppId,
            report.Target?.AppId,
            "triage-app-id-mismatch",
            "The execution manifest and flow run report do not identify the same app.");
        RequireExactMatch(
            manifest.Device?.Platform,
            report.Target?.Platform,
            "triage-platform-mismatch",
            "The execution manifest and flow run report do not identify the same platform.");
        RequireDeviceProfileMatch(manifest.Device?.Profile, report.Target?.DeviceProfile);
        RequireLifecycleContainment(manifest.Lifecycle, report);
        RequireExactMatch(
            manifest.Outcome?.Status,
            report.Outcome?.Status,
            "triage-outcome-status-mismatch",
            "The execution manifest and flow run report do not have the same outcome status.");
        RequireExactMatch(
            manifest.Outcome?.ExitCategory,
            ReportExitCategory(report),
            "triage-exit-category-mismatch",
            "The execution manifest and flow run report do not have the same exit category.");
        if (manifest.Outcome?.Terminal is { } manifestTerminal &&
            report.Outcome?.Terminal is { } reportTerminal &&
            manifestTerminal != reportTerminal)
        {
            throw FlowExecutionException.Invalid(
                "triage-outcome-terminal-mismatch",
                "The execution manifest and flow run report do not have the same terminal state.");
        }
        var reportVerified = report.Verification?.Verified ?? report.Outcome?.Verified;
        if (manifest.Outcome?.Verified is { } manifestVerified &&
            reportVerified is { } verified &&
            manifestVerified != verified)
        {
            throw FlowExecutionException.Invalid(
                "triage-outcome-verification-mismatch",
                "The execution manifest and flow run report do not have the same verification state.");
        }
        if (!string.Equals(
                manifest.FingerprintVersion,
                MauiFlowIncidentFingerprint.RuleVersion,
                StringComparison.Ordinal))
        {
            throw FlowExecutionException.Unsupported(
                "triage-fingerprint-version-unsupported",
                "The execution manifest uses an unsupported fingerprint rule version.");
        }
        foreach (var (name, fingerprint) in new[]
                 {
                     ("test", manifest.TestIdentityFingerprint),
                     ("incident", manifest.IncidentFingerprint),
                     ("occurrence", manifest.OccurrenceFingerprint),
                 })
        {
            if (fingerprint is not null && !IsStrictFingerprint(fingerprint))
            {
                throw FlowExecutionException.Invalid(
                    $"triage-{name}-fingerprint-invalid",
                    $"The execution manifest contains an invalid {name} fingerprint.");
            }
        }

        var reportArtifacts = (manifest.Artifacts ?? [])
            .Where(static artifact =>
                string.Equals(artifact.Kind, "flow-run-report", StringComparison.Ordinal) ||
                string.Equals(artifact.Role, "semantic-report", StringComparison.Ordinal))
            .ToArray();
        if (reportArtifacts.Length != 1)
        {
            throw FlowExecutionException.Invalid(
                "triage-report-artifact-reference-invalid",
                "The execution manifest must contain exactly one flow-run report artifact reference.");
        }

        var artifact = reportArtifacts[0];
        var digest = "sha256:" +
            Convert.ToHexString(SHA256.HashData(reportBytes)).ToLowerInvariant();
        if (!string.Equals(artifact.Digest, digest, StringComparison.Ordinal) ||
            artifact.SizeBytes != reportBytes.Length ||
            !string.Equals(
                artifact.RelativePath,
                MauiFlowRunReportSerializer.FileName,
                StringComparison.Ordinal))
        {
            throw FlowExecutionException.Invalid(
                "triage-report-artifact-binding-mismatch",
                "The supplied flow-run bytes do not match the manifest artifact digest, size, and identity.");
        }
    }

    private static void RequireExactMatch(
        string? left,
        string? right,
        string code,
        string message)
    {
        if (!string.IsNullOrWhiteSpace(left) &&
            !string.IsNullOrWhiteSpace(right) &&
            !string.Equals(left.Trim(), right.Trim(), StringComparison.Ordinal))
        {
            throw FlowExecutionException.Invalid(code, message);
        }
    }

    private static bool RequiresFlowDigest(MauiFlowRunReport report)
        => report.Steps.Count > 0 ||
           (report.Target is { } target &&
            (!string.IsNullOrWhiteSpace(target.Platform) ||
             !string.IsNullOrWhiteSpace(target.AppId) ||
             !string.IsNullOrWhiteSpace(target.AppBuildFingerprint) ||
             !string.IsNullOrWhiteSpace(target.PackageDigest))) ||
           report.Failure?.Class is not
               MauiFlowFailureClasses.FlowInvalid and not
               MauiFlowFailureClasses.SchemaUnsupported;

    private static string? ReportExitCategory(MauiFlowRunReport report)
        => report.ExtensionData is not null &&
           report.ExtensionData.TryGetValue("exitCategory", out var value) &&
           value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static void RequireFingerprintMatch(
        string? left,
        string? right,
        string domain,
        string code,
        string message)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return;
        var leftFingerprint = Fingerprint(left, domain);
        var rightFingerprint = Fingerprint(right, domain);
        if (leftFingerprint is null ||
            rightFingerprint is null ||
            !string.Equals(leftFingerprint, rightFingerprint, StringComparison.Ordinal))
        {
            throw FlowExecutionException.Invalid(code, message);
        }
    }

    private static void RequireDeviceProfileMatch(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return;
        var canonicalLeft = CanonicalDeviceProfile(left);
        var canonicalRight = CanonicalDeviceProfile(right);
        if (!string.Equals(canonicalLeft, canonicalRight, StringComparison.Ordinal))
        {
            throw FlowExecutionException.Invalid(
                "triage-device-profile-mismatch",
                "The execution manifest and flow run report do not identify the same device profile.");
        }
    }

    private static string CanonicalDeviceProfile(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized is
            "phone" or "tablet" or "desktop" or "wearable" or "tv" or
            "emulator" or "simulator" or "physical" or "virtual"
            ? normalized
            : Fingerprint(value, "device-profile") ?? string.Empty;
    }

    /// <summary>
    /// The manifest lifecycle window covers the whole invocation (build, install, launch, replay,
    /// cleanup); the report window covers the replay alone. The two are therefore never equal, and
    /// requiring equality made every real <c>flow run</c> pair unreadable by <c>flow triage</c>.
    /// The invariant that was actually intended is containment: the replay has to have happened
    /// during the invocation the manifest describes.
    /// </summary>
    /// <remarks>
    /// This relaxes a consistency check, not an identity check. Sameness of run is still proved by
    /// the run id, flow digest, flow id and revision, app build, app source, package digest, app
    /// id, platform, device profile, outcome, and — decisively — by the manifest artifact entry
    /// committing to the exact SHA-256, byte length, and file name of the report bytes being read.
    /// A report from a different invocation cannot satisfy those and would not be admitted here by
    /// a timestamp comparison either.
    /// </remarks>
    private static void RequireLifecycleContainment(
        MauiTestExecutionLifecycleFacts? lifecycle,
        MauiFlowRunReport report)
    {
        var invocationStart = lifecycle?.StartedAt?.ToUniversalTime();
        var invocationEnd = lifecycle?.EndedAt?.ToUniversalTime();
        var replayStart = report.StartedAt?.ToUniversalTime();
        var replayEnd = report.EndedAt?.ToUniversalTime();

        if (invocationStart is { } manifestOpen &&
            invocationEnd is { } manifestClose &&
            manifestClose < manifestOpen)
        {
            throw FlowExecutionException.Invalid(
                "triage-lifecycle-window-invalid",
                "The execution manifest lifecycle window ends before it starts.");
        }
        if (replayStart is { } reportOpen &&
            replayEnd is { } reportClose &&
            reportClose < reportOpen)
        {
            throw FlowExecutionException.Invalid(
                "triage-lifecycle-window-invalid",
                "The flow run report window ends before it starts.");
        }

        // Both timestamps are produced by one process from one clock, so no slack is warranted:
        // a replay that starts before its own invocation, or outlives it, is a real inconsistency.
        if (invocationStart is { } start &&
            (replayStart is { } startedBefore && startedBefore < start ||
             replayEnd is { } endedBefore && endedBefore < start))
        {
            throw FlowExecutionException.Invalid(
                "triage-started-at-mismatch",
                "The flow run report describes a replay that started before the execution manifest invocation.");
        }
        if (invocationEnd is { } end &&
            (replayEnd is { } endedAfter && endedAfter > end ||
             replayStart is { } startedAfter && startedAfter > end))
        {
            throw FlowExecutionException.Invalid(
                "triage-ended-at-mismatch",
                "The flow run report describes a replay that ended after the execution manifest invocation.");
        }
    }

    private static bool IsStrictFingerprint(string value)
        => value.Length == 71 &&
           value.StartsWith("sha256:", StringComparison.Ordinal) &&
           value.AsSpan(7).ToArray().All(static character =>
               character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string? Fingerprint(string? value, string domain)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        if (trimmed.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            var normalized = "sha256:" + trimmed[7..].ToLowerInvariant();
            return IsStrictFingerprint(normalized) ? normalized : null;
        }
        if (trimmed.Length == 64 && trimmed.All(Uri.IsHexDigit))
            return "sha256:" + trimmed.ToLowerInvariant();
        return "sha256:" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(domain + "\u001f" + trimmed))).ToLowerInvariant();
    }
}

internal static class FlowTriageMarkdownWriter
{
    public static string Format(MauiFlowTriage triage)
    {
        ArgumentNullException.ThrowIfNull(triage);
        var safe = MauiFlowTriageSerializer.CreateSafeProjection(triage);
        var builder = new StringBuilder();
        builder.AppendLine("# MAUI DevFlow triage");
        Append(builder, "Class", safe.Classification.FailureClass);
        Append(builder, "Code", safe.Classification.Code);
        Append(builder, "Category", safe.Classification.Category);
        Append(builder, "Phase", safe.Classification.Phase);
        Append(builder, "Evidence", safe.Evidence.State);
        Append(builder, "Imported evidence", safe.ImportedEvidence ? "true" : "false");
        Append(builder, "Repair eligible", safe.RepairEligible ? "true" : "false");
        Append(builder, "Local reproduction required", safe.LocalReproductionRequired ? "true" : "false");
        Append(builder, "Retryable", safe.Retryable ? "true" : "false");
        Append(builder, "Test fingerprint", safe.TestIdentityFingerprint);
        Append(builder, "Incident fingerprint", safe.IncidentFingerprint);
        Append(builder, "Occurrence fingerprint", safe.OccurrenceFingerprint);
        Append(builder, "Summary", safe.Summary);
        AppendList(builder, "Allowed next actions", safe.AllowedNextActions);
        AppendList(builder, "Missing facts", safe.Evidence.MissingFacts);
        AppendList(builder, "Repair eligibility codes", safe.RepairEligibilityCodes);
        return builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static void Append(StringBuilder builder, string label, string? value)
        => builder.Append("- ").Append(label).Append(": `")
            .Append(string.IsNullOrWhiteSpace(value) ? "unknown" : value)
            .AppendLine("`");

    private static void AppendList(StringBuilder builder, string heading, IEnumerable<string> values)
    {
        builder.AppendLine().Append("## ").AppendLine(heading);
        var materialized = values.Distinct(StringComparer.Ordinal).OrderBy(static value => value, StringComparer.Ordinal).ToArray();
        if (materialized.Length == 0)
        {
            builder.AppendLine("- none");
            return;
        }
        foreach (var value in materialized)
            builder.Append("- `").Append(value).AppendLine("`");
    }
}

internal static class BoundedExecutionJsonReader
{
    private const int MaximumBytes = 1_048_576;
    private const int MaximumTokens = 100_000;
    private const int MaximumStringBytes = 16_384;

    public static async Task<byte[]> ReadAsync(
        string path,
        string kind,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw FlowExecutionException.Invalid("bounded-json-path-missing", $"A {kind} path is required.");

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            throw FlowExecutionException.Invalid("bounded-json-path-invalid", $"The {kind} path is invalid.");
        }

        if (!File.Exists(fullPath) ||
            !string.Equals(Path.GetExtension(fullPath), ".json", StringComparison.OrdinalIgnoreCase))
        {
            throw FlowExecutionException.Invalid(
                "bounded-json-not-found",
                $"The {kind} must be an existing JSON file.");
        }

        var info = new FileInfo(fullPath);
        ExecutionPathSafety.RejectReparsePoints(
            fullPath,
            "bounded-json-reparse-point",
            $"The {kind} path and its existing ancestors cannot be symbolic links or reparse points.");
        if (info.Length is <= 0 or > MaximumBytes)
            throw FlowExecutionException.Invalid("bounded-json-size-invalid", $"The {kind} exceeds the supported 1 MB bound.");

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken).ConfigureAwait(false);
            ValidateBudget(bytes);
        }
        catch (FlowExecutionException)
        {
            throw;
        }
        catch (JsonException)
        {
            throw FlowExecutionException.Invalid("bounded-json-invalid", $"The {kind} is not supported bounded JSON.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw FlowExecutionException.Invalid("bounded-json-read-failed", $"The {kind} could not be read.");
        }
        return bytes;
    }

    private static void ValidateBudget(ReadOnlySpan<byte> bytes)
    {
        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions
        {
            MaxDepth = 64,
            CommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false,
        });
        var tokens = 0;
        while (reader.Read())
        {
            if (++tokens > MaximumTokens)
                throw FlowExecutionException.Invalid("bounded-json-complexity", "The JSON document exceeds the supported complexity bound.");
            if (reader.TokenType == JsonTokenType.PropertyName && reader.ValueSpan.Length > 256)
                throw FlowExecutionException.Invalid("bounded-json-property", "The JSON document contains an oversized property name.");
            if (reader.TokenType == JsonTokenType.String && reader.ValueSpan.Length > MaximumStringBytes)
                throw FlowExecutionException.Invalid("bounded-json-string", "The JSON document contains an oversized string.");
        }
    }
}

internal static class ImmutableExecutionFileWriter
{
    public static async Task<string> WriteAsync(
        string path,
        byte[] content,
        CancellationToken cancellationToken)
    {
        var fullPath = ExecutionPathSafety.PrepareParentForNewFile(path);
        var directory = Path.GetDirectoryName(fullPath)!;
        var temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            ExecutionPathSafety.RejectReparsePoints(
                directory,
                "execution-output-reparse-point",
                "The output path and its existing ancestors cannot be symbolic links or reparse points.");
            await using (var stream = new FileStream(
                temporary,
                new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                }))
            {
                await stream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            ExecutionPathSafety.RejectReparsePoints(
                directory,
                "execution-output-reparse-point",
                "The output path and its existing ancestors cannot be symbolic links or reparse points.");
            if (ExecutionPathSafety.EntryExists(fullPath))
            {
                throw FlowExecutionException.Invalid(
                    "execution-output-exists",
                    "The output path already exists.");
            }
            File.Move(temporary, fullPath);
            return fullPath;
        }
        catch
        {
            try { File.Delete(temporary); } catch { }
            throw;
        }
    }
}
