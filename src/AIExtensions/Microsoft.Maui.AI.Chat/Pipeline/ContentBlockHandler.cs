// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat;

/// <summary>
/// The extension point that turns raw Microsoft.Extensions.AI content into a typed
/// <see cref="ContentBlock"/>. Implement <see cref="Handle"/> as a small state machine over a
/// streaming block and return a <see cref="BlockMappingResult{TState}"/>.
/// </summary>
/// <remarks>
/// Register custom handlers via <see cref="UIAgentOptions.AddBlockHandler{TState}"/>. They run in the
/// <see cref="BlockMappingPipeline"/> (custom first, then built-ins), with
/// <see cref="TextBlockHandler"/> as the fallback.
/// </remarks>
public abstract class ContentBlockHandler<TState> where TState : new()
{
    public abstract BlockMappingResult<TState> Handle(BlockMappingContext context, TState state);

    /// <summary>
    /// Applies a backend-invoked function result directly to an emitted block state. Generated tool-block
    /// handlers override this so typed <c>[ToolResult]</c> properties work even without function-invocation
    /// middleware.
    /// </summary>
    /// <param name="state">The emitted handler state.</param>
    /// <param name="result">The correlated function result.</param>
    /// <returns><see langword="true"/> when this handler applied the result.</returns>
    protected virtual bool ApplyFunctionResult(
        TState state,
        FunctionResultContent result) =>
        false;

    internal bool TryApplyFunctionResult(
        TState state,
        FunctionResultContent result) =>
        ApplyFunctionResult(state, result);
}
