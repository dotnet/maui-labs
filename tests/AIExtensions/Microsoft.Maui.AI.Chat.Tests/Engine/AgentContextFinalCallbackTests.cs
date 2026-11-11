// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat.Tests.TestHelpers;

namespace Microsoft.Maui.AI.Chat.Tests.Engine;

/// <summary>
/// The status callback that reports the end of a turn may start the next one, for example to send a
/// queued message.
/// </summary>
public class AgentContextFinalCallbackTests
{
    [Fact]
    public async Task IdleCallback_CanSendTheNextQueuedMessage()
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
        context.RegisterOnStatusChanged(status =>
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
    public async Task ErrorCallback_CanSendANewMessage()
    {
        var calls = 0;
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) => ++calls == 1
            ? FailAsync(cancellationToken)
            : ReplyAsync("recovered", cancellationToken));
        using var context = new AgentContext(new UIAgent(client));
        Task? next = null;
        context.RegisterOnStatusChanged(status =>
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

    [Fact]
    public async Task IdleCallback_CancelAsyncCompletesImmediately()
    {
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) => ReplyAsync("reply", cancellationToken));
        using var context = new AgentContext(new UIAgent(client));
        bool? cancelCompletedInCallback = null;
        context.RegisterOnStatusChanged(status =>
        {
            if (status == ConversationStatus.Idle)
                cancelCompletedInCallback ??= context.CancelAsync().IsCompletedSuccessfully;
        });

        await context.SendMessageAsync("first");

        Assert.True(cancelCompletedInCallback);
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
