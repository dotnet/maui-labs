using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

[Collection("CLI")]
public class BrokerHeartbeatTests
{
    [Fact]
    public async Task ResponsiveAgent_RemainsRegistered_AndBothReadEndpointsAgree()
    {
        await using var broker = await TestBroker.StartAsync();
        using var agent = await broker.RegisterAsync(heartbeat: true);
        using var stop = new CancellationTokenSource();
        var responder = RespondToHeartbeatsAsync(agent, stop.Token);

        await Task.Delay(1100);
        Assert.Equal(1, broker.Server.AgentCount);
        Assert.Single(await broker.ListAgentsAsync());
        Assert.Equal(1, await broker.HealthAgentCountAsync());

        stop.Cancel();
        agent.Abort();
        await responder;
    }

    [Fact]
    public async Task HalfOpenAgent_WithoutPong_IsEvictedAndPortIsReusable()
    {
        await using var broker = await TestBroker.StartAsync();
        using var agent = await broker.RegisterAsync(heartbeat: true);
        var originalPort = (await broker.ListAgentsAsync())[0].Port;

        await broker.WaitForCountAsync(0);
        Assert.Empty(await broker.ListAgentsAsync());
        Assert.Equal(0, await broker.HealthAgentCountAsync());

        using var replacement = await broker.RegisterAsync(heartbeat: false);
        Assert.Equal(originalPort, (await broker.ListAgentsAsync())[0].Port);
    }

    [Fact]
    public async Task LegacyAgent_IsNotEvictedForMissingHeartbeatSupport()
    {
        await using var broker = await TestBroker.StartAsync();
        using var agent = await broker.RegisterAsync(heartbeat: false);
        await Task.Delay(1100);
        Assert.Single(await broker.ListAgentsAsync());
    }

    [Fact]
    public async Task WrongHeartbeatNonce_DoesNotKeepAgentRegistered()
    {
        await using var broker = await TestBroker.StartAsync();
        using var agent = await broker.RegisterAsync(heartbeat: true);
        var buffer = new byte[256];
        var ping = await agent.ReceiveAsync(buffer, CancellationToken.None);
        Assert.Equal(WebSocketMessageType.Text, ping.MessageType);
        var wrongPong = Encoding.UTF8.GetBytes("{\"type\":\"pong\",\"nonce\":\"wrong\"}");
        await agent.SendAsync(wrongPong, WebSocketMessageType.Text, true, CancellationToken.None);

        await broker.WaitForCountAsync(0);
        Assert.Empty(await broker.ListAgentsAsync());
    }

    [Fact]
    public async Task SupersededSocket_CannotEvictNewRegistration()
    {
        await using var broker = await TestBroker.StartAsync();
        using var old = await broker.RegisterAsync(heartbeat: true);
        using var replacement = await broker.RegisterAsync(heartbeat: false);
        old.Abort();
        await Task.Delay(250);
        Assert.Single(await broker.ListAgentsAsync());
        Assert.Equal(1, await broker.HealthAgentCountAsync());
    }

    private static async Task RespondToHeartbeatsAsync(ClientWebSocket socket, CancellationToken token)
    {
        var buffer = new byte[256];
        try
        {
            while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                var received = await socket.ReceiveAsync(buffer, token);
                if (received.MessageType == WebSocketMessageType.Close)
                    break;
                using var ping = JsonDocument.Parse(buffer.AsMemory(0, received.Count));
                var nonce = ping.RootElement.GetProperty("nonce").GetString();
                var pong = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type = "pong", nonce }));
                await socket.SendAsync(pong, WebSocketMessageType.Text, true, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (WebSocketException) when (socket.State != WebSocketState.Open) { }
    }

    private sealed class TestBroker : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _run;
        private readonly string _directory;
        private readonly HttpClient _http = new();

        public BrokerServer Server { get; }
        public int Port { get; }

        private TestBroker(int port, string directory)
        {
            Port = port;
            _directory = directory;
            Server = new BrokerServer(port, TimeSpan.FromMinutes(5), null, null,
                TimeSpan.FromMilliseconds(150), TimeSpan.FromMilliseconds(600),
                Path.Combine(directory, "broker.json"));
            _run = Server.RunAsync(_stop.Token);
        }

        public static async Task<TestBroker> StartAsync()
        {
            using var portReservation = new TcpListener(IPAddress.Loopback, 0);
            portReservation.Start();
            var port = ((IPEndPoint)portReservation.LocalEndpoint).Port;
            portReservation.Stop();
            var broker = new TestBroker(port, Path.Combine(Path.GetTempPath(), "devflow-heartbeat-" + Guid.NewGuid()));
            for (var i = 0; i < 100 && !broker.Server.IsRunning; i++)
                await Task.Delay(10);
            Assert.True(broker.Server.IsRunning);
            return broker;
        }

        public async Task<ClientWebSocket> RegisterAsync(bool heartbeat)
        {
            var socket = new ClientWebSocket();
            await socket.ConnectAsync(new Uri($"ws://localhost:{Port}/ws/agent"), CancellationToken.None);
            var registration = JsonSerializer.Serialize(new
            {
                type = "register",
                project = "heartbeat-test",
                tfm = "net10.0",
                platform = "macos",
                appName = "Test agent",
                processId = 321,
                supportsHeartbeat = heartbeat
            });
            await socket.SendAsync(Encoding.UTF8.GetBytes(registration), WebSocketMessageType.Text, true, CancellationToken.None);
            var response = new byte[256];
            var received = await socket.ReceiveAsync(response, CancellationToken.None);
            using var json = JsonDocument.Parse(response.AsMemory(0, received.Count));
            Assert.Equal("registered", json.RootElement.GetProperty("type").GetString());
            return socket;
        }

        public async Task<AgentRegistration[]> ListAgentsAsync()
            => JsonSerializer.Deserialize<AgentRegistration[]>(
                await _http.GetStringAsync($"http://localhost:{Port}/api/agents"))!;

        public async Task<int> HealthAgentCountAsync()
        {
            using var json = JsonDocument.Parse(await _http.GetStringAsync($"http://localhost:{Port}/api/health"));
            return json.RootElement.GetProperty("agents").GetInt32();
        }

        public async Task WaitForCountAsync(int count)
        {
            for (var i = 0; i < 100; i++)
            {
                if (Server.AgentCount == count) return;
                await Task.Delay(20);
            }
            Assert.Equal(count, Server.AgentCount);
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            await _run;
            Server.Dispose();
            _http.Dispose();
            _stop.Dispose();
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
    }
}
