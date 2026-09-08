using System.Net;
using System.Net.Sockets;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Client.Tests;

/// <summary>
/// The agent binds one loopback family, while <c>localhost</c> may resolve to the other one first
/// (see dotnet/maui-labs#341). Modern .NET steers this with
/// <c>SocketsHttpHandler.ConnectCallback</c>; netstandard2.0 has no such hook and uses a probe-based
/// handler instead. Running these on both target families is what keeps the two implementations
/// behaviorally identical.
/// </summary>
public class AgentClientLoopbackTests
{
    private const string StatusBody = """{"running":true}""";

    [Fact]
    public async Task GetStatusAsync_LocalhostReachesIPv4OnlyAgent()
    {
        using var agent = FakeAgent.Start(IPAddress.Loopback, _ => FakeAgent.Response.Json(StatusBody));
        using var client = new AgentClient("localhost", agent.Port);

        var status = await client.GetStatusAsync();

        Assert.NotNull(status);
        Assert.True(status!.Running);
    }

    [Fact]
    public async Task GetStatusAsync_LocalhostReachesIPv6OnlyAgent()
    {
        if (!Socket.OSSupportsIPv6)
            return; // No IPv6 loopback on this host. xUnit v2 lacks a runtime Assert.Skip, so this
                    // shows as "passed" rather than "skipped" — the repo-wide convention. The
                    // IPv4-only test above already covers the core fallback on such hosts.

        using var agent = FakeAgent.Start(IPAddress.IPv6Loopback, _ => FakeAgent.Response.Json(StatusBody));
        using var client = new AgentClient("localhost", agent.Port);

        var status = await client.GetStatusAsync();

        Assert.NotNull(status);
        Assert.True(status!.Running);
    }

    [Fact]
    public async Task GetStatusAsync_ExplicitIPv4HostStillConnects()
    {
        // The documented --agent-host 127.0.0.1 workaround must keep working via the default path,
        // which skips the loopback handling entirely.
        using var agent = FakeAgent.Start(IPAddress.Loopback, _ => FakeAgent.Response.Json(StatusBody));
        using var client = new AgentClient("127.0.0.1", agent.Port);

        var status = await client.GetStatusAsync();

        Assert.NotNull(status);
        Assert.True(status!.Running);
    }

    [Fact]
    public async Task LocalhostRequests_KeepTheAliasHostHeader()
    {
        // Whichever address family the client picks is a transport detail: the agent must still see
        // the request it would get from any other DevFlow consumer.
        using var agent = FakeAgent.Start(IPAddress.Loopback, _ => FakeAgent.Response.Json(StatusBody));
        using var client = new AgentClient("localhost", agent.Port);

        await client.GetStatusAsync();

        var request = Assert.Single(agent.Requests);
        Assert.Equal($"localhost:{agent.Port}", request.Headers["Host"]);
    }

    [Fact]
    public async Task GetStatusAsync_NoAgentListeningReturnsNull()
    {
        int port;
        using (var agent = FakeAgent.Start(IPAddress.Loopback, _ => FakeAgent.Response.Json(StatusBody)))
            port = agent.Port; // Disposed immediately: nothing is listening on this port anymore.

        using var client = new AgentClient("localhost", port);

        Assert.Null(await client.GetStatusAsync());
    }
}
