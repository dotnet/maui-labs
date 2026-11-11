// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

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
    /// still waiting for input when the turn stopped no longer respond, and a UI action that was still
    /// running shows no result when it returns, because the result is never sent.
    /// </remarks>
    public bool IsStopped { get; internal set; }

    /// <summary>
    /// Gets whether the turn's last attempt failed. A failed turn committed nothing to the history.
    /// </summary>
    /// <remarks>
    /// Set before the <see cref="ConversationStatus.Error"/> status change that reports the failure is
    /// raised, and cleared when <see cref="AgentContext.RetryAsync"/> runs the turn again, before the
    /// <see cref="ConversationStatus.Streaming"/> status change is raised. A failed turn that is not the
    /// last turn can no longer be retried: another message was sent after it.
    /// </remarks>
    public bool IsFailed { get; internal set; }

    /// <summary>
    /// Gets the identifier of the model that answered, as reported by
    /// <see cref="ChatResponseUpdate.ModelId"/>, or <see langword="null"/> when the provider does not
    /// report it.
    /// </summary>
    /// <remarks>
    /// The last model identifier reported during the turn wins. It is updated while the response
    /// streams, before the blocks of the reporting update are added, and restored from a thread.
    /// </remarks>
    public string? ModelId { get; internal set; }

    /// <summary>
    /// Gets the token usage reported for the response, summed over every request the turn made, or
    /// <see langword="null"/> when the provider reports no <see cref="UsageContent"/>.
    /// </summary>
    /// <remarks>
    /// Usage is commonly reported at the end of a response, so it is complete when the turn is no
    /// longer streaming. It is restored from a thread.
    /// </remarks>
    public UsageDetails? Usage { get; internal set; }

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
        ModelId = null;
        Usage = null;
    }

    internal void ObserveResponse(ChatResponseUpdate update)
    {
        ModelId = ResponseMetadata.GetModelId(update, ModelId);
        Usage = ResponseMetadata.AddUsage(update, Usage);
    }
}
