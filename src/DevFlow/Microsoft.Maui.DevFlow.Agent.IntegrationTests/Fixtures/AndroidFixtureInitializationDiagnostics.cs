using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;

/// <summary>Writes the safe, bounded failure record required when Android fixture startup stops before replay.</summary>
internal static class AndroidFixtureInitializationDiagnostics
{
    internal const int MaxExceptionChainEntries = 8;
    internal const int MaxSafeErrorTextCharacters = 512;

    public static AndroidFixtureInitializationDiagnosticWriteResult Write(
        string artifactRoot,
        Exception exception,
        string lifecyclePhase = "android-fixture-initialization")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactRoot);
        ArgumentNullException.ThrowIfNull(exception);

        try
        {
            var root = Path.GetFullPath(artifactRoot);
            var directory = Path.Combine(root, "host-diagnostics");
            Directory.CreateDirectory(directory);

            var document = CreateDocument(exception, lifecyclePhase);
            var path = Path.Combine(directory, "fixture-initialization.json");
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions));
                File.Move(temporary, path, overwrite: true);
            }
            finally
            {
                try { File.Delete(temporary); } catch { }
            }

            return new AndroidFixtureInitializationDiagnosticWriteResult
            {
                Artifact = new MauiFlowArtifactReference
                {
                    ArtifactId = "android-fixture-initialization-diagnostic",
                    Kind = "fixture-initialization-diagnostic",
                    Path = path,
                    Digest = ComputeFileFingerprint(path),
                    MediaType = "application/json",
                    Redacted = true,
                    CreatedAt = DateTimeOffset.UtcNow,
                },
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new AndroidFixtureInitializationDiagnosticWriteResult
            {
                Error = "The bounded fixture initialization diagnostic could not be written.",
            };
        }
    }

    static AndroidFixtureInitializationDiagnosticDocument CreateDocument(
        Exception exception,
        string lifecyclePhase)
    {
        var document = new AndroidFixtureInitializationDiagnosticDocument
        {
            LifecyclePhase = AndroidLifecycleDiagnosticRedactor.SafePhase(lifecyclePhase),
        };
        var current = exception;
        while (current is not null && document.ExceptionChain.Count < MaxExceptionChainEntries)
        {
            var details = GetDetails(current);
            document.ExceptionChain.Add(new AndroidFixtureInitializationDiagnosticEntry
            {
                ExceptionType = GetSafeExceptionType(current),
                LifecycleFailureKind = (current as PlatformFlowLifecycleException)?.Kind.ToString().ToLowerInvariant(),
                LifecyclePhase = AndroidLifecycleDiagnosticRedactor.SafePhase(
                    details?.LifecyclePhase ?? lifecyclePhase),
                ActionName = AndroidLifecycleDiagnosticRedactor.Sanitize(
                    details?.ActionName ?? "not-observed",
                    128),
                AdbCommandCategory = AndroidLifecycleDiagnosticRedactor.SafeCategory(
                    details?.AdbCommandCategory),
                ExitCode = details?.ExitCode,
                TimeoutSeconds = details?.TimeoutSeconds,
                TimedOut = details?.TimedOut ?? current is TimeoutException,
                CancellationRequested = details?.CancellationRequested ??
                    current is OperationCanceledException,
                SafeErrorText = AndroidLifecycleDiagnosticRedactor.Sanitize(
                    details?.SafeErrorText ?? current.Message,
                    MaxSafeErrorTextCharacters),
            });
            current = current.InnerException;
        }

        document.Truncated = current is not null;
        return document;
    }

    static PlatformFlowLifecycleFailureDetails? GetDetails(Exception exception) => exception switch
    {
        PlatformFlowLifecycleException lifecycle => lifecycle.Details,
        PlatformAdbCommandException adb => adb.Details,
        _ => null,
    };

    static string GetSafeExceptionType(Exception exception) => exception switch
    {
        PlatformFlowLifecycleException => "platform-flow-lifecycle",
        PlatformAdbCommandException => "adb-command",
        PlatformProcessTimeoutException => "process-timeout",
        OperationCanceledException => "operation-canceled",
        TimeoutException => "timeout",
        _ => "exception",
    };

    static string ComputeFileFingerprint(string path)
    {
        using var stream = File.OpenRead(path);
        var hash = SHA256.HashData(stream);
        return $"sha256:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };
}

internal sealed class AndroidFixtureInitializationDiagnosticWriteResult
{
    public MauiFlowArtifactReference? Artifact { get; init; }
    public string? Error { get; init; }
    public bool Ok => Artifact is not null;
}

internal sealed class AndroidFixtureInitializationDiagnosticDocument
{
    [JsonPropertyName("schema")]
    public int Schema { get; } = 1;

    [JsonPropertyName("kind")]
    public string Kind { get; } = "android-fixture-initialization";

    [JsonPropertyName("generatedAt")]
    public DateTimeOffset GeneratedAt { get; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("lifecyclePhase")]
    public string LifecyclePhase { get; init; } = "android-fixture-initialization";

    [JsonPropertyName("classification")]
    public string Classification { get; } = "infrastructure";

    [JsonPropertyName("exceptionChain")]
    public List<AndroidFixtureInitializationDiagnosticEntry> ExceptionChain { get; } = [];

    [JsonPropertyName("truncated")]
    public bool Truncated { get; set; }
}

internal sealed class AndroidFixtureInitializationDiagnosticEntry
{
    [JsonPropertyName("exceptionType")]
    public string ExceptionType { get; init; } = "exception";

    [JsonPropertyName("lifecycleFailureKind")]
    public string? LifecycleFailureKind { get; init; }

    [JsonPropertyName("lifecyclePhase")]
    public string LifecyclePhase { get; init; } = "android-fixture-initialization";

    [JsonPropertyName("actionName")]
    public string ActionName { get; init; } = "not-observed";

    [JsonPropertyName("adbCommandCategory")]
    public string AdbCommandCategory { get; init; } = "not-observed";

    [JsonPropertyName("exitCode")]
    public int? ExitCode { get; init; }

    [JsonPropertyName("timeoutSeconds")]
    public int? TimeoutSeconds { get; init; }

    [JsonPropertyName("timedOut")]
    public bool TimedOut { get; init; }

    [JsonPropertyName("cancellationRequested")]
    public bool CancellationRequested { get; init; }

    [JsonPropertyName("safeErrorText")]
    public string SafeErrorText { get; init; } = "No safe error text was recorded.";
}

/// <summary>Central redaction for Android lifecycle diagnostics and messages that enter artifacts.</summary>
internal static class AndroidLifecycleDiagnosticRedactor
{
    const string Redacted = "[REDACTED]";
    const string Truncated = " [truncated]";

    static readonly Regex SensitiveAssignment = new(
        @"(?i)\b(token|password|passwd|secret|authorization|api[_-]?key|access[_-]?key|client[_-]?secret)\b\s*[:=]\s*(?:bearer\s+)?[^\s,;]+",
        RegexOptions.CultureInvariant);
    static readonly Regex BearerToken = new(
        @"(?i)\bbearer\s+[A-Za-z0-9._~+/=-]+",
        RegexOptions.CultureInvariant);
    static readonly Regex AdbSerialArgument = new(
        @"(?i)(-s\s+)(?:""[^""]+""|'[^']+'|\S+)",
        RegexOptions.CultureInvariant);
    static readonly Regex LabeledSerial = new(
        @"(?i)\b(serial|device(?:\s+serial)?)\s*(?:[:=]|\bis\b)\s*['""]?[A-Za-z0-9._:-]+",
        RegexOptions.CultureInvariant);
    static readonly Regex EmulatorSerial = new(
        @"(?i)\bemulator-\d+\b",
        RegexOptions.CultureInvariant);
    static readonly Regex HostPath = new(
        @"(?i)(?:[A-Z]:\\Users\\|/Users/|/home/)[^\s,;]+",
        RegexOptions.CultureInvariant);
    static readonly Regex EnvironmentAssignment = new(
        @"(?i)\b(ANDROID_HOME|ANDROID_SDK_ROOT|PATH|HOME|USERPROFILE)\s*=\s*[^\s,;]+",
        RegexOptions.CultureInvariant);
    static readonly Regex Whitespace = new(@"\s+", RegexOptions.CultureInvariant);
    static readonly Regex UnsafeCategory = new(@"[^a-z0-9-]", RegexOptions.CultureInvariant);

    public static string Sanitize(string? value, int maximumCharacters, params string?[] sensitiveValues)
    {
        var result = value ?? string.Empty;
        foreach (var sensitiveValue in sensitiveValues)
        {
            if (!string.IsNullOrWhiteSpace(sensitiveValue))
            {
                result = result.Replace(
                    sensitiveValue,
                    Redacted,
                    StringComparison.OrdinalIgnoreCase);
            }
        }

        result = SensitiveAssignment.Replace(result, "$1=" + Redacted);
        result = BearerToken.Replace(result, "Bearer " + Redacted);
        result = AdbSerialArgument.Replace(result, "$1" + Redacted);
        result = LabeledSerial.Replace(result, "$1=" + Redacted);
        result = EmulatorSerial.Replace(result, Redacted);
        result = HostPath.Replace(result, Redacted);
        result = EnvironmentAssignment.Replace(result, "$1=" + Redacted);
        result = Whitespace.Replace(result, " ").Trim();
        if (string.IsNullOrWhiteSpace(result))
            result = "No safe error text was recorded.";

        var maximum = Math.Max(Truncated.Length + 1, maximumCharacters);
        return result.Length <= maximum
            ? result
            : result[..(maximum - Truncated.Length)] + Truncated;
    }

    public static string SafeCategory(string? value)
    {
        var category = value?.Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(category) || category.Length > 64 || UnsafeCategory.IsMatch(category)
            ? "not-observed"
            : category;
    }

    public static string SafePhase(string? value)
    {
        var phase = value?.Trim().ToLowerInvariant();
        return phase is "android-fixture-initialization" or "android-device-lifecycle"
            ? phase
            : "android-fixture-initialization";
    }

    public static string Fingerprint(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return $"sha256:{Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()}";
    }
}
