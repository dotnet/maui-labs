// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat.Tests.Pipeline;

public class ReasoningHandlerTests
{
    [Fact]
    public async Task TextReasoningContent_EmitsAndAccumulatesOneBlock()
    {
        var pipeline = CreatePipeline();
        var first = await ProcessAsync(pipeline, CreateReasoningUpdate("Step 1: "));
        var block = Assert.IsType<ReasoningContentBlock>(Assert.Single(first));
        var changes = 0;
        block.OnChanged(() => changes++);

        var second = await ProcessAsync(
            pipeline,
            CreateReasoningUpdate("analyze the problem"));

        Assert.Empty(second);
        Assert.Equal("Step 1: analyze the problem", block.Text);
        Assert.Equal(1, changes);
        Assert.Equal(BlockLifecycleState.Active, block.LifecycleState);
    }

    [Fact]
    public async Task ReasoningFollowedByText_CompletesBeforeTextBlock()
    {
        var pipeline = CreatePipeline();
        var reasoning = Assert.IsType<ReasoningContentBlock>(
            Assert.Single(await ProcessAsync(pipeline, CreateReasoningUpdate("Thinking..."))));

        var textBlocks = await ProcessAsync(
            pipeline,
            new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                MessageId = "message-1",
                Contents = [new TextContent("The answer is 42.")],
            });

        Assert.Equal(BlockLifecycleState.Inactive, reasoning.LifecycleState);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(textBlocks));
        Assert.Equal("The answer is 42.", text.RawText);
    }

    [Fact]
    public async Task ProtectedReasoning_EmitsEncryptedBlock()
    {
        var pipeline = CreatePipeline();
        var update = new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "message-1",
            Contents =
            [
                new TextReasoningContent(null)
                {
                    ProtectedData = "encrypted-data",
                },
            ],
        };

        var block = Assert.IsType<ReasoningContentBlock>(
            Assert.Single(await ProcessAsync(pipeline, update)));

        Assert.True(block.IsEncrypted);
        Assert.Equal("encrypted-data", block.ProtectedData);
    }

    [Fact]
    public async Task EmptyReasoningContent_ProducesNoBlock()
    {
        var pipeline = CreatePipeline();

        var blocks = await ProcessAsync(
            pipeline,
            CreateReasoningUpdate(text: null));

        Assert.Empty(blocks);
    }

    [Fact]
    public async Task ReasoningFromADifferentMessage_StartsANewBlock()
    {
        var pipeline = CreatePipeline();
        var first = Assert.IsType<ReasoningContentBlock>(
            Assert.Single(await ProcessAsync(pipeline, CreateReasoningUpdate("First ", messageId: "first"))));
        Assert.Empty(await ProcessAsync(pipeline, CreateReasoningUpdate("thought", messageId: "first")));

        var second = Assert.IsType<ReasoningContentBlock>(
            Assert.Single(await ProcessAsync(pipeline, CreateReasoningUpdate("Second thought", messageId: "second"))));

        Assert.Equal("First thought", first.Text);
        Assert.Equal("Second thought", second.Text);
        Assert.Equal(BlockLifecycleState.Inactive, first.LifecycleState);
        Assert.Equal(BlockLifecycleState.Active, second.LifecycleState);
    }

    [Fact]
    public async Task ReasoningAfterProtectedData_StartsANewBlockAndTheDataStaysWithItsItem()
    {
        var pipeline = CreatePipeline();
        var first = Assert.IsType<ReasoningContentBlock>(
            Assert.Single(await ProcessAsync(pipeline, CreateReasoningUpdate("**First**\n\nFirst body", messageId: null))));
        Assert.Empty(await ProcessAsync(pipeline, CreateProtectedUpdate("first-item")));

        var second = Assert.IsType<ReasoningContentBlock>(
            Assert.Single(await ProcessAsync(pipeline, CreateReasoningUpdate("**Second**\n\nSecond body", messageId: null))));
        Assert.Empty(await ProcessAsync(pipeline, CreateProtectedUpdate("second-item")));

        Assert.Equal("**First**\n\nFirst body", first.Text);
        Assert.Equal("first-item", first.ProtectedData);
        Assert.False(first.IsEncrypted);
        Assert.Equal(BlockLifecycleState.Inactive, first.LifecycleState);
        Assert.Equal("**Second**\n\nSecond body", second.Text);
        Assert.Equal("second-item", second.ProtectedData);
    }

    [Fact]
    public async Task ProtectedDataAndTheNextItemInOneUpdate_CompleteTheFirstItemAndStartTheNext()
    {
        var pipeline = CreatePipeline();
        var first = Assert.IsType<ReasoningContentBlock>(
            Assert.Single(await ProcessAsync(pipeline, CreateReasoningUpdate("First", messageId: null))));

        var blocks = await ProcessAsync(pipeline, new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents =
            [
                new TextReasoningContent(string.Empty) { ProtectedData = "first-item" },
                new TextReasoningContent("Second"),
            ],
        });

        var second = Assert.IsType<ReasoningContentBlock>(Assert.Single(blocks));
        Assert.Equal("First", first.Text);
        Assert.Equal("first-item", first.ProtectedData);
        Assert.Equal("Second", second.Text);
        Assert.Null(second.ProtectedData);
    }

    [Fact]
    public async Task CompleteMessageWithTwoReasoningItems_ShowsTwoBlocks()
    {
        var pipeline = CreatePipeline();

        var blocks = await ProcessAsync(pipeline, new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "message-1",
            Contents =
            [
                new TextReasoningContent("First") { ProtectedData = "first-item" },
                new TextReasoningContent("Second") { ProtectedData = "second-item" },
            ],
        });

        Assert.Equal(["First", "Second"], blocks.Cast<ReasoningContentBlock>().Select(block => block.Text));
        Assert.Equal(["first-item", "second-item"], blocks.Cast<ReasoningContentBlock>().Select(block => block.ProtectedData));
    }

    private static ChatResponseUpdate CreateReasoningUpdate(string text, string? messageId) =>
        new()
        {
            Role = ChatRole.Assistant,
            MessageId = messageId,
            Contents = [new TextReasoningContent(text)],
        };

    private static ChatResponseUpdate CreateProtectedUpdate(string protectedData) =>
        new()
        {
            Role = ChatRole.Assistant,
            Contents = [new TextReasoningContent(string.Empty) { ProtectedData = protectedData }],
        };

    private static BlockMappingPipeline CreatePipeline() =>
        new(new UIAgentOptions());

    private static ChatResponseUpdate CreateReasoningUpdate(string? text) =>
        new()
        {
            Role = ChatRole.Assistant,
            MessageId = "message-1",
            Contents = [new TextReasoningContent(text)],
        };

    private static async Task<List<ContentBlock>> ProcessAsync(
        BlockMappingPipeline pipeline,
        ChatResponseUpdate update)
    {
        var blocks = new List<ContentBlock>();
        await foreach (var block in pipeline.Process(update))
            blocks.Add(block);
        return blocks;
    }
}
