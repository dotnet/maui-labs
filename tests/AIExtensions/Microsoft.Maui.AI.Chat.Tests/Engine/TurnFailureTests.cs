// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat.Tests.TestHelpers;

namespace Microsoft.Maui.AI.Chat.Tests.Engine;

/// <summary>
/// A turn whose attempt failed says so itself, and keeps the exception that failed it, so a view does not
/// have to catch the status change that reported the failure.
/// </summary>
public class TurnFailureTests
{
    [Fact]
    public async Task FailedTurn_IsFailedWithItsError_UntilRetryRunsItAgain()
    {
        var replies = new Queue<Func<CancellationToken, IAsyncEnumerable<ChatResponseUpdate>>>(
        [
            FailAsync,
            ct => ReplyAsync("recovered", ct),
        ]);
        using var context = CreateContext(replies);
        var observed = new List<string>();
        context.OnStatusChanged(status => observed.Add($"{status}:{context.Turns[^1].IsFailed}"));
        context.OnResponseBlocksReset(turn => observed.Add($"cleared:{turn.IsFailed}"));

        await context.SendMessageAsync("first");

        var turn = Assert.Single(context.Turns);
        Assert.True(turn.IsFailed);
        Assert.False(turn.IsStopped);
        Assert.NotNull(turn.Error);
        Assert.Same(context.Error, turn.Error);
        Assert.Equal("provider failed", turn.Error.Message);

        await context.RetryAsync();

        Assert.Same(turn, Assert.Single(context.Turns));
        Assert.False(turn.IsFailed);
        Assert.Null(turn.Error);
        Assert.Equal(
            ["Streaming:False", "Error:True", "cleared:True", "Streaming:False", "Idle:False"],
            observed);
    }

    [Fact]
    public async Task RetryThatFailsAgain_IsFailedAgain_WithTheNewError()
    {
        var replies = new Queue<Func<CancellationToken, IAsyncEnumerable<ChatResponseUpdate>>>(
        [
            FailAsync,
            ct => FailWithAsync("provider failed again", ct),
        ]);
        using var context = CreateContext(replies);

        await context.SendMessageAsync("first");
        await context.RetryAsync();

        Assert.Equal(ConversationStatus.Error, context.Status);
        var turn = Assert.Single(context.Turns);
        Assert.True(turn.IsFailed);
        Assert.Same(context.Error, turn.Error);
        Assert.Equal("provider failed again", turn.Error?.Message);
    }

    [Fact]
    public async Task Failure_RaisesTheTurnsErrorThenIsFailed_BeforeTheContextsErrorAndStatus_AndRetryClearsThemInTheSameOrder()
    {
        var replies = new Queue<Func<CancellationToken, IAsyncEnumerable<ChatResponseUpdate>>>(
        [
            FailAsync,
            ct => FailWithAsync("provider failed again", ct),
        ]);
        using var context = CreateContext(replies);
        var log = new List<string>();
        context.PropertyChanged += (_, e) => log.Add(e.PropertyName switch
        {
            nameof(AgentContext.Error) => $"context.Error={context.Error?.Message ?? "none"}",
            _ => $"context.{e.PropertyName}={context.Status}",
        });
        ((INotifyCollectionChanged)context.Turns).CollectionChanged += (_, e) =>
        {
            foreach (ConversationTurn turn in e.NewItems ?? Array.Empty<ConversationTurn>())
            {
                // Each change is raised once both values and the context's state are in place.
                turn.PropertyChanged += (_, e) => log.Add(e.PropertyName switch
                {
                    nameof(ConversationTurn.Error) =>
                        $"turn.Error={turn.Error?.Message ?? "none"} (IsFailed={turn.IsFailed}, {context.Status}, context.Error={context.Error?.Message ?? "none"})",
                    _ => $"turn.{e.PropertyName}={turn.IsFailed}",
                });
            }
        };

        await context.SendMessageAsync("first");

        Assert.Equal(
            [
                "turn.Error=provider failed (IsFailed=True, Error, context.Error=provider failed)",
                "turn.IsFailed=True",
                "context.Error=provider failed",
                "context.Status=Error",
            ],
            log[^4..]);

        log.Clear();
        await context.RetryAsync();

        Assert.Equal(
            [
                "turn.Error=none (IsFailed=False, Streaming, context.Error=none)",
                "turn.IsFailed=False",
                "context.Error=none",
                "context.Status=Streaming",
                "turn.Error=provider failed again (IsFailed=True, Error, context.Error=provider failed again)",
                "turn.IsFailed=True",
                "context.Error=provider failed again",
                "context.Status=Error",
            ],
            log);
    }

    [Fact]
    public async Task FailedTurn_StaysFailedWithItsError_WhenANewMessageIsSentInstead()
    {
        var replies = new Queue<Func<CancellationToken, IAsyncEnumerable<ChatResponseUpdate>>>(
        [
            FailAsync,
            ct => ReplyAsync("hello", ct),
        ]);
        using var context = CreateContext(replies);

        await context.SendMessageAsync("first");
        var failure = context.Error;
        await context.SendMessageAsync("second");

        // The context only reports the last attempt; the turn keeps why it failed.
        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Null(context.Error);
        Assert.NotNull(failure);
        Assert.Collection(
            context.Turns,
            turn =>
            {
                Assert.True(turn.IsFailed);
                Assert.Same(failure, turn.Error);
            },
            turn =>
            {
                Assert.False(turn.IsFailed);
                Assert.Null(turn.Error);
            });
    }

    [Fact]
    public async Task StoppedAndCompletedTurns_AreNotFailed()
    {
        var never = new TaskCompletionSource();
        var replies = new Queue<Func<CancellationToken, IAsyncEnumerable<ChatResponseUpdate>>>(
        [
            ct => ReplyAsync("done", ct),
            ct => WaitAsync(never.Task, ct),
        ]);
        using var context = CreateContext(replies);

        await context.SendMessageAsync("first");
        var stopped = context.SendMessageAsync("second");
        await context.CancelAsync();
        await stopped;

        Assert.Collection(
            context.Turns,
            turn =>
            {
                Assert.False(turn.IsFailed);
                Assert.Null(turn.Error);
            },
            turn =>
            {
                Assert.False(turn.IsFailed);
                Assert.Null(turn.Error);
                Assert.True(turn.IsStopped);
            });
    }

    [Fact]
    public async Task StoppedTurnWhoseCommitFails_IsFailedWithTheCommitError()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) => WaitAsync(started, cancellationToken));
        var thread = new FailingThread();
        using var context = new AgentContext(new UIAgent(client, options => options.Thread = thread));

        var send = context.SendMessageAsync("first");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await context.CancelAsync();
        await send;

        var turn = Assert.Single(context.Turns);
        Assert.False(turn.IsStopped);
        Assert.True(turn.IsFailed);
        Assert.Same(thread.Failure, turn.Error);
        Assert.Same(thread.Failure, context.Error);
        Assert.Equal(ConversationStatus.Error, context.Status);
    }

    private static AgentContext CreateContext(
        Queue<Func<CancellationToken, IAsyncEnumerable<ChatResponseUpdate>>> replies)
    {
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) => replies.Dequeue()(cancellationToken));
        return new AgentContext(new UIAgent(client));
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> ReplyAsync(
        string text,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = Guid.NewGuid().ToString("N"),
            Contents = [new TextContent(text)],
        };
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> WaitAsync(
        Task gate,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        yield break;
    }

    /// <summary>Signals that the request was sent, then waits until the turn is stopped.</summary>
    private static async IAsyncEnumerable<ChatResponseUpdate> WaitAsync(
        TaskCompletionSource started,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        started.TrySetResult();
        await Task.Delay(Timeout.Infinite, cancellationToken);
        yield break;
    }

    private static IAsyncEnumerable<ChatResponseUpdate> FailAsync(CancellationToken cancellationToken)
        => FailWithAsync("provider failed", cancellationToken);

    private static async IAsyncEnumerable<ChatResponseUpdate> FailWithAsync(
        string message,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException(message);
#pragma warning disable CS0162 // Unreachable code detected
        yield break;
#pragma warning restore CS0162
    }

    /// <summary>A thread whose storage fails when a turn is committed.</summary>
    private sealed class FailingThread : IConversationThread
    {
        internal IOException Failure { get; } = new("The disk is full.");

        public string ThreadId => "failing";

        public bool IsStateful => false;

        public string? ConversationId => null;

        public void AppendUserMessage(ChatMessage message) => throw Failure;

        public void AppendUpdate(ChatResponseUpdate update) => throw Failure;

        public void CompleteTurn() => throw Failure;

        public IReadOnlyList<ChatResponseUpdate> GetUpdates() => [];

        public void Clear()
        {
        }
    }
}
