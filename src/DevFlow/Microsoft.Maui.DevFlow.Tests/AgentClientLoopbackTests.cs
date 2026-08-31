using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// Verifies that <see cref="AgentClient"/> connecting to the <c>localhost</c> alias attempts both
/// loopback address families: it reaches the agent whether the agent listens on IPv4
/// (<c>127.0.0.1</c>) or IPv6 (<c>::1</c>) loopback only, without forcing a single family.
/// Regression coverage for dotnet/maui-labs#341.
/// </summary>
public class AgentClientLoopbackTests
{
    [Fact]
    public async Task GetStatus_LocalhostReachesIPv4OnlyAgent()
    {
        // The DevFlow agent binds IPv4 loopback only; macOS commonly resolves localhost to ::1
        // first, so the client must fall back to 127.0.0.1.
        await using var agent = CannedAgent.Start(IPAddress.Loopback);
        using var client = new AgentClient("localhost", agent.Port);

        var status = await client.GetStatusAsync();

        Assert.NotNull(status);
        Assert.True(status!.Running);
    }

    [Fact]
    public async Task ResolveLoopbackCandidates_LocalhostBypassesDns()
    {
        var dnsCalled = false;
        var candidates = await AgentClient.ResolveLoopbackCandidatesAsync(
            "localhost",
            CancellationToken.None,
            (_, _) =>
            {
                dnsCalled = true;
                throw new InvalidOperationException("DNS should not be queried for localhost.");
            });

        Assert.False(dnsCalled);
        Assert.Equal(IPAddress.Loopback, candidates[0]);
        if (Socket.OSSupportsIPv6)
            Assert.Contains(IPAddress.IPv6Loopback, candidates);
        else
            Assert.Single(candidates);
    }

    [Fact]
    public async Task GetStatus_LocalhostReachesIPv6OnlyAgent()
    {
        if (!Socket.OSSupportsIPv6)
            return; // No IPv6 loopback on this host. xUnit v2 lacks a runtime Assert.Skip, so this
                    // shows as "passed" rather than "skipped" — the repo-wide convention (see the
                    // platform guards in AppleSimulatorCommandsTests). The IPv4-only test above
                    // already exercises the core localhost fallback on such hosts.

        // Proves we do not force IPv4: an agent bound to ::1 only is still reachable via localhost.
        await using var agent = CannedAgent.Start(IPAddress.IPv6Loopback);
        using var client = new AgentClient("localhost", agent.Port);

        var status = await client.GetStatusAsync();

        Assert.NotNull(status);
        Assert.True(status!.Running);
    }

    [Fact]
    public async Task GetStatus_ExplicitIPv4HostStillConnects()
    {
        // The documented --agent-host 127.0.0.1 workaround must keep working via the default path.
        await using var agent = CannedAgent.Start(IPAddress.Loopback);
        using var client = new AgentClient("127.0.0.1", agent.Port);

        var status = await client.GetStatusAsync();

        Assert.NotNull(status);
        Assert.True(status!.Running);
    }

    /// <summary>
    /// Minimal single-address-family HTTP server that answers any request with a fixed agent
    /// status payload. Used to simulate an agent that listens on only one loopback family.
    /// </summary>
    private sealed class CannedAgent : IAsyncDisposable
    {
        private const string StatusBody = """{"running":true}""";

        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _loop;

        private CannedAgent(TcpListener listener)
        {
            _listener = listener;
            _loop = AcceptLoopAsync(_cts.Token);
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public static CannedAgent Start(IPAddress address)
        {
            var listener = new TcpListener(address, 0);
            listener.Start();
            return new CannedAgent(listener);
        }

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(ct);
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (SocketException) { break; }

                _ = HandleAsync(client, ct);
            }
        }

        private static async Task HandleAsync(TcpClient client, CancellationToken ct)
        {
            using (client)
            {
                try
                {
                    var stream = client.GetStream();
                    var buffer = new byte[4096];
                    _ = await stream.ReadAsync(buffer, ct); // drain (and ignore) the request line/headers

                    var bodyBytes = Encoding.UTF8.GetBytes(StatusBody);
                    var header =
                        "HTTP/1.1 200 OK\r\n"
                        + "Content-Type: application/json\r\n"
                        + $"Content-Length: {bodyBytes.Length}\r\n"
                        + "Connection: close\r\n\r\n";

                    await stream.WriteAsync(Encoding.UTF8.GetBytes(header), ct);
                    await stream.WriteAsync(bodyBytes, ct);
                    await stream.FlushAsync(ct);
                }
                catch
                {
                    // Connection torn down mid-response — irrelevant to the assertions.
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            _listener.Stop();
            try { await _loop; } catch { }
            _cts.Dispose();
        }
    }
}
