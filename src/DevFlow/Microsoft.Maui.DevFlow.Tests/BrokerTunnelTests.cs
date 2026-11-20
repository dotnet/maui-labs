using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// Agents that cannot listen on a port (WebAssembly in a browser) register as tunneled: the broker
/// listens on the assigned port and relays each request over the agent's WebSocket. These run a
/// private broker (no shared state file) and drive it with a real tunneled agent and a raw socket.
/// </summary>
public sealed class BrokerTunnelTests : IAsyncLifetime
{
    private readonly CancellationTokenSource _cts = new();
    private BrokerServer _broker = null!;
    private Task _brokerTask = Task.CompletedTask;
    private int _brokerPort;

    public async Task InitializeAsync()
    {
        _brokerPort = FindFreePort();
        _broker = new BrokerServer(_brokerPort, idleTimeout: TimeSpan.FromMinutes(5)) { PublishesState = false };
        _brokerTask = _broker.RunAsync(_cts.Token);
        for (var attempt = 0; attempt < 100 && !_broker.IsRunning; attempt++)
            await Task.Delay(50);
        Assert.True(_broker.IsRunning, "The test broker did not start.");
    }

    public async Task DisposeAsync()
    {
        _cts.Cancel();
        try { await _brokerTask.WaitAsync(TimeSpan.FromSeconds(10)); } catch (OperationCanceledException) { }
        _broker.Dispose();
        _cts.Dispose();
    }

    [Fact]
    public async Task TunneledAgent_AnswersOnAssignedPort()
    {
        using var registration = new BrokerRegistration(
            "TunnelApp.csproj", "net10.0", "Browser", "TunnelApp", sessionId: null, brokerPort: _brokerPort)
        {
            UseTunnel = true
        };
        var assignedPort = await registration.TryRegisterAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(assignedPort);

        var options = new AgentOptions { Port = assignedPort.Value, UseBrokerTunnel = true };
        using var service = new DevFlowAgentService(options);
        service.SetBrokerRegistration(registration);
        service.StartServerOnly(dispatcher: null);
        Assert.True(service.IsRunning);

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using var status = await http.GetAsync($"http://127.0.0.1:{assignedPort}/api/v1/agent/status");
        using var body = JsonDocument.Parse(await status.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        Assert.Equal("application/json", status.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Microsoft.Maui.DevFlow.Agent", body.RootElement.GetProperty("agent").GetProperty("name").GetString());

        using var missing = await http.GetAsync($"http://localhost:{assignedPort}/api/v1/no-such-route?x=1");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("Route not found", JsonDocument.Parse(await missing.Content.ReadAsStringAsync()).RootElement.GetProperty("error").GetString());

        // The DevFlow CLI dials IPv4 loopback while naming the host "localhost".
        using var hostMismatch = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{assignedPort}/api/v1/agent/status");
        hostMismatch.Headers.Host = $"localhost:{assignedPort}";
        using var hostMismatchResponse = await http.SendAsync(hostMismatch);
        Assert.Equal(HttpStatusCode.OK, hostMismatchResponse.StatusCode);
    }

    [Fact]
    public async Task TunneledAgent_IsListedWithTunnelTransport_AndIsNotTheLeaseAuthority()
    {
        using var registration = new BrokerRegistration(
            "TunnelApp.csproj", "net10.0", "Browser", "TunnelApp", sessionId: null, brokerPort: _brokerPort)
        {
            UseTunnel = true
        };
        await registration.TryRegisterAsync(TimeSpan.FromSeconds(5));

        using var http = new HttpClient();
        using var agents = JsonDocument.Parse(await http.GetStringAsync($"http://127.0.0.1:{_brokerPort}/api/agents"));

        var agent = Assert.Single(agents.RootElement.EnumerateArray());
        Assert.Equal("tunnel", agent.GetProperty("transport").GetString());
        Assert.False(registration.HasBrokerAuthority);
    }

    [Fact]
    public async Task Tunnel_RelaysLargeBinaryResponsesAndRequestBodies()
    {
        var payload = new byte[3 * 1_048_576 + 17];
        Random.Shared.NextBytes(payload);
        using var agent = await ConnectAgentAsync(origin: "http://localhost:5123", tunnel: true);
        var port = await RegisterAsync(agent);
        var agentLoop = AnswerOneRequestAsync(agent, request =>
        {
            Assert.Equal("POST", request.GetProperty("method").GetString());
            Assert.Equal("/api/echo?name=a%20b", request.GetProperty("target").GetString());
            Assert.Equal("{\"hello\":\"✓\"}", request.GetProperty("body").GetString());
            return new Dictionary<string, object?>
            {
                ["status"] = 201,
                ["contentType"] = "image/png",
                ["headers"] = new Dictionary<string, string> { ["Retry-After"] = "2" },
                ["bodyBase64"] = Convert.ToBase64String(payload)
            };
        });

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using var response = await http.PostAsync(
            $"http://127.0.0.1:{port}/api/echo?name=a%20b",
            new StringContent("{\"hello\":\"✓\"}", Encoding.UTF8, "application/json"));
        await agentLoop;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("2", Assert.Single(response.Headers.GetValues("Retry-After")));
        Assert.Equal(payload, await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Tunnel_CancelsAgentRequest_WhenClientDisconnects()
    {
        using var agent = await ConnectAgentAsync(origin: null, tunnel: true);
        var port = await RegisterAsync(agent);

        using (var client = new TcpClient())
        {
            await client.ConnectAsync(IPAddress.Loopback, port);
            await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes(
                $"POST /api/v1/ui/tap HTTP/1.1\r\nHost: localhost:{port}\r\nContent-Length: 0\r\n\r\n"));

            using var request = await ReceiveFrameAsync(agent);
            Assert.Equal("http-request", request.RootElement.GetProperty("type").GetString());
            var requestId = request.RootElement.GetProperty("id").GetString();

            client.Close();

            using var cancel = await ReceiveFrameAsync(agent);
            Assert.Equal("http-cancel", cancel.RootElement.GetProperty("type").GetString());
            Assert.Equal(requestId, cancel.RootElement.GetProperty("id").GetString());
        }
    }

    [Fact]
    public async Task Tunnel_CancelsAgentRequest_AndAnswers504_WhenAgentIsTooSlow()
    {
        var brokerPort = FindFreePort();
        using var cts = new CancellationTokenSource();
        using var broker = new BrokerServer(brokerPort, idleTimeout: TimeSpan.FromMinutes(5))
        {
            PublishesState = false,
            TunnelRequestTimeout = TimeSpan.FromMilliseconds(500)
        };
        var brokerTask = broker.RunAsync(cts.Token);
        for (var attempt = 0; attempt < 100 && !broker.IsRunning; attempt++)
            await Task.Delay(50);

        try
        {
            using var agent = await ConnectAgentAsync(origin: null, tunnel: true, brokerPort);
            var port = await RegisterAsync(agent);

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var pending = http.GetAsync($"http://127.0.0.1:{port}/api/v1/agent/status");
            using var request = await ReceiveFrameAsync(agent);
            using var cancel = await ReceiveFrameAsync(agent);
            using var response = await pending;

            Assert.Equal("http-cancel", cancel.RootElement.GetProperty("type").GetString());
            Assert.Equal(request.RootElement.GetProperty("id").GetString(), cancel.RootElement.GetProperty("id").GetString());
            Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        }
        finally
        {
            cts.Cancel();
            try { await brokerTask.WaitAsync(TimeSpan.FromSeconds(10)); } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task TunneledAgent_CancelsRequest_WhenBrokerSendsCancelFrame()
    {
        using var registration = new BrokerRegistration(
            "TunnelApp.csproj", "net10.0", "Browser", "TunnelApp", sessionId: null, brokerPort: _brokerPort)
        {
            UseTunnel = true
        };
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        registration.TunnelRequestHandler = async (_, ct) =>
        {
            using var registrationOnCancel = ct.Register(() => cancelled.TrySetResult());
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return HttpResponse.Error("unreachable", 500);
        };
        var assignedPort = await registration.TryRegisterAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(assignedPort);

        using (var client = new TcpClient())
        {
            await client.ConnectAsync(IPAddress.Loopback, assignedPort.Value);
            await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes(
                $"GET /api/v1/agent/status HTTP/1.1\r\nHost: localhost:{assignedPort}\r\n\r\n"));
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }

        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task BrowserOrigin_MayOnlyRegisterTunneledAgents()
    {
        using var direct = await ConnectAgentAsync(origin: "http://localhost:5123", tunnel: false);
        var buffer = new byte[1024];
        var result = await direct.ReceiveAsync(buffer, CancellationToken.None);

        Assert.Equal(WebSocketMessageType.Close, result.MessageType);
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, direct.CloseStatus);
    }

    [Fact]
    public async Task NonLoopbackOrigin_IsRejected()
    {
        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Origin", "https://example.com");

        await Assert.ThrowsAsync<WebSocketException>(() =>
            socket.ConnectAsync(new Uri($"ws://localhost:{_brokerPort}/ws/agent"), CancellationToken.None));
    }

    [Fact]
    public async Task Tunnel_StopsListening_WhenAgentDisconnects()
    {
        var agent = await ConnectAgentAsync(origin: null, tunnel: true);
        var port = await RegisterAsync(agent);
        agent.Abort();
        agent.Dispose();

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var deadline = DateTime.UtcNow.AddSeconds(10);
        var listening = true;
        while (listening && DateTime.UtcNow < deadline)
        {
            try
            {
                using var response = await http.GetAsync($"http://127.0.0.1:{port}/api/v1/agent/status");
                await Task.Delay(100);
            }
            catch (HttpRequestException)
            {
                listening = false;
            }
        }

        Assert.False(listening, $"The tunnel kept listening on port {port} after its agent left.");
    }

    private async Task<ClientWebSocket> ConnectAgentAsync(string? origin, bool tunnel, int? brokerPort = null)
    {
        var socket = new ClientWebSocket();
        if (origin is not null)
            socket.Options.SetRequestHeader("Origin", origin);
        await socket.ConnectAsync(new Uri($"ws://localhost:{brokerPort ?? _brokerPort}/ws/agent"), CancellationToken.None);

        var registration = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["type"] = "register",
            ["project"] = "Raw.csproj",
            ["tfm"] = "net10.0",
            ["platform"] = "Browser",
            ["appName"] = "Raw",
            ["processId"] = Random.Shared.Next(100_000, 1_000_000),
            ["transport"] = tunnel ? "tunnel" : null
        });
        await socket.SendAsync(Encoding.UTF8.GetBytes(registration), WebSocketMessageType.Text, true, CancellationToken.None);
        return socket;
    }

    private static async Task<int> RegisterAsync(ClientWebSocket agent)
    {
        var buffer = new byte[1024];
        var result = await agent.ReceiveAsync(buffer, CancellationToken.None);
        using var response = JsonDocument.Parse(buffer.AsMemory(0, result.Count));
        Assert.Equal("registered", response.RootElement.GetProperty("type").GetString());
        return response.RootElement.GetProperty("port").GetInt32();
    }

    private static async Task<JsonDocument> ReceiveFrameAsync(ClientWebSocket agent)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var message = new MemoryStream();
        var buffer = new byte[8192];
        WebSocketReceiveResult result;
        do
        {
            result = await agent.ReceiveAsync(buffer, timeout.Token);
            message.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);
        return JsonDocument.Parse(message.ToArray());
    }

    private static async Task AnswerOneRequestAsync(
        ClientWebSocket agent,
        Func<JsonElement, Dictionary<string, object?>> answer)
    {
        using var request = await ReceiveFrameAsync(agent);
        Assert.Equal("http-request", request.RootElement.GetProperty("type").GetString());
        var reply = answer(request.RootElement);
        reply["type"] = "http-response";
        reply["id"] = request.RootElement.GetProperty("id").GetString();
        await agent.SendAsync(JsonSerializer.SerializeToUtf8Bytes(reply), WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private static int FindFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
