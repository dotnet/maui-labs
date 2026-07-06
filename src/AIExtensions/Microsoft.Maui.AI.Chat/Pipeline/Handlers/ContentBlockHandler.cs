// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

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
}
