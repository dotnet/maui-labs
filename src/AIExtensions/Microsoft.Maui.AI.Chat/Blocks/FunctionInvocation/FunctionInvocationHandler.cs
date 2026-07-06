// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat;

/// <summary>Built-in handler mapping M.E.AI tool calls/results into a <see cref="FunctionInvocationContentBlock"/>.</summary>
/// <remarks>Matches a <c>FunctionCallContent</c>, then its <c>FunctionResultContent</c> by <c>CallId</c>.</remarks>
internal sealed class FunctionInvocationHandler : ContentBlockHandler<FunctionInvocationContentBlock>
{
    public override BlockMappingResult<FunctionInvocationContentBlock> Handle(
        BlockMappingContext context, FunctionInvocationContentBlock state)
    {
        // Check for FunctionCallContent — only when not already tracking a call
        if (state.Call is null)
        {
            FunctionCallContent? callContent = null;
            foreach (var content in context.UnhandledContents)
            {
                if (content is FunctionCallContent fc)
                {
                    callContent = fc;
                    break;
                }
            }

            if (callContent is not null)
            {
                context.MarkHandled(callContent);
                state.Call = callContent;
                state.Id = callContent.CallId;
                return BlockMappingResult<FunctionInvocationContentBlock>.Emit(state, state);
            }
        }

        // Check for a FunctionResultContent whose CallId matches our active block's call.
        // Scan by CallId (not just the first result) so that when several tool calls in a turn
        // have their results batched into one update — in any order — each block still claims
        // its own result. Taking the first result and bailing on a CallId mismatch would leave
        // a block unmatched (Result == null), which then triggers a redundant re-invocation.
        if (state.Call is not null)
        {
            foreach (var content in context.UnhandledContents)
            {
                if (content is FunctionResultContent frc && frc.CallId == state.Call.CallId)
                {
                    context.MarkHandled(frc);
                    state.Result = frc;
                    return BlockMappingResult<FunctionInvocationContentBlock>.Complete();
                }
            }
        }

        // No matching content — wait
        return BlockMappingResult<FunctionInvocationContentBlock>.Pass();
    }
}
