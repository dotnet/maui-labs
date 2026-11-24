using Microsoft.Maui.DevFlow.Agent.Core;

namespace Microsoft.Maui.DevFlow.Tests;

public class BrokerRegistrationTests
{
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

    [Fact]
    public async Task TryRegisterAsync_BrokerAcceptsOnlyLiteralLoopbackHost_Registers()
    {
        // Mirrors the broker's IPv4 endpoint on macOS/Linux, which Android reaches through adb reverse:
        // it serves only the 127.0.0.1 prefix and returns 404 for a "localhost" Host header.
        var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        probe.Start();
        var port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        using var listener = new System.Net.HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        var broker = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync();
            var wsContext = await context.AcceptWebSocketAsync(null);
            using var socket = wsContext.WebSocket;
            var buffer = new byte[4096];
            await socket.ReceiveAsync(buffer, CancellationToken.None);
            var reply = System.Text.Encoding.UTF8.GetBytes("""{"type":"registered","id":"agent-1","port":10345}""");
            await socket.SendAsync(reply, System.Net.WebSockets.WebSocketMessageType.Text, true, CancellationToken.None);
            return context.Request.Headers["Host"];
        });

        using var registration = new BrokerRegistration("proj.csproj", "net11.0-android", "Android", "SampleApp", null, port);
        var assignedPort = await registration.TryRegisterAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(10345, assignedPort);
        Assert.Equal($"127.0.0.1:{port}", await broker.WaitAsync(TimeSpan.FromSeconds(5)));
    }
}
