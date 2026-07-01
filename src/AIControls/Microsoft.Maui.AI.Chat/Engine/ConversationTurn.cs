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

    public string Id { get; internal set; } = string.Empty;

    public IReadOnlyList<ContentBlock> RequestBlocks => _requestBlocks;

    public IReadOnlyList<ContentBlock> ResponseBlocks => _responseBlocks;

    internal void AddRequestBlock(ContentBlock block)
    {
        _requestBlocks.Add(block);
    }

    internal void AddResponseBlock(ContentBlock block)
    {
        _responseBlocks.Add(block);
    }

    internal void RemoveResponseBlock(ContentBlock block)
    {
        _responseBlocks.Remove(block);
    }

    internal void ClearResponseBlocks()
    {
        _responseBlocks.Clear();
    }
}
