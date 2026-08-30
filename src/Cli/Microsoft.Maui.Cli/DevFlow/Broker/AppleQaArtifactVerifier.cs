using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.Cli.DevFlow.Broker;

/// <summary>Safe result of checking a returned Apple QA directory or ZIP without extracting it.</summary>
internal sealed class AppleQaArtifactVerificationResult
{
    [JsonPropertyName("ok")]
    public bool Ok { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }

    [JsonPropertyName("platform")]
    public string? Platform { get; init; }

    [JsonPropertyName("experimental")]
    public bool? Experimental { get; init; }

    [JsonPropertyName("officialCoverage")]
    public bool? OfficialCoverage { get; init; }

    [JsonPropertyName("macCatalystEquivalent")]
    public bool? MacCatalystEquivalent { get; init; }

    [JsonPropertyName("entryCount")]
    public int EntryCount { get; init; }

    [JsonPropertyName("totalUncompressedBytes")]
    public long TotalUncompressedBytes { get; init; }

    [JsonPropertyName("manifestDigest")]
    public string? ManifestDigest { get; init; }

    [JsonPropertyName("executed")]
    public bool Executed { get; init; }

    [JsonPropertyName("rawContentRetained")]
    public bool RawContentRetained { get; init; }

    [JsonPropertyName("repairProposalAuthority")]
    public bool RepairProposalAuthority { get; init; }

    [JsonPropertyName("verifiedArtifacts")]
    public List<AppleQaVerifiedArtifact> VerifiedArtifacts { get; init; } = [];

    [JsonPropertyName("importedDiagnostics")]
    public List<MauiArtifactTrustRecord> ImportedDiagnostics { get; init; } = [];

    [JsonPropertyName("skippedDiagnosticImports")]
    public int SkippedDiagnosticImports { get; init; }

    public static AppleQaArtifactVerificationResult Failure(string error)
        => new() { Error = error };
}

/// <summary>Hash-verified metadata for one manifest-declared return entry.</summary>
internal sealed class AppleQaVerifiedArtifact
{
    [JsonPropertyName("kind")]
    public string? Kind { get; init; }

    [JsonPropertyName("digest")]
    public string? Digest { get; init; }

    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; init; }

    [JsonPropertyName("diagnosticImported")]
    public bool DiagnosticImported { get; set; }
}

/// <summary>
/// Bounded, non-executing verifier for returned iOS, Mac Catalyst, and experimental AppKit QA
/// artifacts. It accepts only the documented run tree, validates every manifest-declared hash,
/// rejects archive traversal/duplicates/symlinks/bombs, and streams supported diagnostics only
/// through the existing untrusted artifact importer.
/// </summary>
internal sealed class AppleQaArtifactVerifier
{
    internal const long MaxArchiveBytes = 128L * 1024 * 1024;
    internal const long MaxEntryBytes = 64L * 1024 * 1024;
    internal const long MaxTotalUncompressedBytes = 128L * 1024 * 1024;
    internal const int MaxEntryCount = 512;
    internal const int MaxCompressionRatio = 200;
    internal const int MaxDiagnosticImports = 64;
    private const int MaxManifestBytes = 1_048_576;

    private static readonly HashSet<string> RootMetadataFiles = new(StringComparer.Ordinal)
    {
        "manifest.json",
        "flow-run.json",
        "apple-xctest-spike.json",
        "apple-xctest-host-ready.json",
        "apple-flow-qa.json",
        "appkit-tier1-manifest.json",
        "appkit-capabilities.json",
        "qualification.json",
    };

    private readonly ArtifactTrustImportService _imports;

    public AppleQaArtifactVerifier(ArtifactTrustImportService? imports = null)
    {
        _imports = imports ?? new ArtifactTrustImportService();
    }

    /// <summary>
    /// Verifies a ZIP produced by the handoff command or an extracted return directory. No entry is
    /// extracted, launched, replayed, persisted, or given repair/source proposal authority.
    /// </summary>
    public AppleQaArtifactVerificationResult Verify(
        string sourcePath,
        bool importDiagnostics = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var fullPath = Path.GetFullPath(sourcePath);
            RejectReparsePath(fullPath);
            if (Directory.Exists(fullPath))
                return VerifyDirectory(fullPath, importDiagnostics, cancellationToken);
            if (File.Exists(fullPath) && string.Equals(Path.GetExtension(fullPath), ".zip", StringComparison.OrdinalIgnoreCase))
                return VerifyZip(fullPath, importDiagnostics, cancellationToken);
            return AppleQaArtifactVerificationResult.Failure(
                "Apple QA handoff input must be an extracted return directory or a .zip file.");
        }
        catch (ArtifactVerificationException exception)
        {
            return AppleQaArtifactVerificationResult.Failure(exception.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidDataException)
        {
            return AppleQaArtifactVerificationResult.Failure("Apple QA handoff input could not be read safely.");
        }
    }

    private AppleQaArtifactVerificationResult VerifyDirectory(
        string root,
        bool importDiagnostics,
        CancellationToken cancellationToken)
    {
        RejectReparsePoint(root);
        var entries = new List<SourceEntry>();
        var directories = new List<string>();
        var total = 0L;
        CollectDirectoryEntries(root, string.Empty, entries, directories, ref total, cancellationToken);
        var manifest = FindManifest(entries, directDirectory: true);
        var manifestBytes = ReadBounded(manifest, MaxManifestBytes, cancellationToken);
        var identity = ReadManifestIdentity(manifestBytes);
        var prefix = manifest.LogicalPath == "manifest.json"
            ? $"artifacts/devflow/{identity.RunId}/{identity.Platform}"
            : ManifestPrefix(manifest.LogicalPath, identity.Platform);

        if (manifest.LogicalPath == "manifest.json")
        {
            foreach (var entry in entries)
                entry.LogicalPath = $"{prefix}/{entry.RelativePath}";
            for (var index = 0; index < directories.Count; index++)
                directories[index] = $"{prefix}/{directories[index]}";
            manifest = entries.Single(entry => string.Equals(entry.RelativePath, "manifest.json", StringComparison.Ordinal));
        }

        return VerifyEntries(entries, directories, manifest, identity, importDiagnostics, cancellationToken);
    }

    private AppleQaArtifactVerificationResult VerifyZip(
        string path,
        bool importDiagnostics,
        CancellationToken cancellationToken)
    {
        RejectReparsePoint(path);
        var info = new FileInfo(path);
        if (info.Length > MaxArchiveBytes)
            throw new ArtifactVerificationException("Apple QA handoff archive exceeds the supported compressed size limit.");

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var entries = new List<SourceEntry>();
        var directories = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var total = 0L;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryNormalizeArchivePath(entry.FullName, out var normalized, out var directory))
                throw new ArtifactVerificationException("Apple QA handoff archive contains an unsafe entry path.");
            if (!seen.Add(normalized))
                throw new ArtifactVerificationException("Apple QA handoff archive contains duplicate entry paths.");
            if (IsSymlink(entry))
                throw new ArtifactVerificationException("Apple QA handoff archive contains a symbolic-link entry.");
            if (entries.Count + directories.Count >= MaxEntryCount)
                throw new ArtifactVerificationException("Apple QA handoff archive contains too many entries.");
            if (directory)
            {
                directories.Add(normalized);
                continue;
            }
            ValidateEntryLimits(entry.Length, entry.CompressedLength, ref total);
            entries.Add(new SourceEntry(
                relativePath: normalized,
                logicalPath: normalized,
                length: entry.Length,
                open: entry.Open));
        }

        var manifest = FindManifest(entries, directDirectory: false);
        var manifestBytes = ReadBounded(manifest, MaxManifestBytes, cancellationToken);
        var identity = ReadManifestIdentity(manifestBytes);
        return VerifyEntries(entries, directories, manifest, identity, importDiagnostics, cancellationToken);
    }

    private AppleQaArtifactVerificationResult VerifyEntries(
        List<SourceEntry> entries,
        IReadOnlyList<string> directories,
        SourceEntry manifest,
        ManifestIdentity identity,
        bool importDiagnostics,
        CancellationToken cancellationToken)
    {
        var prefix = ManifestPrefix(manifest.LogicalPath, identity.Platform);
        foreach (var entry in entries)
        {
            if (!IsAllowedLogicalPath(entry.LogicalPath, prefix, identity.Platform))
                throw new ArtifactVerificationException("Apple QA handoff contains an entry outside the supported return allowlist.");
        }
        foreach (var directory in directories)
        {
            if (!IsAllowedDirectoryPath(directory, prefix, identity.Platform))
                throw new ArtifactVerificationException("Apple QA handoff contains a directory outside the supported return allowlist.");
        }

        var manifestBytes = ReadBounded(manifest, MaxManifestBytes, cancellationToken);
        var manifestText = StrictUtf8(manifestBytes);
        var parsed = MauiAppleFlowQaManifestReader.ParseJson(manifestText);
        if (!parsed.Ok || parsed.Input.AppleQa is null)
            throw new ArtifactVerificationException("Apple QA handoff manifest does not satisfy the supported versioned contract.");
        if (!string.Equals(parsed.Input.Platform, identity.Platform, StringComparison.Ordinal))
            throw new ArtifactVerificationException("Apple QA handoff manifest platform does not match its return path.");

        var declared = ReadDeclaredArtifacts(manifestBytes);
        var byPath = entries.ToDictionary(static entry => entry.LogicalPath, StringComparer.OrdinalIgnoreCase);
        var verified = new List<AppleQaVerifiedArtifact>();
        var verifiedByPath = new Dictionary<string, AppleQaVerifiedArtifact>(StringComparer.OrdinalIgnoreCase);
        var verifiedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var artifact in declared)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsAllowedLogicalPath(artifact.Path, prefix, identity.Platform) ||
                !byPath.TryGetValue(artifact.Path, out var entry))
            {
                throw new ArtifactVerificationException("Apple QA handoff is missing a manifest-declared artifact.");
            }
            if (entry.Length != artifact.SizeBytes)
                throw new ArtifactVerificationException("Apple QA handoff artifact size does not match its manifest.");

            var digest = HashEntry(entry, cancellationToken);
            if (!string.Equals(digest, artifact.Digest, StringComparison.OrdinalIgnoreCase))
                throw new ArtifactVerificationException("Apple QA handoff artifact hash does not match its manifest.");

            verifiedPaths.Add(entry.LogicalPath);
            var verifiedArtifact = new AppleQaVerifiedArtifact
            {
                Kind = MauiQualificationSanitizer.SafeKind(artifact.Kind),
                Digest = artifact.Digest,
                SizeBytes = artifact.SizeBytes,
            };
            verified.Add(verifiedArtifact);
            verifiedByPath.Add(entry.LogicalPath, verifiedArtifact);
        }

        foreach (var entry in entries)
        {
            if (!string.Equals(entry.LogicalPath, manifest.LogicalPath, StringComparison.OrdinalIgnoreCase) &&
                !verifiedPaths.Contains(entry.LogicalPath))
            {
                throw new ArtifactVerificationException(
                    "Apple QA handoff contains an entry not declared by the manifest.");
            }
        }

        var imported = new List<MauiArtifactTrustRecord>();
        var skipped = 0;
        if (importDiagnostics)
        {
            foreach (var entry in entries.Where(entry => ShouldImportDiagnostic(entry.LogicalPath, prefix)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (imported.Count >= MaxDiagnosticImports)
                {
                    skipped++;
                    continue;
                }

                var kind = entry.LogicalPath.EndsWith(".mauitrace", StringComparison.OrdinalIgnoreCase)
                    ? ArtifactTrustImportKinds.Evidence
                    : ArtifactTrustImportKinds.FlowRun;
                using var input = entry.Open();
                var result = _imports.Import(input, kind, cancellationToken: cancellationToken);
                if (!result.Ok || result.Artifact is null ||
                    !string.Equals(result.Artifact.Verification.State, MauiArtifactTrustStates.Untrusted, StringComparison.Ordinal))
                {
                    skipped++;
                    continue;
                }
                imported.Add(result.Artifact);
                if (verifiedByPath.TryGetValue(entry.LogicalPath, out var item))
                    item.DiagnosticImported = true;
            }
        }

        return new AppleQaArtifactVerificationResult
        {
            Ok = true,
            Platform = parsed.Input.Platform,
            Experimental = parsed.Input.AppleQa.Experimental,
            OfficialCoverage = parsed.Input.AppleQa.OfficialCoverage,
            MacCatalystEquivalent = parsed.Input.AppleQa.MacCatalystEquivalent,
            EntryCount = entries.Count + directories.Count,
            TotalUncompressedBytes = entries.Sum(static entry => entry.Length),
            ManifestDigest = Convert.ToHexString(SHA256.HashData(manifestBytes)).ToLowerInvariant(),
            Executed = false,
            RawContentRetained = false,
            RepairProposalAuthority = false,
            VerifiedArtifacts = verified,
            ImportedDiagnostics = imported,
            SkippedDiagnosticImports = skipped,
        };
    }

    private static void CollectDirectoryEntries(
        string directory,
        string relativeDirectory,
        List<SourceEntry> entries,
        List<string> directories,
        ref long total,
        CancellationToken cancellationToken)
    {
        foreach (var path in Directory.EnumerateFileSystemEntries(directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            RejectReparsePoint(path);
            var name = Path.GetFileName(path);
            if (!IsSafePathSegment(name))
                throw new ArtifactVerificationException("Apple QA handoff directory contains an unsafe entry path.");
            var relative = string.IsNullOrEmpty(relativeDirectory) ? name : $"{relativeDirectory}/{name}";
            if (Directory.Exists(path))
            {
                if (entries.Count + directories.Count >= MaxEntryCount)
                    throw new ArtifactVerificationException("Apple QA handoff directory contains too many entries.");
                directories.Add(relative);
                CollectDirectoryEntries(path, relative, entries, directories, ref total, cancellationToken);
                continue;
            }

            if (entries.Count + directories.Count >= MaxEntryCount)
                throw new ArtifactVerificationException("Apple QA handoff directory contains too many entries.");
            var info = new FileInfo(path);
            ValidateEntryLimits(info.Length, info.Length, ref total);
            entries.Add(new SourceEntry(
                relativePath: relative,
                logicalPath: relative,
                length: info.Length,
                open: () => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)));
        }
    }

    private static SourceEntry FindManifest(IReadOnlyList<SourceEntry> entries, bool directDirectory)
    {
        var manifests = entries.Where(entry =>
                string.Equals(entry.LogicalPath, "manifest.json", StringComparison.OrdinalIgnoreCase) ||
                IsManifestLogicalPath(entry.LogicalPath))
            .ToList();
        if (manifests.Count != 1)
            throw new ArtifactVerificationException("Apple QA handoff must contain exactly one supported manifest.json entry.");
        if (!directDirectory && string.Equals(manifests[0].LogicalPath, "manifest.json", StringComparison.OrdinalIgnoreCase))
            throw new ArtifactVerificationException("Apple QA ZIP must preserve the documented artifacts/devflow return path.");
        return manifests[0];
    }

    private static ManifestIdentity ReadManifestIdentity(ReadOnlySpan<byte> manifest)
    {
        try
        {
            using var document = JsonDocument.Parse(manifest.ToArray(), new JsonDocumentOptions { MaxDepth = 64 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("platform", out var platform) ||
                platform.ValueKind != JsonValueKind.Object ||
                !platform.TryGetProperty("name", out var platformName) ||
                platformName.ValueKind != JsonValueKind.String ||
                !IsApplePlatform(platformName.GetString()) ||
                !root.TryGetProperty("workflow", out var workflow) ||
                workflow.ValueKind != JsonValueKind.Object ||
                !workflow.TryGetProperty("runId", out var runId) ||
                runId.ValueKind != JsonValueKind.String ||
                !IsSafePathSegment(runId.GetString() ?? string.Empty))
            {
                throw new ArtifactVerificationException("Apple QA handoff manifest has no supported platform/run identity.");
            }
            return new ManifestIdentity(platformName.GetString()!, runId.GetString()!);
        }
        catch (JsonException)
        {
            throw new ArtifactVerificationException("Apple QA handoff manifest is not valid JSON.");
        }
    }

    private static List<ManifestArtifact> ReadDeclaredArtifacts(ReadOnlySpan<byte> manifest)
    {
        try
        {
            using var document = JsonDocument.Parse(manifest.ToArray(), new JsonDocumentOptions { MaxDepth = 64 });
            var root = document.RootElement;
            if (!root.TryGetProperty("artifacts", out var artifacts) ||
                artifacts.ValueKind != JsonValueKind.Array ||
                artifacts.GetArrayLength() is 0 or > MaxEntryCount)
            {
                throw new ArtifactVerificationException("Apple QA handoff manifest has no supported artifact hash list.");
            }

            var result = new List<ManifestArtifact>();
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var artifact in artifacts.EnumerateArray())
            {
                if (artifact.ValueKind != JsonValueKind.Object ||
                    !TryGetString(artifact, "kind", out var kind) ||
                    !TryGetString(artifact, "path", out var path) ||
                    !TryGetString(artifact, "sha256", out var digest) ||
                    !artifact.TryGetProperty("sizeBytes", out var size) ||
                    size.ValueKind != JsonValueKind.Number ||
                    !size.TryGetInt64(out var bytes) ||
                    bytes < 0 ||
                    !IsSha256(digest) ||
                    !TryNormalizeArchivePath(path, out var normalized, out var directory) ||
                    directory ||
                    !paths.Add(normalized))
                {
                    throw new ArtifactVerificationException("Apple QA handoff manifest contains an invalid artifact hash entry.");
                }
                result.Add(new ManifestArtifact(kind, normalized, digest.ToLowerInvariant(), bytes));
            }
            return result;
        }
        catch (JsonException)
        {
            throw new ArtifactVerificationException("Apple QA handoff manifest is not valid JSON.");
        }
    }

    private static bool IsAllowedLogicalPath(string path, string prefix, string platform)
    {
        if (string.Equals(path, $"{prefix}/manifest.json", StringComparison.Ordinal))
            return true;
        if (path.StartsWith(prefix + "/", StringComparison.Ordinal))
        {
            var relative = path[(prefix.Length + 1)..];
            if (RootMetadataFiles.Contains(relative))
                return true;
            var parts = relative.Split('/');
            if (parts.Length is > 1 and <= 6 &&
                parts.All(IsSafePathSegment) &&
                (parts[0] is "host-diagnostics" or "apple-flow-runs" or "flow-runs" ||
                 parts[0].Contains("-attempt-", StringComparison.Ordinal)) &&
                (parts[^1] is "flow-run.json" or "apple-test-agent-run.json" ||
                 parts[^1].EndsWith(".mauitrace", StringComparison.OrdinalIgnoreCase) ||
                 (parts[0] == "host-diagnostics" &&
                  (parts[^1].EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                   parts[^1].EndsWith(".txt", StringComparison.OrdinalIgnoreCase)))))
            {
                return true;
            }
            return false;
        }

        var resultPrefix = $"artifacts/TestResults/devflow-flow/{platform}/";
        if (!path.StartsWith(resultPrefix, StringComparison.Ordinal))
            return false;
        var resultName = path[resultPrefix.Length..];
        return resultName.IndexOf('/') < 0 &&
            IsSafePathSegment(resultName) &&
            resultName.EndsWith(".trx", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAllowedDirectoryPath(string path, string prefix, string platform)
    {
        if (path is "artifacts" or "artifacts/devflow")
            return true;

        var prefixParts = prefix.Split('/');
        var current = string.Empty;
        foreach (var part in prefixParts)
        {
            current = current.Length == 0 ? part : $"{current}/{part}";
            if (string.Equals(path, current, StringComparison.Ordinal))
                return true;
        }

        if (path.StartsWith(prefix + "/", StringComparison.Ordinal))
        {
            var relative = path[(prefix.Length + 1)..];
            var parts = relative.Split('/');
            return parts.Length <= 5 &&
                parts.All(IsSafePathSegment) &&
                (parts[0] is "host-diagnostics" or "apple-flow-runs" or "flow-runs" ||
                 parts[0].Contains("-attempt-", StringComparison.Ordinal));
        }

        var resultPrefix = $"artifacts/TestResults/devflow-flow/{platform}";
        return path is "artifacts/TestResults" or "artifacts/TestResults/devflow-flow" ||
            string.Equals(path, resultPrefix, StringComparison.Ordinal);
    }

    private static bool ShouldImportDiagnostic(string path, string prefix)
    {
        if (!path.StartsWith(prefix + "/", StringComparison.Ordinal))
            return false;
        var relative = path[(prefix.Length + 1)..];
        return relative.EndsWith(".mauitrace", StringComparison.OrdinalIgnoreCase) ||
            (!string.Equals(relative, "flow-run.json", StringComparison.OrdinalIgnoreCase) &&
             relative.EndsWith("/flow-run.json", StringComparison.OrdinalIgnoreCase));
    }

    private static string HashEntry(SourceEntry entry, CancellationToken cancellationToken)
    {
        using var input = entry.Open();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[16 * 1024];
        var total = 0L;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = input.Read(buffer, 0, buffer.Length);
            if (read == 0)
                break;
            total += read;
            if (total > entry.Length || total > MaxEntryBytes)
                throw new ArtifactVerificationException("Apple QA handoff entry exceeds its declared size limit.");
            hash.AppendData(buffer, 0, read);
        }
        if (total != entry.Length)
            throw new ArtifactVerificationException("Apple QA handoff entry size changed during verification.");
        return "sha256:" + Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static byte[] ReadBounded(SourceEntry entry, int maximum, CancellationToken cancellationToken)
    {
        if (entry.Length > maximum)
            throw new ArtifactVerificationException("Apple QA handoff manifest exceeds the supported size limit.");
        using var input = entry.Open();
        using var output = new MemoryStream((int)Math.Min(entry.Length, 64 * 1024));
        var buffer = new byte[16 * 1024];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = input.Read(buffer, 0, buffer.Length);
            if (read == 0)
                break;
            if (output.Length + read > maximum)
                throw new ArtifactVerificationException("Apple QA handoff manifest exceeds the supported size limit.");
            output.Write(buffer, 0, read);
        }
        if (output.Length != entry.Length)
            throw new ArtifactVerificationException("Apple QA handoff manifest size changed during verification.");
        return output.ToArray();
    }

    private static string StrictUtf8(ReadOnlySpan<byte> bytes)
    {
        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw new ArtifactVerificationException("Apple QA handoff manifest is not valid UTF-8 JSON.");
        }
    }

    private static void ValidateEntryLimits(long length, long compressedLength, ref long total)
    {
        if (length < 0 || compressedLength < 0 || length > MaxEntryBytes)
            throw new ArtifactVerificationException("Apple QA handoff contains an oversized entry.");
        if (length > 0 && (compressedLength == 0 || length > compressedLength * MaxCompressionRatio))
            throw new ArtifactVerificationException("Apple QA handoff archive exceeds the supported decompression ratio.");
        if (total > MaxTotalUncompressedBytes - length)
            throw new ArtifactVerificationException("Apple QA handoff archive exceeds the supported decompressed size limit.");
        total += length;
    }

    private static bool TryNormalizeArchivePath(string value, out string normalized, out bool directory)
    {
        normalized = string.Empty;
        directory = false;
        if (string.IsNullOrWhiteSpace(value) ||
            value.StartsWith("/", StringComparison.Ordinal) ||
            value.Contains('\\') ||
            value.Contains(':') ||
            value.IndexOf('\0') >= 0)
        {
            return false;
        }

        directory = value.EndsWith("/", StringComparison.Ordinal);
        var candidate = directory ? value[..^1] : value;
        if (candidate.Length == 0)
            return false;
        var parts = candidate.Split('/');
        if (parts.Length == 0 || parts.Any(part => !IsSafePathSegment(part)))
            return false;
        normalized = candidate;
        return true;
    }

    private static bool IsManifestLogicalPath(string path)
    {
        var parts = path.Split('/');
        return parts.Length == 5 &&
            parts[0] == "artifacts" &&
            parts[1] == "devflow" &&
            IsSafePathSegment(parts[2]) &&
            IsApplePlatform(parts[3]) &&
            parts[4] == "manifest.json";
    }

    private static string ManifestPrefix(string manifestPath, string platform)
    {
        var suffix = $"/{platform}/manifest.json";
        if (!manifestPath.EndsWith(suffix, StringComparison.Ordinal))
            throw new ArtifactVerificationException("Apple QA handoff manifest path does not match its platform.");
        return manifestPath[..^"/manifest.json".Length];
    }

    private static bool IsSafePathSegment(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 128 &&
        value.All(character =>
            (character >= 'a' && character <= 'z') ||
            (character >= 'A' && character <= 'Z') ||
            (character >= '0' && character <= '9') ||
            character == '.' ||
            character == '_' ||
            character == '-');

    private static bool IsApplePlatform(string? value) => value is "ios" or "maccatalyst" or "macos";

    private static bool IsSha256(string value) =>
        value.Length == 71 &&
        value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) &&
        value.AsSpan(7).ToString().All(Uri.IsHexDigit);

    private static bool TryGetString(JsonElement element, string name, out string value)
    {
        value = string.Empty;
        return element.TryGetProperty(name, out var property) &&
            property.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(value = property.GetString() ?? string.Empty);
    }

    private static bool IsSymlink(ZipArchiveEntry entry)
    {
        const int FileTypeMask = 0xF000;
        const int Symlink = 0xA000;
        var unixType = (entry.ExternalAttributes >> 16) & FileTypeMask;
        return unixType == Symlink ||
            (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0;
    }

    private static void RejectReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new ArtifactVerificationException("Apple QA handoff paths may not contain symbolic links or reparse points.");
    }

    private static void RejectReparsePath(string path)
    {
        if (Directory.Exists(path))
        {
            for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
                RejectReparsePoint(directory.FullName);
            return;
        }

        RejectReparsePoint(path);
        for (var directory = new FileInfo(path).Directory; directory is not null; directory = directory.Parent)
            RejectReparsePoint(directory.FullName);
    }

    private sealed class SourceEntry(
        string relativePath,
        string logicalPath,
        long length,
        Func<Stream> open)
    {
        public string RelativePath { get; } = relativePath;
        public string LogicalPath { get; set; } = logicalPath;
        public long Length { get; } = length;
        public Func<Stream> Open { get; } = open;
    }

    private sealed record ManifestIdentity(string Platform, string RunId);

    private sealed record ManifestArtifact(string Kind, string Path, string Digest, long SizeBytes);

    private sealed class ArtifactVerificationException(string message) : Exception(message);
}
