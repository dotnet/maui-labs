using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.Cli.DevFlow.Execution;
using Microsoft.Maui.DevFlow.Driver;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

/// <summary>
/// Readiness is not registration. These pin the difference, because conflating them made
/// <c>flow run</c> fail reproducibly on a healthy app that was merely slow to start.
/// </summary>
public class ExactAgentBindingReadinessTests
{
    private static ExactAgentBindingExpectation Expectation(
        string platform = "android",
        string targetFramework = "net10.0-android",
        IReadOnlyList<string>? aliases = null,
        int? processId = null) => new()
    {
        SessionId = "session-1",
        TargetFramework = targetFramework,
        Platform = platform,
        PlatformAliases = aliases ?? [platform],
        PackageId = "com.example.app",
        ProcessId = processId,
    };

    private static AgentStatus Status(bool running) => new()
    {
        Running = running,
        App = new AppDescriptor { PackageId = "com.example.app" },
    };

    private static ExactAgentBindingResolver Resolver() => new(
        _ => Task.FromResult<AgentRegistration[]?>([]),
        pollInterval: TimeSpan.Zero);

    [Fact]
    public async Task WaitForLiveStatus_AgentThatBecomesReadyLate_Succeeds()
    {
        // The exact shape observed on a freshly installed Debug build: registered and forwarded,
        // but not yet answering "running" when the first probe lands.
        var probes = 0;
        var status = await Resolver().WaitForLiveStatusAsync(
            () =>
            {
                probes++;
                return Task.FromResult<AgentStatus?>(probes < 3 ? null : Status(running: true));
            },
            Expectation(),
            TimeSpan.FromSeconds(30));

        Assert.True(status.Running);
        Assert.True(probes >= 3);
    }

    [Fact]
    public async Task WaitForLiveStatus_AgentThatAnswersButNeverRuns_ReportsNotRunning()
    {
        var error = await Assert.ThrowsAsync<FlowExecutionException>(() =>
            Resolver().WaitForLiveStatusAsync(
                () => Task.FromResult<AgentStatus?>(Status(running: false)),
                Expectation(),
                TimeSpan.Zero));

        Assert.Equal("agent-not-running", error.Code);
    }

    [Fact]
    public async Task WaitForLiveStatus_AgentThatNeverAnswers_ReportsNotReachable()
    {
        // A distinct code, because "never answered" points at forwarding and ports while
        // "answered but not running" points at the app, and one message for both costs a
        // reproduction to tell apart.
        var error = await Assert.ThrowsAsync<FlowExecutionException>(() =>
            Resolver().WaitForLiveStatusAsync(
                () => Task.FromResult<AgentStatus?>(null),
                Expectation(),
                TimeSpan.Zero));

        Assert.Equal("agent-not-reachable", error.Code);
    }

    [Fact]
    public async Task WaitForLiveStatus_AgentThatAnswersThenGoesSilent_SaysItStoppedAnswering()
    {
        // Reachable-then-silent is the crash shape. The code stays 'agent-not-running' because the
        // port worked and the app is still where the fault is, but "never reported itself running"
        // describes a probe sequence that did not happen and hides the one fact worth acting on.
        var probes = 0;

        var error = await Assert.ThrowsAsync<FlowExecutionException>(() =>
            Resolver().WaitForLiveStatusAsync(
                () =>
                {
                    probes++;
                    return Task.FromResult<AgentStatus?>(probes == 1 ? Status(running: false) : null);
                },
                Expectation(),
                TimeSpan.FromMilliseconds(50)));

        Assert.Equal("agent-not-running", error.Code);
        Assert.Contains("stopped answering", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("never reported itself running", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WaitForLiveStatus_ReadThatThrows_IsTreatedAsNotYetReady()
    {
        var probes = 0;
        var status = await Resolver().WaitForLiveStatusAsync(
            () =>
            {
                probes++;
                if (probes < 3)
                    throw new HttpRequestException("connection refused");
                return Task.FromResult<AgentStatus?>(Status(running: true));
            },
            Expectation(),
            TimeSpan.FromSeconds(30));

        Assert.True(status.Running);
    }

    [Fact]
    public async Task WaitForLiveStatus_ReadThatThrowsOnTheDeadline_StillReportsNotReachable()
    {
        // The probe that lands on the deadline used to escape the retry filter, so the operator
        // got a raw HttpRequestException instead of the structured outcome every caller
        // classifies. The transport failure belongs in the inner exception, not in the contract.
        var transport = new HttpRequestException("connection refused");

        var error = await Assert.ThrowsAsync<FlowExecutionException>(() =>
            Resolver().WaitForLiveStatusAsync(
                () => throw transport,
                Expectation(),
                TimeSpan.Zero));

        Assert.Equal("agent-not-reachable", error.Code);
        Assert.Same(transport, error.InnerException);
        Assert.Contains(nameof(HttpRequestException), error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("connection refused", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WaitForLiveStatus_AgentThatAnswersAfterATransportBlip_DoesNotBlameTheBlip()
    {
        // An early refused connection followed by an agent that answers and stays not-running is a
        // fault in the app, not in transport. Carrying the stale exception into the verdict would
        // point the reader at forwarding while the message says the agent answered.
        var probes = 0;

        var error = await Assert.ThrowsAsync<FlowExecutionException>(() =>
            Resolver().WaitForLiveStatusAsync(
                () =>
                {
                    if (probes++ == 0)
                        throw new HttpRequestException("connection refused");
                    return Task.FromResult<AgentStatus?>(Status(running: false));
                },
                Expectation(),
                TimeSpan.FromMilliseconds(50)));

        Assert.Equal("agent-not-running", error.Code);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain(nameof(HttpRequestException), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateLiveStatus_ProcessMismatchOnWindows_NamesBothProcessIdsAndSingleInstance()
    {
        // Windows reached this check and reported only "a different process identity", which is
        // not enough evidence to tell a genuine bind to the wrong app from WinUI redirecting the
        // launch into an instance an earlier run left running.
        var error = Assert.Throws<FlowExecutionException>(
            () => ExactAgentBindingResolver.ValidateLiveStatus(
                MismatchedStatus(),
                Expectation(
                    platform: "windows",
                    targetFramework: "net10.0-windows10.0.19041.0",
                    aliases: ["windows", "winui"],
                    processId: 1234)));

        Assert.Equal("agent-process-mismatch", error.Code);
        Assert.Contains("1234", error.Message, StringComparison.Ordinal);
        Assert.Contains("5678", error.Message, StringComparison.Ordinal);
        Assert.Contains("single-instance", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateLiveStatus_ProcessMismatchOffWindows_NamesBothProcessIdsWithoutWinUiCause()
    {
        // Android has no activation redirection to blame, so asserting one there would be a
        // confident explanation of something that cannot have happened. The ids still have to be
        // named, because they are the whole evidence for the refusal.
        var error = Assert.Throws<FlowExecutionException>(
            () => ExactAgentBindingResolver.ValidateLiveStatus(
                MismatchedStatus(),
                Expectation(processId: 1234)));

        Assert.Equal("agent-process-mismatch", error.Code);
        Assert.Contains("1234", error.Message, StringComparison.Ordinal);
        Assert.Contains("5678", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("single-instance", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("WinUI", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateLiveStatus_ProcessMismatchOnWpf_DoesNotBlameWinUiSingleInstance()
    {
        // WPF ships the same Windows TFM as WinUI, so the target framework cannot decide this;
        // only the platform aliases can. WPF has no single-instance redirection, and blaming one
        // would send the operator to close an app that is not the cause.
        var error = Assert.Throws<FlowExecutionException>(
            () => ExactAgentBindingResolver.ValidateLiveStatus(
                MismatchedStatus(),
                Expectation(
                    platform: "wpf",
                    targetFramework: "net10.0-windows10.0.19041.0",
                    aliases: ["wpf"],
                    processId: 1234)));

        Assert.Equal("agent-process-mismatch", error.Code);
        Assert.Contains("1234", error.Message, StringComparison.Ordinal);
        Assert.Contains("5678", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("single-instance", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("WinUI", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WaitForLiveStatus_CancelledReadThatSurfacesAsTransport_StaysCancelled()
    {
        // A request cancelled mid-flight can come back as a transport or disposal failure instead
        // of OperationCanceledException. Reporting that as 'agent-not-reachable' would turn a
        // cancellation whose completion nobody proved into a definite infrastructure verdict.
        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Resolver().WaitForLiveStatusAsync(
                () =>
                {
                    cancellation.Cancel();
                    throw new HttpRequestException("the request was aborted");
                },
                Expectation(),
                TimeSpan.Zero,
                cancellation.Token));
    }

    private static AgentStatus MismatchedStatus() => new()
    {
        Running = true,
        App = new AppDescriptor { PackageId = "com.example.app", ProcessId = 5678 },
    };
}
