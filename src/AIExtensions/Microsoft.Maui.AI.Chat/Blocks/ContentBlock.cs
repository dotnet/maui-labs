// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat;

/// <summary>
/// Base class for every renderable unit in a conversation. Owns an <see cref="Id"/>, a
/// <see cref="LifecycleState"/>, a <see cref="Role"/>, and a change-notification used to stream
/// incremental updates to the UI.
/// </summary>
/// <remarks>
/// Blocks are produced by a <see cref="ContentBlockHandler{TState}"/> from raw
/// Microsoft.Extensions.AI content, grouped into a <see cref="ConversationTurn"/> by
/// <see cref="AgentContext"/>, and surfaced to the UI for rendering.
/// </remarks>
public abstract class ContentBlock
{
    // Public setter so custom block handlers (e.g. in the sample) can assign Id
    // from a FunctionCallContent.CallId when projecting M.E.AI content into a block.
    public string Id { get; set; } = string.Empty;

    public BlockLifecycleState LifecycleState { get; internal set; }

    public ChatRole? Role { get; internal set; }

    public string? AuthorName { get; internal set; }

    /// <summary>Gets when the source message/update was created, when supplied by the provider.</summary>
    public DateTimeOffset? CreatedAt { get; internal set; }

    internal string? RestoredTurnId { get; set; }

    internal bool StartsRestoredTurn { get; set; }

    internal bool IsRestoredRequest { get; set; }

    private readonly List<Action> _callbacks = new();

    public ContentBlockChangedSubscription OnChanged(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _callbacks.Add(callback);
        return new ContentBlockChangedSubscription(this, callback);
    }

    protected void NotifyChanged()
    {
        // Snapshot the callbacks to allow safe removal during iteration
        var snapshot = _callbacks.ToArray();
        for (var i = 0; i < snapshot.Length; i++)
        {
            snapshot[i]();
        }
    }

    internal void InvokeNotifyChanged() => NotifyChanged();

    internal void RemoveCallback(Action callback)
    {
        _callbacks.Remove(callback);
    }
}
