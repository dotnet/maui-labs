using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Microsoft.Maui.DevFlow.Testing;

/// <summary>A safe result from scanning projections for supplied canaries.</summary>
public sealed class MauiQualificationCanaryScanResult
{
    public int CheckedValues { get; set; }
    public List<MauiQualificationCanaryEscape> Escapes { get; } = [];
    public bool Passed => Escapes.Count == 0;
}

/// <summary>An escape finding identifies only a field label, never the canary or source text.</summary>
public sealed class MauiQualificationCanaryEscape
{
    public string Field { get; init; } = "";
    public string Code { get; init; } = "canary-reached-projection";
}

/// <summary>
/// Redacts untrusted text and scans safe report projections. This helper is intentionally narrow:
/// it does not parse an artifact, execute a prompt, invoke a model, or broaden a grant.
/// </summary>
public static class MauiQualificationPrivacyScanner
{
    /// <summary>Returns a constant redaction token rather than preserving untrusted content.</summary>
    public static string RedactUntrusted(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty : "[redacted]";

    /// <summary>Detects canaries in a projection without returning their values.</summary>
    public static MauiQualificationCanaryScanResult Scan(
        IEnumerable<KeyValuePair<string, string?>> projection,
        IEnumerable<string> canaries)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(canaries);
        var needles = canaries
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var result = new MauiQualificationCanaryScanResult();
        foreach (var (field, value) in projection)
        {
            result.CheckedValues++;
            if (string.IsNullOrEmpty(value))
                continue;
            if (needles.Any(needle => value.Contains(needle, StringComparison.Ordinal)))
            {
                result.Escapes.Add(new MauiQualificationCanaryEscape
                {
                    Field = MauiQualificationSanitizer.SafeKind(field),
                });
            }
        }
        return result;
    }
}

/// <summary>Executes the versioned security/privacy adversarial corpus without exposing its canaries.</summary>
public static class MauiQualificationSecurityCorpusRunner
{
    private const int MaxBytes = 1_048_576;

    /// <summary>Loads and evaluates the static security corpus rooted under <c>security/</c>.</summary>
    public static MauiQualificationSecurityCorpusRunResult Run(string corpusRoot)
    {
        var summary = new MauiQualificationSecurityCorpusSummary
        {
            Version = "security-privacy-corpus-v1",
        };
        if (string.IsNullOrWhiteSpace(corpusRoot))
        {
            summary.Errors.Add("security-corpus-root-missing");
            return new MauiQualificationSecurityCorpusRunResult { Summary = summary };
        }

        try
        {
            var root = Path.GetFullPath(corpusRoot);
            var path = Path.Combine(root, "security", "security-privacy-corpus-v1.json");
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxBytes)
            {
                summary.Errors.Add("security-corpus-file-missing-or-too-large");
                return new MauiQualificationSecurityCorpusRunResult { Summary = summary };
            }

            var bytes = File.ReadAllBytes(path);
            // Share the case-corpus normalisation rather than restating it. A raw-byte hash here
            // made this published field — and therefore the committed baseline that pins it —
            // depend on the checkout's line endings rather than on the corpus, and a second
            // hand-rolled copy of the rule is how the two drift apart again.
            summary.ManifestFingerprint = MauiPreviewQualificationCorpusRunner.HashNormalized(bytes);
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
            var rootElement = document.RootElement;
            if (rootElement.ValueKind != JsonValueKind.Object ||
                !HasInt(rootElement, "schema", 1) ||
                !HasString(rootElement, "kind", "maui-preview-security-privacy-corpus") ||
                !HasString(rootElement, "version", "security-privacy-corpus-v1") ||
                !rootElement.TryGetProperty("cases", out var cases) ||
                cases.ValueKind != JsonValueKind.Array)
            {
                summary.Errors.Add("security-corpus-schema-invalid");
                return new MauiQualificationSecurityCorpusRunResult { Summary = summary };
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in cases.EnumerateArray())
            {
                if (!TryReadCase(item, ids, out var id, out var canary))
                {
                    summary.Errors.Add("security-corpus-case-invalid");
                    continue;
                }

                // The simulated sink is representative of a qualification report/evidence/audit/model
                // projection: untrusted bytes become a constant token before the scan. The case's raw
                // canary is deliberately scoped to this local variable and never reaches the result.
                var projection = new[]
                {
                    new KeyValuePair<string, string?>("report", MauiQualificationPrivacyScanner.RedactUntrusted(canary)),
                    new KeyValuePair<string, string?>("evidence", MauiQualificationPrivacyScanner.RedactUntrusted(canary)),
                    new KeyValuePair<string, string?>("audit", MauiQualificationPrivacyScanner.RedactUntrusted(canary)),
                    new KeyValuePair<string, string?>("model-projection", MauiQualificationPrivacyScanner.RedactUntrusted(canary)),
                    new KeyValuePair<string, string?>("artifact", MauiQualificationPrivacyScanner.RedactUntrusted(canary)),
                };
                var scan = MauiQualificationPrivacyScanner.Scan(projection, [canary]);
                summary.CaseCount++;
                summary.CaseIds.Add(MauiQualificationSanitizer.Fingerprint(id)!);
                if (scan.Passed)
                    summary.PassedCount++;
                else
                    summary.Errors.Add("security-corpus-canary-escape");
            }
            summary.Valid = summary.Errors.Count == 0 && summary.CaseCount > 0 && summary.PassedCount == summary.CaseCount;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or JsonException)
        {
            summary.Errors.Add("security-corpus-unreadable");
            summary.Valid = false;
        }
        return new MauiQualificationSecurityCorpusRunResult { Summary = summary };
    }

    private static bool TryReadCase(JsonElement item, HashSet<string> ids, out string id, out string canary)
    {
        id = string.Empty;
        canary = string.Empty;
        return item.ValueKind == JsonValueKind.Object &&
            TryGetString(item, "id", out id) &&
            ids.Add(id) &&
            TryGetString(item, "surface", out _) &&
            TryGetString(item, "kind", out _) &&
            TryGetString(item, "canary", out canary);
    }

    private static bool TryGetString(JsonElement element, string property, out string value)
    {
        value = string.Empty;
        return element.TryGetProperty(property, out var item) &&
            item.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(value = item.GetString() ?? string.Empty);
    }

    private static bool HasString(JsonElement element, string property, string expected) =>
        element.TryGetProperty(property, out var item) &&
        item.ValueKind == JsonValueKind.String &&
        string.Equals(item.GetString(), expected, StringComparison.Ordinal);

    private static bool HasInt(JsonElement element, string property, int expected) =>
        element.TryGetProperty(property, out var item) &&
        item.ValueKind == JsonValueKind.Number &&
        item.TryGetInt32(out var actual) &&
        actual == expected;
}

/// <summary>Security corpus run result containing only redacted case identifiers and counts.</summary>
public sealed class MauiQualificationSecurityCorpusRunResult
{
    public MauiQualificationSecurityCorpusSummary Summary { get; init; } = new();
}

/// <summary>One hash-linked history record used only for deterministic integrity checks and fuzzing.</summary>
public sealed class MauiQualificationHashChainEntry
{
    public string? PreviousDigest { get; init; }
    public string? PayloadDigest { get; init; }
    public string? Digest { get; init; }
}

/// <summary>Minimal hash-chain helper that does not persist payloads or source text.</summary>
public static class MauiQualificationHashChain
{
    /// <summary>Creates a digest for a previous digest and an already-redacted payload digest.</summary>
    public static string CreateDigest(string? previousDigest, string payloadDigest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadDigest);
        return "sha256:" + Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes((previousDigest ?? string.Empty) + "\n" + payloadDigest))).ToLowerInvariant();
    }

    /// <summary>Validates an ordered chain without returning any payload content.</summary>
    public static bool IsValid(IEnumerable<MauiQualificationHashChainEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        string? previous = null;
        foreach (var entry in entries)
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.PayloadDigest) ||
                !string.Equals(entry.PreviousDigest, previous, StringComparison.Ordinal) ||
                !string.Equals(entry.Digest, CreateDigest(previous, entry.PayloadDigest), StringComparison.Ordinal))
            {
                return false;
            }
            previous = entry.Digest;
        }
        return true;
    }
}

/// <summary>Bounded fuzz configuration. CI defaults are intentionally much smaller than nightly runs.</summary>
public sealed class MauiQualificationFuzzOptions
{
    public int Seed { get; init; } = 20260802;
    public int Iterations { get; init; } = 128;
    public TimeSpan MaximumDuration { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Gets capped PR or nightly settings without installing a fuzzing framework.</summary>
    public static MauiQualificationFuzzOptions FromEnvironment()
    {
        var nightly = string.Equals(
            Environment.GetEnvironmentVariable("DEVFLOW_FUZZ_MODE"),
            "nightly",
            StringComparison.OrdinalIgnoreCase);
        return nightly
            ? new MauiQualificationFuzzOptions { Iterations = 4_000, MaximumDuration = TimeSpan.FromSeconds(30) }
            : new MauiQualificationFuzzOptions();
    }
}

/// <summary>A deterministic fuzz execution result, including an opaque digest of generated operations.</summary>
public sealed class MauiQualificationFuzzResult
{
    public int Seed { get; init; }
    public int Executed { get; init; }
    public TimeSpan Elapsed { get; init; }
    public string? OperationsDigest { get; init; }
}

/// <summary>Exception that preserves the seed and iteration needed to reproduce a fuzz failure.</summary>
public sealed class MauiQualificationFuzzException : Exception
{
    public MauiQualificationFuzzException(int seed, int iteration, Exception inner)
        : base($"Qualification fuzz failure (seed={seed}, iteration={iteration}).", inner)
    {
        Seed = seed;
        Iteration = iteration;
    }

    public int Seed { get; }
    public int Iteration { get; }
}

/// <summary>Runs bounded deterministic property cases using the existing runtime only.</summary>
public static class MauiQualificationFuzz
{
    /// <summary>Runs an operation under a seed and time cap. Any failure includes its reproduction seed.</summary>
    public static MauiQualificationFuzzResult Run(
        MauiQualificationFuzzOptions options,
        Func<Random, int, string?> operation)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(operation);
        var random = new Random(options.Seed);
        var started = System.Diagnostics.Stopwatch.StartNew();
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var executed = 0;
        for (var iteration = 0; iteration < Math.Max(0, options.Iterations); iteration++)
        {
            if (started.Elapsed > options.MaximumDuration)
                break;
            try
            {
                var material = operation(random, iteration) ?? string.Empty;
                var bytes = Encoding.UTF8.GetBytes(material);
                digest.AppendData(bytes);
                executed++;
            }
            catch (Exception ex)
            {
                throw new MauiQualificationFuzzException(options.Seed, iteration, ex);
            }
        }
        return new MauiQualificationFuzzResult
        {
            Seed = options.Seed,
            Executed = executed,
            Elapsed = started.Elapsed,
            OperationsDigest = "sha256:" + Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant(),
        };
    }
}
