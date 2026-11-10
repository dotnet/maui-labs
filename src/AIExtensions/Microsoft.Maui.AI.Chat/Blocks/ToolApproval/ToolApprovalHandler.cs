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
        // An emitted approval is complete, so every later request reaches a fresh handler state and
        // emits its own block, including requests that arrive in separate updates.
        if (state.Emitted)
        {
            return BlockMappingResult<ApprovalHandlerState>.Complete();
        }

        foreach (var content in context.UnhandledContents)
        {
            if (content is not ToolApprovalRequestContent approvalRequest)
            {
                continue;
            }

            context.MarkHandled(approvalRequest);

            // Delegate the inner FunctionCallContent to produce a typed block
            var innerBlock = context.CreateInnerBlock(approvalRequest.ToolCall)
                as FunctionInvocationContentBlock
                ?? CreateFallbackInnerBlock(approvalRequest.ToolCall);

            state.Emitted = true;
            return BlockMappingResult<ApprovalHandlerState>.Emit(
                new ToolApprovalBlock(innerBlock, approvalRequest)
                {
                    Id = GetBlockId(approvalRequest, innerBlock),
                },
                state);
        }

        return BlockMappingResult<ApprovalHandlerState>.Pass();
    }

    private static FunctionInvocationContentBlock CreateFallbackInnerBlock(ToolCallContent toolCall)
    {
        var block = new FunctionInvocationContentBlock();
        if (toolCall is FunctionCallContent functionCall)
        {
            block.Call = functionCall;
        }

        return block;
    }

    private static string GetBlockId(
        ToolApprovalRequestContent approvalRequest,
        FunctionInvocationContentBlock innerBlock)
    {
        if (approvalRequest.ToolCall is FunctionCallContent { CallId: { Length: > 0 } callId })
            return callId;

        if (!string.IsNullOrEmpty(approvalRequest.RequestId))
            return approvalRequest.RequestId;

        return string.IsNullOrEmpty(innerBlock.Id)
            ? Guid.NewGuid().ToString("N")
            : innerBlock.Id;
    }

    internal sealed class ApprovalHandlerState
    {
        internal bool Emitted { get; set; }
    }
}
