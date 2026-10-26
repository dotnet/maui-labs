using System.Net;
using System.Net.Sockets;
using Microsoft.Maui.Cli.DevFlow.Inspector;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Inspector.Tests;

internal sealed class LayoutQaSession(AgentClient agent, InspectorServer inspector) : IAsyncDisposable
{
    internal const string AppName = "MauiTodo Layout QA";
    public string InspectorUrl => $"http://127.0.0.1:{inspector.Port}/";

    public static async Task<LayoutQaSession> CreateAsync(int agentPort)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(agentPort, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(agentPort, 65535);
        var agent = new AgentClient("localhost", agentPort);
        InspectorServer? inspector = null;
        try
        {
            var status = await agent.GetStatusAsync();
            if (status?.AppName != AppName)
                throw new InvalidOperationException(
                    $"LAYOUT_QA_AGENT_PORT must target '{AppName}', not '{status?.AppName ?? "an unavailable agent"}'.");

            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var inspectorPort = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            // This dedicated Inspector and the direct client share the verified port.
            // INSPECTOR_URL may point at another app and is deliberately not used.
            inspector = new InspectorServer(inspectorPort, "localhost", agentPort);
            inspector.Start();
            return new LayoutQaSession(agent, inspector);
        }
        catch
        {
            inspector?.Dispose();
            agent.Dispose();
            throw;
        }
    }

    public async Task PrepareAsync(Func<AgentClient, Task> prepare)
    {
        try
        {
            await prepare(agent);
        }
        finally
        {
            await agent.ControlMutationLeaseAsync("release");
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await inspector.StopAsync();
        }
        finally
        {
            inspector.Dispose();
            agent.Dispose();
        }
    }
}
