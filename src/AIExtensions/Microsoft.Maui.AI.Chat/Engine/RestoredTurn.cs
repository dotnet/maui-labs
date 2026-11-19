// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat;

/// <summary>The blocks and metadata replayed for one committed turn.</summary>
internal sealed class RestoredTurn
{
    internal RestoredTurn(string id, bool isStopped)
    {
        Turn = new ConversationTurn
        {
            Id = id,
            IsStopped = isStopped,
        };
    }

    internal ConversationTurn Turn { get; }

    internal string Id => Turn.Id;

    internal bool IsStopped => Turn.IsStopped;

    internal string? ModelId => Turn.ModelId;

    internal UsageDetails? Usage => Turn.Usage;

    internal IReadOnlyList<ContentBlock> RequestBlocks => Turn.RequestBlocks;

    internal IReadOnlyList<ContentBlock> ResponseBlocks => Turn.ResponseBlocks;

    internal void AddRequestBlock(ContentBlock block) => Turn.AddRequestBlock(block);

    internal void AddResponseBlock(ContentBlock block) => Turn.AddResponseBlock(block);

    internal void ObserveResponse(ChatResponseUpdate update) => Turn.ObserveResponse(update);
}
