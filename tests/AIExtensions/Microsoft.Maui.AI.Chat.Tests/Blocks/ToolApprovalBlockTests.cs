// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat.Tests.TestHelpers;

namespace Microsoft.Maui.AI.Chat.Tests.Blocks;

public class ToolApprovalBlockTests
{
    [Fact]
    public void CreatedWithPendingStatus()
    {
        var innerBlock = new FunctionInvocationContentBlock
        {
            Call = new FunctionCallContent("call-1", "DeleteFile", null)
        };
        var request = new ToolApprovalRequestContent("req-1", innerBlock.Call);
        var block = new ToolApprovalBlock(innerBlock, innerBlock, request);

        Assert.Equal(ApprovalStatus.Pending, block.Status);
        Assert.Same(innerBlock, block.InnerBlock);
        Assert.Same(request, block.ApprovalRequest);
    }

    [Fact]
    public async Task Approve_SetsStatusAndSignalsResume()
    {
        var block = CreateBlock();

        block.Approve();

        Assert.Equal(ApprovalStatus.Approved, block.Status);
        var resultTask = block.GetResultAsync();
        Assert.True(resultTask.IsCompleted);
        var result = await resultTask;
        Assert.IsType<ToolApprovalResponseContent>(result);
    }

    [Fact]
    public void Reject_SetsStatusAndSignalsResume()
    {
        var block = CreateBlock();

        block.Reject("Not safe");

        Assert.Equal(ApprovalStatus.Rejected, block.Status);
        Assert.True(block.GetResultAsync().IsCompleted);
    }

    [Fact]
    public async Task Reject_PropagatesReasonToApprovalResponse()
    {
        var block = CreateBlock();

        block.Reject("Not safe");

        var response = Assert.IsType<ToolApprovalResponseContent>(await block.GetResultAsync());
        Assert.False(response.Approved);
        Assert.Equal("Not safe", response.Reason);
    }

    [Fact]
    public void Approve_RaisesItsStatus()
    {
        var block = CreateBlock();
        var changes = block.RecordPropertyChanges();

        block.Approve();

        Assert.Equal([nameof(ToolApprovalBlock.Status), nameof(ToolApprovalBlock.IsAwaitingInput)], changes);
    }

    [Fact]
    public void Reject_RaisesItsStatus()
    {
        var block = CreateBlock();
        var changes = block.RecordPropertyChanges();

        block.Reject();

        Assert.Equal([nameof(ToolApprovalBlock.Status), nameof(ToolApprovalBlock.IsAwaitingInput)], changes);
    }

    [Fact]
    public void Approve_WithoutAgentContext_Succeeds()
    {
        var block = CreateBlock();

        block.Approve();

        Assert.Equal(ApprovalStatus.Approved, block.Status);
        Assert.True(block.GetResultAsync().IsCompleted);
    }

    [Fact]
    public async Task Approve_Twice_IsSingleUse()
    {
        var block = CreateBlock();
        var changes = block.RecordPropertyChanges();

        block.Approve();
        var firstResult = await block.GetResultAsync();
        block.Approve();
        var secondResult = await block.GetResultAsync();

        Assert.Equal(ApprovalStatus.Approved, block.Status);
        Assert.Equal([nameof(ToolApprovalBlock.Status), nameof(ToolApprovalBlock.IsAwaitingInput)], changes);
        Assert.Same(firstResult, secondResult);
    }

    [Fact]
    public async Task RejectAfterApprove_IsNoOp()
    {
        var block = CreateBlock();
        var changes = block.RecordPropertyChanges();

        block.Approve();
        var approvedResult = await block.GetResultAsync();
        block.Reject("changed mind");

        Assert.Equal(ApprovalStatus.Approved, block.Status);
        Assert.Equal([nameof(ToolApprovalBlock.Status), nameof(ToolApprovalBlock.IsAwaitingInput)], changes);
        Assert.Same(approvedResult, await block.GetResultAsync());
    }

    [Fact]
    public async Task ApproveAfterReject_IsNoOp()
    {
        var block = CreateBlock();
        var changes = block.RecordPropertyChanges();

        block.Reject("not safe");
        var rejectedResult = await block.GetResultAsync();
        block.Approve();

        Assert.Equal(ApprovalStatus.Rejected, block.Status);
        Assert.Equal([nameof(ToolApprovalBlock.Status), nameof(ToolApprovalBlock.IsAwaitingInput)], changes);
        Assert.Same(rejectedResult, await block.GetResultAsync());
    }

    private static ToolApprovalBlock CreateBlock()
    {
        var innerBlock = new FunctionInvocationContentBlock
        {
            Call = new FunctionCallContent("call-1", "DeleteFile", null)
        };
        var request = new ToolApprovalRequestContent("req-1", innerBlock.Call);
        return new ToolApprovalBlock(innerBlock, innerBlock, request);
    }
}
