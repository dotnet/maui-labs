// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat.Tests.Pipeline;

/// <summary>
/// The blocks of one update follow the order of the contents they show, whatever the order of the handlers
/// that create them. A complete response delivers each message as one update.
/// </summary>
public class BlockOrderTests
{
    [Fact]
    public async Task OneUpdate_EmitsBlocksInTheOrderOfItsContents()
    {
        var pipeline = new BlockMappingPipeline(new UIAgentOptions());

        var blocks = await ProcessAsync(pipeline, new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "message-1",
            Contents =
            [
                new TextReasoningContent("Planning"),
                new TextContent("Let me calculate that."),
                new FunctionCallContent("call-1", "calculate"),
                new DataContent(new byte[] { 1, 2, 3 }, "image/png"),
                new ToolApprovalRequestContent("approval-1", new FunctionCallContent("call-2", "delete")),
            ],
        });

        Assert.Collection(
            blocks,
            block => Assert.Equal("Planning", Assert.IsType<ReasoningContentBlock>(block).Text),
            block => Assert.Equal("Let me calculate that.", Assert.IsType<TextContentBlock>(block).RawText),
            block => Assert.Equal("call-1", Assert.IsType<FunctionInvocationContentBlock>(block).Call?.CallId),
            block => Assert.IsType<MediaContentBlock>(block),
            block => Assert.Equal("call-2", Assert.IsType<ToolApprovalBlock>(block).Call?.CallId));
    }

    [Fact]
    public async Task UserMessageWithTextAndImage_ShowsTheTextFirst()
    {
        var pipeline = new BlockMappingPipeline(new UIAgentOptions());

        var blocks = await ProcessAsync(pipeline, new ChatResponseUpdate
        {
            Role = ChatRole.User,
            Contents = [new TextContent("What is this?"), new DataContent(new byte[] { 1, 2, 3 }, "image/png")],
        });

        Assert.Collection(
            blocks,
            block => Assert.IsType<TextContentBlock>(block),
            block => Assert.IsType<MediaContentBlock>(block));
    }

    [Fact]
    public async Task TextAroundACall_ShowsTheTextBeforeAndAfterTheCall()
    {
        var pipeline = new BlockMappingPipeline(new UIAgentOptions());

        var blocks = await ProcessAsync(pipeline, new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "message-1",
            Contents =
            [
                new TextContent("Before"),
                new FunctionCallContent("call-1", "calculate"),
                new TextContent("After"),
            ],
        });

        Assert.Collection(
            blocks,
            block => Assert.Equal("Before", Assert.IsType<TextContentBlock>(block).RawText),
            block => Assert.IsType<FunctionInvocationContentBlock>(block),
            block => Assert.Equal("After", Assert.IsType<TextContentBlock>(block).RawText));
    }

    [Fact]
    public async Task CustomHandlerBlock_TakesThePlaceOfItsContent()
    {
        var options = new UIAgentOptions();
        options.AddBlockHandler(new DelegateBlockHandler<CitationBlock>((context, state) =>
        {
            foreach (var content in context.UnhandledContents)
            {
                if (content is FunctionCallContent { Name: "cite" } call)
                {
                    context.MarkHandled(call);
                    state.Id = call.CallId;
                    state.Source = "Docs";
                    return BlockMappingResult<CitationBlock>.Emit(state, state);
                }
            }

            return BlockMappingResult<CitationBlock>.Pass();
        }));
        var pipeline = new BlockMappingPipeline(options);

        var blocks = await ProcessAsync(pipeline, new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "message-1",
            Contents =
            [
                new TextContent("As documented"),
                new FunctionCallContent("cite-1", "cite"),
                new DataContent(new byte[] { 1, 2, 3 }, "image/png"),
            ],
        });

        Assert.Collection(
            blocks,
            block => Assert.IsType<TextContentBlock>(block),
            block => Assert.IsType<CitationBlock>(block),
            block => Assert.IsType<MediaContentBlock>(block));
    }

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
