// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat;

/// <summary>Built-in handler mapping a M.E.AI <c>ToolApprovalRequestContent</c> into a <see cref="ToolApprovalBlock"/>.</summary>
/// <remarks>Runs before <see cref="FunctionInvocationHandler"/> so it claims approval requests first.</remarks>
internal sealed class ToolApprovalHandler : ContentBlockHandler<ToolApprovalHandler.ApprovalHandlerState>
{
    public override BlockMappingResult<ApprovalHandlerState> Handle(
        BlockMappingContext context, ApprovalHandlerState state)
    {
        ToolApprovalRequestContent? approvalRequest = null;
        foreach (var content in context.UnhandledContents)
        {
            if (content is ToolApprovalRequestContent tar)
            {
                approvalRequest = tar;
                break;
            }
        }

        if (approvalRequest is null)
        {
            return BlockMappingResult<ApprovalHandlerState>.Pass();
        }

        context.MarkHandled(approvalRequest);

        // Delegate the inner FunctionCallContent to produce a typed block
        var innerBlock = context.CreateInnerBlock(approvalRequest.ToolCall)
            as FunctionInvocationContentBlock;

        if (innerBlock is null)
        {
            innerBlock = new FunctionInvocationContentBlock();
            if (approvalRequest.ToolCall is FunctionCallContent fc)
            {
                innerBlock.Call = fc;
            }
        }

        var block = new ToolApprovalBlock(innerBlock, approvalRequest);
        block.Id = approvalRequest.ToolCall is FunctionCallContent fcc
            ? fcc.CallId
            : Guid.NewGuid().ToString("N");

        return BlockMappingResult<ApprovalHandlerState>.Emit(block, state);
    }

    internal sealed class ApprovalHandlerState
    {
    }
}
