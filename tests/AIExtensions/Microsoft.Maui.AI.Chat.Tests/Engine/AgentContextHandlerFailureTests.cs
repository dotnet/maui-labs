// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat.Tests.TestHelpers;

namespace Microsoft.Maui.AI.Chat.Tests.Engine;

/// <summary>
/// An exception thrown by an app's change handler must leave the conversation in a consistent state: a turn
/// that was committed stays committed and is never retried, and a turn that was not is failed, not stuck.
/// </summary>
public class AgentContextHandlerFailureTests
{
    [Fact]
    public async Task IdleStatusHandlerThrows_CommittedTurnIsNotRetryable()
    {
        var requests = new List<IReadOnlyList<ChatMessage>>();
        using var context = CreateContext(requests);
        var thrown = false;
        context.OnStatusChanged(status =>
        {
            if (status == ConversationStatus.Idle && !thrown)
            {
                thrown = true;
                throw new InvalidOperationException("status handler failed");
            }
        });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SendMessageAsync("first"));

        Assert.Equal("status handler failed", error.Message);
        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Null(context.Error);
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.RetryAsync());

        await context.SendMessageAsync("second");

        Assert.Equal(["user:first", "assistant:reply 1", "user:second"], Describe(requests[^1]));
        Assert.Equal(2, context.Turns.Count);
    }

    [Fact]
    public async Task StateHandlerThrowsWhenPredictionIsRejected_CommittedTurnIsNotRetryable()
    {
        var requests = new List<IReadOnlyList<ChatMessage>>();
        var client = CreateClient(requests, predict: true);
        using var agent = new UIAgent<DocumentState>(client, options =>
            options.StateMapper = context =>
            {
                if (context.Update.RawRepresentation is DocumentState state)
                    context.SetPredictiveState(state);
            });
        using var context = new AgentContext(agent);
        var statuses = new List<ConversationStatus>();
        context.OnStatusChanged(statuses.Add);
        var thrown = false;
        agent.State.PropertyChanged += (_, _) =>
        {
            if (!agent.State.HasPendingPredictiveState && !thrown)
            {
                thrown = true;
                throw new InvalidOperationException("state handler failed");
            }
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SendMessageAsync("first"));

        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Equal(ConversationStatus.Idle, statuses[^1]);
        Assert.Null(context.Error);
        Assert.Equal("Original", agent.State.Value.Document);
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.RetryAsync());

        await context.SendMessageAsync("second");

        Assert.Equal(["user:first", "assistant:reply 1", "user:second"], Describe(requests[^1]));
    }

    [Fact]
    public async Task StreamingStatusHandlerThrows_TurnFailsAndCanBeRetried()
    {
        var requests = new List<IReadOnlyList<ChatMessage>>();
        using var context = CreateContext(requests);
        var thrown = false;
        context.OnStatusChanged(status =>
        {
            if (status == ConversationStatus.Streaming && !thrown)
            {
                thrown = true;
                throw new InvalidOperationException("status handler failed");
            }
        });

        await context.SendMessageAsync("first");

        Assert.Equal(ConversationStatus.Error, context.Status);
        Assert.Equal("status handler failed", context.Error?.Message);
        Assert.Empty(requests);

        await context.RetryAsync();

        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Equal(["user:first"], Describe(Assert.Single(requests)));
        Assert.Equal("reply 1", Assert.Single(Assert.Single(context.Turns).ResponseBlocks.OfType<TextContentBlock>()).RawText);
    }

    [Fact]
    public async Task StateHandlerThrowsWhenStoppedPredictionIsRejected_TurnStillStops()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) => PredictThenWait(started, cancellationToken));
        using var agent = new UIAgent<DocumentState>(client, options =>
            options.StateMapper = context =>
            {
                if (context.Update.RawRepresentation is DocumentState state)
                    context.SetPredictiveState(state);
            });
        using var context = new AgentContext(agent);
        agent.State.PropertyChanged += (_, _) =>
        {
            if (!agent.State.HasPendingPredictiveState)
                throw new InvalidOperationException("state handler failed");
        };

        var send = context.SendMessageAsync("first");
        await started.Task;
        await context.CancelAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => send);
        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.True(Assert.Single(context.Turns).IsStopped);
        Assert.Equal("Original", agent.State.Value.Document);
    }

    [Fact]
    public async Task ChangeHandlerThrowsWhenAStopEndsAPendingApproval_TurnStillStops()
    {
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ResponseEmitters.EmitApprovalRequest("delete-1", "DeleteFile", ct: ct));
        model.Enqueue(ct => ResponseEmitters.EmitTextResponse("reply 2", ct));
        using var context = new AgentContext(new UIAgent(model));
        var awaitingInput = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.OnStatusChanged(status =>
        {
            if (status == ConversationStatus.AwaitingInput)
                awaitingInput.TrySetResult();
        });

        var send = context.SendMessageAsync("first");
        await awaitingInput.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var approval = Assert.Single(context.Turns[0].ResponseBlocks.OfType<ToolApprovalBlock>());
        approval.PropertyChanged += (_, _) => throw new InvalidOperationException("change handler failed");
        await context.CancelAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => send);
        Assert.Equal("change handler failed", error.Message);
        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.True(Assert.Single(context.Turns).IsStopped);
        Assert.False(approval.IsAwaitingInput);

        await context.SendMessageAsync("second");

        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Equal(2, context.Turns.Count);
    }

    [Fact]
    public async Task ChangeHandlerThrowsWhenAFailureEndsAPendingApproval_TurnFailsAndCanBeRetried()
    {
        var model = new ScriptedChatClient();
        model.Enqueue(ApprovalThenError);
        model.Enqueue(ct => ResponseEmitters.EmitTextResponse("reply 1", ct));
        using var context = new AgentContext(new UIAgent(model));
        context.OnBlockAdded((_, block) =>
        {
            if (block is ToolApprovalBlock approval)
            {
                approval.PropertyChanged += (_, _) =>
                {
                    if (!approval.IsAwaitingInput)
                        throw new InvalidOperationException("change handler failed");
                };
            }
        });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SendMessageAsync("first"));

        Assert.Equal("change handler failed", error.Message);
        Assert.Equal(ConversationStatus.Error, context.Status);
        Assert.Equal("The connection was lost.", context.Error?.Message);
        Assert.False(Assert.Single(context.Turns[0].ResponseBlocks.OfType<ToolApprovalBlock>()).IsAwaitingInput);

        await context.RetryAsync();

        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Equal("reply 1", Assert.Single(Assert.Single(context.Turns).ResponseBlocks.OfType<TextContentBlock>()).RawText);
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> ApprovalThenError(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "response-1",
            Contents = [new ToolApprovalRequestContent("approval-1", new FunctionCallContent("delete-1", "DeleteFile"))],
        };
        await Task.Yield();
        throw new InvalidOperationException("The connection was lost.");
    }

    private static AgentContext CreateContext(List<IReadOnlyList<ChatMessage>> requests)
        => new(new UIAgent(CreateClient(requests, predict: false)));

    private static DelegatingStreamingChatClient CreateClient(
        List<IReadOnlyList<ChatMessage>> requests,
        bool predict)
    {
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) =>
        {
            requests.Add(messages.ToList());
            return Reply($"reply {requests.Count}", predict, cancellationToken);
        });
        return client;
    }

    private static List<string> Describe(IReadOnlyList<ChatMessage> messages)
        => messages.Select(message => $"{message.Role}:{message.Text}").ToList();

    private static async IAsyncEnumerable<ChatResponseUpdate> Reply(
        string text,
        bool predict,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (predict)
        {
            yield return new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                RawRepresentation = new DocumentState { Document = "Draft" },
            };
        }

        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = Guid.NewGuid().ToString("N"),
            Contents = [new TextContent(text)],
        };
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> PredictThenWait(
        TaskCompletionSource started,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            RawRepresentation = new DocumentState { Document = "Draft" },
        };
        started.TrySetResult();
        await Task.Delay(Timeout.Infinite, cancellationToken);
    }

    private sealed class DocumentState
    {
        public string Document { get; set; } = "Original";
    }
}
