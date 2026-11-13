// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat.Tests.TestHelpers;

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
    public async Task Process_UpdateWithOtherContent_CompletesActiveBlock()
    {
        var pipeline = CreatePipelineWithTextHandler();
        var blocks = await ProcessAsync(pipeline, new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "msg-1",
            Contents = [new TextContent("Hello")],
        });

        await ProcessAsync(pipeline, new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "msg-1",
            Contents = [new FunctionCallContent("call-1", "GetWeather")],
        });

        Assert.Equal(BlockLifecycleState.Inactive, blocks[0].LifecycleState);
    }

    [Fact]
    public async Task Process_UpdateWithoutContents_KeepsTheMessageInOneBlock()
    {
        // Upstream completes the block on any update without text, so a metadata-only update in the
        // middle of a message (such as a late model id) split it into two blocks with the same id.
        var pipeline = CreatePipelineWithTextHandler();
        var block = Assert.IsType<TextContentBlock>(Assert.Single(await ProcessAsync(pipeline, new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "msg-1",
            Contents = [new TextContent("First ")],
        })));

        Assert.Empty(await ProcessAsync(pipeline, new ChatResponseUpdate { MessageId = "msg-1", ModelId = "model" }));
        Assert.Empty(await ProcessAsync(pipeline, new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "msg-1",
            Contents = [new TextContent("second")],
        }));

        Assert.Equal("First second", block.RawText);
        Assert.Equal(BlockLifecycleState.Active, block.LifecycleState);
    }

    [Fact]
    public async Task Process_UsageOnlyUpdate_KeepsTheMessageInOneBlock()
    {
        var pipeline = CreatePipelineWithTextHandler();
        var block = Assert.IsType<TextContentBlock>(Assert.Single(await ProcessAsync(pipeline, new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = [new TextContent("First ")],
        })));

        Assert.Empty(await ProcessAsync(pipeline, new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = [new UsageContent(new UsageDetails { InputTokenCount = 3 })],
        }));
        Assert.Empty(await ProcessAsync(pipeline, new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = [new TextContent("second")],
        }));

        Assert.Equal("First second", block.RawText);
    }

    [Fact]
    public void AddBlockHandler_NullHandlerThrows()
    {
        var options = new UIAgentOptions();

        Assert.Throws<ArgumentNullException>(() => options.AddBlockHandler<RichContentBlock>(null!));
    }

    [Fact]
    public async Task Process_TextFromANewMessage_StartsANewBlock()
    {
        var pipeline = CreatePipelineWithTextHandler();
        var first = Assert.IsType<TextContentBlock>(Assert.Single(await ProcessAsync(
            pipeline,
            new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                MessageId = "msg-1",
                Contents = [new TextContent("First")],
            })));

        var second = Assert.IsType<TextContentBlock>(Assert.Single(await ProcessAsync(
            pipeline,
            new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                MessageId = "msg-2",
                Contents = [new TextContent("Second")],
            })));

        Assert.Equal("First", first.RawText);
        Assert.Equal(BlockLifecycleState.Inactive, first.LifecycleState);
        Assert.Equal("Second", second.RawText);
        Assert.Equal("msg-2", second.Id);
    }

    [Theory]
    [InlineData(null, "msg-1")]
    [InlineData("msg-1", null)]
    public async Task Process_TextWithoutAMessageId_ContinuesTheActiveBlock(
        string? firstMessageId,
        string? secondMessageId)
    {
        var pipeline = CreatePipelineWithTextHandler();
        var block = Assert.IsType<TextContentBlock>(Assert.Single(await ProcessAsync(
            pipeline,
            new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                MessageId = firstMessageId,
                Contents = [new TextContent("Hello")],
            })));

        Assert.Empty(await ProcessAsync(
            pipeline,
            new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                MessageId = secondMessageId,
                Contents = [new TextContent(" world")],
            }));

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
    public async Task Process_TextUpdate_RaisesTheTextChange()
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
        var changes = block.RecordPropertyChanges();

        var update2 = new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "msg-1",
            Contents = [new TextContent(" world")]
        };
        await ProcessAsync(pipeline, update2);

        Assert.Equal([nameof(RichContentBlock.RawText), nameof(RichContentBlock.Content)], changes);
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
        var changes = block.RecordPropertyChanges();
        Assert.Equal(BlockLifecycleState.Active, block.LifecycleState);

        pipeline.Finalize();

        Assert.Equal(BlockLifecycleState.Inactive, block.LifecycleState);
        Assert.Equal([nameof(ContentBlock.LifecycleState)], changes);
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
