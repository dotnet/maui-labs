using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.Cli.DevFlow.Broker;

/// <summary>Memory-only retention limits for imported diagnostic artifact projections.</summary>
internal sealed class ArtifactTrustStoreOptions
{
    public int MaxRetainedArtifacts { get; init; } = 64;
    public TimeSpan Retention { get; init; } = TimeSpan.FromMinutes(30);
}

/// <summary>Result of adding an imported artifact to the token-gated store.</summary>
internal sealed class ArtifactTrustStoreAddResult
{
    public bool Ok { get; init; }
    public string? CapabilityToken { get; init; }
    public MauiArtifactTrustStatus? Status { get; init; }
    public string? Error { get; init; }

    public static ArtifactTrustStoreAddResult Failure(string error) => new() { Error = error };
}

/// <summary>Result of a token-gated imported-artifact read.</summary>
internal sealed class ArtifactTrustStoreReadResult
{
    public int StatusCode { get; init; }
    public string? Error { get; init; }
    public MauiArtifactTrustStatus? Status { get; init; }
    public MauiImportedArtifactSafeProjection? Projection { get; init; }
}

/// <summary>Result of binding an imported artifact to a local run.</summary>
internal sealed class ArtifactTrustStoreBindResult
{
    public int StatusCode { get; init; }
    public string? Error { get; init; }
    public MauiLocalReproductionEvaluation? Evaluation { get; init; }
    public MauiArtifactTrustStatus? Status { get; init; }
}

/// <summary>Broker-derived trust for one imported artifact bound to one exact local run.</summary>
internal sealed class ArtifactTrustStoreRepairResult
{
    public int StatusCode { get; init; }
    public string? Error { get; init; }
    public string? Trust { get; init; }
}

/// <summary>
/// Bounded in-memory store for imported artifact projections. Raw reports and ZIP bytes are never
/// persisted. Every record is reachable only with the short-lived capability token minted at
/// import time; there is intentionally no list or raw-download API.
/// </summary>
internal sealed class ArtifactTrustStore
{
    private readonly object _gate = new();
    private readonly ArtifactTrustStoreOptions _options;
    private readonly TimeProvider _clock;
    private readonly Dictionary<string, StoredArtifact> _artifacts = new(StringComparer.Ordinal);

    public ArtifactTrustStore(ArtifactTrustStoreOptions? options = null, TimeProvider? clock = null)
    {
        _options = options ?? new ArtifactTrustStoreOptions();
        _clock = clock ?? TimeProvider.System;
        if (_options.MaxRetainedArtifacts < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "At least one imported artifact must be retained.");
        if (_options.Retention <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "Imported artifact retention must be positive.");
    }

    public ArtifactTrustStoreAddResult Add(MauiArtifactTrustRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Identity?.IsValid != true)
            return ArtifactTrustStoreAddResult.Failure("A broker-generated imported-artifact identity is required.");

        lock (_gate)
        {
            PruneExpiredLocked();
            if (_artifacts.ContainsKey(record.Identity.Id!))
                return ArtifactTrustStoreAddResult.Failure("The imported artifact identity is already retained.");

            while (_artifacts.Count >= _options.MaxRetainedArtifacts)
                EvictOldestLocked();

            var now = _clock.GetUtcNow();
            var storedRecord = CloneRecord(record);
            storedRecord.ImportedAt ??= now;
            var stored = new StoredArtifact(
                storedRecord,
                CreateCapabilityToken(),
                now,
                now + _options.Retention);
            _artifacts.Add(storedRecord.Identity!.Id!, stored);
            return new ArtifactTrustStoreAddResult
            {
                Ok = true,
                CapabilityToken = stored.CapabilityToken,
                Status = CreateStatus(stored),
            };
        }
    }

    public ArtifactTrustStoreReadResult GetStatus(string artifactId, string? capabilityToken)
    {
        lock (_gate)
        {
            var lookup = FindAuthorizedLocked(artifactId, capabilityToken);
            return lookup.Result ?? new ArtifactTrustStoreReadResult
            {
                StatusCode = 200,
                Status = CreateStatus(lookup.Artifact!),
            };
        }
    }

    public ArtifactTrustStoreReadResult GetSafeProjection(string artifactId, string? capabilityToken)
    {
        lock (_gate)
        {
            var lookup = FindAuthorizedLocked(artifactId, capabilityToken);
            if (lookup.Result is not null)
                return lookup.Result;

            return new ArtifactTrustStoreReadResult
            {
                StatusCode = 200,
                Projection = CloneProjection(lookup.Artifact!.Record.Projection),
            };
        }
    }

    public ArtifactTrustStoreBindResult BindLocalReproduction(
        string artifactId,
        string? capabilityToken,
        MauiLocalReproductionFacts localRun,
        MauiLocalReproductionExpectation current)
    {
        ArgumentNullException.ThrowIfNull(localRun);
        ArgumentNullException.ThrowIfNull(current);

        lock (_gate)
        {
            var lookup = FindAuthorizedLocked(artifactId, capabilityToken);
            if (lookup.Result is not null)
            {
                return new ArtifactTrustStoreBindResult
                {
                    StatusCode = lookup.Result.StatusCode,
                    Error = lookup.Result.Error,
                };
            }

            var stored = lookup.Artifact!;
            var evaluation = MauiArtifactTrustEvaluator.EvaluateLocalReproduction(
                stored.Record,
                localRun,
                current,
                _clock.GetUtcNow());

            // A failed later attempt must not downgrade an already established local
            // reproduction. Before that point, retain the explicit failure binding for review.
            if (evaluation.Binding.Matched == true ||
                !string.Equals(
                    stored.Record.Verification.State,
                    MauiArtifactTrustStates.LocallyReproduced,
                    StringComparison.Ordinal))
            {
                stored.Record.LocalReproduction = CloneBinding(evaluation.Binding);
                if (evaluation.Binding.Matched == true)
                    stored.Record.Verification = CloneVerification(evaluation.Verification)
                        ?? throw new InvalidOperationException("Local reproduction verification could not be cloned.");
            }

            return new ArtifactTrustStoreBindResult
            {
                StatusCode = 200,
                Evaluation = CloneEvaluation(evaluation),
                Status = CreateStatus(stored),
            };
        }
    }

    public ArtifactTrustStoreRepairResult GetRepairTrust(
        string artifactId,
        string? capabilityToken,
        string localRunId)
    {
        lock (_gate)
        {
            var lookup = FindAuthorizedLocked(artifactId, capabilityToken);
            if (lookup.Result is not null)
            {
                return new ArtifactTrustStoreRepairResult
                {
                    StatusCode = lookup.Result.StatusCode,
                    Error = lookup.Result.Error,
                };
            }

            var record = lookup.Artifact!.Record;
            var binding = record.LocalReproduction;
            if (!string.Equals(
                    record.Verification?.State,
                    MauiArtifactTrustStates.LocallyReproduced,
                    StringComparison.Ordinal) ||
                binding?.Matched != true ||
                !string.Equals(binding.LocalRunId, localRunId, StringComparison.Ordinal) ||
                !string.Equals(
                    binding.Verification?.State,
                    MauiArtifactTrustStates.LocallyReproduced,
                    StringComparison.Ordinal))
            {
                return new ArtifactTrustStoreRepairResult
                {
                    StatusCode = 409,
                    Error = "The imported artifact is not locally reproduced by this exact broker-owned run.",
                };
            }

            return new ArtifactTrustStoreRepairResult
            {
                StatusCode = 200,
                Trust = MauiArtifactTrustStates.LocallyReproduced,
            };
        }
    }

    private LookupResult FindAuthorizedLocked(string artifactId, string? capabilityToken)
    {
        PruneExpiredLocked();
        if (string.IsNullOrWhiteSpace(artifactId) ||
            !_artifacts.TryGetValue(artifactId, out var artifact))
        {
            return new LookupResult
            {
                Result = new ArtifactTrustStoreReadResult
                {
                    StatusCode = 404,
                    Error = "Imported artifact was not found.",
                },
            };
        }

        if (!HasCapability(artifact.CapabilityToken, capabilityToken))
        {
            return new LookupResult
            {
                Result = new ArtifactTrustStoreReadResult
                {
                    StatusCode = 403,
                    Error = "A valid imported-artifact capability token is required.",
                },
            };
        }

        return new LookupResult { Artifact = artifact };
    }

    private void PruneExpiredLocked()
    {
        var now = _clock.GetUtcNow();
        foreach (var id in _artifacts
                     .Where(pair => pair.Value.ExpiresAt <= now)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            _artifacts.Remove(id);
        }
    }

    private void EvictOldestLocked()
    {
        var oldest = _artifacts
            .OrderBy(pair => pair.Value.CreatedAt)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Key)
            .FirstOrDefault();
        if (oldest is not null)
            _artifacts.Remove(oldest);
    }

    private static MauiArtifactTrustStatus CreateStatus(StoredArtifact stored)
        => new()
        {
            Identity = CloneIdentity(stored.Record.Identity),
            ArtifactKind = stored.Record.ArtifactKind,
            ImportedAt = stored.Record.ImportedAt,
            ExpiresAt = stored.ExpiresAt,
            Verification = CloneVerification(stored.Record.Verification),
            RawContentRetained = false,
            HasSafeProjection = stored.Record.Projection is not null,
        };

    private static bool HasCapability(string expected, string? supplied)
    {
        if (string.IsNullOrWhiteSpace(supplied) || supplied.Length > 128)
            return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(supplied));
    }

    private static string CreateCapabilityToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static MauiArtifactTrustRecord CloneRecord(MauiArtifactTrustRecord value)
        => JsonSerializer.Deserialize(
            JsonSerializer.SerializeToUtf8Bytes(value, MauiTestingJsonContext.Default.MauiArtifactTrustRecord),
            MauiTestingJsonContext.Default.MauiArtifactTrustRecord)
            ?? throw new InvalidOperationException("Imported artifact metadata could not be cloned.");

    private static MauiImportedArtifactSafeProjection? CloneProjection(MauiImportedArtifactSafeProjection? value)
        => value is null
            ? null
            : JsonSerializer.Deserialize(
                JsonSerializer.SerializeToUtf8Bytes(value, MauiTestingJsonContext.Default.MauiImportedArtifactSafeProjection),
                MauiTestingJsonContext.Default.MauiImportedArtifactSafeProjection);

    private static MauiArtifactTrustVerificationResult? CloneVerification(MauiArtifactTrustVerificationResult? value)
        => value is null
            ? null
            : JsonSerializer.Deserialize(
                JsonSerializer.SerializeToUtf8Bytes(value, MauiTestingJsonContext.Default.MauiArtifactTrustVerificationResult),
                MauiTestingJsonContext.Default.MauiArtifactTrustVerificationResult);

    private static MauiImportedArtifactIdentity? CloneIdentity(MauiImportedArtifactIdentity? value)
        => value is null
            ? null
            : JsonSerializer.Deserialize(
                JsonSerializer.SerializeToUtf8Bytes(value, MauiTestingJsonContext.Default.MauiImportedArtifactIdentity),
                MauiTestingJsonContext.Default.MauiImportedArtifactIdentity);

    private static MauiLocalReproductionBinding CloneBinding(MauiLocalReproductionBinding value)
        => JsonSerializer.Deserialize(
            JsonSerializer.SerializeToUtf8Bytes(value, MauiTestingJsonContext.Default.MauiLocalReproductionBinding),
            MauiTestingJsonContext.Default.MauiLocalReproductionBinding)
            ?? throw new InvalidOperationException("Local reproduction binding could not be cloned.");

    private static MauiLocalReproductionEvaluation CloneEvaluation(MauiLocalReproductionEvaluation value)
        => JsonSerializer.Deserialize(
            JsonSerializer.SerializeToUtf8Bytes(value, MauiTestingJsonContext.Default.MauiLocalReproductionEvaluation),
            MauiTestingJsonContext.Default.MauiLocalReproductionEvaluation)
            ?? throw new InvalidOperationException("Local reproduction evaluation could not be cloned.");

    private sealed class StoredArtifact(
        MauiArtifactTrustRecord record,
        string capabilityToken,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt)
    {
        public MauiArtifactTrustRecord Record { get; } = record;
        public string CapabilityToken { get; } = capabilityToken;
        public DateTimeOffset CreatedAt { get; } = createdAt;
        public DateTimeOffset ExpiresAt { get; } = expiresAt;
    }

    private sealed class LookupResult
    {
        public StoredArtifact? Artifact { get; init; }
        public ArtifactTrustStoreReadResult? Result { get; init; }
    }
}

/// <summary>Bounded route response that never carries raw imported bytes or unredacted content.</summary>
internal sealed class ArtifactTrustRouteResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }

    [JsonPropertyName("capabilityToken")]
    public string? CapabilityToken { get; init; }

    [JsonPropertyName("status")]
    public MauiArtifactTrustStatus? Status { get; init; }

    [JsonPropertyName("projection")]
    public MauiImportedArtifactSafeProjection? Projection { get; init; }

    [JsonPropertyName("reproduction")]
    public MauiLocalReproductionEvaluation? Reproduction { get; init; }

    public static ArtifactTrustRouteResponse Failure(string error)
        => new() { Error = error };
}

/// <summary>
/// The broker derives local-run facts from its own completed run record; callers supply only the
/// trusted current-workspace expectations to compare against it.
/// </summary>
internal sealed class ArtifactTrustLocalReproductionRequest
{
    [JsonPropertyName("localRunId")]
    public string? LocalRunId { get; set; }

    [JsonPropertyName("current")]
    public MauiLocalReproductionExpectation? Current { get; set; }
}
