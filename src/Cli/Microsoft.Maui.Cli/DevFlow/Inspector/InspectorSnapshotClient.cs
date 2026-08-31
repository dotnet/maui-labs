using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.Cli.DevFlow.Inspector;

internal static class InspectorSnapshotClient
{
    public static async Task<List<ElementInfo>?> GetActiveVisualTreeAsync(
        int agentPort,
        CancellationToken cancellationToken = default)
    {
        var brokerPort = BrokerClient.ReadBrokerPortPublic() ?? BrokerServer.DefaultPort;
        var agents = await BrokerClient.ListAgentsAsync(brokerPort);
        var matches = agents?.Where(candidate => candidate.Port == agentPort).Take(2).ToArray();
        if (matches is not { Length: 1 })
            return null;
        return await GetActiveVisualTreeAsync(
            brokerPort,
            matches[0].Id,
            cancellationToken);
    }

    public static async Task<List<ElementInfo>?> GetActiveVisualTreeAsync(
        int brokerPort,
        string agentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        using var response = await http.GetAsync(
            $"http://localhost:{brokerPort}/inspector/{Uri.EscapeDataString(agentId)}/api/inspect/snapshot",
            cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (!root.TryGetProperty("ok", out var ok) ||
            ok.ValueKind != JsonValueKind.True ||
            !root.TryGetProperty("projection", out var projection) ||
            !string.Equals(projection.GetString(), "activeVisual", StringComparison.Ordinal) ||
            !root.TryGetProperty("roots", out var roots) ||
            roots.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return CliJson.Deserialize<List<ElementInfo>>(roots.GetRawText());
    }
}
