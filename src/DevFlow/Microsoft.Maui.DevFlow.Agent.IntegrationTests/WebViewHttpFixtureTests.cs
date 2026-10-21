using System.Net.Sockets;
using Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests;

public class WebViewHttpFixtureTests
{
    [Fact]
    public async Task ClientReset_IncompleteRequest_DoesNotStopOtherConnectionsOrDisposal()
    {
        var fixture = new WebViewHttpFixture();
        var port = fixture.Port;
        try
        {
            using var idle = new TcpClient();
            await idle.ConnectAsync("127.0.0.1", port);
            for (var attempt = 1; attempt <= 20; attempt++)
            {
                using var abandoned = new TcpClient();
                await abandoned.ConnectAsync("127.0.0.1", port);
                await WaitForAsync(() => fixture.AcceptedConnectionCount == attempt + 1);
                await abandoned.GetStream().WriteAsync(
                    System.Text.Encoding.ASCII.GetBytes("GET /index.html HTTP/1.1\r\nX-Abandoned: "));
                await WaitForAsync(() => fixture.StartedRequestCount == attempt);
                abandoned.Client.LingerState = new LingerOption(true, 0);
                abandoned.Client.Close();
                await WaitForAsync(() => fixture.ClientDisconnects.Count == attempt);
            }
            Assert.All(fixture.ClientDisconnects, error =>
                Assert.True(error is SocketError.ConnectionReset or SocketError.ConnectionAborted, error.ToString()));
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            Assert.Contains("HTTP WebView fixture", await http.GetStringAsync(fixture.GetUrl("maccatalyst")));
        }
        finally
        {
            await fixture.DisposeAsync();
        }
        using var socket = new TcpClient();
        await Assert.ThrowsAsync<SocketException>(() => socket.ConnectAsync("127.0.0.1", port));
    }

    static async Task WaitForAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
            await Task.Delay(25, timeout.Token);
    }
}
