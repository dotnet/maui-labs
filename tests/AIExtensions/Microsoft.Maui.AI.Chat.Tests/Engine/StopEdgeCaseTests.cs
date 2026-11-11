// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat.Tests.TestHelpers;

namespace Microsoft.Maui.AI.Chat.Tests.Engine;

/// <summary>A stopped turn keeps what it shows, and its history stays valid for the next request.</summary>
public class StopEdgeCaseTests
{
    [Fact]
    public async Task StopBeforeTheRequestIsSent_KeepsTheMessageShownAndInTheHistory()
    {
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Second reply.", ct));
        using var context = new AgentContext(new UIAgent(model));
        using var cancellation = new CancellationTokenSource();
        var registration = context.OnTurnAdded(_ => cancellation.Cancel());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => context.SendMessageAsync("first", cancellation.Token));
        registration.Dispose();

        var turn = Assert.Single(context.Turns);
        Assert.True(turn.IsStopped);
        Assert.Equal("first", Assert.IsType<TextContentBlock>(Assert.Single(turn.RequestBlocks)).RawText);
        Assert.Empty(turn.ResponseBlocks);
        Assert.Empty(model.Requests);
        Assert.Equal(ConversationStatus.Idle, context.Status);

        await context.SendMessageAsync("second");

        Assert.Equal(["first", "second"], Assert.Single(model.Requests).Select(message => message.Text));
    }

    [Fact]
    public async Task RetryStoppedBeforeTheRequestIsSent_KeepsTheMessageInTheHistory()
    {
        var model = new ScriptedChatClient();
        model.Enqueue(ct => ResponseEmitters.EmitErrorAfterTokens([], new InvalidOperationException("Boom"), ct));
        model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Second reply.", ct));
        using var context = new AgentContext(new UIAgent(model));
        await context.SendMessageAsync("first");
        Assert.Equal(ConversationStatus.Error, context.Status);
        using var cancellation = new CancellationTokenSource();
        var registration = context.OnResponseBlocksReset(_ => cancellation.Cancel());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => context.RetryAsync(cancellation.Token));
        registration.Dispose();

        var turn = Assert.Single(context.Turns);
        Assert.True(turn.IsStopped);
        Assert.Single(turn.RequestBlocks);
        Assert.Equal(ConversationStatus.Idle, context.Status);

        await context.SendMessageAsync("second");

        Assert.Equal(2, model.Requests.Count);
        Assert.Equal(["first", "second"], model.Requests[1].Select(message => message.Text));
    }

    [Fact]
    public async Task StopWhileOnlyReasoningStreamed_ShowsTheReasoningButLeavesItOutOfTheHistory()
    {
        var thread = new InMemoryConversationThread("thread-1");
        var model = new ScriptedChatClient();
        model.Enqueue(ReasoningThenWaitAsync);
        model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Reply.", ct));
        model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Restored reply.", ct));
        using var context = new AgentContext(new UIAgent(model, options => options.Thread = thread));
        context.OnBlockAdded((turn, block) =>
        {
            if (block is ReasoningContentBlock)
                _ = context.CancelAsync();
        });

        await context.SendMessageAsync("Think hard");
        await context.SendMessageAsync("next");

        var stopped = context.Turns[0];
        Assert.True(stopped.IsStopped);
        Assert.Equal("Let me think", Assert.IsType<ReasoningContentBlock>(Assert.Single(stopped.ResponseBlocks)).Text);
        Assert.Equal(["Think hard", "next"], model.Requests[1].Select(message => message.Text));

        using var restored = new AgentContext(new UIAgent(model, options => options.Thread = thread));
        await restored.RestoreAsync();
        await restored.SendMessageAsync("after restore");

        Assert.IsType<ReasoningContentBlock>(Assert.Single(restored.Turns[0].ResponseBlocks));
        Assert.Equal(
            ["Think hard", "next", "Reply.", "after restore"],
            model.Requests[2].Select(message => message.Text));
    }

    [Fact]
    public async Task StopAfterReasoningAndText_KeepsTheReasoningInTheHistory()
    {
        var model = new ScriptedChatClient();
        model.Enqueue(ReasoningAndTextThenWaitAsync);
        model.Enqueue(ct => ResponseEmitters.EmitTextResponse("Reply.", ct));
        using var context = new AgentContext(new UIAgent(model));
        context.OnBlockAdded((turn, block) =>
        {
            if (block is TextContentBlock && block.Role == ChatRole.Assistant)
                _ = context.CancelAsync();
        });

        await context.SendMessageAsync("Think hard");
        await context.SendMessageAsync("next");

        var assistant = Assert.Single(model.Requests[1], message => message.Role == ChatRole.Assistant);
        Assert.Contains(assistant.Contents, content => content is TextReasoningContent);
        Assert.Equal("Partial answer", assistant.Text);
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> ReasoningThenWaitAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "response-1",
            Contents = [new TextReasoningContent("Let me think")],
        };
        await Task.Delay(Timeout.Infinite, cancellationToken);
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> ReasoningAndTextThenWaitAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "response-1",
            Contents = [new TextReasoningContent("Let me think")],
        };
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "response-1",
            Contents = [new TextContent("Partial answer")],
        };
        await Task.Delay(Timeout.Infinite, cancellationToken);
    }
}
