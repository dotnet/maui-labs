// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.AI.Chat;

/// <summary>
/// The outcome a <see cref="ContentBlockHandler{TState}"/> returns: <c>Pass</c> (not mine), <c>Emit</c>
/// (a new block), <c>Update</c> (more streamed content for my active block), or <c>Complete</c> (done).
/// </summary>
public readonly struct BlockMappingResult<TState>
{
    internal enum ResultKind { Pass, Emit, Update, Complete }

    internal ResultKind Kind { get; }
    internal ContentBlock? Block { get; }
    internal TState? State { get; }

    private BlockMappingResult(ResultKind kind, ContentBlock? block, TState? state)
    {
        Kind = kind;
        Block = block;
        State = state;
    }

    public static BlockMappingResult<TState> Pass() => new(ResultKind.Pass, null, default);

    public static BlockMappingResult<TState> Emit(ContentBlock block, TState state)
    {
        ArgumentNullException.ThrowIfNull(block);
        return new(ResultKind.Emit, block, state);
    }

    public static BlockMappingResult<TState> Update(TState state) => new(ResultKind.Update, null, state);

    public static BlockMappingResult<TState> Complete() => new(ResultKind.Complete, null, default);
}
