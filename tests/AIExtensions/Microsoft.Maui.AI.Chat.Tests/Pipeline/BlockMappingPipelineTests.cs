// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat.Tests.Pipeline;

public class BlockMappingPipelineTests
{
    private static BlockMappingPipeline CreatePipelineWithTextHandler()
    {
        var options = new UIAgentOptions();
        return new BlockMappingPipeline(options);
    }

    private static async Task<List<ContentBlock>> ProcessAsync(
        BlockMappingPipeline pipeline, ChatResponseUpdate update)
    {
        var blocks = new List<ContentBlock>();
        await foreach (var block in pipeline.Process(update))
        {
            blocks.Add(block);
        }
        return blocks;
    }

    [Fact]
    public async Task Process_SingleTextUpdate_EmitsOneTextContentBlock()
    {
        var pipeline = CreatePipelineWithTextHandler();
        var createdAt = new DateTimeOffset(2026, 8, 7, 12, 34, 0, TimeSpan.Zero);
        var update = new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "msg-1",
            CreatedAt = createdAt,
            Contents = [new TextContent("Hello")]
        };

        var blocks = await ProcessAsync(pipeline, update);

        Assert.Single(blocks);
        var block = Assert.IsType<TextContentBlock>(blocks[0]);
        Assert.Equal("Hello", block.RawText);
        Assert.Equal("msg-1", block.Id);
        Assert.Equal(ChatRole.Assistant, block.Role);
        Assert.Equal(createdAt, block.CreatedAt);
        Assert.Equal(BlockLifecycleState.Active, block.LifecycleState);
    }

    [Fact]
    public async Task Process_MultipleTextUpdates_SameMessageId_SingleBlockAccumulates()
    {
        var pipeline = CreatePipelineWithTextHandler();
        var update1 = new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "msg-1",
            Contents = [new TextContent("Hello")]
        };
        var update2 = new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "msg-1",
            Contents = [new TextContent(" world")]
        };

        var blocks1 = await ProcessAsync(pipeline, update1);
        Assert.Single(blocks1);

        var blocks2 = await ProcessAsync(pipeline, update2);
        Assert.Empty(blocks2);

        var block = Assert.IsType<TextContentBlock>(blocks1[0]);
        Assert.Equal("Hello world", block.RawText);
    }

    [Fact]
    public async Task Process_SingleUpdate_TextAndToolCall_EmitsTwoBlocks()
    {
        // One assistant message that both says something AND calls a tool maps to two
        // distinct blocks: a text block and a function-invocation block (one -> many).
        var pipeline = CreatePipelineWithTextHandler();
        var update = new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "msg-1",
            Contents =
            [
                new TextContent("Let me check the weather."),
                new FunctionCallContent("call-1", "GetWeather",
                    new Dictionary<string, object?> { ["city"] = "Tokyo" }),
            ]
        };

        var blocks = await ProcessAsync(pipeline, update);

        Assert.Equal(2, blocks.Count);
        Assert.Contains(blocks, b => b is TextContentBlock);
        Assert.Contains(blocks, b => b is FunctionInvocationContentBlock);
    }

    [Fact]
    public async Task Process_TextUpdate_NotifyChangedOnUpdate()
    {
        var pipeline = CreatePipelineWithTextHandler();
        var update1 = new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "msg-1",
            Contents = [new TextContent("Hello")]
        };
        var blocks = await ProcessAsync(pipeline, update1);
        var block = blocks[0];
        var changeCount = 0;
        block.OnChanged(() => changeCount++);

        var update2 = new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "msg-1",
            Contents = [new TextContent(" world")]
        };
        await ProcessAsync(pipeline, update2);

        Assert.Equal(1, changeCount);
    }

    [Fact]
    public async Task Process_UpdateWithNoMatchingHandler_NoBlocksEmitted()
    {
        var pipeline = CreatePipelineWithTextHandler();
        var update = new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "msg-1",
            Contents = [new UsageContent(new() { InputTokenCount = 10 })]
        };

        var blocks = await ProcessAsync(pipeline, update);
        Assert.Empty(blocks);
    }

    [Fact]
    public async Task Finalize_CompletesActiveHandlers_TransitionsBlocksToInactive()
    {
        var pipeline = CreatePipelineWithTextHandler();
        var update = new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "msg-1",
            Contents = [new TextContent("Hello")]
        };
        var blocks = await ProcessAsync(pipeline, update);
        var block = blocks[0];
        Assert.Equal(BlockLifecycleState.Active, block.LifecycleState);

        pipeline.Finalize();

        Assert.Equal(BlockLifecycleState.Inactive, block.LifecycleState);
    }

    [Fact]
    public async Task Process_AfterFinalize_NewTextEmitsNewBlock()
    {
        var pipeline = CreatePipelineWithTextHandler();

        var update1 = new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "msg-1",
            Contents = [new TextContent("First")]
        };
        await ProcessAsync(pipeline, update1);
        pipeline.Finalize();

        var update2 = new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "msg-2",
            Contents = [new TextContent("Second")]
        };
        var blocks = await ProcessAsync(pipeline, update2);

        Assert.Single(blocks);
        var block = Assert.IsType<TextContentBlock>(blocks[0]);
        Assert.Equal("Second", block.RawText);
        Assert.Equal("msg-2", block.Id);
    }
}
