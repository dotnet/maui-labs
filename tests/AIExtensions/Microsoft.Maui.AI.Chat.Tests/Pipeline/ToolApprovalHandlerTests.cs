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
        Assert.Equal("DeleteFile", Assert.IsAssignableFrom<FunctionInvocationContentBlock>(block.InnerBlock).ToolName);
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
        var inner = Assert.IsAssignableFrom<FunctionInvocationContentBlock>(approvalBlock.InnerBlock);
        Assert.Equal("GetWeather", inner.ToolName);
        Assert.NotNull(inner.Call);
        Assert.Equal("call-1", inner.Call!.CallId);
        Assert.Same(inner.Call, approvalBlock.Call);
    }

    [Fact]
    public async Task Process_SeparateUpdatesEmitEveryApproval()
    {
        var pipeline = new BlockMappingPipeline(new UIAgentOptions());

        var first = await ProcessAsync(
            pipeline,
            CreateApprovalUpdate("message-1", "call-1", "delete_file"));
        var second = await ProcessAsync(
            pipeline,
            CreateApprovalUpdate("message-1", "call-2", "send_email"));

        var firstApproval = Assert.IsType<ToolApprovalBlock>(Assert.Single(first));
        var secondApproval = Assert.IsType<ToolApprovalBlock>(Assert.Single(second));
        Assert.Equal("call-1", firstApproval.Id);
        Assert.Equal("delete_file", firstApproval.ToolName);
        Assert.Equal(BlockLifecycleState.Inactive, firstApproval.LifecycleState);
        Assert.Equal("call-2", secondApproval.Id);
        Assert.Equal("send_email", secondApproval.ToolName);
    }

    [Fact]
    public async Task Process_SeveralApprovalsInOneUpdate_EmitEveryApproval()
    {
        var pipeline = new BlockMappingPipeline(new UIAgentOptions());
        var update = new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = "message-1",
            Contents =
            [
                new ToolApprovalRequestContent(
                    "request-1",
                    new FunctionCallContent("call-1", "delete_file")),
                new ToolApprovalRequestContent(
                    "request-2",
                    new FunctionCallContent("call-2", "send_email")),
            ],
        };

        var blocks = await ProcessAsync(pipeline, update);

        Assert.Equal(
            ["call-1", "call-2"],
            blocks.Cast<ToolApprovalBlock>().Select(block => block.Id));
    }

    [Fact]
    public void ApprovalWithoutCallId_UsesRequestIdAsBlockId()
    {
        var handler = new ToolApprovalHandler();
        var update = new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents =
            [
                new ToolApprovalRequestContent(
                    "request-1",
                    new FunctionCallContent(string.Empty, "DeleteFile")),
            ],
        };

        var result = handler.Handle(
            new BlockMappingContext(update),
            new ToolApprovalHandler.ApprovalHandlerState());

        Assert.Equal("request-1", Assert.IsType<ToolApprovalBlock>(result.Block).Id);
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
        var inner = Assert.IsType<FunctionInvocationContentBlock>(block.InnerBlock);
        Assert.Equal("DeleteFile", inner.ToolName);
        Assert.Equal("DeleteFile", block.ToolName);
    }

    private static ChatResponseUpdate CreateApprovalUpdate(
        string messageId,
        string callId,
        string toolName)
    {
        var call = new FunctionCallContent(callId, toolName);
        return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            MessageId = messageId,
            Contents = [new ToolApprovalRequestContent($"request-{callId}", call)],
        };
    }

    private static async Task<List<ContentBlock>> ProcessAsync(
        BlockMappingPipeline pipeline,
        ChatResponseUpdate update)
    {
        var blocks = new List<ContentBlock>();
        await foreach (var block in pipeline.Process(update))
        {
            blocks.Add(block);
        }

        return blocks;
    }
}
