// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat;

internal sealed class UIActionHandler
    : ContentBlockHandler<UIActionHandler.UIActionHandlerState>
{
    private readonly IReadOnlyDictionary<string, AIFunction> _actions;
    private readonly IServiceProvider? _services;

    internal UIActionHandler(
        IReadOnlyDictionary<string, AIFunction> actions,
        IServiceProvider? services)
    {
        _actions = actions;
        _services = services;
    }

    public override BlockMappingResult<UIActionHandlerState> Handle(
        BlockMappingContext context,
        UIActionHandlerState state)
    {
        if (state.Block?.Call is { } activeCall)
        {
            foreach (var content in context.UnhandledContents)
            {
                if (content is FunctionResultContent result
                    && result.CallId == activeCall.CallId)
                {
                    context.MarkHandled(result);
                    state.Block.InnerBlock.Result = result;
                    return BlockMappingResult<UIActionHandlerState>.Complete();
                }
            }

            // Let a fresh handler state claim any later call. Reusing this active state would replace
            // the block and mark the new call handled without emitting it.
            return BlockMappingResult<UIActionHandlerState>.Pass();
        }

        foreach (var content in context.UnhandledContents)
        {
            if (content is not FunctionCallContent call
                || call.InformationalOnly
                || !_actions.TryGetValue(call.Name, out var action))
            {
                continue;
            }

            context.MarkHandled(call);
            var innerBlock = new FunctionInvocationContentBlock { Call = call };
            var block = new UIActionBlock(action, innerBlock, _services)
            {
                Id = innerBlock.Id,
            };
            foreach (var candidate in context.UnhandledContents)
            {
                if (candidate is FunctionResultContent result
                    && result.CallId == call.CallId)
                {
                    context.MarkHandled(result);
                    innerBlock.Result = result;
                    break;
                }
            }
            state.Block = block;
            return BlockMappingResult<UIActionHandlerState>.Emit(block, state);
        }

        return BlockMappingResult<UIActionHandlerState>.Pass();
    }

    internal sealed class UIActionHandlerState
    {
        internal UIActionBlock? Block { get; set; }
    }
}
