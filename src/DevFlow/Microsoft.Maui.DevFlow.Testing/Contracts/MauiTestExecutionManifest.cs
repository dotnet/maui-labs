using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Maui.DevFlow.Testing;

/// <summary>
/// Provider-neutral host, build, artifact, device, and lifecycle facts for one test occurrence.
/// Values are bounded identities or digests; the contract has no raw-log, prompt, secret, or
/// device-serial fields.
/// </summary>
public sealed class MauiTestExecutionManifest
{
    [JsonPropertyName("schema")] public int Schema { get; set; } = 1;
    [JsonPropertyName("manifestId")] public string? ManifestId { get; set; }
    [JsonPropertyName("runId")] public string? RunId { get; set; }
    [JsonPropertyName("flowId")] public string? FlowId { get; set; }
    [JsonPropertyName("flowRevision")] public int? FlowRevision { get; set; }
    [JsonPropertyName("flowDigest")] public string? FlowDigest { get; set; }
    [JsonPropertyName("fingerprintVersion")] public string FingerprintVersion { get; set; } = MauiFlowIncidentFingerprint.RuleVersion;
    [JsonPropertyName("testIdentityFingerprint")] public string? TestIdentityFingerprint { get; set; }
    [JsonPropertyName("incidentFingerprint")] public string? IncidentFingerprint { get; set; }
    [JsonPropertyName("occurrenceFingerprint")] public string? OccurrenceFingerprint { get; set; }
    [JsonPropertyName("host")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MauiTestExecutionHostFacts? Host { get; set; }
    [JsonPropertyName("build")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MauiTestExecutionBuildFacts? Build { get; set; }
    [JsonPropertyName("device")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MauiTestExecutionDeviceFacts? Device { get; set; }
    [JsonPropertyName("lifecycle")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MauiTestExecutionLifecycleFacts? Lifecycle { get; set; }
    [JsonPropertyName("outcome")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MauiTestExecutionOutcomeFacts? Outcome { get; set; }
    [JsonPropertyName("artifacts")] public List<MauiTestExecutionArtifact> Artifacts { get; set; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Redacted execution-host facts without machine name, user name, or filesystem paths.</summary>
public sealed class MauiTestExecutionHostFacts
{
    [JsonPropertyName("os")] public string? Os { get; set; }
    [JsonPropertyName("architecture")] public string? Architecture { get; set; }
    [JsonPropertyName("runtime")] public string? Runtime { get; set; }
    [JsonPropertyName("runnerKind")] public string? RunnerKind { get; set; }
    [JsonPropertyName("environmentFingerprint")] public string? EnvironmentFingerprint { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Build identity facts. Project and repository values are fingerprints, never paths.</summary>
public sealed class MauiTestExecutionBuildFacts
{
    [JsonPropertyName("repositoryFingerprint")] public string? RepositoryFingerprint { get; set; }
    [JsonPropertyName("projectFingerprint")] public string? ProjectFingerprint { get; set; }
    [JsonPropertyName("sourceRevision")] public string? SourceRevision { get; set; }
    [JsonPropertyName("targetFramework")] public string? TargetFramework { get; set; }
    [JsonPropertyName("configuration")] public string? Configuration { get; set; }
    [JsonPropertyName("appId")] public string? AppId { get; set; }
    [JsonPropertyName("appBuildFingerprint")] public string? AppBuildFingerprint { get; set; }
    [JsonPropertyName("appSourceFingerprint")] public string? AppSourceFingerprint { get; set; }
    [JsonPropertyName("packageDigest")] public string? PackageDigest { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Device profile facts with a one-way identity and no serial-number field.</summary>
public sealed class MauiTestExecutionDeviceFacts
{
    [JsonPropertyName("platform")] public string? Platform { get; set; }
    [JsonPropertyName("runtimeKind")] public string? RuntimeKind { get; set; }
    [JsonPropertyName("profile")] public string? Profile { get; set; }
    [JsonPropertyName("architecture")] public string? Architecture { get; set; }
    [JsonPropertyName("apiLevel")] public string? ApiLevel { get; set; }
    [JsonPropertyName("osVersion")] public string? OsVersion { get; set; }
    [JsonPropertyName("formFactor")] public string? FormFactor { get; set; }
    [JsonPropertyName("virtual")] public bool? Virtual { get; set; }
    [JsonPropertyName("deviceIdentityFingerprint")] public string? DeviceIdentityFingerprint { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Owned lifecycle state for build, deployment, launch, replay, and cleanup.</summary>
public sealed class MauiTestExecutionLifecycleFacts
{
    [JsonPropertyName("startedAt")] public DateTimeOffset? StartedAt { get; set; }
    [JsonPropertyName("endedAt")] public DateTimeOffset? EndedAt { get; set; }
    [JsonPropertyName("cleanupPolicy")] public string? CleanupPolicy { get; set; }
    [JsonPropertyName("installedByInvocation")] public bool? InstalledByInvocation { get; set; }
    [JsonPropertyName("launchedByInvocation")] public bool? LaunchedByInvocation { get; set; }
    [JsonPropertyName("cleanupCompleted")] public bool? CleanupCompleted { get; set; }
    [JsonPropertyName("stages")] public List<MauiTestExecutionLifecycleStage> Stages { get; set; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>One value-free lifecycle stage.</summary>
public sealed class MauiTestExecutionLifecycleStage
{
    [JsonPropertyName("sequence")] public int? Sequence { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("detailCode")] public string? DetailCode { get; set; }
    [JsonPropertyName("durationMs")] public long? DurationMs { get; set; }
    [JsonPropertyName("ownedResource")] public bool? OwnedResource { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>A bounded artifact identity. RelativePath must remain below the artifact root.</summary>
public sealed class MauiTestExecutionArtifact
{
    [JsonPropertyName("artifactId")] public string? ArtifactId { get; set; }
    [JsonPropertyName("role")] public string? Role { get; set; }
    [JsonPropertyName("kind")] public string? Kind { get; set; }
    [JsonPropertyName("mediaType")] public string? MediaType { get; set; }
    [JsonPropertyName("digest")] public string? Digest { get; set; }
    [JsonPropertyName("sizeBytes")] public long? SizeBytes { get; set; }
    [JsonPropertyName("relativePath")] public string? RelativePath { get; set; }
    [JsonPropertyName("redacted")] public bool? Redacted { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Terminal execution state without console or exception text.</summary>
public sealed class MauiTestExecutionOutcomeFacts
{
    /// <summary>The preserved primary run status, for example <c>passed</c> or <c>failed</c>.</summary>
    [JsonPropertyName("status")] public string? Status { get; set; }

    /// <summary>
    /// The category the whole command exited with. It is not a projection of
    /// <see cref="Status"/>: an owned cleanup failure after a passing run keeps
    /// <c>status: passed</c> and reports <c>infrastructure-failure</c> here, and
    /// <see cref="SecondaryFailures"/> says why.
    /// </summary>
    [JsonPropertyName("exitCategory")] public string? ExitCategory { get; set; }
    [JsonPropertyName("terminal")] public bool? Terminal { get; set; }
    [JsonPropertyName("verified")] public bool? Verified { get; set; }
    [JsonPropertyName("unknownCompletion")] public bool? UnknownCompletion { get; set; }

    /// <summary>Mirrors <see cref="MauiFlowRunReport.SecondaryFailures"/> for the same occurrence.</summary>
    [JsonPropertyName("secondaryFailures")] public List<MauiFlowSecondaryFailure> SecondaryFailures { get; set; } = [];

    [JsonExtensionData] public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Creates deterministic, redacted execution-manifest JSON.</summary>
public static class MauiTestExecutionManifestSerializer
{
    public static MauiTestExecutionManifest CreateSafeProjection(MauiTestExecutionManifest? manifest)
    {
        manifest ??= new MauiTestExecutionManifest();
        return new MauiTestExecutionManifest
        {
            Schema = 1,
            ManifestId = MauiTestingContractRedactor.SafeIdentifierOrFingerprint(
                manifest.ManifestId,
                "manifest-id"),
            RunId = MauiTestingContractRedactor.SafeIdentifierOrFingerprint(
                manifest.RunId,
                "run-id"),
            FlowId = MauiTestingContractRedactor.SafeIdentifierOrFingerprint(
                manifest.FlowId,
                "flow-id"),
            FlowRevision = manifest.FlowRevision is > 0 ? manifest.FlowRevision : null,
            FlowDigest = MauiTestingContractRedactor.Fingerprint(manifest.FlowDigest, "flow"),
            FingerprintVersion = MauiFlowIncidentFingerprint.RuleVersion,
            TestIdentityFingerprint = MauiTestingContractRedactor.Fingerprint(
                manifest.TestIdentityFingerprint,
                "test"),
            IncidentFingerprint = MauiTestingContractRedactor.Fingerprint(
                manifest.IncidentFingerprint,
                "incident"),
            OccurrenceFingerprint = MauiTestingContractRedactor.Fingerprint(
                manifest.OccurrenceFingerprint,
                "occurrence"),
            Host = Sanitize(manifest.Host),
            Build = Sanitize(manifest.Build),
            Device = Sanitize(manifest.Device),
            Lifecycle = Sanitize(manifest.Lifecycle),
            Outcome = Sanitize(manifest.Outcome),
            Artifacts = (manifest.Artifacts ?? [])
                .Where(static artifact => artifact is not null)
                .Take(128)
                .Select(Sanitize)
                .OrderBy(static artifact => artifact.ArtifactId, StringComparer.Ordinal)
                .ThenBy(static artifact => artifact.Kind, StringComparer.Ordinal)
                .ThenBy(static artifact => artifact.RelativePath, StringComparer.Ordinal)
                .ToList(),
        };
    }

    public static byte[] SerializeToUtf8Bytes(MauiTestExecutionManifest? manifest)
        => JsonSerializer.SerializeToUtf8Bytes(
            CreateSafeProjection(manifest),
            MauiTestingJsonContext.Default.MauiTestExecutionManifest);

    private static MauiTestExecutionHostFacts? Sanitize(MauiTestExecutionHostFacts? source)
    {
        if (source is null)
            return null;
        var result = new MauiTestExecutionHostFacts
        {
            Os = MauiTestingContractRedactor.SafeCode(source.Os),
            Architecture = MauiTestingContractRedactor.SafeCode(source.Architecture),
            Runtime = MauiTestingContractRedactor.SafeCode(source.Runtime),
            RunnerKind = MauiTestingContractRedactor.SafeCode(source.RunnerKind),
            EnvironmentFingerprint = MauiTestingContractRedactor.Fingerprint(
                source.EnvironmentFingerprint,
                "host-environment"),
        };
        return result.Os is null &&
               result.Architecture is null &&
               result.Runtime is null &&
               result.RunnerKind is null &&
               result.EnvironmentFingerprint is null
            ? null
            : result;
    }

    private static MauiTestExecutionBuildFacts? Sanitize(MauiTestExecutionBuildFacts? source)
    {
        if (source is null)
            return null;
        var result = new MauiTestExecutionBuildFacts
        {
            RepositoryFingerprint = MauiTestingContractRedactor.Fingerprint(
                source.RepositoryFingerprint,
                "repository"),
            ProjectFingerprint = MauiTestingContractRedactor.Fingerprint(
                source.ProjectFingerprint,
                "project"),
            SourceRevision = MauiTestingContractRedactor.SafeIdentifierOrFingerprint(
                source.SourceRevision,
                "source-revision"),
            TargetFramework = MauiTestingContractRedactor.SafeCode(source.TargetFramework),
            Configuration = MauiTestingContractRedactor.SafeCode(source.Configuration),
            AppId = MauiTestingContractRedactor.SafeIdentifierOrFingerprint(source.AppId, "app-id"),
            AppBuildFingerprint = MauiTestingContractRedactor.Fingerprint(
                source.AppBuildFingerprint,
                "app-build"),
            AppSourceFingerprint = MauiTestingContractRedactor.Fingerprint(
                source.AppSourceFingerprint,
                "app-source"),
            PackageDigest = MauiTestingContractRedactor.Fingerprint(source.PackageDigest, "package"),
        };
        return result.RepositoryFingerprint is null &&
               result.ProjectFingerprint is null &&
               result.SourceRevision is null &&
               result.TargetFramework is null &&
               result.Configuration is null &&
               result.AppId is null &&
               result.AppBuildFingerprint is null &&
               result.AppSourceFingerprint is null &&
               result.PackageDigest is null
            ? null
            : result;
    }

    private static MauiTestExecutionDeviceFacts? Sanitize(MauiTestExecutionDeviceFacts? source)
    {
        if (source is null)
            return null;
        var result = new MauiTestExecutionDeviceFacts
        {
            Platform = MauiTestingContractRedactor.SafeCode(source.Platform),
            RuntimeKind = MauiTestingContractRedactor.SafeCode(source.RuntimeKind),
            Profile = MauiTestingContractRedactor.SafeDeviceProfile(source.Profile),
            Architecture = MauiTestingContractRedactor.SafeCode(source.Architecture),
            ApiLevel = MauiTestingContractRedactor.SafeCode(source.ApiLevel),
            OsVersion = MauiTestingContractRedactor.SafeCode(source.OsVersion),
            FormFactor = MauiTestingContractRedactor.SafeCode(source.FormFactor),
            Virtual = source.Virtual,
            DeviceIdentityFingerprint = MauiTestingContractRedactor.Fingerprint(
                source.DeviceIdentityFingerprint,
                "device"),
        };
        return result.Platform is null &&
               result.RuntimeKind is null &&
               result.Profile is null &&
               result.Architecture is null &&
               result.ApiLevel is null &&
               result.OsVersion is null &&
               result.FormFactor is null &&
               result.Virtual is null &&
               result.DeviceIdentityFingerprint is null
            ? null
            : result;
    }

    private static MauiTestExecutionLifecycleFacts? Sanitize(MauiTestExecutionLifecycleFacts? source)
    {
        if (source is null)
            return null;
        var seenSequences = new HashSet<int>();
        var result = new MauiTestExecutionLifecycleFacts
        {
            StartedAt = source.StartedAt,
            EndedAt = source.EndedAt,
            CleanupPolicy = MauiTestingContractRedactor.SafeCode(source.CleanupPolicy),
            InstalledByInvocation = source.InstalledByInvocation,
            LaunchedByInvocation = source.LaunchedByInvocation,
            CleanupCompleted = source.CleanupCompleted,
            Stages = (source.Stages ?? [])
                .Where(static stage => stage is not null)
                .Where(stage =>
                    stage.Sequence is > 0 &&
                    seenSequences.Add(stage.Sequence.Value))
                .Select(static stage => new MauiTestExecutionLifecycleStage
                {
                    Sequence = stage.Sequence,
                    Name = MauiTestingContractRedactor.SafeCode(stage.Name),
                    Status = MauiTestingContractRedactor.SafeCode(stage.Status),
                    DetailCode = MauiTestingContractRedactor.SafeCode(stage.DetailCode),
                    DurationMs = stage.DurationMs is >= 0 ? stage.DurationMs : null,
                    OwnedResource = stage.OwnedResource,
                })
                .Where(static stage => stage.Name is not null && stage.Status is not null)
                .OrderBy(static stage => stage.Sequence ?? int.MaxValue)
                .ThenBy(static stage => stage.Name, StringComparer.Ordinal)
                .Take(64)
                .ToList(),
        };
        return result.StartedAt is null &&
               result.EndedAt is null &&
               result.CleanupPolicy is null &&
               result.InstalledByInvocation is null &&
               result.LaunchedByInvocation is null &&
               result.CleanupCompleted is null &&
               result.Stages.Count == 0
            ? null
            : result;
    }

    private static MauiTestExecutionOutcomeFacts? Sanitize(MauiTestExecutionOutcomeFacts? source)
    {
        if (source is null)
            return null;
        var result = new MauiTestExecutionOutcomeFacts
        {
            Status = MauiTestingContractRedactor.SafeCode(source.Status),
            ExitCategory = MauiTestingContractRedactor.SafeCode(source.ExitCategory),
            Terminal = source.Terminal,
            Verified = source.Verified,
            UnknownCompletion = source.UnknownCompletion,
            SecondaryFailures = SanitizeSecondaryFailures(source.SecondaryFailures),
        };
        return result.Status is null &&
               result.ExitCategory is null &&
               result.Terminal is null &&
               result.Verified is null &&
               result.UnknownCompletion is null &&
               result.SecondaryFailures.Count == 0
            ? null
            : result;
    }

    /// <summary>
    /// Projects secondary failures through the one shared normalizer, so the manifest mirror and
    /// the flow-run report always retain exactly the same entries.
    /// </summary>
    internal static List<MauiFlowSecondaryFailure> SanitizeSecondaryFailures(
        IEnumerable<MauiFlowSecondaryFailure>? source)
        => MauiFlowRunReportSerializer.NormalizeSecondaryFailures(source);

    private static MauiTestExecutionArtifact Sanitize(MauiTestExecutionArtifact source) => new()
    {
        ArtifactId = MauiTestingContractRedactor.SafeIdentifierOrFingerprint(
            source.ArtifactId,
            "artifact-id"),
        Role = MauiTestingContractRedactor.SafeCode(source.Role),
        Kind = MauiTestingContractRedactor.SafeCode(source.Kind),
        MediaType = MauiTestingContractRedactor.SafeCode(source.MediaType),
        Digest = MauiTestingContractRedactor.Fingerprint(source.Digest, "artifact"),
        SizeBytes = source.SizeBytes is >= 0 ? source.SizeBytes : null,
        RelativePath = MauiTestingContractRedactor.SafeRelativePath(source.RelativePath),
        Redacted = source.Redacted,
    };
}

internal static class MauiTestingContractRedactor
{
    public static bool IsStrictFingerprint(string? value)
        => value is { Length: 71 } &&
           value.StartsWith("sha256:", StringComparison.Ordinal) &&
           value.AsSpan(7).ToArray().All(static character =>
               character is >= '0' and <= '9' or >= 'a' and <= 'f');

    public static string? NormalizeFingerprint(string? value)
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
        return null;
    }

    public static string? SafeCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || LooksOpaqueSecretValue(value))
            return null;
        var trimmed = value.Trim();
        if (trimmed.Any(char.IsWhiteSpace) ||
            trimmed.Any(static character =>
                !char.IsAsciiLetterOrDigit(character) &&
                character is not '-' and not '_' and not '.' and not ':' and not '/') ||
            IsAbsolutePath(trimmed))
        {
            return null;
        }
        return MauiFlowReportRedactor.SafeIdentifier(trimmed);
    }

    public static string? SafeIdentifierOrFingerprint(string? value, string domain)
    {
        if (string.IsNullOrWhiteSpace(value) || LooksOpaqueSecretValue(value))
            return null;
        var trimmed = value.Trim();
        if (trimmed.Length <= 128 &&
            trimmed.All(static character =>
                char.IsAsciiLetterOrDigit(character) ||
                character is '-' or '_' or '.' or ':') &&
            !IsAbsolutePath(trimmed))
        {
            return trimmed;
        }
        return Fingerprint(trimmed, domain);
    }

    public static string? SafeDeviceProfile(string? value)
    {
        var code = SafeCode(value);
        if (code is null)
            return null;
        return code.ToLowerInvariant() is
            "phone" or "tablet" or "desktop" or "wearable" or "tv" or
            "emulator" or "simulator" or "physical" or "virtual"
            ? code
            : Fingerprint(code, "device-profile");
    }

    public static string? Fingerprint(string? value, string domain)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        if (trimmed.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            return NormalizeFingerprint(trimmed);
        if (NormalizeFingerprint(trimmed) is { } normalized)
            return normalized;
        return Hash(domain + "\u001f" + trimmed);
    }

    public static string Hash(string material)
        => "sha256:" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();

    public static string? SafeRelativePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            FlowSecretReference.LooksSensitive(value))
        {
            return null;
        }

        var normalized = value.Trim().Replace('\\', '/');
        if (IsAbsolutePath(normalized))
        {
            return null;
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(static segment => segment is "." or ".."))
            return null;
        var safe = segments
            .Select(static segment => MauiFlowReportRedactor.SafeFileSegment(segment))
            .ToArray();
        return safe.Any(string.IsNullOrWhiteSpace) ? null : string.Join("/", safe!);
    }

    private static bool IsAbsolutePath(string value)
        => value.StartsWith("/", StringComparison.Ordinal) ||
           value.StartsWith("\\", StringComparison.Ordinal) ||
           value.StartsWith("//", StringComparison.Ordinal) ||
           value.StartsWith("\\\\", StringComparison.Ordinal) ||
           (value.Length >= 2 && char.IsAsciiLetter(value[0]) && value[1] == ':') ||
           Uri.TryCreate(value, UriKind.Absolute, out _);

    private static bool LooksOpaqueSecretValue(string value)
    {
        var trimmed = value.Trim();
        var compact = new string(trimmed.Where(char.IsAsciiLetterOrDigit).ToArray());
        return !trimmed.Any(char.IsWhiteSpace) &&
            compact.Length >= 20 &&
            compact.Any(char.IsUpper) &&
            compact.Any(char.IsLower);
    }
}

/// <summary>Creates a privacy-safe canonical identity for a runtime and device profile.</summary>
public static class MauiTestExecutionRuntimeProfile
{
    public const string RuleVersion = "maui-runtime-device-profile-v1";

    public static string? CreateFingerprint(
        MauiTestExecutionBuildFacts? build,
        MauiTestExecutionDeviceFacts? device)
    {
        if (build is null || device is null)
            return null;

        var targetFramework = MauiTestingContractRedactor.SafeCode(build.TargetFramework);
        var platform = MauiTestingContractRedactor.SafeCode(device.Platform);
        var runtime = MauiTestingContractRedactor.SafeCode(device.RuntimeKind);
        var architecture = MauiTestingContractRedactor.SafeCode(device.Architecture);
        var osVersion = MauiTestingContractRedactor.SafeCode(device.OsVersion);
        var formFactor = MauiTestingContractRedactor.SafeCode(device.FormFactor) ??
            MauiTestingContractRedactor.SafeDeviceProfile(device.Profile);
        var apiLevel = MauiTestingContractRedactor.SafeCode(device.ApiLevel);
        if (apiLevel is null &&
            string.Equals(platform, "android", StringComparison.OrdinalIgnoreCase))
        {
            apiLevel = osVersion;
        }
        apiLevel ??= "not-applicable";

        if (targetFramework is null ||
            platform is null ||
            runtime is null ||
            architecture is null ||
            osVersion is null ||
            formFactor is null)
        {
            return null;
        }

        return MauiTestingContractRedactor.Hash(string.Join(
            "\u001e",
            RuleVersion,
            targetFramework.ToLowerInvariant(),
            platform.ToLowerInvariant(),
            runtime.ToLowerInvariant(),
            apiLevel.ToLowerInvariant(),
            osVersion.ToLowerInvariant(),
            architecture.ToLowerInvariant(),
            formFactor.ToLowerInvariant()));
    }
}
