// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using Microsoft.Maui.AI.Chat.Tests.TestHelpers;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat.Tests.Samples;

public class S09_CancelTest
{
    // Stopping keeps the partial response, marks the turn stopped, and returns to Idle.

    [Fact]
    public async Task CancelAsync_KeepsPartialBlocks_ReturnsIdle()
    {
        var streamStarted = new TaskCompletionSource();
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((msgs, opts, ct) => SlowStream(streamStarted, ct));

        var agent = new UIAgent(client);
        var context = new AgentContext(agent);

        var sendTask = context.SendMessageAsync("Write a long essay");
        await streamStarted.Task;

        Assert.Equal(ConversationStatus.Streaming, context.Status);

        await context.CancelAsync();
        await sendTask;

        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Null(context.Error);

        var turn = context.Turns[0];
        Assert.True(turn.IsStopped);
        var partial = Assert.IsType<TextContentBlock>(Assert.Single(turn.ResponseBlocks));
        Assert.Equal("Once upon a time", partial.RawText);
    }

    [Fact]
    public async Task CancelAsync_WhenIdle_IsNoOp()
    {
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((msgs, opts, ct) =>
            ResponseEmitters.EmitTextResponse("Done", ct));

        var agent = new UIAgent(client);
        var context = new AgentContext(agent);
        await context.SendMessageAsync("Hi");

        Assert.Equal(ConversationStatus.Idle, context.Status);
        await context.CancelAsync();
        Assert.Equal(ConversationStatus.Idle, context.Status);
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> SlowStream(
        TaskCompletionSource streamStarted,
        [EnumeratorCancellation] CancellationToken ct)
    {
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = Guid.NewGuid().ToString("N"),
            Contents = [new TextContent("Once upon a time")]
        };
        streamStarted.TrySetResult();
        try
        {
            await Task.Delay(Timeout.Infinite, ct);
        }
        catch (OperationCanceledException)
        {
            yield break;
        }
    }
}
