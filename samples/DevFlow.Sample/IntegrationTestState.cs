#if DEBUG && DEVFLOW_INTEGRATION_TEST
using System.Security.Cryptography;
using System.Text;

namespace DevFlow.Sample;

/// <summary>
/// Test-build-only state oracle. It intentionally hashes only the fixed sample fixture values;
/// it never returns user-entered data, storage values, credentials, or device identifiers.
/// </summary>
internal sealed class IntegrationTestState
{
    internal const string SeedId = "devflow-sample-v1";
    const string BackendState = "no-external-backend-v1";
    readonly TodoService _todos;
    readonly string _processInstanceId = Guid.NewGuid().ToString("N");

    public IntegrationTestState(TodoService todos)
        => _todos = todos;

    public IntegrationTestStateSnapshot ApplySeed(string? requestedSeedId, string? route)
    {
        if (!string.IsNullOrWhiteSpace(requestedSeedId) &&
            !string.Equals(requestedSeedId, SeedId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unsupported integration-test seed '{requestedSeedId}'.");
        }

        _todos.ResetToIntegrationSeed();
        return Snapshot(route);
    }

    public IntegrationTestStateSnapshot Snapshot(string? route)
    {
        var canonicalState = string.Join(
            "\n",
            _todos.Items.Select(static item =>
                $"{item.Title}\u001f{item.Description}\u001f{item.IsCompleted}"));
        return new IntegrationTestStateSnapshot(
            SeedId,
            Fingerprint(SeedId),
            Fingerprint(BackendState),
            Fingerprint(canonicalState),
            _processInstanceId,
            RedactRoute(route));
    }

    static string Fingerprint(string value)
        => $"sha256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()}";

    static string? RedactRoute(string? route)
    {
        if (string.IsNullOrWhiteSpace(route))
            return route;

        var end = route.IndexOfAny(['?', '#']);
        return end >= 0 ? route[..end] : route;
    }
}

internal sealed record IntegrationTestStateSnapshot(
    string SeedId,
    string SeedFingerprint,
    string BackendStateFingerprint,
    string StateFingerprint,
    string ProcessInstanceId,
    string? Route);
#endif
