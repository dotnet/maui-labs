// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat.Tests.Pipeline;

public class ToolApprovalHandlerTests
{
    [Fact]
    public void ToolApprovalRequestContent_EmitsToolApprovalBlock()
    {
        var handler = new ToolApprovalHandler();

        var toolCall = new FunctionCallContent("call-1", "DeleteFile",
            new Dictionary<string, object?> { ["path"] = "/data.txt" });
        var approvalRequest = new ToolApprovalRequestContent("req-1", toolCall);
        var update = new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = [approvalRequest]
        };
        var context = new BlockMappingContext(update);
        var state = new ToolApprovalHandler.ApprovalHandlerState();

        var result = handler.Handle(context, state);

        Assert.Equal(BlockMappingResult<ToolApprovalHandler.ApprovalHandlerState>.ResultKind.Emit,
            result.Kind);
        var block = Assert.IsType<ToolApprovalBlock>(result.Block);
        Assert.Equal(ApprovalStatus.Pending, block.Status);
        Assert.Equal("DeleteFile", block.InnerBlock.ToolName);
        Assert.Equal("call-1", block.Id);
    }

    [Fact]
    public async Task CreateInnerBlock_ProducesCorrectFunctionInvocationBlock()
    {
        // The handler needs access to inactive handlers for CreateInnerBlock
        var options = new UIAgentOptions();
        var pipeline = new BlockMappingPipeline(options);

        var toolCall = new FunctionCallContent("call-1", "GetWeather",
            new Dictionary<string, object?> { ["city"] = "Seattle" });
        var approvalRequest = new ToolApprovalRequestContent("req-1", toolCall);
        var update = new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = [approvalRequest]
        };

        var blocks = new List<ContentBlock>();
        await foreach (var block in pipeline.Process(update))
        {
            blocks.Add(block);
        }

        Assert.Single(blocks);
        var approvalBlock = Assert.IsType<ToolApprovalBlock>(blocks[0]);
        Assert.NotNull(approvalBlock.InnerBlock);
        Assert.Equal("GetWeather", approvalBlock.InnerBlock.ToolName);
        Assert.NotNull(approvalBlock.InnerBlock.Call);
        Assert.Equal("call-1", approvalBlock.InnerBlock.Call!.CallId);
    }

    [Fact]
    public void NonApprovalContent_PassesThrough()
    {
        var handler = new ToolApprovalHandler();

        var update = new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = [new TextContent("Hello")]
        };
        var context = new BlockMappingContext(update);
        var state = new ToolApprovalHandler.ApprovalHandlerState();

        var result = handler.Handle(context, state);

        Assert.Equal(BlockMappingResult<ToolApprovalHandler.ApprovalHandlerState>.ResultKind.Pass,
            result.Kind);
    }

    [Fact]
    public void FallbackInnerBlock_WhenNoHandlerMatches()
    {
        // Without inactive handlers, CreateInnerBlock returns null → fallback path
        var handler = new ToolApprovalHandler();

        var toolCall = new FunctionCallContent("call-1", "DeleteFile", null);
        var approvalRequest = new ToolApprovalRequestContent("req-1", toolCall);
        var update = new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = [approvalRequest]
        };
        // Context without inactive handlers (no pipeline)
        var context = new BlockMappingContext(update);
        var state = new ToolApprovalHandler.ApprovalHandlerState();

        var result = handler.Handle(context, state);

        Assert.Equal(BlockMappingResult<ToolApprovalHandler.ApprovalHandlerState>.ResultKind.Emit,
            result.Kind);
        var block = Assert.IsType<ToolApprovalBlock>(result.Block);
        Assert.NotNull(block.InnerBlock);
        Assert.Equal("DeleteFile", block.InnerBlock.ToolName);
    }
}
