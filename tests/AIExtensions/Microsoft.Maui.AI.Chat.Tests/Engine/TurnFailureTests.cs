// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat.Tests.TestHelpers;

namespace Microsoft.Maui.AI.Chat.Tests.Engine;

/// <summary>
/// A turn whose attempt failed says so itself, so a view does not have to catch the status change that
/// reported the failure.
/// </summary>
public class TurnFailureTests
{
    [Fact]
    public async Task FailedTurn_IsFailedUntilRetryRunsItAgain()
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

        await context.RetryAsync();

        Assert.Same(turn, Assert.Single(context.Turns));
        Assert.False(turn.IsFailed);
        Assert.Equal(
            ["Streaming:False", "Error:True", "cleared:True", "Streaming:False", "Idle:False"],
            observed);
    }

    [Fact]
    public async Task RetryThatFailsAgain_IsFailedAgain()
    {
        var replies = new Queue<Func<CancellationToken, IAsyncEnumerable<ChatResponseUpdate>>>(
        [
            FailAsync,
            FailAsync,
        ]);
        using var context = CreateContext(replies);

        await context.SendMessageAsync("first");
        await context.RetryAsync();

        Assert.Equal(ConversationStatus.Error, context.Status);
        Assert.True(Assert.Single(context.Turns).IsFailed);
    }

    [Fact]
    public async Task FailedTurn_StaysFailed_WhenANewMessageIsSentInstead()
    {
        var replies = new Queue<Func<CancellationToken, IAsyncEnumerable<ChatResponseUpdate>>>(
        [
            FailAsync,
            ct => ReplyAsync("hello", ct),
        ]);
        using var context = CreateContext(replies);

        await context.SendMessageAsync("first");
        await context.SendMessageAsync("second");

        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Collection(
            context.Turns,
            turn => Assert.True(turn.IsFailed),
            turn => Assert.False(turn.IsFailed));
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
            turn => Assert.False(turn.IsFailed),
            turn =>
            {
                Assert.False(turn.IsFailed);
                Assert.True(turn.IsStopped);
            });
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

    private static async IAsyncEnumerable<ChatResponseUpdate> FailAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException("provider failed");
#pragma warning disable CS0162 // Unreachable code detected
        yield break;
#pragma warning restore CS0162
    }
}
