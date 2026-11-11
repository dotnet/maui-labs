// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat.Tests.TestHelpers;

namespace Microsoft.Maui.AI.Chat.Tests.Engine;

public class ConversationTurnMetadataTests
{
    [Fact]
    public async Task SendMessageAsync_LastReportedModelWinsAndUsageIsSummed()
    {
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) => EmitAsync(
            cancellationToken,
            Text("Hello", modelId: "model-a"),
            Text(" there", modelId: null),
            Usage(input: 5, output: 7, modelId: "model-b"),
            Usage(input: 3, output: 2, modelId: null)));
        using var context = new AgentContext(new UIAgent(client));

        await context.SendMessageAsync("Hi");

        var turn = Assert.Single(context.Turns);
        Assert.Equal("model-b", turn.ModelId);
        Assert.NotNull(turn.Usage);
        Assert.Equal(8, turn.Usage.InputTokenCount);
        Assert.Equal(9, turn.Usage.OutputTokenCount);
        Assert.Equal(17, turn.Usage.TotalTokenCount);
        Assert.Equal("Hello there", Assert.Single(turn.ResponseBlocks.OfType<TextContentBlock>()).RawText);
    }

    [Fact]
    public async Task SendMessageAsync_ModelIsKnownWhenTheReportingUpdatesBlockIsAdded()
    {
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) => EmitAsync(
            cancellationToken,
            Text("Hello", modelId: "model-a")));
        using var context = new AgentContext(new UIAgent(client));
        string? modelAtBlock = null;
        context.OnBlockAdded((turn, block) =>
        {
            if (block is TextContentBlock { Role.Value: "assistant" })
                modelAtBlock = turn.ModelId;
        });

        await context.SendMessageAsync("Hi");

        Assert.Equal("model-a", modelAtBlock);
    }

    [Fact]
    public async Task SendMessageAsync_SumsUsageOverEveryRound()
    {
        var calls = 0;
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) => ++calls == 1
            ? EmitAsync(
                cancellationToken,
                new ChatResponseUpdate
                {
                    Role = ChatRole.Assistant,
                    MessageId = "assistant-1",
                    ModelId = "model-1",
                    Contents =
                    [
                        new FunctionCallContent("call-1", "Highlight"),
                        new UsageContent(new UsageDetails { InputTokenCount = 10, OutputTokenCount = 1 }),
                    ],
                })
            : EmitAsync(
                cancellationToken,
                Text("Done", modelId: "model-2"),
                Usage(input: 4, output: 3, modelId: null)));
        using var context = new AgentContext(new UIAgent(
            client,
            options => options.RegisterUIAction(
                AIFunctionFactory.Create(() => "highlighted", "Highlight", "Highlights."))));

        await context.SendMessageAsync("Highlight it");

        Assert.Equal(2, calls);
        var turn = Assert.Single(context.Turns);
        Assert.Equal("model-2", turn.ModelId);
        Assert.Equal(14, turn.Usage?.InputTokenCount);
        Assert.Equal(4, turn.Usage?.OutputTokenCount);
    }

    [Fact]
    public async Task SendMessageAsync_WithoutModelOrUsage_LeavesThemNull()
    {
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) =>
            ResponseEmitters.EmitTextResponse("Hello", cancellationToken));
        using var context = new AgentContext(new UIAgent(client));

        await context.SendMessageAsync("Hi");

        var turn = Assert.Single(context.Turns);
        Assert.Null(turn.ModelId);
        Assert.Null(turn.Usage);
    }

    [Fact]
    public async Task RetryAsync_ForgetsTheFailedAttemptsModelAndUsage()
    {
        var calls = 0;
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) => ++calls == 1
            ? FailAfterAsync(Usage(input: 5, output: 5, modelId: "failing-model"), cancellationToken)
            : ResponseEmitters.EmitTextResponse("Recovered", cancellationToken));
        using var context = new AgentContext(new UIAgent(client));

        await context.SendMessageAsync("Hi");

        var turn = Assert.Single(context.Turns);
        Assert.Equal(ConversationStatus.Error, context.Status);
        Assert.Equal("failing-model", turn.ModelId);

        await context.RetryAsync();

        Assert.Equal(ConversationStatus.Idle, context.Status);
        Assert.Null(turn.ModelId);
        Assert.Null(turn.Usage);
    }

    [Fact]
    public async Task RestoreAsync_RestoresModelAndUsage()
    {
        var thread = new InMemoryConversationThread("thread-1");
        var client = new DelegatingStreamingChatClient();
        client.SetHandler((messages, options, cancellationToken) => EmitAsync(
            cancellationToken,
            Text("Hello", modelId: "model-a"),
            Usage(input: 5, output: 7, modelId: null)));
        using (var live = new AgentContext(new UIAgent(client, options => options.Thread = thread)))
        {
            await live.SendMessageAsync("Hi");
        }

        using var restored = new AgentContext(new UIAgent(
            new DelegatingStreamingChatClient(),
            options => options.Thread = thread));
        await restored.RestoreAsync();

        var turn = Assert.Single(restored.Turns);
        Assert.Equal("model-a", turn.ModelId);
        Assert.Equal(5, turn.Usage?.InputTokenCount);
        Assert.Equal(7, turn.Usage?.OutputTokenCount);
    }

    private static ChatResponseUpdate Text(string text, string? modelId) => new()
    {
        Role = ChatRole.Assistant,
        MessageId = "assistant",
        ModelId = modelId,
        Contents = [new TextContent(text)],
    };

    private static ChatResponseUpdate Usage(long input, long output, string? modelId) => new()
    {
        Role = ChatRole.Assistant,
        MessageId = "assistant",
        ModelId = modelId,
        Contents =
        [
            new UsageContent(new UsageDetails
            {
                InputTokenCount = input,
                OutputTokenCount = output,
                TotalTokenCount = input + output,
            }),
        ],
    };

    private static async IAsyncEnumerable<ChatResponseUpdate> EmitAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken,
        params ChatResponseUpdate[] updates)
    {
        foreach (var update in updates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return update;
        }

        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> FailAfterAsync(
        ChatResponseUpdate update,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return update;
        await Task.CompletedTask;
        throw new InvalidOperationException("failed");
    }
}
