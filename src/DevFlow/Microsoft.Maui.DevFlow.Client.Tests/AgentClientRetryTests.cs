using System.Diagnostics;
using System.Net;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Client.Tests;

/// <summary>
/// <c>TransientFailureRetryCount</c> exists so a client can race an agent — or an ADB port forward —
/// that is not listening yet. Whether a connection refusal is recognised as transient depends on how
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

    [Fact]
    public async Task TapAsync_RetriesTransientTransportFailures()
    {
        // Nothing ever listens on this port, so every attempt fails the same way. Comparing two runs
        // isolates the retry loop from however long a connection refusal happens to take on the host:
        // the difference can only come from retries, since the backoff (400 + 800 ms) and the extra
        // connection attempts both happen exclusively on the retrying run.
        var port = FakeAgent.ReserveFreePort();
        var retryDelay = TimeSpan.FromMilliseconds(400);

        // Warm up first: on some hosts the very first refused connect is markedly slower than later
        // ones, which would otherwise inflate the baseline and mask the retries.
        await TimeTapAsync(port, retryCount: 0, retryDelay);

        var withoutRetries = await TimeTapAsync(port, retryCount: 0, retryDelay);
        var withRetries = await TimeTapAsync(port, retryCount: 2, retryDelay);

        var difference = withRetries - withoutRetries;
        Assert.True(
            difference >= TimeSpan.FromMilliseconds(800),
            $"Expected two retries to add at least the 1200 ms backoff, but the retrying call took only "
                + $"{difference.TotalMilliseconds:F0} ms longer ({withRetries.TotalMilliseconds:F0} ms vs "
                + $"{withoutRetries.TotalMilliseconds:F0} ms). Transport failures are most likely no longer "
                + "classified as transient on this target framework.");
    }

    [Fact]
    public async Task TapAsync_SucceedsAgainstAnAgentThatStartsLate()
    {
        // The scenario the retry knob is for: the client fires before the agent has bound its port.
        // TapAsync is used rather than GetStatusAsync because the latter has its own retry window for
        // UI reads, which would mask whether transient-failure retries fired at all.
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

    [Fact]
    public async Task TapAsync_MutatingRetriesCanBeDisabled()
    {
        // Opting out must be honored, because a retried POST can duplicate the agent-side effect.
        var port = FakeAgent.ReserveFreePort();
        var retryDelay = TimeSpan.FromMilliseconds(400);

        await TimeTapAsync(port, retryCount: 0, retryDelay);

        var baseline = await TimeTapAsync(port, retryCount: 0, retryDelay);
        var optedOut = await TimeTapAsync(port, retryCount: 2, retryDelay, retryMutatingRequests: false);

        Assert.True(
            optedOut - baseline < TimeSpan.FromMilliseconds(800),
            $"RetryMutatingRequests=false should not retry, but the call took "
                + $"{(optedOut - baseline).TotalMilliseconds:F0} ms longer than the non-retrying baseline.");
    }

    private static async Task<TimeSpan> TimeTapAsync(
        int port,
        int retryCount,
        TimeSpan retryDelay,
        bool retryMutatingRequests = true)
    {
        using var client = new AgentClient("localhost", port)
        {
            TransientFailureRetryCount = retryCount,
            TransientFailureRetryDelay = retryDelay,
            RetryMutatingRequests = retryMutatingRequests,
        };

        var stopwatch = Stopwatch.StartNew();
        Assert.False(await client.TapAsync("el-1"));
        stopwatch.Stop();
        return stopwatch.Elapsed;
    }

    private static Task<FakeAgent> StartAgentAfterAsync(TimeSpan delay, int port, string body)
        => Task.Run(async () =>
        {
            await Task.Delay(delay).ConfigureAwait(false);
            return FakeAgent.Start(IPAddress.Loopback, port, _ => FakeAgent.Response.Json(body));
        });
}
