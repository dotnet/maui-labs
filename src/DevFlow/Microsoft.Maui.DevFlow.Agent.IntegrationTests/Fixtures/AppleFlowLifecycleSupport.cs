using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Maui.DevFlow.Driver;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;

/// <summary>
/// Shared, host-owned Apple lifecycle helpers. These helpers deliberately contain only reset,
/// seed, checkpoint, and diagnostic mechanics; flow semantics remain in MauiFlowRunner.
/// </summary>
internal static class AppleFlowLifecycleSupport
{
    internal static async Task<string> ComputeFileFingerprintAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return $"sha256:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    internal static async Task<PlatformSeedResult> SeedAsync(
        AgentClient client,
        PlatformFlowSeedRequest request,
        MauiFlowResetResult? reset,
        CancellationToken cancellationToken)
    {
        var retries = client.TransientFailureRetryCount;
        var retryMutations = client.RetryMutatingRequests;
        client.TransientFailureRetryCount = 0;
        client.RetryMutatingRequests = false;
        try
        {
            var state = await new SampleIntegrationTestControlClient(client)
                .SeedAsync(request.SeedId, cancellationToken)
                .ConfigureAwait(false);
            EnsureExpectedFingerprint("seed", request.ExpectedSeedFingerprint, state.SeedFingerprint);
            EnsureExpectedFingerprint("backend state", request.ExpectedBackendStateFingerprint, state.BackendStateFingerprint);

            var result = new PlatformSeedResult
            {
                SeedId = state.SeedId,
                SeedFingerprint = state.SeedFingerprint,
                BackendStateFingerprint = state.BackendStateFingerprint,
                StateFingerprint = state.StateFingerprint,
                ProcessInstanceId = state.ProcessInstanceId,
                AppStateSeed = new MauiFlowAppStateSeedFingerprint
                {
                    SeedId = state.SeedId,
                    Fingerprint = state.SeedFingerprint,
                    Version = "1",
                    Source = "sample-integration-test-extension",
                },
                BackendTestDataSeed = new MauiFlowBackendTestDataSeedFingerprint
                {
                    SeedId = state.SeedId,
                    Fingerprint = state.BackendStateFingerprint,
                    Dataset = "none",
                    Version = "1",
                    Source = "sample-no-external-backend",
                },
                StateOracle = new MauiIndependentBusinessOracleResult
                {
                    OracleId = "sample-integration-state",
                    Succeeded = true,
                    Independent = true,
                    ObservedAt = DateTimeOffset.UtcNow,
                    EvidenceReference = "sample-test-state",
                    Message = "The test-only sample state endpoint returned the deterministic fingerprint.",
                },
            };

            if (reset is not null)
            {
                reset.SeedFingerprint = result.SeedFingerprint;
                reset.BackendStateFingerprint = result.BackendStateFingerprint;
                reset.AppStateSeed = result.AppStateSeed;
                reset.BackendTestDataSeed = result.BackendTestDataSeed;
            }

            return result;
        }
        finally
        {
            client.TransientFailureRetryCount = retries;
            client.RetryMutatingRequests = retryMutations;
        }
    }

    internal static MauiFlowCheckpoint MergeCheckpoint(MauiFlowCheckpoint requested, MauiFlowCheckpoint observed)
        => new()
        {
            AppBuildFingerprint = requested.AppBuildFingerprint ?? observed.AppBuildFingerprint,
            AgentInstanceId = requested.AgentInstanceId ?? observed.AgentInstanceId,
            SeedFingerprint = requested.SeedFingerprint ?? observed.SeedFingerprint,
            BackendStateFingerprint = requested.BackendStateFingerprint ?? observed.BackendStateFingerprint,
            Route = requested.Route ?? observed.Route,
            Window = requested.Window ?? observed.Window,
            Modal = requested.Modal ?? observed.Modal,
            Locale = requested.Locale ?? observed.Locale,
            Theme = requested.Theme ?? observed.Theme,
            Orientation = requested.Orientation ?? observed.Orientation,
            DisplayProfile = requested.DisplayProfile ?? observed.DisplayProfile,
            CollectionItemKey = requested.CollectionItemKey ?? observed.CollectionItemKey,
        };

    internal static void EnsureCheckpointMatches(MauiFlowCheckpoint expected, MauiFlowCheckpoint observed, string platform)
    {
        var mismatches = new List<string>();
        AddMismatch(mismatches, "app build", expected.AppBuildFingerprint, observed.AppBuildFingerprint);
        AddMismatch(mismatches, "agent instance", expected.AgentInstanceId, observed.AgentInstanceId);
        AddMismatch(mismatches, "seed", expected.SeedFingerprint, observed.SeedFingerprint);
        AddMismatch(mismatches, "backend state", expected.BackendStateFingerprint, observed.BackendStateFingerprint);
        AddMismatch(mismatches, "route", expected.Route, observed.Route);
        AddMismatch(mismatches, "locale", expected.Locale, observed.Locale);
        AddMismatch(mismatches, "theme", expected.Theme, observed.Theme);
        AddMismatch(mismatches, "orientation", expected.Orientation, observed.Orientation);
        AddMismatch(mismatches, "display", expected.DisplayProfile, observed.DisplayProfile);
        if (mismatches.Count > 0)
        {
            throw PlatformFlowLifecycleException.Precondition(
                $"{platform} replay preconditions did not match: {string.Join("; ", mismatches)}");
        }
    }

    internal static async Task<PlatformHostDiagnostics> WriteDiagnosticsAsync(
        string artifactRoot,
        string runId,
        string platform,
        IReadOnlyDictionary<string, string?> facts,
        CancellationToken cancellationToken)
    {
        var directory = Path.Combine(Path.GetFullPath(artifactRoot), SanitizeFileName(runId));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{platform}-host-diagnostics.json");
        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(facts, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken).ConfigureAwait(false);

        var diagnostics = new PlatformHostDiagnostics();
        diagnostics.Artifacts.Add(new MauiFlowArtifactReference
        {
            ArtifactId = $"{platform}-host-diagnostics-{SanitizeFileName(runId)}",
            Kind = $"{platform}-host-diagnostics",
            Path = path,
            Digest = await ComputeFileFingerprintAsync(path, cancellationToken).ConfigureAwait(false),
            MediaType = "application/json",
            Redacted = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        return diagnostics;
    }

    internal static void EnsureExpectedFingerprint(string kind, string? expected, string? observed)
    {
        if (!string.IsNullOrWhiteSpace(expected) &&
            !string.Equals(expected, observed, StringComparison.Ordinal))
        {
            throw PlatformFlowLifecycleException.Precondition(
                $"Expected {kind} fingerprint '{expected}', observed '{observed ?? "<none>"}'.");
        }
    }

    internal static void EnsureObserved(string kind, string? value, string platform)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw PlatformFlowLifecycleException.Precondition($"{platform} checkpoint did not provide {kind}.");
    }

    internal static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "run" : sanitized[..Math.Min(sanitized.Length, 96)];
    }

    static void AddMismatch(List<string> mismatches, string name, string? expected, string? observed)
    {
        if (!string.IsNullOrWhiteSpace(expected) &&
            !string.Equals(expected, observed, StringComparison.Ordinal))
        {
            mismatches.Add($"{name} expected '{expected}', observed '{observed ?? "<none>"}'");
        }
    }
}
