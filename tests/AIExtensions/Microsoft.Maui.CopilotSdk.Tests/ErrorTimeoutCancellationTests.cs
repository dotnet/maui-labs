using Microsoft.Extensions.AI;

namespace Microsoft.Maui.CopilotSdk.Tests;

public class ErrorTimeoutCancellationTests
{
    [Fact]
    public async Task Session_error_event_throws_copilot_sdk_exception()
    {
        var backend = new FakeCopilotBackend();
        var session = backend.AddSession(new FakeCopilotSession
        {
            OnSend = (s, _) =>
            {
                s.Emit(SdkEvents.Error("something broke", "E42", "provider_error"));
                return Task.CompletedTask;
            },
        });

        await using var client = TestChatClient.Create(backend);
        var ex = await Assert.ThrowsAsync<CopilotSdkException>(
            () => client.GetResponseAsync(TestExtensions.UserMessage("hi")));

        Assert.Equal("something broke", ex.Message);
        Assert.Equal("E42", ex.ErrorCode);
        Assert.Equal("provider_error", ex.ErrorType);

        // An error path aborts and disposes the session.
        Assert.Equal(1, session.AbortCount);
        Assert.Equal(1, session.DisposeCount);
    }

    [Fact]
    public async Task Abort_event_throws_operation_canceled()
    {
        var backend = new FakeCopilotBackend();
        backend.AddSession(new FakeCopilotSession
        {
            OnSend = (s, _) =>
            {
                s.Emit(SdkEvents.Abort("user_abort"));
                return Task.CompletedTask;
            },
        });

        await using var client = TestChatClient.Create(backend);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetResponseAsync(TestExtensions.UserMessage("hi")));
    }

    [Fact]
    public async Task Inactivity_timeout_throws_timeout_exception_and_aborts()
    {
        var backend = new FakeCopilotBackend();
        var session = backend.AddSession(new FakeCopilotSession
        {
            // Emit a single delta then go silent so the inactivity timer fires.
            OnSend = (s, _) =>
            {
                s.Emit(SdkEvents.Delta("partial", "m1"));
                return Task.CompletedTask;
            },
        });

        var configuration = new CopilotSdkConfiguration { StreamingInactivityTimeout = TimeSpan.FromMilliseconds(100) };
        await using var client = TestChatClient.Create(backend, configuration);

        await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await foreach (var _ in client.GetStreamingResponseAsync(TestExtensions.UserMessage("hi")))
            {
                // drain until the timeout fires
            }
        });

        Assert.Equal(1, session.AbortCount);
        Assert.Equal(1, session.DisposeCount);
    }

    [Fact]
    public async Task Caller_cancellation_mid_stream_stays_operation_canceled_and_aborts()
    {
        using var cts = new CancellationTokenSource();
        var backend = new FakeCopilotBackend();
        var session = backend.AddSession(new FakeCopilotSession
        {
            OnSend = (s, _) =>
            {
                s.Emit(SdkEvents.Delta("first", "m1")); // one event, then never idle
                return Task.CompletedTask;
            },
        });

        // Generous inactivity timeout so cancellation, not timeout, is the cause.
        var configuration = new CopilotSdkConfiguration { StreamingInactivityTimeout = TimeSpan.FromMinutes(5) };
        await using var client = TestChatClient.Create(backend, configuration);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var update in client.GetStreamingResponseAsync(TestExtensions.UserMessage("hi"), cancellationToken: cts.Token))
            {
                cts.Cancel(); // cancel after receiving the first update
            }
        });

        Assert.Equal(1, session.AbortCount);
        Assert.Equal(1, session.DisposeCount);
    }

    [Fact]
    public async Task Already_cancelled_token_throws_before_creating_a_session()
    {
        var backend = new FakeCopilotBackend();
        backend.AddSession(new FakeCopilotSession());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await using var client = TestChatClient.Create(backend);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetResponseAsync(TestExtensions.UserMessage("hi"), cancellationToken: cts.Token));

        Assert.Empty(backend.Calls);
    }
}
