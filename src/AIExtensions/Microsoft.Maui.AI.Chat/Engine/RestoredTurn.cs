// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.AI.Chat;

/// <summary>The blocks and metadata replayed for one committed turn.</summary>
internal sealed class RestoredTurn
{
    internal RestoredTurn(string id, bool isStopped)
    {
        Id = id;
        IsStopped = isStopped;
    }

    internal string Id { get; }

    internal bool IsStopped { get; }

    internal List<ContentBlock> RequestBlocks { get; } = new();

    internal List<ContentBlock> ResponseBlocks { get; } = new();
}
