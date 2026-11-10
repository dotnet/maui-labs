// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.AI.Chat;

/// <summary>
/// One exchange in a conversation: the request blocks (the user message) and the response blocks
/// produced for it.
/// </summary>
/// <remarks>Created and populated by <see cref="AgentContext"/>.</remarks>
public class ConversationTurn
{
    private readonly List<ContentBlock> _requestBlocks = new();
    private readonly List<ContentBlock> _responseBlocks = new();

    public string Id { get; internal set; } = Guid.NewGuid().ToString("N");

    public IReadOnlyList<ContentBlock> RequestBlocks => _requestBlocks;

    public IReadOnlyList<ContentBlock> ResponseBlocks => _responseBlocks;

    /// <summary>
    /// Gets whether the response was stopped before it completed, by
    /// <see cref="AgentContext.CancelAsync()"/> or the caller's cancellation token.
    /// </summary>
    /// <remarks>
    /// A stopped turn keeps its request and partial response. Approvals and UI actions that were
    /// still waiting for input when the turn stopped no longer respond.
    /// </remarks>
    public bool IsStopped { get; internal set; }

    internal void AddRequestBlock(ContentBlock block)
    {
        _requestBlocks.Add(block);
    }

    internal void AddResponseBlock(ContentBlock block)
    {
        _responseBlocks.Add(block);
    }

    internal void ClearResponseBlocks()
    {
        _responseBlocks.Clear();
    }
}
