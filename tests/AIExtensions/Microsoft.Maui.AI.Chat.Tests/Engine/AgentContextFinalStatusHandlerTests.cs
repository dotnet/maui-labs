// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat.Tests.TestHelpers;

namespace Microsoft.Maui.AI.Chat.Tests.Engine;

/// <summary>
/// A handler of the status change that reports the end of a turn may start the next one, for example to send
/// a queued message.
/// </summary>
public class AgentContextFinalStatusHandlerTests
{
    [Fact]
    public async Task IdleHandler_CanSendTheNextQueuedMessage()
    {
        var requests = new List<IReadOnlyList<ChatMessage>>();
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) =>
        {
            requests.Add(messages.ToList());
            return ReplyAsync($"reply {requests.Count}", cancellationToken);
        });
        using var context = new AgentContext(new UIAgent(client));
        var queue = new Queue<string>(["second", "third"]);
        var sends = new List<Task>();
        var queueSent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.OnStatusChanged(status =>
        {
            if (status != ConversationStatus.Idle)
                return;

            if (queue.Count > 0)
                sends.Add(context.SendMessageAsync(queue.Dequeue()));
            else
                queueSent.TrySetResult();
        });

        await context.SendMessageAsync("first");
        await queueSent.Task.WaitAsync(TimeSpan.FromSeconds(10));
        foreach (var send in sends)
            await send;

        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Equal(3, context.Turns.Count);
        Assert.Equal(
            ["user:first", "assistant:reply 1", "user:second", "assistant:reply 2", "user:third"],
            requests[^1].Select(message => $"{message.Role}:{message.Text}"));
    }

    [Fact]
    public async Task ErrorHandler_CanSendANewMessage()
    {
        var calls = 0;
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) => ++calls == 1
            ? FailAsync(cancellationToken)
            : ReplyAsync("recovered", cancellationToken));
        using var context = new AgentContext(new UIAgent(client));
        Task? next = null;
        context.OnStatusChanged(status =>
        {
            if (status == ConversationStatus.Error && next is null)
                next = context.SendMessageAsync("second");
        });

        await context.SendMessageAsync("first");
        Assert.NotNull(next);
        await next;

        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Equal(2, context.Turns.Count);
        Assert.Equal("recovered", Assert.Single(context.Turns[1].ResponseBlocks.OfType<TextContentBlock>()).RawText);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LaterStatusHandler_ReadsTheNextSendsStatus_AndTheTurnSaysHowItEnded_WhenAnEarlierOneSendsTheNextMessage(bool fail)
    {
        var calls = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) => ++calls switch
        {
            1 when fail => FailAsync(cancellationToken),
            1 => ReplyAsync("reply 1", cancellationToken),
            _ => ReplyAfterAsync(release.Task, "reply 2", cancellationToken),
        });
        using var context = new AgentContext(new UIAgent(client));
        var ended = fail ? ConversationStatus.Error : ConversationStatus.Idle;
        Task? next = null;
        context.OnStatusChanged(status =>
        {
            if (status == ended && next is null)
                next = context.SendMessageAsync("second");
        });
        var seen = new List<string>();
        context.OnStatusChanged(status => seen.Add($"{status}, first turn failed: {context.Turns[0].IsFailed}"));

        await context.SendMessageAsync("first");

        // The second handler hears the next turn start before the end of the first one, which the first handler
        // reacted to. When the end reaches it, it reads the status of the next send, as PropertyChanged reports the
        // current value; the first turn says how it ended.
        var first = $"first turn failed: {fail}";
        Assert.Equal(
            [
                "Streaming, first turn failed: False",
                $"Streaming, {first}",
                $"Streaming, {first}",
            ],
            seen);
        Assert.Equal(ConversationStatus.Streaming, context.Status);

        release.SetResult();
        Assert.NotNull(next);
        await next;

        Assert.Equal($"Idle, {first}", seen[^1]);
        Assert.Equal(4, seen.Count);
        Assert.Equal(2, context.Turns.Count);
    }

    [Fact]
    public async Task IdleHandler_CancelAsyncCompletesImmediately()
    {
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) => ReplyAsync("reply", cancellationToken));
        using var context = new AgentContext(new UIAgent(client));
        bool? cancelCompletedInHandler = null;
        context.OnStatusChanged(status =>
        {
            if (status == ConversationStatus.Idle)
                cancelCompletedInHandler ??= context.CancelAsync().IsCompletedSuccessfully;
        });

        await context.SendMessageAsync("first");

        Assert.True(cancelCompletedInHandler);
        Assert.False(Assert.Single(context.Turns).IsStopped);
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

    private static async IAsyncEnumerable<ChatResponseUpdate> ReplyAfterAsync(
        Task gate,
        string text,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = Guid.NewGuid().ToString("N"),
            Contents = [new TextContent(text)],
        };
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
