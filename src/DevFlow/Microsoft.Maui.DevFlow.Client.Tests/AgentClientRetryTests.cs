using System.Net;
using System.Net.Http;
using System.Net.Sockets;
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
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(3, 4)]
    public async Task TapAsync_MakesExactlyOneAttemptPerConfiguredRetry(int retryCount, int expectedAttempts)
    {
        // The agent resets every connection, so each attempt fails the same way at the transport
        // level while still completing a TCP accept — which is what makes the attempts countable
        // rather than inferred from elapsed time.
        using var agent = FakeAgent.Start(_ => FakeAgent.Response.Reset());
        using var client = new AgentClient("localhost", agent.Port)
        {
            TransientFailureRetryCount = retryCount,
            TransientFailureRetryDelay = TimeSpan.Zero,
        };

        Assert.False(await client.TapAsync("el-1"));

        Assert.Equal(expectedAttempts, agent.Requests.Count);
    }

    [Fact]
    public async Task TapAsync_StopsRetryingOnceTheAgentAnswers()
    {
        // Two transport failures, then a real answer: the call must succeed on the third attempt and
        // not keep burning the remaining retries.
        var failuresLeft = 2;
        using var agent = FakeAgent.Start(_ =>
            Interlocked.Decrement(ref failuresLeft) >= 0
                ? FakeAgent.Response.Reset()
                : FakeAgent.Response.Json(TapBody));
        using var client = new AgentClient("localhost", agent.Port)
        {
            TransientFailureRetryCount = 10,
            TransientFailureRetryDelay = TimeSpan.Zero,
        };

        Assert.True(await client.TapAsync("el-1"));

        Assert.Equal(3, agent.Requests.Count);
    }

    [Fact]
    public async Task TapAsync_DoesNotRetryWhenMutatingRetriesAreDisabled()
    {
        // Opting out must be honored, because a retried POST can duplicate the agent-side effect.
        using var agent = FakeAgent.Start(_ => FakeAgent.Response.Reset());
        using var client = new AgentClient("localhost", agent.Port)
        {
            TransientFailureRetryCount = 3,
            TransientFailureRetryDelay = TimeSpan.Zero,
            RetryMutatingRequests = false,
        };

        Assert.False(await client.TapAsync("el-1"));

        Assert.Single(agent.Requests);
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

    private static Task<FakeAgent> StartAgentAfterAsync(TimeSpan delay, int port, string body)
        => Task.Run(async () =>
        {
            await Task.Delay(delay).ConfigureAwait(false);
            return FakeAgent.Start(IPAddress.Loopback, port, _ => FakeAgent.Response.Json(body));
        });
}
