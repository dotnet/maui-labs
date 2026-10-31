// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.AI.Chat;

/// <summary>Maps provider-specific raw activity updates into a streaming activity block.</summary>
/// <typeparam name="TBlock">The activity block type maintained by this handler.</typeparam>
public abstract class ActivityHandler<TBlock> : ContentBlockHandler<TBlock>
    where TBlock : ActivityContentBlock, new()
{
    public sealed override BlockMappingResult<TBlock> Handle(BlockMappingContext context, TBlock state)
    {
        if (string.IsNullOrEmpty(state.Id))
        {
            if (!TryCreateBlock(context, state))
                return BlockMappingResult<TBlock>.Pass();

            if (string.IsNullOrEmpty(state.Id))
                state.Id = context.Update.MessageId ?? Guid.NewGuid().ToString("N");
            OnContentUpdated(state);
            return BlockMappingResult<TBlock>.Emit(state, state);
        }

        if (!TryUpdateBlock(context, state, out var isCompleted))
            return BlockMappingResult<TBlock>.Pass();

        OnContentUpdated(state);
        return isCompleted
            ? BlockMappingResult<TBlock>.Complete()
            : BlockMappingResult<TBlock>.Update(state);
    }

    /// <summary>Creates the initial activity state from a raw response snapshot.</summary>
    protected abstract bool TryCreateBlock(BlockMappingContext context, TBlock state);

    /// <summary>Updates the activity state from a correlated raw response delta.</summary>
    protected abstract bool TryUpdateBlock(
        BlockMappingContext context,
        TBlock state,
        out bool isCompleted);

    /// <summary>Called after an activity is emitted or changes, including a final update.</summary>
    protected virtual void OnContentUpdated(TBlock block)
    {
    }
}
