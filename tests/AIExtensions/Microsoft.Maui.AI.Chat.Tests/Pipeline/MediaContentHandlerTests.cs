// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat.Tests.TestHelpers;

namespace Microsoft.Maui.AI.Chat.Tests.Pipeline;

public class MediaContentHandlerTests
{
    private static BlockMappingPipeline CreatePipeline()
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
    public async Task DataContent_EmitsMediaContentBlock()
    {
        var pipeline = CreatePipeline();
        var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var update = new ChatResponseUpdate
        {
            Role = ChatRole.User,
            MessageId = "msg-1",
            Contents = [new DataContent(imageBytes, "image/png")]
        };

        var blocks = await ProcessAsync(pipeline, update);

        Assert.Single(blocks);
        var block = Assert.IsType<MediaContentBlock>(blocks[0]);
        Assert.Same(update.Contents[0], block.Content);
        Assert.Equal(ChatRole.User, block.Role);
        Assert.Equal(BlockLifecycleState.Active, block.LifecycleState);
    }

    [Fact]
    public async Task UriContent_EmitsMediaContentBlock()
    {
        var pipeline = CreatePipeline();
        var image = new UriContent(new Uri("https://example.com/cat.png"), "image/png");

        var block = Assert.IsType<MediaContentBlock>(Assert.Single(await ProcessAsync(
            pipeline,
            new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                MessageId = "msg-1",
                Contents = [image],
            })));

        Assert.Same(image, block.Content);
    }

    [Fact]
    public void MediaContentBlock_RequiresDataOrUriContent()
    {
        Assert.Throws<ArgumentNullException>(() => new MediaContentBlock(null!));
        Assert.Throws<ArgumentException>(() => new MediaContentBlock(new TextContent("not media")));
    }

    [Fact]
    public async Task ImageGenerationToolResult_EmitsMediaContentBlockFromOutputs()
    {
        var pipeline = CreatePipeline();
        var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var result = new ImageGenerationToolResultContent("call-1")
        {
            Outputs = [new DataContent(imageBytes, "image/png")]
        };
        var update = new ChatResponseUpdate
        {
            Role = ChatRole.Tool,
            MessageId = "msg-img",
            Contents = [result]
        };

        var blocks = await ProcessAsync(pipeline, update);

        var block = Assert.IsType<MediaContentBlock>(Assert.Single(blocks));
        Assert.Same(result.Outputs[0], block.Content);
    }

    [Fact]
    public async Task ImageGenerationToolResult_WithSeveralOutputs_EmitsOneBlockPerOutput()
    {
        var pipeline = CreatePipeline();
        var result = new ImageGenerationToolResultContent("call-1")
        {
            Outputs =
            [
                new DataContent(new byte[] { 1 }, "image/png"),
                new TextContent("caption"),
                new UriContent(new Uri("https://example.com/2.png"), "image/png"),
            ],
        };

        var blocks = await ProcessAsync(pipeline, new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = [result],
        });

        Assert.Collection(
            blocks,
            block => Assert.Same(result.Outputs[0], Assert.IsType<MediaContentBlock>(block).Content),
            block => Assert.Same(result.Outputs[2], Assert.IsType<MediaContentBlock>(block).Content));
        Assert.NotEqual(blocks[0].Id, blocks[1].Id);
    }

    [Fact]
    public async Task ImageGenerationToolResult_PartialImages_ReplaceTheCallsBlock()
    {
        var pipeline = CreatePipeline();
        var block = Assert.IsType<MediaContentBlock>(Assert.Single(await ProcessAsync(
            pipeline,
            CreateImageResultUpdate("call-1", 1))));
        var changes = block.RecordPropertyChanges();

        var partial = await ProcessAsync(pipeline, CreateImageResultUpdate("call-1", 2));
        var final = CreateImageResultUpdate("call-1", 3);
        var completed = await ProcessAsync(pipeline, final);

        Assert.Empty(partial);
        Assert.Empty(completed);
        Assert.Same(
            ((ImageGenerationToolResultContent)final.Contents[0]).Outputs![0],
            block.Content);
        Assert.Equal([nameof(MediaContentBlock.Content), nameof(MediaContentBlock.Content)], changes);
        Assert.Equal(BlockLifecycleState.Active, block.LifecycleState);

        pipeline.Finalize();
        Assert.Equal(BlockLifecycleState.Inactive, block.LifecycleState);
    }

    [Fact]
    public async Task ImageGenerationToolResult_AfterOtherContent_StillReplacesTheCallsBlock()
    {
        var pipeline = CreatePipeline();
        var block = Assert.IsType<MediaContentBlock>(Assert.Single(await ProcessAsync(
            pipeline,
            CreateImageResultUpdate("call-1", 1))));
        await ProcessAsync(pipeline, new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = [new TextContent("Drawing...")],
        });
        var final = CreateImageResultUpdate("call-1", 2);

        var blocks = await ProcessAsync(pipeline, final);

        Assert.DoesNotContain(blocks, emitted => emitted is MediaContentBlock);
        Assert.Same(((ImageGenerationToolResultContent)final.Contents[0]).Outputs![0], block.Content);
    }

    [Fact]
    public async Task ImageGenerationToolResults_ForDifferentCalls_EmitSeparateBlocks()
    {
        var pipeline = CreatePipeline();

        var first = Assert.Single(await ProcessAsync(pipeline, CreateImageResultUpdate("call-1", 1)));
        var second = Assert.Single(await ProcessAsync(pipeline, CreateImageResultUpdate("call-2", 2)));

        Assert.NotSame(first, second);
        Assert.Equal(BlockLifecycleState.Inactive, first.LifecycleState);
    }

    [Fact]
    public async Task MultipleDataContents_EmitBlocksWithUniqueIds()
    {
        var pipeline = CreatePipeline();
        var update = new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "msg-1",
            Contents =
            [
                new DataContent(new byte[] { 1 }, "image/png"),
                new DataContent(new byte[] { 2 }, "image/png"),
            ],
        };

        var blocks = await ProcessAsync(pipeline, update);

        Assert.Equal(2, blocks.Count);
        Assert.All(blocks, block => Assert.IsType<MediaContentBlock>(block));
        Assert.NotEqual(blocks[0].Id, blocks[1].Id);
    }

    [Fact]
    public async Task DataContentsInSeparateUpdates_EmitSeparateBlocks()
    {
        var pipeline = CreatePipeline();
        var update1 = new ChatResponseUpdate
        {
            Role = ChatRole.User,
            MessageId = "msg-1",
            Contents = [new DataContent(new byte[] { 1, 2, 3 }, "image/png")]
        };
        var update2 = new ChatResponseUpdate
        {
            Role = ChatRole.User,
            MessageId = "msg-1",
            Contents = [new DataContent(new byte[] { 4, 5, 6 }, "image/jpeg")]
        };

        var first = Assert.Single(await ProcessAsync(pipeline, update1));
        var second = Assert.Single(await ProcessAsync(pipeline, update2));

        Assert.Same(update1.Contents[0], Assert.IsType<MediaContentBlock>(first).Content);
        Assert.Same(update2.Contents[0], Assert.IsType<MediaContentBlock>(second).Content);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(BlockLifecycleState.Inactive, first.LifecycleState);
    }

    [Fact]
    public async Task TextAndDataContent_ProducesSeparateBlocks()
    {
        var pipeline = CreatePipeline();
        var update = new ChatResponseUpdate
        {
            Role = ChatRole.User,
            MessageId = "msg-1",
            Contents =
            [
                new TextContent("Check this image"),
                new DataContent(new byte[] { 1, 2, 3 }, "image/png")
            ]
        };

        var blocks = await ProcessAsync(pipeline, update);

        // The blocks follow the order of the contents; upstream emits them in handler order (media first).
        Assert.Equal(2, blocks.Count);
        Assert.IsType<TextContentBlock>(blocks[0]);
        Assert.IsType<MediaContentBlock>(blocks[1]);
    }

    [Fact]
    public async Task DataContentFollowedByText_CompletesMediaEmitsText()
    {
        var pipeline = CreatePipeline();

        var mediaUpdate = new ChatResponseUpdate
        {
            Role = ChatRole.User,
            MessageId = "msg-1",
            Contents = [new DataContent(new byte[] { 1, 2, 3 }, "image/png")]
        };
        var mediaBlocks = await ProcessAsync(pipeline, mediaUpdate);

        var textUpdate = new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "msg-2",
            Contents = [new TextContent("I see an image.")]
        };
        var textBlocks = await ProcessAsync(pipeline, textUpdate);

        var mediaBlock = Assert.IsType<MediaContentBlock>(mediaBlocks[0]);
        Assert.Equal(BlockLifecycleState.Inactive, mediaBlock.LifecycleState);

        Assert.Single(textBlocks);
        Assert.IsType<TextContentBlock>(textBlocks[0]);
    }

    [Fact]
    public async Task AudioDataContent_EmitsMediaContentBlock()
    {
        var pipeline = CreatePipeline();
        var update = new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "msg-1",
            Contents = [new DataContent(new byte[] { 0xFF, 0xFB }, "audio/mpeg")]
        };

        var blocks = await ProcessAsync(pipeline, update);

        Assert.Single(blocks);
        var block = Assert.IsType<MediaContentBlock>(blocks[0]);
        Assert.Equal("audio/mpeg", Assert.IsType<DataContent>(block.Content).MediaType);
    }

    [Fact]
    public async Task MediaBlock_HasAUniqueId_NotTheMessageId()
    {
        var pipeline = CreatePipeline();
        var update = new ChatResponseUpdate
        {
            Role = ChatRole.User,
            MessageId = "test-msg-id",
            Contents = [new DataContent(new byte[] { 1 }, "image/png")]
        };

        var blocks = await ProcessAsync(pipeline, update);

        var block = Assert.IsType<MediaContentBlock>(blocks[0]);
        Assert.False(string.IsNullOrEmpty(block.Id));
        Assert.NotEqual("test-msg-id", block.Id);
    }

    private static ChatResponseUpdate CreateImageResultUpdate(string callId, byte image) => new()
    {
        Role = ChatRole.Assistant,
        MessageId = "msg-img",
        Contents =
        [
            new ImageGenerationToolResultContent(callId)
            {
                Outputs = [new DataContent(new byte[] { image }, "image/png")],
            },
        ],
    };
}
