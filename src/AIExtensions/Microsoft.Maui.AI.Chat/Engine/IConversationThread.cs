// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat;

/// <summary>
/// Represents persistent storage for the raw updates in one conversation thread.
/// Implementations own persistence, serialization, and reconstruction of message history.
/// </summary>
/// <remarks>
/// Conversation threads are single-thread-affine and are not thread-safe. Callers must serialize
/// access and enter the owning application thread before using the agent or thread.
/// </remarks>
public interface IConversationThread
{
    /// <summary>Gets the unique identifier for this conversation thread.</summary>
    string ThreadId { get; }

    /// <summary>
    /// Gets whether the provider maintains conversation history remotely.
    /// </summary>
    bool IsStateful { get; }

    /// <summary>
    /// Gets the current provider conversation identifier for a stateful conversation.
    /// </summary>
    string? ConversationId { get; }

    /// <summary>
    /// Starts a pending turn with the user-initiated message.
    /// A new call replaces any previous turn that was not completed.
    /// </summary>
    void AppendUserMessage(ChatMessage message);

    /// <summary>
    /// Appends a raw update to the pending turn. Updates can be provider output or an
    /// engine-generated continuation input for a tool or approval round.
    /// </summary>
    /// <remarks>
    /// Implementations must preserve the update's role, message identifier, contents, and
    /// additional properties so logical turn boundaries can be replayed.
    /// </remarks>
    void AppendUpdate(ChatResponseUpdate update);

    /// <summary>
    /// Commits the pending turn. Only completed turns may be returned by
    /// <see cref="GetUpdates"/> or <see cref="GetMessageHistory"/>.
    /// </summary>
    void CompleteTurn();

    /// <summary>
    /// Discards the pending, uncommitted turn after cancellation or failure.
    /// Committed turns and their provider conversation state are preserved.
    /// </summary>
    /// <remarks>Calling this when no turn is pending must be a no-op.</remarks>
    void AbortTurn();

    /// <summary>Returns the raw updates for all committed turns.</summary>
    IReadOnlyList<ChatResponseUpdate> GetUpdates();

    /// <summary>
    /// Reconstructs the committed message history suitable for a stateless provider.
    /// </summary>
    IReadOnlyList<ChatMessage> GetMessageHistory();

    /// <summary>
    /// Removes all committed and pending updates and resets provider conversation state.
    /// </summary>
    void Clear();
}
