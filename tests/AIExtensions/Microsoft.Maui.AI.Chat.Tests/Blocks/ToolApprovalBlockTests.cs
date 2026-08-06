// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

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
        var block = new ToolApprovalBlock(innerBlock, request);

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
    public void Approve_FiresNotifyChanged()
    {
        var block = CreateBlock();
        var changed = false;
        block.OnChanged(() => changed = true);

        block.Approve();

        Assert.True(changed);
    }

    [Fact]
    public void Reject_FiresNotifyChanged()
    {
        var block = CreateBlock();
        var changed = false;
        block.OnChanged(() => changed = true);

        block.Reject();

        Assert.True(changed);
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
        var changedCount = 0;
        block.OnChanged(() => changedCount++);

        block.Approve();
        var firstResult = await block.GetResultAsync();
        block.Approve();
        var secondResult = await block.GetResultAsync();

        Assert.Equal(ApprovalStatus.Approved, block.Status);
        Assert.Equal(1, changedCount);
        Assert.Same(firstResult, secondResult);
    }

    [Fact]
    public async Task RejectAfterApprove_IsNoOp()
    {
        var block = CreateBlock();
        var changedCount = 0;
        block.OnChanged(() => changedCount++);

        block.Approve();
        var approvedResult = await block.GetResultAsync();
        block.Reject("changed mind");

        Assert.Equal(ApprovalStatus.Approved, block.Status);
        Assert.Equal(1, changedCount);
        Assert.Same(approvedResult, await block.GetResultAsync());
    }

    [Fact]
    public async Task ApproveAfterReject_IsNoOp()
    {
        var block = CreateBlock();
        var changedCount = 0;
        block.OnChanged(() => changedCount++);

        block.Reject("not safe");
        var rejectedResult = await block.GetResultAsync();
        block.Approve();

        Assert.Equal(ApprovalStatus.Rejected, block.Status);
        Assert.Equal(1, changedCount);
        Assert.Same(rejectedResult, await block.GetResultAsync());
    }

    private static ToolApprovalBlock CreateBlock()
    {
        var innerBlock = new FunctionInvocationContentBlock
        {
            Call = new FunctionCallContent("call-1", "DeleteFile", null)
        };
        var request = new ToolApprovalRequestContent("req-1", innerBlock.Call);
        return new ToolApprovalBlock(innerBlock, request);
    }
}
