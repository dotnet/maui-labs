using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

/// <summary>
/// Covers the broker's active agent-liveness reaper (issue #342): crashed/half-open agents must
/// be evicted promptly instead of lingering as stale registrations.
/// </summary>
[Collection("CLI")]
public class BrokerServerReaperTests
{
    [Fact]
    public async Task Reaper_KeepsAgent_WhenLivenessCheckSucceeds()
    {
        await WithBrokerAsync(
            reapInterval: TimeSpan.FromMilliseconds(150),
            liveness: static (_, _) => Task.FromResult(true),
            body: async (broker, port) =>
            {
                using var agent = await RegisterAgentAsync(port, "/proj/App.csproj", "net10.0-macos");
                Assert.Equal(1, broker.AgentCount);

                // Let several reap cycles elapse — a live agent must survive every sweep.
                await Task.Delay(TimeSpan.FromSeconds(1));

                Assert.Equal(1, broker.AgentCount);
            });
    }

    [Fact]
    public async Task Reaper_EvictsAgent_WhenLivenessCheckFails()
    {
        await WithBrokerAsync(
            reapInterval: TimeSpan.FromMilliseconds(150),
            liveness: static (_, _) => Task.FromResult(false),
            body: async (broker, port) =>
            {
                using var agent = await RegisterAgentAsync(port, "/proj/App.csproj", "net10.0-macos");

                await WaitUntilAsync(() => broker.AgentCount == 0, TimeSpan.FromSeconds(5));

                Assert.Equal(0, broker.AgentCount);
            });
    }

    [Fact]
    public async Task Reaper_EvictsAgent_WhenConnectionAbortedLikeACrash()
    {
        await WithBrokerAsync(
            reapInterval: TimeSpan.FromMilliseconds(150),
            liveness: null, // exercise the real WebSocket ping-based liveness check
            body: async (broker, port) =>
            {
                var agent = await RegisterAgentAsync(port, "/proj/App.csproj", "net10.0-macos");
                await WaitUntilAsync(() => broker.AgentCount == 1, TimeSpan.FromSeconds(2));
                Assert.Equal(1, broker.AgentCount);

                // Simulate an app crash: tear the socket down with no clean close handshake.
                agent.Abort();
                agent.Dispose();

                await WaitUntilAsync(() => broker.AgentCount == 0, TimeSpan.FromSeconds(10));
                Assert.Equal(0, broker.AgentCount);
            });
    }

    [Fact]
    public async Task Reaper_ReleasesPort_AfterEviction_AllowingReuse()
    {
        await WithBrokerAsync(
            reapInterval: TimeSpan.FromMilliseconds(150),
            liveness: static (_, _) => Task.FromResult(false),
            body: async (broker, port) =>
            {
                var firstPort = await RegisterAgentAndGetPortAsync(port, "/proj/App.csproj", "net10.0-macos");
                await WaitUntilAsync(() => broker.AgentCount == 0, TimeSpan.FromSeconds(5));
                Assert.Equal(0, broker.AgentCount);

                // A fresh registration after eviction must be able to reclaim the released port.
                var secondPort = await RegisterAgentAndGetPortAsync(port, "/proj/App.csproj", "net10.0-macos");
                Assert.Equal(firstPort, secondPort);
            });
    }

    [Fact]
    public async Task Reaper_BrokerHttpEndpoints_AgreeAndDropStaleAgent_AfterCrash()
    {
        // Regression for the issue #342 contradiction: `agent status`/`agents` (read /api/agents)
        // must never list an agent that `broker status`/`diagnose` (read /api/health agent count)
        // report as gone. Both endpoints read the same _agents store, so once the reaper evicts a
        // crashed agent they agree — there is a single source of truth with a single liveness filter.
        await WithBrokerAsync(
            reapInterval: TimeSpan.FromMilliseconds(150),
            liveness: null, // real WebSocket ping-based liveness
            body: async (broker, port) =>
            {
                var agent = await RegisterAgentAsync(port, "/proj/App.csproj", "net10.0-macos");
                await WaitUntilAsync(() => broker.AgentCount == 1, TimeSpan.FromSeconds(2));

                // Before the crash both endpoints agree the agent is present.
                Assert.Equal(1, await GetHealthAgentCountAsync(port));
                Assert.Equal(1, await GetAgentsListLengthAsync(port));

                // Simulate an app crash: tear the socket down with no clean close handshake.
                agent.Abort();
                agent.Dispose();

                await WaitUntilAsync(() => broker.AgentCount == 0, TimeSpan.FromSeconds(10));

                // After eviction the two endpoints still agree — and the stale agent is gone from both.
                var healthCount = await GetHealthAgentCountAsync(port);
                var agentsLength = await GetAgentsListLengthAsync(port);
                Assert.Equal(0, healthCount);
                Assert.Equal(0, agentsLength);
                Assert.Equal(healthCount, agentsLength);
            });
    }

    private static async Task<int> GetHealthAgentCountAsync(int brokerPort)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var body = await http.GetStringAsync($"http://localhost:{brokerPort}/api/health");
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("agents").GetInt32();
    }

    private static async Task<int> GetAgentsListLengthAsync(int brokerPort)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var body = await http.GetStringAsync($"http://localhost:{brokerPort}/api/agents");
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetArrayLength();
    }

    [Fact]
    public async Task ReadEndpoints_EvictDeadAgentOnRead_WithoutWaitingForTimerSweep()
    {
        // The timer sweep is effectively disabled (10 min), so any eviction observed here can only
        // come from the read-time liveness filter that `agent status`/`agents`/`diagnose` rely on.
        await WithBrokerAsync(
            reapInterval: TimeSpan.FromMinutes(10),
            liveness: static (_, _) => Task.FromResult(false), // every probe reports the agent dead
            body: async (broker, port) =>
            {
                using var agent = await RegisterAgentAsync(port, "/proj/App.csproj", "net10.0-macos");
                Assert.Equal(1, broker.AgentCount);

                // A single read of either endpoint must filter the dead agent out — and both agree.
                var agentsLength = await GetAgentsListLengthAsync(port);
                var healthCount = await GetHealthAgentCountAsync(port);

                Assert.Equal(0, agentsLength);
                Assert.Equal(0, healthCount);
                Assert.Equal(0, broker.AgentCount);
            });
    }

    [Fact]
    public async Task Handshake_ReleasesAssignedPort_WhenAgentVanishesBeforePublish()
    {
        // Regression for the port leak that the handshake reorder (send `registered` before publishing
        // to _agents) could open (#342): if that handshake write fails — the agent crashed right after
        // sending its register frame — the agent is never published, so neither EvictAgent nor the
        // reaper can ever reclaim its port. The broker must release the freshly assigned port inline,
        // or repeated early disconnects slowly exhaust the pool. This path is distinct from the
        // post-publish eviction the other reaper tests cover.
        var failNextHandshake = 1;
        Func<WebSocket, byte[], CancellationToken, Task> sender = async (ws, payload, ct) =>
        {
            // Fail only the first handshake, simulating the agent vanishing mid-register; later
            // registrations send normally.
            if (Interlocked.Exchange(ref failNextHandshake, 0) == 1)
                throw new WebSocketException("simulated handshake send failure");
            await ws.SendAsync(payload, WebSocketMessageType.Text, true, ct);
        };

        await WithBrokerAsync(
            reapInterval: TimeSpan.FromMinutes(10), // isolate the assertion from the timer sweep
            liveness: static (_, _) => Task.FromResult(true),
            handshakeResponseSender: sender,
            body: async (broker, port) =>
            {
                // First registration: the broker assigns a port, the handshake send fails, and the
                // agent is never published. With the fix the assigned port is released (no leak).
                await TryRegisterExpectingNoResponseAsync(port, "/proj/App.csproj", "net10.0-macos");

                await WaitUntilAsync(() => broker.AssignedPortCount == 0, TimeSpan.FromSeconds(5));
                Assert.Equal(0, broker.AgentCount);
                Assert.Equal(0, broker.AssignedPortCount);

                // A subsequent healthy registration succeeds and holds exactly one port — proving the
                // failed handshake stranded nothing.
                using var agent = await RegisterAgentAsync(port, "/proj/App.csproj", "net10.0-macos");
                Assert.Equal(1, broker.AgentCount);
                Assert.Equal(1, broker.AssignedPortCount);
            });
    }

    private static async Task WithBrokerAsync(
        TimeSpan reapInterval,
        Func<WebSocket, CancellationToken, Task<bool>>? liveness,
        Func<BrokerServer, int, Task> body,
        Func<WebSocket, byte[], CancellationToken, Task>? handshakeResponseSender = null)
    {
        var tempDir = Directory.CreateTempSubdirectory("maui-broker-reaper-");
        var previousOverride = BrokerPaths.ConfigDirOverride;
        BrokerPaths.ConfigDirOverride = tempDir.FullName;

        var port = GetFreePort();
        using var cts = new CancellationTokenSource();
        var broker = new BrokerServer(
            port,
            idleTimeout: TimeSpan.FromMinutes(10),
            log: null,
            reapInterval: reapInterval,
            keepAliveInterval: TimeSpan.FromSeconds(30),
            agentLivenessCheck: liveness,
            handshakeResponseSender: handshakeResponseSender);

        var runTask = Task.Run(() => broker.RunAsync(cts.Token));
        try
        {
            await WaitUntilAsync(() => broker.IsRunning, TimeSpan.FromSeconds(5));
            Assert.True(broker.IsRunning, "Broker did not start listening.");

            await body(broker, port);
        }
        finally
        {
            cts.Cancel();
            try { await runTask.WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
            broker.Dispose();
            BrokerPaths.ConfigDirOverride = previousOverride;
            try { tempDir.Delete(recursive: true); } catch { }
        }
    }

    private static async Task<ClientWebSocket> RegisterAgentAsync(int brokerPort, string project, string tfm)
    {
        var ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri($"ws://localhost:{brokerPort}/ws/agent"), CancellationToken.None);

        var registration = JsonSerializer.Serialize(new
        {
            type = "register",
            project,
            tfm,
            platform = "macOS",
            appName = "ReaperTestApp"
        });
        await ws.SendAsync(Encoding.UTF8.GetBytes(registration), WebSocketMessageType.Text, true, CancellationToken.None);

        var buffer = new byte[1024];
        var result = await ws.ReceiveAsync(buffer, CancellationToken.None);
        using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(buffer, 0, result.Count));
        Assert.Equal("registered", doc.RootElement.GetProperty("type").GetString());
        return ws;
    }

    private static async Task<int> RegisterAgentAndGetPortAsync(int brokerPort, string project, string tfm)
    {
        var ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri($"ws://localhost:{brokerPort}/ws/agent"), CancellationToken.None);

        var registration = JsonSerializer.Serialize(new
        {
            type = "register",
            project,
            tfm,
            platform = "macOS",
            appName = "ReaperTestApp"
        });
        await ws.SendAsync(Encoding.UTF8.GetBytes(registration), WebSocketMessageType.Text, true, CancellationToken.None);

        var buffer = new byte[1024];
        var result = await ws.ReceiveAsync(buffer, CancellationToken.None);
        using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(buffer, 0, result.Count));
        var assignedPort = doc.RootElement.GetProperty("port").GetInt32();
        // Close cleanly so this helper drives eviction through the reaper rather than the
        // monitor-loop abort path, keeping the port-reuse assertion unambiguous.
        try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "test done", CancellationToken.None); }
        catch { }
        ws.Dispose();
        return assignedPort;
    }

    /// <summary>
    /// Registers an agent but expects the broker to tear the connection down without a `registered`
    /// reply (used to drive the handshake-send-failure path). Never throws.
    /// </summary>
    private static async Task TryRegisterExpectingNoResponseAsync(int brokerPort, string project, string tfm)
    {
        using var ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri($"ws://localhost:{brokerPort}/ws/agent"), CancellationToken.None);

        var registration = JsonSerializer.Serialize(new
        {
            type = "register",
            project,
            tfm,
            platform = "macOS",
            appName = "ReaperTestApp"
        });
        await ws.SendAsync(Encoding.UTF8.GetBytes(registration), WebSocketMessageType.Text, true, CancellationToken.None);

        try
        {
            var buffer = new byte[1024];
            await ws.ReceiveAsync(buffer, CancellationToken.None);
        }
        catch { /* expected: broker aborted the connection after the failed handshake */ }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (!condition() && sw.Elapsed < timeout)
            await Task.Delay(25);
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }
}
