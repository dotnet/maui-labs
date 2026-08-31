using System.Text.Json;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;

/// <summary>
/// Client for the sample's integration-build-only extension. The endpoint intentionally exposes
/// deterministic, non-sensitive state identities rather than app data or credentials.
/// </summary>
internal sealed class SampleIntegrationTestControlClient
{
    const string ExtensionNamespace = "com.example.devflow.integrationtest";
    const string StatePath = "/api/v1/ext/com.example.devflow.integrationtest/state";
    const string SeedPath = "/api/v1/ext/com.example.devflow.integrationtest/seed";
    readonly AgentClient _client;

    public SampleIntegrationTestControlClient(AgentClient client)
        => _client = client ?? throw new ArgumentNullException(nameof(client));

    public async Task<SampleIntegrationTestState> SeedAsync(string seedId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureAvailableAsync(cancellationToken).ConfigureAwait(false);
        var payload = JsonSerializer.SerializeToElement(new { seedId });
        var response = await _client.CallExtensionToolAsync("POST", SeedPath, payload).ConfigureAwait(false);
        return Parse(response);
    }

    public async Task<SampleIntegrationTestState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureAvailableAsync(cancellationToken).ConfigureAwait(false);
        var response = await _client.CallExtensionToolAsync("GET", StatePath).ConfigureAwait(false);
        return Parse(response);
    }

    async Task EnsureAvailableAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var extensions = await _client.GetExtensionsAsync().ConfigureAwait(false);
        if (!extensions.ContainsKey(ExtensionNamespace))
        {
            throw PlatformFlowLifecycleException.Precondition(
                $"The sample integration-test extension '{ExtensionNamespace}' is unavailable. " +
                "Build the sample with -p:DevFlowIntegrationTest=true in Debug configuration.");
        }
    }

    static SampleIntegrationTestState Parse(string json)
    {
        try
        {
            var value = JsonSerializer.Deserialize<SampleIntegrationTestState>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (value is null ||
                string.IsNullOrWhiteSpace(value.SeedId) ||
                string.IsNullOrWhiteSpace(value.SeedFingerprint) ||
                string.IsNullOrWhiteSpace(value.StateFingerprint) ||
                string.IsNullOrWhiteSpace(value.ProcessInstanceId))
            {
                throw new JsonException("The sample test-state response is incomplete.");
            }

            return value;
        }
        catch (JsonException ex)
        {
            throw PlatformFlowLifecycleException.Infrastructure(
                "The sample integration-test extension returned an invalid state response.",
                ex);
        }
    }
}

internal sealed class SampleIntegrationTestState
{
    public string SeedId { get; set; } = "";
    public string SeedFingerprint { get; set; } = "";
    public string? BackendStateFingerprint { get; set; }
    public string StateFingerprint { get; set; } = "";
    public string ProcessInstanceId { get; set; } = "";
    public string? Route { get; set; }
}
