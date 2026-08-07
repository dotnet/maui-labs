// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat.Tests.Pipeline;

public class GeneratedToolBlockIntegrationTests
{
    [Fact]
    public async Task GeneratedHandler_MapsArgumentsAndTypedResult()
    {
        var pipeline = CreatePipeline();
        var calls = await ProcessAsync(
            pipeline,
            new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                Contents =
                [
                    new FunctionCallContent(
                        "call-1",
                        "find_order",
                        new Dictionary<string, object?>
                        {
                            ["orderId"] = JsonSerializer.SerializeToElement("ORD-123"),
                            ["includeHistory"] = true,
                        }),
                ],
            });
        var block = Assert.IsType<GeneratedOrderBlock>(Assert.Single(calls));

        var order = new GeneratedOrder("ORD-123", 42.50m);
        await ProcessAsync(
            pipeline,
            new ChatResponseUpdate
            {
                Role = ChatRole.Tool,
                Contents = [new FunctionResultContent("call-1", order)],
            });

        Assert.Equal("ORD-123", block.OrderId);
        Assert.True(block.IncludeHistory);
        Assert.Same(order, block.Order);
        Assert.NotNull(block.Result);
        Assert.Equal(BlockLifecycleState.Inactive, block.LifecycleState);
    }

    [Fact]
    public async Task GeneratedHandler_MultipleResultProperties_MapJsonObject()
    {
        var pipeline = CreatePipeline();
        var block = Assert.IsType<GeneratedSummaryBlock>(
            Assert.Single(await ProcessAsync(
                pipeline,
                new ChatResponseUpdate
                {
                    Role = ChatRole.Assistant,
                    Contents = [new FunctionCallContent("call-2", "summarize")],
                })));
        var result = JsonSerializer.SerializeToElement(new
        {
            title = "Summary",
            count = 3,
        });

        await ProcessAsync(
            pipeline,
            new ChatResponseUpdate
            {
                Role = ChatRole.Tool,
                Contents = [new FunctionResultContent("call-2", result)],
            });

        Assert.Equal("Summary", block.Title);
        Assert.Equal(3, block.Count);
    }

    [Fact]
    public async Task GeneratedHandler_MalformedProperty_DoesNotFaultOtherMappings()
    {
        var pipeline = CreatePipeline();
        var block = Assert.IsType<GeneratedOrderBlock>(
            Assert.Single(await ProcessAsync(
                pipeline,
                new ChatResponseUpdate
                {
                    Role = ChatRole.Assistant,
                    Contents =
                    [
                        new FunctionCallContent(
                            "call-3",
                            "find_order",
                            new Dictionary<string, object?>
                            {
                                ["orderId"] = "ORD-3",
                                ["includeHistory"] = "not-a-boolean",
                            }),
                    ],
                })));

        Assert.Equal("ORD-3", block.OrderId);
        Assert.False(block.IncludeHistory);
    }

    [Fact]
    public async Task GeneratedHandler_NullArgument_PreservesPropertyDefault()
    {
        var pipeline = CreatePipeline();
        var block = Assert.IsType<GeneratedOrderBlock>(
            Assert.Single(await ProcessAsync(
                pipeline,
                new ChatResponseUpdate
                {
                    Role = ChatRole.Assistant,
                    Contents =
                    [
                        new FunctionCallContent(
                            "call-null",
                            "find_order",
                            new Dictionary<string, object?>
                            {
                                ["orderId"] = null,
                                ["includeHistory"] = true,
                            }),
                    ],
                })));

        Assert.Equal(string.Empty, block.OrderId);
        Assert.True(block.IncludeHistory);
    }

    [Fact]
    public async Task GeneratedHandler_OnlyClaimsItsNamedTool()
    {
        var pipeline = CreatePipeline();

        var blocks = await ProcessAsync(
            pipeline,
            new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                Contents = [new FunctionCallContent("call-other", "other_tool")],
            });

        Assert.IsType<FunctionInvocationContentBlock>(Assert.Single(blocks));
        Assert.DoesNotContain(blocks, block => block is GeneratedOrderBlock);
    }

    private static BlockMappingPipeline CreatePipeline()
    {
        var options = new UIAgentOptions();
        options.AddGeneratedToolBlocks();
        return new BlockMappingPipeline(options);
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

[ToolBlock("find_order")]
public sealed partial class GeneratedOrderBlock : FunctionInvocationContentBlock
{
    [ToolParameter(Name = "orderId")]
    public string OrderId { get; set; } = string.Empty;

    [ToolParameter(Name = "includeHistory")]
    public bool IncludeHistory { get; set; }

    [ToolResult]
    public GeneratedOrder? Order { get; set; }
}

public sealed record GeneratedOrder(string Id, decimal Total);

[ToolBlock("summarize")]
public sealed partial class GeneratedSummaryBlock : FunctionInvocationContentBlock
{
    [ToolResult(Name = "title")]
    public string Title { get; set; } = string.Empty;

    [ToolResult(Name = "count")]
    public int Count { get; set; }
}
