// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using Microsoft.Maui.AI.Chat.Tests.TestHelpers;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat.Tests.Engine;

public class AgentContextThreadTests
{
    [Fact]
    public async Task RetryAsync_Success_ReusesTurnAndCommitsOnlySuccessfulAttempt()
    {
        var thread = new InMemoryConversationThread("thread-1");
        var callCount = 0;
        var context = CreateContext(thread, (messages, options, cancellationToken) =>
        {
            callCount++;
            return callCount == 1
                ? ResponseEmitters.EmitErrorAfterTokens(
                    ["partial"],
                    new InvalidOperationException("failed"),
                    cancellationToken)
                : ResponseEmitters.EmitTextResponse("success", cancellationToken);
        });

        await context.SendMessageAsync("question");

        Assert.Equal(ConversationStatus.Error, context.Status);
        var failedTurn = Assert.Single(context.Turns);
        var originalId = failedTurn.Id;
        Assert.False(string.IsNullOrWhiteSpace(originalId));

        await context.RetryAsync();

        var completedTurn = Assert.Single(context.Turns);
        Assert.Same(failedTurn, completedTurn);
        Assert.Equal(originalId, completedTurn.Id);
        Assert.Single(completedTurn.RequestBlocks);
        var response = Assert.Single(
            completedTurn.ResponseBlocks.OfType<TextContentBlock>());
        Assert.Equal("success", response.RawText);
        Assert.Equal(1, thread.CompleteTurnCallCount);
        Assert.Equal(1, thread.CommittedTurnCount);
        Assert.Equal(2, thread.GetMessageHistory().Count);
        Assert.DoesNotContain(
            thread.GetUpdates().SelectMany(update => update.Contents),
            content => content is TextContent text && text.Text == "partial");
    }

    [Fact]
    public async Task RetryAsync_RepeatedFailure_RemainsRetryableUntilSuccess()
    {
        var thread = new InMemoryConversationThread("thread-1");
        var callCount = 0;
        var context = CreateContext(thread, (messages, options, cancellationToken) =>
        {
            callCount++;
            return callCount < 3
                ? ResponseEmitters.EmitErrorAfterTokens(
                    [$"partial-{callCount}"],
                    new InvalidOperationException($"failed-{callCount}"),
                    cancellationToken)
                : ResponseEmitters.EmitTextResponse("success", cancellationToken);
        });

        await context.SendMessageAsync("question");
        await context.RetryAsync();

        Assert.Equal(ConversationStatus.Error, context.Status);
        Assert.Equal("failed-2", context.Error?.Message);

        await context.RetryAsync();

        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Null(context.Error);
        Assert.Equal(3, callCount);
        Assert.Single(context.Turns);
        Assert.Equal(1, thread.AppendUserMessageCount);
        Assert.Equal(1, thread.CompleteTurnCallCount);
        Assert.Equal(1, thread.CommittedTurnCount);
    }

    [Fact]
    public async Task RetryAsync_WhenNotInError_Throws()
    {
        var thread = new InMemoryConversationThread("thread-1");
        var context = CreateContext(
            thread,
            (messages, options, cancellationToken) =>
                ResponseEmitters.EmitTextResponse("success", cancellationToken));

        await context.SendMessageAsync("question");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.RetryAsync());
        Assert.Contains("Status == Error", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RetryAsync_CallerCancellation_StopsTurnAndAllowsFreshSend()
    {
        var retryStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new InMemoryConversationThread("thread-1");
        var callCount = 0;
        var context = CreateContext(thread, (messages, options, cancellationToken) =>
        {
            callCount++;
            return callCount switch
            {
                1 => ResponseEmitters.EmitErrorAfterTokens(
                    [],
                    new InvalidOperationException("failed"),
                    cancellationToken),
                2 => SlowStream(retryStarted, cancellationToken),
                _ => ResponseEmitters.EmitTextResponse("fresh", cancellationToken),
            };
        });
        await context.SendMessageAsync("question");
        using var callerCts = new CancellationTokenSource();

        var retry = context.RetryAsync(callerCts.Token);
        await retryStarted.Task;
        callerCts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await retry);
        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Null(context.Error);
        var stoppedTurn = Assert.Single(context.Turns);
        Assert.True(stoppedTurn.IsStopped);
        Assert.Single(stoppedTurn.ResponseBlocks);
        Assert.Equal(1, thread.CompleteTurnCallCount);
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.RetryAsync());

        await context.SendMessageAsync("fresh question");

        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Equal(3, callCount);
        Assert.Equal(2, thread.CompleteTurnCallCount);
        Assert.Equal(2, thread.CommittedTurnCount);
    }

    [Fact]
    public async Task RestoreAsync_ReconstructsTurnsWithStableUniqueIds()
    {
        var thread = new InMemoryConversationThread("thread-1");
        var responseCount = 0;
        var source = CreateContext(thread, (messages, options, cancellationToken) =>
        {
            responseCount++;
            return ResponseEmitters.EmitTextResponse(
                $"response-{responseCount}",
                cancellationToken);
        });
        await source.SendMessageAsync("first");
        await source.SendMessageAsync("second");
        var originalIds = source.Turns.Select(turn => turn.Id).ToArray();

        var restored = CreateContext(
            thread,
            (messages, options, cancellationToken) =>
                ResponseEmitters.EmitEmptyResponse(cancellationToken));

        await restored.RestoreAsync();

        Assert.Equal(2, restored.Turns.Count);
        Assert.All(
            restored.Turns,
            turn => Assert.False(string.IsNullOrWhiteSpace(turn.Id)));
        Assert.Equal(2, restored.Turns.Select(turn => turn.Id).Distinct().Count());
        Assert.Equal(originalIds, restored.Turns.Select(turn => turn.Id));
        Assert.All(restored.Turns, turn =>
        {
            Assert.NotEmpty(turn.RequestBlocks);
            Assert.NotEmpty(turn.ResponseBlocks);
        });
    }

    [Fact]
    public async Task RestoreAsync_CalledTwiceWithoutClear_ThrowsWithoutDuplicatingTurns()
    {
        var thread = new InMemoryConversationThread("thread-1");
        CommitTextTurn(thread, "question", "answer", "turn-1");
        var context = CreateContext(
            thread,
            (messages, options, cancellationToken) =>
                ResponseEmitters.EmitEmptyResponse(cancellationToken));

        await context.RestoreAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.RestoreAsync());
        Assert.Single(context.Turns);
    }

    [Fact]
    public async Task RestoreAsync_ApprovalIsDisplayOnly()
    {
        var thread = new InMemoryConversationThread("thread-1");
        thread.AppendUserMessage(new ChatMessage(ChatRole.User, "Delete it")
        {
            MessageId = "turn-1",
        });
        thread.AppendUpdate(new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "assistant-1",
            Contents =
            [
                new ToolApprovalRequestContent(
                    "approval-1",
                    new FunctionCallContent("call-1", "Delete", null)),
            ],
        });
        thread.CompleteTurn();

        var clientCallCount = 0;
        var context = CreateContext(thread, (messages, options, cancellationToken) =>
        {
            clientCallCount++;
            return ResponseEmitters.EmitTextResponse("unexpected", cancellationToken);
        });

        await context.RestoreAsync();

        var approval = Assert.Single(
            Assert.Single(context.Turns).ResponseBlocks.OfType<ToolApprovalBlock>());
        approval.Approve();
        await Task.Yield();

        Assert.Equal(ApprovalStatus.Approved, approval.Status);
        Assert.Equal(0, clientCallCount);
        Assert.Equal(ConversationStatus.Idle, context.Status);
    }

    [Fact]
    public async Task RestoreAsync_CompletedApprovalRemainsOneDisplayOnlyTurn()
    {
        var thread = new InMemoryConversationThread("thread-1");
        var sourceCallCount = 0;
        var source = CreateContext(thread, (messages, options, cancellationToken) =>
        {
            sourceCallCount++;
            return sourceCallCount == 1
                ? ResponseEmitters.EmitApprovalRequest(
                    "call-1",
                    "Delete",
                    ct: cancellationToken)
                : ResponseEmitters.EmitTextResponse("deleted", cancellationToken);
        });
        source.RegisterOnStatusChanged(status =>
        {
            if (status == ConversationStatus.AwaitingInput)
            {
                source.Turns[^1]
                    .ResponseBlocks
                    .OfType<ToolApprovalBlock>()
                    .Single()
                    .Approve();
            }
        });
        await source.SendMessageAsync("Delete it");
        Assert.Equal(1, thread.CompleteTurnCallCount);

        var restoredClientCallCount = 0;
        var restored = CreateContext(thread, (messages, options, cancellationToken) =>
        {
            restoredClientCallCount++;
            return ResponseEmitters.EmitTextResponse("unexpected", cancellationToken);
        });

        await restored.RestoreAsync();

        var turn = Assert.Single(restored.Turns);
        var approval = Assert.Single(
            turn.ResponseBlocks.OfType<ToolApprovalBlock>());
        Assert.Equal(ApprovalStatus.Approved, approval.Status);
        Assert.Contains(
            turn.ResponseBlocks.OfType<TextContentBlock>(),
            block => block.RawText == "deleted");
        Assert.Equal(0, restoredClientCallCount);
        Assert.Equal(ConversationStatus.Idle, restored.Status);
    }

    [Fact]
    public async Task RestoreAsync_ApprovalResponseWithoutAdditionalProperties_UsesStructuralBoundary()
    {
        var thread = new InMemoryConversationThread(
            "thread-1",
            preserveAdditionalProperties: false);
        var sourceCallCount = 0;
        var source = CreateContext(thread, (messages, options, cancellationToken) =>
        {
            sourceCallCount++;
            return sourceCallCount == 1
                ? ResponseEmitters.EmitApprovalRequest(
                    "call-1",
                    "Delete",
                    ct: cancellationToken)
                : ResponseEmitters.EmitTextResponse("deleted", cancellationToken);
        });
        source.RegisterOnStatusChanged(status =>
        {
            if (status == ConversationStatus.AwaitingInput)
            {
                source.Turns[^1]
                    .ResponseBlocks
                    .OfType<ToolApprovalBlock>()
                    .Single()
                    .Approve();
            }
        });
        await source.SendMessageAsync("Delete it");

        var restored = CreateContext(
            thread,
            (messages, options, cancellationToken) =>
                ResponseEmitters.EmitEmptyResponse(cancellationToken));

        await restored.RestoreAsync();

        var turn = Assert.Single(restored.Turns);
        var approval = Assert.Single(
            turn.ResponseBlocks.OfType<ToolApprovalBlock>());
        Assert.Equal(ApprovalStatus.Approved, approval.Status);
        Assert.Contains(
            turn.ResponseBlocks.OfType<TextContentBlock>(),
            block => block.RawText == "deleted");
    }

    [Fact]
    public async Task Clear_AfterConversation_ClearsContextAndThread()
    {
        var thread = new InMemoryConversationThread("thread-1");
        IReadOnlyList<ChatMessage>? freshMessages = null;
        var callCount = 0;
        var context = CreateContext(thread, (messages, options, cancellationToken) =>
        {
            callCount++;
            if (callCount == 2)
                freshMessages = messages.ToArray();

            return ResponseEmitters.EmitTextResponse(
                $"response-{callCount}",
                cancellationToken);
        });
        await context.SendMessageAsync("first");

        await context.ClearAsync();

        Assert.Empty(context.Turns);
        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Null(context.Error);
        Assert.Empty(thread.GetUpdates());
        Assert.Equal(1, thread.ClearCallCount);

        await context.SendMessageAsync("fresh");

        Assert.NotNull(freshMessages);
        Assert.Single(freshMessages);
        Assert.Equal("fresh", freshMessages[0].Text);
        Assert.Single(context.Turns);
    }

    [Fact]
    public async Task Clear_AfterError_DiscardsPendingAttempt()
    {
        var thread = new InMemoryConversationThread("thread-1");
        var context = CreateContext(
            thread,
            (messages, options, cancellationToken) =>
                ResponseEmitters.EmitErrorAfterTokens(
                    ["partial"],
                    new InvalidOperationException("failed"),
                    cancellationToken));

        await context.SendMessageAsync("question");
        Assert.Equal(ConversationStatus.Error, context.Status);
        Assert.Equal(0, thread.PendingUpdateCount);
        Assert.Equal(0, thread.AppendUserMessageCount);

        await context.ClearAsync();

        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Null(context.Error);
        Assert.Empty(context.Turns);
        Assert.Empty(thread.GetUpdates());
        Assert.Equal(0, thread.PendingUpdateCount);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.RetryAsync());
    }

    [Fact]
    public async Task Clear_AfterRestore_AllowsFreshEmptyRestore()
    {
        var thread = new InMemoryConversationThread("thread-1");
        CommitTextTurn(thread, "question", "answer", "turn-1");
        var context = CreateContext(
            thread,
            (messages, options, cancellationToken) =>
                ResponseEmitters.EmitEmptyResponse(cancellationToken));
        await context.RestoreAsync();
        Assert.Single(context.Turns);

        await context.ClearAsync();
        await context.RestoreAsync();

        Assert.Empty(context.Turns);
        Assert.Empty(thread.GetUpdates());
    }

    [Fact]
    public async Task Clear_DuringConversation_CancelsAndRemovesPendingPersistence()
    {
        var streamStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new InMemoryConversationThread("thread-1");
        var context = CreateContext(
            thread,
            (messages, options, cancellationToken) =>
                SlowStream(streamStarted, cancellationToken));

        var send = context.SendMessageAsync("question");
        await streamStarted.Task;

        await context.ClearAsync();
        await send;

        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Empty(context.Turns);
        Assert.Empty(thread.GetUpdates());
        Assert.Equal(0, thread.PendingUpdateCount);
        Assert.Equal(0, thread.CompleteTurnCallCount);
    }

    [Fact]
    public async Task CancelAsync_CommitsStoppedTurnWithPartialOutput()
    {
        var streamStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new InMemoryConversationThread("thread-1");
        var context = CreateContext(
            thread,
            (messages, options, cancellationToken) =>
                SlowStream(streamStarted, cancellationToken));

        var send = context.SendMessageAsync("question");
        await streamStarted.Task;
        await context.CancelAsync();
        await send;

        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.True(Assert.Single(context.Turns).IsStopped);
        Assert.Equal(1, thread.CompleteTurnCallCount);
        Assert.Equal(
            [(ChatRole.User, "question"), (ChatRole.Assistant, "partial")],
            thread.GetMessageHistory().Select(message => (message.Role, message.Text)));

        var restored = CreateContext(
            thread,
            (messages, options, cancellationToken) =>
                ResponseEmitters.EmitEmptyResponse(cancellationToken));
        await restored.RestoreAsync();

        var restoredTurn = Assert.Single(restored.Turns);
        Assert.True(restoredTurn.IsStopped);
        Assert.Equal(
            "partial",
            Assert.IsType<TextContentBlock>(Assert.Single(restoredTurn.ResponseBlocks)).RawText);
    }

    [Fact]
    public async Task CallerCancellation_CommitsStoppedTurnAndCancelsTask()
    {
        var streamStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new InMemoryConversationThread("thread-1");
        var context = CreateContext(
            thread,
            (messages, options, cancellationToken) =>
                SlowStream(streamStarted, cancellationToken));
        using var callerCts = new CancellationTokenSource();

        var send = context.SendMessageAsync("question", callerCts.Token);
        await streamStarted.Task;
        callerCts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await send);
        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Equal(1, thread.CompleteTurnCallCount);
        Assert.Equal(2, thread.GetMessageHistory().Count);
        var turn = Assert.Single(context.Turns);
        Assert.True(turn.IsStopped);
        Assert.Single(turn.ResponseBlocks);
    }

    [Fact]
    public async Task NewTurns_HaveNonEmptyUniqueIds()
    {
        var thread = new InMemoryConversationThread("thread-1");
        var context = CreateContext(
            thread,
            (messages, options, cancellationToken) =>
                ResponseEmitters.EmitTextResponse("answer", cancellationToken));

        await context.SendMessageAsync("first");
        await context.SendMessageAsync("second");

        Assert.All(
            context.Turns,
            turn => Assert.False(string.IsNullOrWhiteSpace(turn.Id)));
        Assert.Equal(2, context.Turns.Select(turn => turn.Id).Distinct().Count());
    }

    [Fact]
    public async Task RestoreAsync_RecreatesTurnsAndAnnouncesOnlyTheTurnsOnceAllAreLoaded()
    {
        // Ported from upstream's RestoreAsync_RecreatesTurnsWithoutFiringCallbacks. Upstream restores silently;
        // here each restored turn is announced so a view already showing the context renders it (see
        // UPSTREAM-CHANGES.md). Blocks and status are still not announced.
        var thread = new InMemoryConversationThread("thread-1");
        var callCount = 0;
        var firstContext = CreateContext(thread, (messages, options, cancellationToken) =>
            ResponseEmitters.EmitTextResponse($"Response {++callCount}", cancellationToken));
        await firstContext.SendMessageAsync("First");
        await firstContext.SendMessageAsync("Second");

        var restoredContext = CreateContext(thread, (messages, options, cancellationToken) =>
            ResponseEmitters.EmitTextResponse("unused", cancellationToken));
        var announced = new List<(string Request, string Response, int TurnCount)>();
        var blockAddedCount = 0;
        var statusChangedCount = 0;
        using var turnRegistration = restoredContext.RegisterOnTurnAdded(turn =>
            announced.Add((GetRequestText(turn), GetResponseText(turn), restoredContext.Turns.Count)));
        using var blockRegistration = restoredContext.RegisterOnBlockAdded((_, _) => blockAddedCount++);
        using var statusRegistration = restoredContext.RegisterOnStatusChanged(_ => statusChangedCount++);

        await restoredContext.RestoreAsync();

        Assert.Equal(2, restoredContext.Turns.Count);
        Assert.Equal("First", GetRequestText(restoredContext.Turns[0]));
        Assert.Equal("Response 1", GetResponseText(restoredContext.Turns[0]));
        Assert.Equal("Second", GetRequestText(restoredContext.Turns[1]));
        Assert.Equal("Response 2", GetResponseText(restoredContext.Turns[1]));
        Assert.Equal([("First", "Response 1", 2), ("Second", "Response 2", 2)], announced);
        Assert.Equal(0, blockAddedCount);
        Assert.Equal(0, statusChangedCount);
    }

    [Fact]
    public async Task RestoreAsync_EmptyThread_RemainsIdle()
    {
        var context = CreateContext(
            new InMemoryConversationThread("thread-1"),
            (messages, options, cancellationToken) =>
                ResponseEmitters.EmitTextResponse("unused", cancellationToken));

        await context.RestoreAsync();

        Assert.Empty(context.Turns);
        Assert.Equal(ConversationStatus.Idle, context.Status);
    }

    [Fact]
    public async Task RestoreAsync_UserMessageWithoutBlocks_PreservesResponseBoundary()
    {
        var thread = new InMemoryConversationThread("thread-1");
        var callCount = 0;
        var firstContext = CreateContext(thread, (messages, options, cancellationToken) =>
            ResponseEmitters.EmitTextResponse($"Response {++callCount}", cancellationToken));
        await firstContext.SendMessageAsync("First");
        await firstContext.SendMessageAsync(new ChatMessage(ChatRole.User, Array.Empty<AIContent>()));

        var restoredContext = CreateContext(thread, (messages, options, cancellationToken) =>
            ResponseEmitters.EmitTextResponse("unused", cancellationToken));
        await restoredContext.RestoreAsync();

        Assert.Equal(2, restoredContext.Turns.Count);
        Assert.Equal("First", GetRequestText(restoredContext.Turns[0]));
        Assert.Equal("Response 1", GetResponseText(restoredContext.Turns[0]));
        Assert.Empty(restoredContext.Turns[1].RequestBlocks);
        Assert.Equal("Response 2", GetResponseText(restoredContext.Turns[1]));
    }

    [Fact]
    public async Task RestoreAsync_ConsecutiveUserMessagesRemainSeparateTurns()
    {
        var thread = new InMemoryConversationThread("thread-1");
        thread.AppendUserMessage(new ChatMessage(
            ChatRole.User,
            [new TextContent("First"), new DataContent(new byte[] { 1 }, "image/png")]));
        thread.CompleteTurn();
        thread.AppendUserMessage(new ChatMessage(
            ChatRole.User,
            [new TextContent("Second"), new DataContent(new byte[] { 2 }, "image/png")]));
        thread.CompleteTurn();
        var context = CreateContext(thread, (messages, options, cancellationToken) =>
            ResponseEmitters.EmitTextResponse("unused", cancellationToken));

        await context.RestoreAsync();

        Assert.Equal(2, context.Turns.Count);
        Assert.All(context.Turns, turn =>
        {
            Assert.Equal(2, turn.RequestBlocks.Count);
            Assert.Empty(turn.ResponseBlocks);
        });
        Assert.Equal("First", Assert.Single(context.Turns[0].RequestBlocks.OfType<RichContentBlock>()).RawText);
        Assert.Equal("Second", Assert.Single(context.Turns[1].RequestBlocks.OfType<RichContentBlock>()).RawText);
    }

    private static string GetRequestText(ConversationTurn turn)
        => Assert.IsType<TextContentBlock>(Assert.Single(turn.RequestBlocks)).RawText;

    private static string GetResponseText(ConversationTurn turn)
        => Assert.IsType<TextContentBlock>(Assert.Single(turn.ResponseBlocks)).RawText;

    private static AgentContext CreateContext(
        InMemoryConversationThread thread,
        Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken,
            IAsyncEnumerable<ChatResponseUpdate>> handler)
    {
        var client = new DelegatingStreamingChatClient();
        client.SetHandler(handler);
        return new AgentContext(new UIAgent(
            client,
            options => options.Thread = thread));
    }

    private static void CommitTextTurn(
        InMemoryConversationThread thread,
        string request,
        string response,
        string turnId)
    {
        thread.AppendUserMessage(new ChatMessage(ChatRole.User, request)
        {
            MessageId = turnId,
        });
        thread.AppendUpdate(new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = $"{turnId}-response",
            Contents = [new TextContent(response)],
        });
        thread.CompleteTurn();
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> SlowStream(
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
}
