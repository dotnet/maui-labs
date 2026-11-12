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

            // The registered handlers create the block that shows the call, as they would without the approval.
            // A registered UI action that needs approval is run by the client once approved, because
            // FunctionInvokingChatClient cannot invoke it; the approval shows the action's call.
            var created = context.CreateInnerBlock(approvalRequest.ToolCall);
            var action = created as UIActionBlock;
            ContentBlock innerBlock;
            FunctionInvocationContentBlock callBlock;
            switch (created)
            {
                case UIActionBlock uiAction:
                    innerBlock = callBlock = CreateActionCallBlock(uiAction);
                    break;
                case FunctionInvocationContentBlock functionBlock:
                    innerBlock = callBlock = functionBlock;
                    break;
                case { } appBlock:
                    // A block of the app's own type cannot hold the call's result, so the approval keeps one.
                    innerBlock = appBlock;
                    callBlock = CreateCallBlock(approvalRequest.ToolCall, appBlock);
                    break;
                default:
                    innerBlock = callBlock = CreateCallBlock(approvalRequest.ToolCall, shownLike: null);
                    break;
            }

            state.Emitted = true;
            return BlockMappingResult<ApprovalHandlerState>.Emit(
                new ToolApprovalBlock(innerBlock, callBlock, approvalRequest)
                {
                    Id = GetBlockId(approvalRequest, innerBlock),
                    UIAction = action,
                },
                state);
        }

        return BlockMappingResult<ApprovalHandlerState>.Pass();
    }

    private static FunctionInvocationContentBlock CreateActionCallBlock(UIActionBlock action)
    {
        var callBlock = action.CallBlock;
        callBlock.Role = action.Role;
        callBlock.AuthorName = action.AuthorName;
        callBlock.CreatedAt = action.CreatedAt;
        callBlock.LifecycleState = BlockLifecycleState.Inactive;
        return callBlock;
    }

    private static FunctionInvocationContentBlock CreateCallBlock(ToolCallContent toolCall, ContentBlock? shownLike)
    {
        var block = new FunctionInvocationContentBlock();
        if (toolCall is FunctionCallContent functionCall)
        {
            block.Call = functionCall;
        }

        if (shownLike is not null)
        {
            block.Role = shownLike.Role;
            block.AuthorName = shownLike.AuthorName;
            block.CreatedAt = shownLike.CreatedAt;
            block.LifecycleState = BlockLifecycleState.Inactive;
        }

        return block;
    }

    private static string GetBlockId(
        ToolApprovalRequestContent approvalRequest,
        ContentBlock innerBlock)
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
