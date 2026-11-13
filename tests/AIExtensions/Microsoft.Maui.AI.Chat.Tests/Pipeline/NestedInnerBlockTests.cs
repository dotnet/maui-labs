// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat.Tests.Pipeline;

public class NestedInnerBlockTests
{
    [Fact]
    public async Task CreateInnerBlock_CopiesTheUpdatesMetadataAndIsInactive()
    {
        var pipeline = new BlockMappingPipeline(new UIAgentOptions());
        var createdAt = new DateTimeOffset(2026, 8, 7, 12, 34, 0, TimeSpan.Zero);
        var update = new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            AuthorName = "planner",
            MessageId = "msg-1",
            CreatedAt = createdAt,
            Contents =
            [
                new ToolApprovalRequestContent("req-1", new FunctionCallContent("call-1", "GetWeather")),
            ],
        };

        var blocks = new List<ContentBlock>();
        await foreach (var block in pipeline.Process(update))
            blocks.Add(block);

        var inner = Assert.IsType<ToolApprovalBlock>(Assert.Single(blocks)).InnerBlock;
        Assert.Equal(ChatRole.Assistant, inner.Role);
        Assert.Equal("planner", inner.AuthorName);
        Assert.Equal(createdAt, inner.CreatedAt);
        Assert.Equal(BlockLifecycleState.Inactive, inner.LifecycleState);
    }

    [Fact]
    public void CreateInnerBlock_UsesTheUpdatesMessageIdForText()
    {
        var context = new BlockMappingContext(
            new ChatResponseUpdate { Role = ChatRole.Assistant, MessageId = "msg-1" },
            [new HandlerEntry<TextContentBlock>(new TextBlockHandler())]);

        var inner = Assert.IsType<TextContentBlock>(context.CreateInnerBlock(new TextContent("Hi")));

        Assert.Equal("msg-1", inner.Id);
        Assert.Equal(ChatRole.Assistant, inner.Role);
    }

    [Fact]
    public void CreateInnerBlock_NullContent_Throws()
    {
        var context = new BlockMappingContext(new ChatResponseUpdate());

        Assert.Throws<ArgumentNullException>(() => context.CreateInnerBlock(null!));
    }

    [Fact]
    public async Task CreateInnerBlock_CanUseHandlerSetRecursively()
    {
        var options = new UIAgentOptions();
        options.AddBlockHandler(new WrapperHandler());
        var pipeline = new BlockMappingPipeline(options);
        var update = new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents =
            [
                new WrapperContent(
                    new FunctionCallContent("call-1", "GetWeather", null)),
            ],
        };

        var blocks = new List<ContentBlock>();
        await foreach (var block in pipeline.Process(update))
            blocks.Add(block);

        var wrapper = Assert.IsType<WrapperBlock>(Assert.Single(blocks));
        var inner = Assert.IsType<FunctionInvocationContentBlock>(wrapper.Inner);
        Assert.Equal("call-1", inner.Id);
        Assert.Equal("GetWeather", inner.ToolName);
    }

    private sealed class WrapperContent(AIContent inner) : AIContent
    {
        internal AIContent Inner { get; } = inner;
    }

    private sealed class WrapperBlock : ContentBlock
    {
        internal ContentBlock? Inner { get; set; }
    }

    private sealed class WrapperHandler : ContentBlockHandler<WrapperBlock>
    {
        public override BlockMappingResult<WrapperBlock> Handle(
            BlockMappingContext context,
            WrapperBlock state)
        {
            foreach (var content in context.UnhandledContents)
            {
                if (content is not WrapperContent wrapper)
                    continue;

                context.MarkHandled(content);
                state.Id = "wrapper";
                state.Inner = context.CreateInnerBlock(wrapper.Inner);
                return BlockMappingResult<WrapperBlock>.Emit(state, state);
            }

            return state.Id.Length == 0
                ? BlockMappingResult<WrapperBlock>.Pass()
                : BlockMappingResult<WrapperBlock>.Complete();
        }
    }
}
