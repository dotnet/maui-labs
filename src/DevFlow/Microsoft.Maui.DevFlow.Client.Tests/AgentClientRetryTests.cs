using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Client.Tests;

/// <summary>
/// <c>TransientFailureRetryCount</c> exists so a client can race an agent — or an ADB port forward —
/// that is not listening yet. Whether a transport failure is recognised as transient depends on how
/// the platform reports it, and the two families differ: modern .NET raises
/// <c>HttpRequestException -&gt; SocketException</c>, while .NET Framework's <c>HttpClientHandler</c>
/// buries it one level deeper, as <c>HttpRequestException -&gt; WebException -&gt; SocketException</c>.
/// Running these on both target families is what stops the classification from silently regressing
/// on one of them.
/// </summary>
public class AgentClientRetryTests
{
    private const string StatusBody = """{"running":true}""";
    private const string TapBody = """{"success":true}""";

    [Theory]
    [InlineData(0, 1, false)]
    [InlineData(1, 2, false)]
    [InlineData(3, 4, false)]
    [InlineData(0, 1, true)]
    [InlineData(1, 2, true)]
    [InlineData(3, 4, true)]
    public async Task TapAsync_MakesExactlyOneAttemptPerConfiguredRetry(
        int retryCount, int expectedAttempts, bool frameworkException)
    {
        // Count calls at the transport boundary: a TCP reset does not guarantee the same
        // exception classification or number of wire requests across HTTP handler families.
        var transport = new RetryTransport(int.MaxValue, () => TransportFailure(frameworkException));
        using var client = new AgentClient("localhost", 9223, transport)
        {
            AutoAcquireMutationLease = false,
            TransientFailureRetryCount = retryCount,
            TransientFailureRetryDelay = TimeSpan.Zero,
        };

        Assert.False(await client.TapAsync("el-1"));

        Assert.Equal(expectedAttempts, transport.Attempts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TapAsync_StopsRetryingOnceTheAgentAnswers(bool frameworkException)
    {
        // Two transport failures, then a real answer: the call must succeed on the third attempt and
        // not keep burning the remaining retries.
        var transport = new RetryTransport(2, () => TransportFailure(frameworkException));
        using var client = new AgentClient("localhost", 9223, transport)
        {
            AutoAcquireMutationLease = false,
            TransientFailureRetryCount = 10,
            TransientFailureRetryDelay = TimeSpan.Zero,
        };

        Assert.True(await client.TapAsync("el-1"));

        Assert.Equal(3, transport.Attempts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TapAsync_DoesNotRetryWhenMutatingRetriesAreDisabled(bool frameworkException)
    {
        // Opting out must be honored, because a retried POST can duplicate the agent-side effect.
        var transport = new RetryTransport(int.MaxValue, () => TransportFailure(frameworkException));
        using var client = new AgentClient("localhost", 9223, transport)
        {
            AutoAcquireMutationLease = false,
            TransientFailureRetryCount = 3,
            TransientFailureRetryDelay = TimeSpan.Zero,
            RetryMutatingRequests = false,
        };

        Assert.False(await client.TapAsync("el-1"));

        Assert.Equal(1, transport.Attempts);
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("cancelled")]
    [InlineData("protocol")]
    [InlineData("trust")]
    public async Task TapAsync_DoesNotRetryNonTransientFailures(string failure)
    {
        var transport = new RetryTransport(int.MaxValue, () => failure switch
        {
            "timeout" => new TaskCanceledException("timeout", new TimeoutException()),
            "cancelled" => new TaskCanceledException(),
            "protocol" => new HttpRequestException("protocol",
                new WebException("protocol", null, WebExceptionStatus.ProtocolError, response: null)),
            "trust" => new HttpRequestException("trust",
                new WebException("trust", null, WebExceptionStatus.TrustFailure, response: null)),
            _ => throw new ArgumentOutOfRangeException(nameof(failure))
        });
        using var client = new AgentClient("localhost", 9223, transport)
        {
            AutoAcquireMutationLease = false,
            TransientFailureRetryCount = 3,
            TransientFailureRetryDelay = TimeSpan.Zero,
        };

        Assert.False(await client.TapAsync("el-1"));

        Assert.Equal(1, transport.Attempts);
    }

    [Fact]
    public void RefusedConnectionChainsAreTransientOnEveryTargetFramework()
    {
        // The shapes both target families actually produce for a refused connection. Asserting them
        // directly pins the classification without depending on platform timing at all.
        var socketFailure = new SocketException((int)SocketError.ConnectionRefused);

        // Modern .NET.
        Assert.True(AgentClient.IsTransientTransportException(
            new HttpRequestException("refused", socketFailure)));

        // .NET Framework's HttpClientHandler, which buries the socket failure under a WebException.
        var webFailure = new WebException(
            "Unable to connect to the remote server",
            socketFailure,
            WebExceptionStatus.ConnectFailure,
            response: null);
        Assert.True(AgentClient.IsTransientTransportException(
            new HttpRequestException("refused", webFailure)));

        // And the bare socket failure itself, however it reaches the classifier.
        Assert.True(AgentClient.IsTransientTransportException(socketFailure));
    }

    [Fact]
    public void DroppedConnectionChainsAreTransientOnEveryTargetFramework()
    {
        // A connection dropped mid-request — an agent restart, or an ADB port forward going away.
        // Modern .NET reports it as an IOException; .NET Framework reports a bare WebException with
        // no inner exception at all, which is why the status has to be inspected.
        Assert.True(AgentClient.IsTransientTransportException(
            new HttpRequestException("dropped", new IOException("reset"))));

        foreach (var status in new[]
        {
            WebExceptionStatus.ConnectionClosed,
            WebExceptionStatus.ConnectFailure,
            WebExceptionStatus.ReceiveFailure,
            WebExceptionStatus.SendFailure,
            WebExceptionStatus.KeepAliveFailure,
            WebExceptionStatus.NameResolutionFailure,
        })
        {
            Assert.True(
                AgentClient.IsTransientTransportException(
                    new HttpRequestException("dropped", new WebException("dropped", null, status, response: null))),
                $"WebExceptionStatus.{status} should be treated as a transient transport failure.");
        }
    }

    [Fact]
    public void ProtocolAndTrustFailuresAreNotTransient()
    {
        // A protocol error is a real HTTP response the caller must see, and a trust or timeout
        // failure will not fix itself on a retry.
        foreach (var status in new[]
        {
            WebExceptionStatus.ProtocolError,
            WebExceptionStatus.TrustFailure,
            WebExceptionStatus.Timeout,
            WebExceptionStatus.RequestCanceled,
        })
        {
            Assert.False(
                AgentClient.IsTransientTransportException(
                    new WebException("no", null, status, response: null)),
                $"WebExceptionStatus.{status} must not be retried.");
        }
    }

    [Fact]
    public void CallerCancellationIsNotTransient()
    {
        // A caller-initiated cancellation and an HttpClient timeout must never be retried, otherwise
        // a cancelled operation would keep running.
        Assert.False(AgentClient.IsTransientTransportException(new TaskCanceledException()));
        Assert.False(AgentClient.IsTransientTransportException(
            new TaskCanceledException("timeout", new TimeoutException())));
    }

    [Fact]
    public async Task TapAsync_SucceedsAgainstAnAgentThatStartsLate()
    {
        // The end-to-end scenario the retry knob exists for: the client fires before the agent has
        // bound its port. TapAsync is used rather than GetStatusAsync because the latter has its own
        // retry window for UI reads, which would mask whether transient-failure retries fired.
        var port = FakeAgent.ReserveFreePort();
        var agentTask = StartAgentAfterAsync(TimeSpan.FromMilliseconds(500), port, TapBody);

        using var client = new AgentClient("localhost", port)
        {
            AutoAcquireMutationLease = false,
            TransientFailureRetryCount = 20,
            TransientFailureRetryDelay = TimeSpan.FromMilliseconds(50),
        };

        try
        {
            Assert.True(await client.TapAsync("el-1"));
        }
        finally
        {
            (await agentTask).Dispose();
        }
    }

    [Fact]
    public async Task GetStatusAsync_SucceedsAgainstAnAgentThatStartsLate()
    {
        // Mirrors the driver-side delayed-listener scenario end to end.
        var port = FakeAgent.ReserveFreePort();
        var agentTask = StartAgentAfterAsync(TimeSpan.FromMilliseconds(500), port, StatusBody);

        using var client = new AgentClient("localhost", port)
        {
            AutoAcquireMutationLease = false,
            TransientFailureRetryCount = 20,
            TransientFailureRetryDelay = TimeSpan.FromMilliseconds(50),
        };

        try
        {
            var status = await client.GetStatusAsync();

            Assert.NotNull(status);
            Assert.True(status!.Running);
        }
        finally
        {
            (await agentTask).Dispose();
        }
    }

    [Fact]
    public async Task ScreenshotResultAsync_RetriesConnectionDroppedMidBody()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var requests = 0;
        var serverTask = Task.Run(async () =>
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                using var connection = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                using var stream = connection.GetStream();
                await ReadHeadersAsync(stream).ConfigureAwait(false);
                Interlocked.Increment(ref requests);
                if (attempt == 0)
                {
                    var partial = Encoding.ASCII.GetBytes(
                        "HTTP/1.1 200 OK\r\n" +
                        "Content-Type: image/png\r\n" +
                        "Content-Length: 12\r\n" +
                        "Connection: close\r\n\r\nabc");
                    await stream.WriteAsync(partial, 0, partial.Length).ConfigureAwait(false);
                    continue;
                }

                var body = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3, 4 };
                var header = Encoding.ASCII.GetBytes(
                    "HTTP/1.1 200 OK\r\n" +
                    "Content-Type: image/png\r\n" +
                    $"Content-Length: {body.Length}\r\n" +
                    "Connection: close\r\n\r\n");
                await stream.WriteAsync(header, 0, header.Length).ConfigureAwait(false);
                await stream.WriteAsync(body, 0, body.Length).ConfigureAwait(false);
            }
        });

        using var client = new AgentClient("localhost", port)
        {
            TransientFailureRetryCount = 1,
            TransientFailureRetryDelay = TimeSpan.Zero,
        };

        try
        {
            var result = await client.ScreenshotResultAsync(
                window: null,
                elementId: null,
                selector: null,
                maxWidth: null,
                scale: null,
                captureEpoch: null,
                registryGeneration: null,
                resolution: new ScreenshotResolutionOptions
                {
                    MaxEncodedBytes = 1024
                });

            Assert.True(result.Success);
            Assert.Equal(2, Volatile.Read(ref requests));
            await serverTask;
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task ScreenshotResultAsync_TimesOutWhenResponseStallsMidBody()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var releaseServer = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var serverTask = Task.Run(async () =>
        {
            using var connection = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
            using var stream = connection.GetStream();
            await ReadHeadersAsync(stream).ConfigureAwait(false);
            var partial = Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\n" +
                "Content-Type: image/png\r\n" +
                "Content-Length: 12\r\n" +
                "Connection: close\r\n\r\nabc");
            await stream.WriteAsync(partial, 0, partial.Length).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
            await releaseServer.Task.ConfigureAwait(false);
        });

        using var client = new AgentClient("localhost", port)
        {
            HttpTimeout = TimeSpan.FromMilliseconds(250),
            TransientFailureRetryCount = 0,
        };

        try
        {
            var stopwatch = Stopwatch.StartNew();
            var result = await client.ScreenshotResultAsync(
                window: null,
                elementId: null,
                selector: null,
                maxWidth: null,
                scale: null,
                captureEpoch: null,
                registryGeneration: null,
                resolution: new ScreenshotResolutionOptions
                {
                    MaxEncodedBytes = 1024
                });

            Assert.False(result.Success);
            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromSeconds(3),
                $"The stalled screenshot took {stopwatch.Elapsed} to time out.");
        }
        finally
        {
            releaseServer.TrySetResult(true);
            listener.Stop();
            await serverTask;
        }
    }

    private static async Task ReadHeadersAsync(NetworkStream stream)
    {
        var buffer = new byte[1024];
        using var received = new MemoryStream();
        while (true)
        {
            var read = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
            if (read <= 0)
                return;
            received.Write(buffer, 0, read);
            var data = received.ToArray();
            for (var i = 0; i + 3 < data.Length; i++)
            {
                if (data[i] == '\r' && data[i + 1] == '\n' &&
                    data[i + 2] == '\r' && data[i + 3] == '\n')
                {
                    return;
                }
            }
        }
    }

    private static Task<FakeAgent> StartAgentAfterAsync(TimeSpan delay, int port, string body)
        => Task.Run(async () =>
        {
            await Task.Delay(delay).ConfigureAwait(false);
            return FakeAgent.Start(IPAddress.Loopback, port, _ => FakeAgent.Response.Json(body));
        });

    private static HttpRequestException TransportFailure(bool frameworkException)
    {
        var reset = new SocketException((int)SocketError.ConnectionReset);
        return new HttpRequestException("Connection reset", frameworkException
            ? new WebException("Connection reset", reset, WebExceptionStatus.ReceiveFailure, response: null)
            : reset);
    }

    private sealed class RetryTransport(int failuresBeforeSuccess, Func<Exception> failure) : HttpMessageHandler
    {
        public int Attempts { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Attempts++;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/v1/ui/actions/tap", request.RequestUri!.AbsolutePath);
            Assert.Contains("\"elementId\":\"el-1\"", await request.Content!.ReadAsStringAsync());
            if (Attempts <= failuresBeforeSuccess)
                throw failure();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(TapBody)
            };
        }
    }
}
