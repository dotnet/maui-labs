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

        var sendTask = context.SendMessageAsync("Hello");
        await streamStarted.Task;

        await context.CancelAsync();
        await sendTask;

        var turn = context.Turns[0];
        Assert.Empty(turn.ResponseBlocks);

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

    // ---- Integration: Error recovery flow ----
}
