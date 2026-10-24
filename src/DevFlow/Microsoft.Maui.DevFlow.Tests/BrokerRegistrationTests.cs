using Microsoft.Maui.DevFlow.Agent.Core;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Microsoft.Maui.DevFlow.Tests;

public class BrokerRegistrationTests
{
    [Fact]
    public async Task Registration_HeartbeatChallenge_RespondsWithMatchingNonce()
    {
        using var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        var port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();

        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var broker = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync().WaitAsync(deadline.Token);
            var socket = (await context.AcceptWebSocketAsync(null)).WebSocket;
            using (socket)
            {
                var buffer = new byte[1024];
                var request = await socket.ReceiveAsync(buffer, deadline.Token);
                using var registration = JsonDocument.Parse(buffer.AsMemory(0, request.Count));
                Assert.True(registration.RootElement.GetProperty("supportsHeartbeat").GetBoolean());

                var registered = Encoding.UTF8.GetBytes("{\"type\":\"registered\",\"id\":\"test\",\"port\":10223}");
                await socket.SendAsync(registered, WebSocketMessageType.Text, true, deadline.Token);
                var ping = Encoding.UTF8.GetBytes("{\"type\":\"ping\",\"nonce\":\"challenge-123\"}");
                await socket.SendAsync(ping, WebSocketMessageType.Text, true, deadline.Token);
                var response = await socket.ReceiveAsync(buffer, deadline.Token);
                using var pong = JsonDocument.Parse(buffer.AsMemory(0, response.Count));
                Assert.Equal("pong", pong.RootElement.GetProperty("type").GetString());
                Assert.Equal("challenge-123", pong.RootElement.GetProperty("nonce").GetString());
            }
        });

        using var agent = new BrokerRegistration("test.csproj", "net10.0", "macos", "Test", port);
        Assert.Equal(10223, await agent.TryRegisterAsync(TimeSpan.FromSeconds(3)));
        await broker;
    }

    [Fact]
    public void BrokerHttpHosts_PrioritizesLiteralLoopbackWithLocalhostFallback()
    {
        Assert.Equal(new[] { "127.0.0.1", "localhost" }, BrokerRegistration.BrokerHttpHosts);
    }

    [Theory]
    [InlineData(System.Net.HttpStatusCode.BadRequest, true)]
    [InlineData(System.Net.HttpStatusCode.NotFound, true)]
    [InlineData(System.Net.HttpStatusCode.Conflict, false)]
    [InlineData(System.Net.HttpStatusCode.InternalServerError, false)]
    public void ShouldTryAlternateBrokerHost_ResponseStatus_ReturnsExpected(
        System.Net.HttpStatusCode statusCode,
        bool expected)
    {
        Assert.Equal(expected, BrokerRegistration.ShouldTryAlternateBrokerHost(statusCode));
    }
}
