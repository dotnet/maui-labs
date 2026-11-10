// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat.Tests.TestHelpers;

namespace Microsoft.Maui.AI.Chat.Tests.Engine;

/// <summary>Turn commit, stop, discard, and clear semantics of <see cref="AgentContext"/>.</summary>
public class AgentContextTurnTransactionTests
{
    [Fact]
    public async Task CancelAsync_CompletesAfterResponseUnwinds_SoNextSendStartsImmediately()
    {
        var streamStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callCount = 0;
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) =>
            ++callCount == 1
                ? EmitThenWait("partial", streamStarted, cancellationToken)
                : ResponseEmitters.EmitTextResponse("second", cancellationToken));
        var context = new AgentContext(new UIAgent(client));

        var firstSend = context.SendMessageAsync("first");
        await streamStarted.Task;

        await context.CancelAsync();

        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.True(context.Turns[0].IsStopped);
        await context.SendMessageAsync("second");
        await firstSend;
        Assert.Equal(2, context.Turns.Count);
        Assert.Equal(ConversationStatus.Idle, context.Status);
    }

    [Fact]
    public async Task ClearAsync_WhileStreamingWithHistoryAndNoThread_NextRequestHasOnlyNewMessage()
    {
        var streamStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = new List<IReadOnlyList<ChatMessage>>();
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) =>
        {
            requests.Add(messages.ToArray());
            return requests.Count == 2
                ? EmitThenWait("partial", streamStarted, cancellationToken)
                : ResponseEmitters.EmitTextResponse($"response-{requests.Count}", cancellationToken);
        });
        var context = new AgentContext(new UIAgent(client));
        await context.SendMessageAsync("committed");

        var streaming = context.SendMessageAsync("interrupted");
        await streamStarted.Task;
        await context.ClearAsync();
        await streaming;

        Assert.Empty(context.Turns);
        await context.SendMessageAsync("fresh");

        Assert.Equal("fresh", Assert.Single(requests[^1]).Text);
        Assert.Single(context.Turns);
    }

    [Fact]
    public async Task ClearAsync_WaitsForResponseThatIgnoresCancellation_AndItCannotChangeTheClearedConversation()
    {
        var streamStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new InMemoryConversationThread("thread-1");
        var requests = new List<IReadOnlyList<ChatMessage>>();
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) =>
        {
            requests.Add(messages.ToArray());
            return requests.Count == 1
                ? EmitIgnoringCancellation(streamStarted, release.Task)
                : ResponseEmitters.EmitTextResponse("fresh response", cancellationToken);
        });
        var context = new AgentContext(new UIAgent(client, options => options.Thread = thread));

        var streaming = context.SendMessageAsync("stale");
        await streamStarted.Task;
        var clear = context.ClearAsync();

        Assert.False(clear.IsCompleted);
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SendMessageAsync("too early"));

        release.SetResult();
        await clear;
        await streaming;

        Assert.Empty(context.Turns);
        Assert.Empty(thread.GetUpdates());
        Assert.Equal(0, thread.CompleteTurnCallCount);

        await context.SendMessageAsync("fresh");

        Assert.Equal("fresh", Assert.Single(requests[^1]).Text);
        var turn = Assert.Single(context.Turns);
        Assert.False(turn.IsStopped);
        Assert.Equal(1, thread.CompleteTurnCallCount);
    }

    [Fact]
    public async Task Stop_WhileManualUIActionPending_CommitsWithoutCallAndActionNoLongerInvokes()
    {
        var invoked = 0;
        var action = AIFunctionFactory.Create(() => { invoked++; return "done"; }, "PickColor");
        var awaitingInput = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = new List<IReadOnlyList<ChatMessage>>();
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) =>
        {
            requests.Add(messages.ToArray());
            return requests.Count == 1
                ? EmitTextAndCall("Let me ask.", "call-1", "PickColor", cancellationToken)
                : ResponseEmitters.EmitTextResponse("next", cancellationToken);
        });
        var context = new AgentContext(new UIAgent(
            client,
            options => options.RegisterUIAction(action, UIActionInvocationMode.Manual)));
        context.RegisterOnStatusChanged(status =>
        {
            if (status == ConversationStatus.AwaitingInput)
                awaitingInput.TrySetResult();
        });

        var send = context.SendMessageAsync("pick");
        await awaitingInput.Task;
        await context.CancelAsync();
        await send;

        var turn = Assert.Single(context.Turns);
        Assert.True(turn.IsStopped);
        var uiAction = Assert.Single(turn.ResponseBlocks.OfType<UIActionBlock>());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => uiAction.InvokeAsync());
        Assert.Equal(0, invoked);

        await context.SendMessageAsync("again");

        Assert.Equal(
            [(ChatRole.User, "pick"), (ChatRole.Assistant, "Let me ask."), (ChatRole.User, "again")],
            requests[^1].Select(message => (message.Role, message.Text)));
        Assert.DoesNotContain(
            requests[^1].SelectMany(message => message.Contents),
            content => content is FunctionCallContent);
    }

    [Fact]
    public async Task Stop_StatefulThread_DoesNotAdvanceProviderConversation()
    {
        var thread = new InMemoryConversationThread("thread-1");
        thread.AppendUserMessage(new ChatMessage(ChatRole.User, "first"));
        thread.AppendUpdate(new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "assistant-1",
            ConversationId = "conversation-1",
            Contents = [new TextContent("first response")],
        });
        thread.CompleteTurn();

        var streamStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = new List<ChatOptions?>();
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, chatOptions, cancellationToken) =>
        {
            options.Add(chatOptions);
            return options.Count == 1
                ? EmitThenWait("partial", streamStarted, cancellationToken, conversationId: "conversation-2")
                : ResponseEmitters.EmitTextResponse("third response", cancellationToken);
        });
        var context = new AgentContext(new UIAgent(client, o => o.Thread = thread));

        var send = context.SendMessageAsync("second");
        await streamStarted.Task;
        await context.CancelAsync();
        await send;
        await context.SendMessageAsync("third");

        Assert.Equal("conversation-1", options[0]?.ConversationId);
        Assert.Equal("conversation-1", options[1]?.ConversationId);
        Assert.Equal("conversation-1", thread.ConversationId);
    }

    [Fact]
    public async Task Approve_DoesNotRunTheNextRoundInsideTheCall()
    {
        var approving = new ThreadLocal<bool>();
        bool? secondRoundStartedInsideApprove = null;
        var awaitingInput = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callCount = 0;
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) =>
        {
            if (++callCount == 1)
                return ResponseEmitters.EmitApprovalRequest("call-1", "DeleteFile", ct: cancellationToken);

            secondRoundStartedInsideApprove = approving.Value;
            return ResponseEmitters.EmitTextResponse("deleted", cancellationToken);
        });
        var context = new AgentContext(new UIAgent(client));
        context.RegisterOnStatusChanged(status =>
        {
            if (status == ConversationStatus.AwaitingInput)
                awaitingInput.TrySetResult();
        });

        var send = context.SendMessageAsync("delete");
        await awaitingInput.Task;
        var approval = context.Turns[0].ResponseBlocks.OfType<ToolApprovalBlock>().Single();

        approving.Value = true;
        try
        {
            approval.Approve();
        }
        finally
        {
            approving.Value = false;
        }

        await send;

        Assert.Equal(2, callCount);
        Assert.False(secondRoundStartedInsideApprove);
        Assert.Equal(ConversationStatus.Idle, context.Status);
    }

    [Fact]
    public async Task UIAgent_ClearHistoryDuringSend_DoesNotCommitStaleTurn()
    {
        var thread = new InMemoryConversationThread("thread-1");
        var requests = new List<IReadOnlyList<ChatMessage>>();
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) =>
        {
            requests.Add(messages.ToArray());
            return ResponseEmitters.EmitTextResponse($"response-{requests.Count}", cancellationToken);
        });
        var agent = new UIAgent(client, options => options.Thread = thread);

        await foreach (var block in agent.SendMessageAsync(new ChatMessage(ChatRole.User, "stale")))
        {
            if (block.Role == ChatRole.Assistant)
                agent.ClearHistory();
        }

        Assert.Empty(thread.GetUpdates());

        await foreach (var _ in agent.SendMessageAsync(new ChatMessage(ChatRole.User, "fresh")))
        {
        }

        Assert.Equal("fresh", Assert.Single(requests[^1]).Text);
        Assert.Equal(1, thread.CommittedTurnCount);
    }

    [Fact]
    public async Task UIAgent_AbandonedEnumeration_CommitsNothing()
    {
        var thread = new InMemoryConversationThread("thread-1");
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) =>
            ResponseEmitters.EmitMultiTokenTextResponse(cancellationToken, "a", "b", "c"));
        var agent = new UIAgent(client, options => options.Thread = thread);

        await foreach (var block in agent.SendMessageAsync(new ChatMessage(ChatRole.User, "hi")))
        {
            if (block.Role == ChatRole.Assistant)
                break;
        }

        Assert.Empty(thread.GetUpdates());
        Assert.Equal(0, thread.AppendUserMessageCount);
    }

    [Fact]
    public async Task CompletedTurn_DropsCallsThatNeverReceivedAResult()
    {
        var requests = new List<IReadOnlyList<ChatMessage>>();
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) =>
        {
            requests.Add(messages.ToArray());
            return requests.Count == 1
                ? EmitTextAndCall("Calling.", "call-1", "DeclaredOnly", cancellationToken)
                : ResponseEmitters.EmitTextResponse("ok", cancellationToken);
        });
        var context = new AgentContext(new UIAgent(client));

        await context.SendMessageAsync("first");
        await context.SendMessageAsync("second");

        Assert.Single(context.Turns[0].ResponseBlocks.OfType<FunctionInvocationContentBlock>());
        Assert.DoesNotContain(
            requests[^1].SelectMany(message => message.Contents),
            content => content is FunctionCallContent);
        Assert.Equal(
            ["first", "Calling.", "second"],
            requests[^1].Select(message => message.Text));
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> EmitThenWait(
        string text,
        TaskCompletionSource started,
        [EnumeratorCancellation] CancellationToken cancellationToken,
        string? conversationId = null)
    {
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = Guid.NewGuid().ToString("N"),
            ConversationId = conversationId,
            Contents = [new TextContent(text)],
        };
        started.TrySetResult();
        await Task.Delay(Timeout.Infinite, cancellationToken);
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> EmitIgnoringCancellation(
        TaskCompletionSource started,
        Task release)
    {
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "stale-response",
            Contents = [new TextContent("partial")],
        };
        started.TrySetResult();
        await release;
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "stale-response",
            Contents = [new TextContent(" late")],
        };
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> EmitTextAndCall(
        string text,
        string callId,
        string name,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "assistant-1",
            Contents = [new TextContent(text), new FunctionCallContent(callId, name)],
        };
        await Task.CompletedTask;
    }
}
