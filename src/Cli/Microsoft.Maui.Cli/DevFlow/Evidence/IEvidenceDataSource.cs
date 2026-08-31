using System.Text.Json;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.Cli.DevFlow.Evidence;

/// <summary>
/// The reads an evidence capture is allowed to perform. Keeping this narrow is part of the
/// privacy contract: there is no preferences, secure-storage, geolocation, or file-content read
/// available to the builder at all.
/// </summary>
internal interface IEvidenceDataSource
{
    Task<AgentStatus?> GetStatusAsync(CancellationToken ct);
    Task<JsonElement> GetCapabilitiesAsync(CancellationToken ct);
    Task<List<ElementInfo>> GetTreeAsync(CancellationToken ct);
    Task<DiagnosticProblemBatch> GetProblemsAsync(int limit, CancellationToken ct);
    Task<string> GetLogsAsync(int limit, CancellationToken ct);
    Task<List<NetworkRequest>> GetNetworkAsync(int limit, CancellationToken ct);
    Task<JsonElement> GetPlatformInfoAsync(string endpoint, CancellationToken ct);
    Task<byte[]?> GetScreenshotAsync(CancellationToken ct);
}

/// <summary>Adapts a live <see cref="AgentClient"/> to <see cref="IEvidenceDataSource"/>.</summary>
internal sealed class AgentEvidenceDataSource(AgentClient client) : IEvidenceDataSource
{
    public Task<AgentStatus?> GetStatusAsync(CancellationToken ct) => client.GetStatusAsync();

    public Task<JsonElement> GetCapabilitiesAsync(CancellationToken ct) => client.GetCapabilitiesAsync();

    public Task<List<ElementInfo>> GetTreeAsync(CancellationToken ct) => client.GetTreeAsync();

    public Task<DiagnosticProblemBatch> GetProblemsAsync(int limit, CancellationToken ct)
        => client.GetDiagnosticProblemsAsync(limit);

    public Task<string> GetLogsAsync(int limit, CancellationToken ct) => client.GetLogsAsync(limit);

    public Task<List<NetworkRequest>> GetNetworkAsync(int limit, CancellationToken ct)
        => client.GetNetworkRequestsAsync(limit);

    public Task<JsonElement> GetPlatformInfoAsync(string endpoint, CancellationToken ct)
        => client.GetPlatformInfoAsync(endpoint);

    public Task<byte[]?> GetScreenshotAsync(CancellationToken ct) => client.ScreenshotAsync();
}
