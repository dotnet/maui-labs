// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat.Tests.TestHelpers;

namespace Microsoft.Maui.AI.Chat.Tests.Engine;

/// <summary>
/// An exception thrown by an app callback must leave the conversation in a consistent state: a turn that
/// was committed stays committed and is never retried, and a turn that was not is failed, not stuck.
/// </summary>
public class AgentContextCallbackFailureTests
{
    [Fact]
    public async Task IdleStatusCallbackThrows_CommittedTurnIsNotRetryable()
    {
        var requests = new List<IReadOnlyList<ChatMessage>>();
        using var context = CreateContext(requests);
        var thrown = false;
        context.RegisterOnStatusChanged(status =>
        {
            if (status == ConversationStatus.Idle && !thrown)
            {
                thrown = true;
                throw new InvalidOperationException("callback failed");
            }
        });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SendMessageAsync("first"));

        Assert.Equal("callback failed", error.Message);
        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Null(context.Error);
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.RetryAsync());

        await context.SendMessageAsync("second");

        Assert.Equal(["user:first", "assistant:reply 1", "user:second"], Describe(requests[^1]));
        Assert.Equal(2, context.Turns.Count);
    }

    [Fact]
    public async Task StateCallbackThrowsWhenPredictionIsRejected_CommittedTurnIsNotRetryable()
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
        context.RegisterOnStatusChanged(statuses.Add);
        var thrown = false;
        agent.State.OnChanged(() =>
        {
            if (!agent.State.HasPendingPredictiveState && !thrown)
            {
                thrown = true;
                throw new InvalidOperationException("state callback failed");
            }
        });

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
    public async Task StreamingStatusCallbackThrows_TurnFailsAndCanBeRetried()
    {
        var requests = new List<IReadOnlyList<ChatMessage>>();
        using var context = CreateContext(requests);
        var thrown = false;
        context.RegisterOnStatusChanged(status =>
        {
            if (status == ConversationStatus.Streaming && !thrown)
            {
                thrown = true;
                throw new InvalidOperationException("callback failed");
            }
        });

        await context.SendMessageAsync("first");

        Assert.Equal(ConversationStatus.Error, context.Status);
        Assert.Equal("callback failed", context.Error?.Message);
        Assert.Empty(requests);

        await context.RetryAsync();

        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Equal(["user:first"], Describe(Assert.Single(requests)));
        Assert.Equal("reply 1", Assert.Single(Assert.Single(context.Turns).ResponseBlocks.OfType<TextContentBlock>()).RawText);
    }

    [Fact]
    public async Task StateCallbackThrowsWhenStoppedPredictionIsRejected_TurnStillStops()
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
        agent.State.OnChanged(() =>
        {
            if (!agent.State.HasPendingPredictiveState)
                throw new InvalidOperationException("state callback failed");
        });

        var send = context.SendMessageAsync("first");
        await started.Task;
        await context.CancelAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => send);
        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.True(Assert.Single(context.Turns).IsStopped);
        Assert.Equal("Original", agent.State.Value.Document);
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
