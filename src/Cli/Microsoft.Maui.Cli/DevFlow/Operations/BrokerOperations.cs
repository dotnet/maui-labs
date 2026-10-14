using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow.Broker;

namespace Microsoft.Maui.Cli.DevFlow;

internal sealed record BrokerStatusResult(bool Running, int? Port = null, int Agents = 0, bool? StateFile = null, bool Stale = false);

internal sealed class BrokerOperations
{
    internal Task<int?> StartAsync() =>
        BrokerClient.EnsureBrokerRunningAsync();

    internal Task<bool> StopAsync() =>
        BrokerClient.ShutdownBrokerAsync();

    internal async Task RunAsync(Action<string> log, CancellationToken cancellationToken)
    {
        using var server = new BrokerServer(log: log);
        await server.RunAsync(cancellationToken);
    }

    internal Task<BrokerStatusResult> GetStatusAsync() =>
        GetStatusAsync(BrokerClient.ReadBrokerPortPublic());

    internal async Task<BrokerStatusResult> GetStatusAsync(int? statePort, int fallbackPort = BrokerServer.DefaultPort)
    {
        try
        {
            var port = statePort ?? fallbackPort;
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var response = await http.GetStringAsync($"http://localhost:{port}/api/health");
            using var doc = JsonDocument.Parse(response);

            return new(true, port, doc.RootElement.GetProperty("agents").GetInt32(),
                StateFile: statePort is null ? false : null);
        }
        catch
        {
            return new(false, statePort, Stale: statePort.HasValue);
        }
    }

    internal string[]? GetLogTail()
    {
        var path = BrokerPaths.LogFile;

        return File.Exists(path) ? ReadLogTail(path) : null;
    }

    internal string[] ReadLogTail(string path) =>
        File.ReadAllLines(path).TakeLast(50).ToArray();
}
