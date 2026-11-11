// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat.Tests.Pipeline;

/// <summary>A call listed more than once in one update, as a complete response can, is shown once.</summary>
public class RepeatedCallTests
{
    [Fact]
    public async Task CallRepeatedInOneUpdate_ShowsOneBlockThatGetsTheResult()
    {
        var pipeline = new BlockMappingPipeline(new UIAgentOptions());
        var call = new FunctionCallContent("call-1", "calculate");
        var result = new FunctionResultContent("call-1", 4);

        var first = await ProcessAsync(pipeline, Assistant(new TextContent("Before"), call, call));
        var second = await ProcessAsync(pipeline, new ChatResponseUpdate(ChatRole.Tool, [result, result]));
        var third = await ProcessAsync(pipeline, Assistant(call, new TextContent("After")));

        var block = Assert.Single(first.OfType<FunctionInvocationContentBlock>());
        Assert.Equal(2, first.Count);
        Assert.Empty(second);
        Assert.Equal("After", Assert.IsType<TextContentBlock>(Assert.Single(third)).RawText);
        Assert.Same(result, block.Result);
    }

    [Fact]
    public async Task CallRepeatedAsAnotherInstanceInOneUpdate_ShowsOneBlock()
    {
        var pipeline = new BlockMappingPipeline(new UIAgentOptions());

        var blocks = await ProcessAsync(pipeline, Assistant(
            new FunctionCallContent("call-1", "calculate"),
            new FunctionCallContent("call-1", "calculate"),
            new FunctionResultContent("call-1", 4)));

        var block = Assert.IsType<FunctionInvocationContentBlock>(Assert.Single(blocks));
        Assert.True(block.HasResult);
    }

    [Fact]
    public async Task ApprovalAndItsCallInOneUpdate_ShowsOneApproval()
    {
        var pipeline = new BlockMappingPipeline(new UIAgentOptions());
        var call = new FunctionCallContent("call-1", "delete");

        var blocks = await ProcessAsync(pipeline, Assistant(new ToolApprovalRequestContent("approval-1", call), call));

        Assert.IsType<ToolApprovalBlock>(Assert.Single(blocks));
    }

    private static ChatResponseUpdate Assistant(params AIContent[] contents)
        => new(ChatRole.Assistant, contents);

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
