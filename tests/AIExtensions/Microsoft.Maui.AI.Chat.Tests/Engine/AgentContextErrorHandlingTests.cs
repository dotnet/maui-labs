// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using Microsoft.Maui.AI.Chat.Tests.TestHelpers;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat.Tests.Engine;

public class AgentContextErrorHandlingTests
{
    // ---- Error during iteration ----

    [Fact]
    public async Task Error_DuringStreaming_SetsStatusToError()
    {
        var expectedError = new InvalidOperationException("LLM failure");
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((msgs, opts, ct) =>
            ResponseEmitters.EmitErrorAfterTokens([], expectedError, ct));

        var agent = new UIAgent(client);
        var context = new AgentContext(agent);

        await context.SendMessageAsync("Hello");

        Assert.Equal(ConversationStatus.Error, context.Status);
        Assert.Same(expectedError, context.Error);
    }

    [Fact]
    public async Task Error_DuringStreaming_RaisesTheStatusChange()
    {
        var statusChanges = new List<ConversationStatus>();
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((msgs, opts, ct) =>
            ResponseEmitters.EmitErrorAfterTokens([], new Exception("fail"), ct));

        var agent = new UIAgent(client);
        var context = new AgentContext(agent);
        context.OnStatusChanged(s => statusChanges.Add(s));

        await context.SendMessageAsync("Hello");

        Assert.Contains(ConversationStatus.Streaming, statusChanges);
        Assert.Contains(ConversationStatus.Error, statusChanges);
        Assert.Equal(ConversationStatus.Error, statusChanges[^1]);
    }

    [Fact]
    public async Task Error_AfterPartialTokens_PreservesPartialBlocks()
    {
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((msgs, opts, ct) =>
            ResponseEmitters.EmitErrorAfterTokens(
                ["Hello", " world"], new Exception("mid-stream fail"), ct));

        var agent = new UIAgent(client);
        var context = new AgentContext(agent);

        await context.SendMessageAsync("Hi");

        Assert.Equal(ConversationStatus.Error, context.Status);

        Assert.Single(context.Turns);
        var turn = context.Turns[0];

        Assert.NotEmpty(turn.RequestBlocks);
        Assert.NotEmpty(turn.ResponseBlocks);
    }

    [Fact]
    public async Task Error_SuccessfulBlocksBeforeError_Unaffected()
    {
        var callCount = 0;
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((msgs, opts, ct) =>
        {
            callCount++;
            if (callCount == 1)
            {
                return ResponseEmitters.EmitErrorAfterTokens(
                    ["partial"], new Exception("fail"), ct);
            }
            return ResponseEmitters.EmitTextResponse("OK", ct);
        });

        var agent = new UIAgent(client);
        var context = new AgentContext(agent);

        await context.SendMessageAsync("First");
        Assert.Equal(ConversationStatus.Error, context.Status);

        var turn = context.Turns[0];
        Assert.NotEmpty(turn.ResponseBlocks);
    }

    [Fact]
    public async Task StreamFailureAfterToolCall_RollsBackDanglingToolCallHistory()
    {
        var sentMessages = new List<IReadOnlyList<ChatMessage>>();
        var callCount = 0;
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) =>
        {
            sentMessages.Add(messages.ToArray());
            callCount++;
            return callCount == 1
                ? EmitToolCallThenFail(cancellationToken)
                : ResponseEmitters.EmitTextResponse("recovered", cancellationToken);
        });
        var tool = AIFunctionFactory.Create(() => "unused", "lookup");
        var context = new AgentContext(new UIAgent(
            new FunctionInvokingChatClient(client),
            new ChatOptions { Tools = [tool] }));

        await context.SendMessageAsync("first");
        Assert.Equal(ConversationStatus.Error, context.Status);

        await context.SendMessageAsync("second");

        Assert.Equal(2, sentMessages.Count);
        var retryRequest = sentMessages[1];
        var retryMessage = Assert.Single(retryRequest);
        Assert.Equal(ChatRole.User, retryMessage.Role);
        Assert.Equal("second", retryMessage.Text);
        Assert.DoesNotContain(
            retryRequest.SelectMany(message => message.Contents),
            content => content is FunctionCallContent);
    }

    // ---- CancelAsync ----

    [Fact]
    public async Task CancelAsync_WhenNotStreaming_IsNoOp()
    {
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((msgs, opts, ct) =>
            ResponseEmitters.EmitTextResponse("OK", ct));

        var agent = new UIAgent(client);
        var context = new AgentContext(agent);

        await context.SendMessageAsync("Hello");
        Assert.Equal(ConversationStatus.Idle, context.Status);

        await context.CancelAsync();
        Assert.Equal(ConversationStatus.Idle, context.Status);
    }

    [Fact]
    public async Task CancelAsync_DuringStreaming_StopsAndGoesIdle()
    {
        var streamStarted = new TaskCompletionSource();
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((msgs, opts, ct) => SlowStream(streamStarted, ct));

        var agent = new UIAgent(client);
        var context = new AgentContext(agent);

        var sendTask = context.SendMessageAsync("Hello");

        await streamStarted.Task;
        Assert.Equal(ConversationStatus.Streaming, context.Status);

        await context.CancelAsync();
        await sendTask;

        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Null(context.Error);

        static async IAsyncEnumerable<ChatResponseUpdate> SlowStream(
            TaskCompletionSource streamStarted,
            [EnumeratorCancellation] CancellationToken ct)
        {
            yield return new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                MessageId = Guid.NewGuid().ToString("N"),
                Contents = [new TextContent("tok1")]
            };
            streamStarted.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, ct); }
            catch (OperationCanceledException) { yield break; }
        }
    }

    [Fact]
    public async Task CancelAsync_KeepsPartialResponseAndMarksTurnStopped()
    {
        var streamStarted = new TaskCompletionSource();
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((msgs, opts, ct) => SlowStream(streamStarted, ct));

        var agent = new UIAgent(client);
        var context = new AgentContext(agent);
        ConversationTurn? clearedTurn = null;
        context.OnResponseBlocksReset(turn => clearedTurn = turn);

        var sendTask = context.SendMessageAsync("Hello");
        await streamStarted.Task;

        await context.CancelAsync();
        await sendTask;

        var turn = context.Turns[0];
        Assert.True(turn.IsStopped);
        var partial = Assert.IsType<TextContentBlock>(Assert.Single(turn.ResponseBlocks));
        Assert.Equal("partial", partial.RawText);
        Assert.Equal(BlockLifecycleState.Inactive, partial.LifecycleState);
        Assert.Null(clearedTurn);

        static async IAsyncEnumerable<ChatResponseUpdate> SlowStream(
            TaskCompletionSource streamStarted,
            [EnumeratorCancellation] CancellationToken ct)
        {
            yield return new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                MessageId = Guid.NewGuid().ToString("N"),
                Contents = [new TextContent("partial")]
            };
            streamStarted.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, ct); }
            catch (OperationCanceledException) { yield break; }
        }
    }

    [Fact]
    public async Task CallerCancellation_CancelsTask_StopsTurn_AndReturnsIdle()
    {
        var streamStarted = new TaskCompletionSource();
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) =>
            SlowCancelableStream(streamStarted, cancellationToken));
        var context = new AgentContext(new UIAgent(client));
        using var callerCts = new CancellationTokenSource();

        var sendTask = context.SendMessageAsync("Hello", callerCts.Token);
        await streamStarted.Task;

        callerCts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await sendTask);
        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Null(context.Error);
        var turn = Assert.Single(context.Turns);
        Assert.True(turn.IsStopped);
        Assert.Single(turn.ResponseBlocks);
    }

    [Fact]
    public async Task CallerCancellation_WhenClientThrowsDifferentException_StillCancelsTask()
    {
        var streamStarted = new TaskCompletionSource();
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) =>
            ThrowNonCancellationExceptionAfterCancellation(streamStarted, cancellationToken));
        var context = new AgentContext(new UIAgent(client));
        using var callerCts = new CancellationTokenSource();

        var sendTask = context.SendMessageAsync("Hello", callerCts.Token);
        await streamStarted.Task;
        callerCts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await sendTask);
        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Null(context.Error);
        Assert.True(Assert.Single(context.Turns).IsStopped);
    }

    [Fact]
    public async Task CallerCancellation_AfterErrorTransition_KeepsErrorAndRethrowsCancellation()
    {
        var expectedError = new InvalidOperationException("diagnostic");
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) =>
            ResponseEmitters.EmitErrorAfterTokens([], expectedError, cancellationToken));
        var context = new AgentContext(new UIAgent(client));
        using var callerCts = new CancellationTokenSource();
        context.OnStatusChanged(status =>
        {
            if (status == ConversationStatus.Error)
                callerCts.Cancel();
        });

        var sendTask = context.SendMessageAsync("Hello", callerCts.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await sendTask);
        Assert.Equal(ConversationStatus.Error, context.Status);
        Assert.Same(expectedError, context.Error);
        Assert.False(Assert.Single(context.Turns).IsStopped);
    }

    [Fact]
    public async Task GracefulCancellation_DisposesLifecycleAndAllowsNextSend()
    {
        var streamStarted = new TaskCompletionSource();
        var client = new DelegatingStreamingChatClient();
        var callCount = 0;
        client.SetHandler((messages, options, cancellationToken) =>
        {
            callCount++;
            return callCount == 1
                ? SlowCancelableStream(streamStarted, cancellationToken)
                : ResponseEmitters.EmitTextResponse("second response", cancellationToken);
        });
        var context = new AgentContext(new UIAgent(client));

        var firstSend = context.SendMessageAsync("first");
        await streamStarted.Task;
        await context.CancelAsync();
        await firstSend;

        await context.SendMessageAsync("second");

        Assert.Equal(2, context.Turns.Count);
        Assert.True(context.Turns[0].IsStopped);
        Assert.NotEmpty(context.Turns[0].ResponseBlocks);
        Assert.False(context.Turns[1].IsStopped);
        Assert.NotEmpty(context.Turns[1].ResponseBlocks);
        Assert.Equal(ConversationStatus.Idle, context.Status);
    }

    [Fact]
    public async Task GracefulCancellation_CommitsRequestAndPartialAssistantHistory()
    {
        var streamStarted = new TaskCompletionSource();
        var sentMessages = new List<IReadOnlyList<ChatMessage>>();
        var client = new DelegatingStreamingChatClient();
        var callCount = 0;
        client.SetHandler((messages, options, cancellationToken) =>
        {
            sentMessages.Add(messages.ToArray());
            callCount++;
            return callCount == 1
                ? SlowCancelableStream(streamStarted, cancellationToken)
                : ResponseEmitters.EmitTextResponse("second response", cancellationToken);
        });
        var context = new AgentContext(new UIAgent(client));

        var firstSend = context.SendMessageAsync("first");
        await streamStarted.Task;
        await context.CancelAsync();
        await firstSend;

        await context.SendMessageAsync("second");

        Assert.Equal(2, sentMessages.Count);
        Assert.Equal(
            [(ChatRole.User, "first"), (ChatRole.Assistant, "partial"), (ChatRole.User, "second")],
            sentMessages[1].Select(message => (message.Role, message.Text)));
    }

    [Fact]
    public async Task CancelWhileAwaitingApproval_CommitsRequestWithoutDanglingApproval()
    {
        var awaitingInput = new TaskCompletionSource();
        var sentMessages = new List<IReadOnlyList<ChatMessage>>();
        var client = new DelegatingStreamingChatClient();
        var callCount = 0;
        client.SetHandler((messages, options, cancellationToken) =>
        {
            sentMessages.Add(messages.ToArray());
            callCount++;
            return callCount == 1
                ? ResponseEmitters.EmitApprovalRequest(
                    "call-1",
                    "DeleteFile",
                    ct: cancellationToken)
                : ResponseEmitters.EmitTextResponse("fresh response", cancellationToken);
        });
        var context = new AgentContext(new UIAgent(client));
        context.OnStatusChanged(status =>
        {
            if (status == ConversationStatus.AwaitingInput)
                awaitingInput.TrySetResult();
        });

        var firstSend = context.SendMessageAsync("first");
        await awaitingInput.Task;
        await context.CancelAsync();
        await firstSend;

        await context.SendMessageAsync("second");

        Assert.Equal(2, sentMessages.Count);
        var secondRequest = sentMessages[1];
        Assert.Equal(
            [(ChatRole.User, "first"), (ChatRole.User, "second")],
            secondRequest.Select(message => (message.Role, message.Text)));
        Assert.DoesNotContain(
            secondRequest.SelectMany(message => message.Contents),
            content => content is ToolApprovalRequestContent);
        var stoppedTurn = context.Turns[0];
        Assert.True(stoppedTurn.IsStopped);
        var approval = Assert.IsType<ToolApprovalBlock>(Assert.Single(stoppedTurn.ResponseBlocks));
        approval.Approve();
        Assert.Equal(ApprovalStatus.Pending, approval.Status);
    }

    [Fact]
    public async Task Clear_DuringStreaming_CancelsCurrentSendAndAllowsFreshConversation()
    {
        var streamStarted = new TaskCompletionSource();
        var client = new DelegatingStreamingChatClient();
        var callCount = 0;
        client.SetHandler((messages, options, cancellationToken) =>
        {
            callCount++;
            return callCount == 1
                ? SlowCancelableStream(streamStarted, cancellationToken)
                : ResponseEmitters.EmitTextResponse("fresh response", cancellationToken);
        });
        var context = new AgentContext(new UIAgent(client));

        var firstSend = context.SendMessageAsync("first");
        await streamStarted.Task;

        await context.ClearAsync();
        await firstSend;
        await context.SendMessageAsync("fresh");

        Assert.Single(context.Turns);
        Assert.NotEmpty(context.Turns[0].ResponseBlocks);
        Assert.Equal(ConversationStatus.Idle, context.Status);
    }

    // ---- Integration: Error recovery flow ----

    private static async IAsyncEnumerable<ChatResponseUpdate> SlowCancelableStream(
        TaskCompletionSource streamStarted,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = Guid.NewGuid().ToString("N"),
            Contents = [new TextContent("partial")],
        };
        streamStarted.TrySetResult();
        await Task.Delay(Timeout.Infinite, cancellationToken);
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> ThrowNonCancellationExceptionAfterCancellation(
        TaskCompletionSource streamStarted,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = Guid.NewGuid().ToString("N"),
            Contents = [new TextContent("partial")],
        };
        streamStarted.TrySetResult();

        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw new InvalidOperationException("client translated cancellation");
        }
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> EmitToolCallThenFail(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "assistant-1",
            Contents = [new FunctionCallContent("call-1", "lookup")],
        };
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException("stream failed");
    }
}
