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
    public async Task Error_DuringStreaming_FiresStatusChangedCallback()
    {
        var statusChanges = new List<ConversationStatus>();
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((msgs, opts, ct) =>
            ResponseEmitters.EmitErrorAfterTokens([], new Exception("fail"), ct));

        var agent = new UIAgent(client);
        var context = new AgentContext(agent);
        context.RegisterOnStatusChanged(s => statusChanges.Add(s));

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
    public async Task ToolFailure_RollsBackDanglingToolCallHistory()
    {
        var sentMessages = new List<IReadOnlyList<ChatMessage>>();
        var callCount = 0;
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) =>
        {
            sentMessages.Add(messages.ToArray());
            callCount++;
            return callCount == 1
                ? ResponseEmitters.EmitToolCallResponse(
                    "call-1",
                    "explode",
                    ct: cancellationToken)
                : ResponseEmitters.EmitTextResponse("recovered", cancellationToken);
        });
        var explodingTool = AIFunctionFactory.Create(
            (Func<string>)(() => throw new InvalidOperationException("tool failed")),
            "explode");
        var context = new AgentContext(new UIAgent(client, new ChatOptions
        {
            Tools = [explodingTool],
        }));

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
    public async Task CancelAsync_DiscardsResponseBlocks()
    {
        var streamStarted = new TaskCompletionSource();
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((msgs, opts, ct) => SlowStream(streamStarted, ct));

        var agent = new UIAgent(client);
        var context = new AgentContext(agent);
        ConversationTurn? clearedTurn = null;
        context.RegisterOnResponseBlocksCleared(turn => clearedTurn = turn);

        var sendTask = context.SendMessageAsync("Hello");
        await streamStarted.Task;

        await context.CancelAsync();
        await sendTask;

        var turn = context.Turns[0];
        Assert.Empty(turn.ResponseBlocks);
        Assert.Same(turn, clearedTurn);

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
    public async Task CallerCancellation_CancelsTask_DiscardsResponseBlocks_AndReturnsIdle()
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
        Assert.Empty(Assert.Single(context.Turns).ResponseBlocks);
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
        Assert.Empty(Assert.Single(context.Turns).ResponseBlocks);
    }

    [Fact]
    public async Task CallerCancellation_AfterErrorTransition_ResetsIdleAndRethrowsCancellation()
    {
        var expectedError = new InvalidOperationException("diagnostic");
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) =>
            ResponseEmitters.EmitErrorAfterTokens([], expectedError, cancellationToken));
        var context = new AgentContext(new UIAgent(client));
        using var callerCts = new CancellationTokenSource();
        context.RegisterOnStatusChanged(status =>
        {
            if (status == ConversationStatus.Error)
                callerCts.Cancel();
        });

        var sendTask = context.SendMessageAsync("Hello", callerCts.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await sendTask);
        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Null(context.Error);
        Assert.Empty(Assert.Single(context.Turns).ResponseBlocks);
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
        Assert.Empty(context.Turns[0].ResponseBlocks);
        Assert.NotEmpty(context.Turns[1].ResponseBlocks);
        Assert.Equal(ConversationStatus.Idle, context.Status);
    }

    [Fact]
    public async Task GracefulCancellation_DoesNotCommitPartialAssistantHistory()
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
        var secondRequest = sentMessages[1];
        var message = Assert.Single(secondRequest);
        Assert.Equal(ChatRole.User, message.Role);
        Assert.Equal("second", message.Text);
        Assert.DoesNotContain(
            secondRequest.SelectMany(message => message.Contents).OfType<TextContent>(),
            content => content.Text == "partial");
    }

    [Fact]
    public async Task CancelWhileAwaitingApproval_RollsBackDanglingApprovalHistory()
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
        context.RegisterOnStatusChanged(status =>
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
        var secondMessage = Assert.Single(secondRequest);
        Assert.Equal(ChatRole.User, secondMessage.Role);
        Assert.Equal("second", secondMessage.Text);
        Assert.DoesNotContain(
            secondRequest.SelectMany(message => message.Contents),
            content => content is ToolApprovalRequestContent);
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

        context.Clear();
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
}
