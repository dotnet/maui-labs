// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat;

/// <summary>
/// Represents persistent storage for the raw updates in one conversation thread.
/// Implementations own persistence and serialization.
/// </summary>
/// <remarks>
/// The engine writes a thread only when a turn commits: it calls <see cref="AppendUserMessage"/>,
/// then <see cref="AppendUpdate"/> for every update of the turn, then <see cref="CompleteTurn"/>,
/// back to back. Failed, discarded, and still-running turns are never written, so implementations
/// do not need to roll anything back. The engine rebuilds message history from
/// <see cref="GetUpdates"/>.
/// <para>
/// Conversation threads are single-thread-affine and are not thread-safe. Callers must serialize
/// access and enter the owning application thread before using the agent or thread.
/// </para>
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
    /// Gets the provider conversation identifier of the last committed turn for a stateful
    /// conversation.
    /// </summary>
    string? ConversationId { get; }

    /// <summary>
    /// Starts a turn with the message that initiated it.
    /// A new call replaces any previous turn that was not completed.
    /// </summary>
    void AppendUserMessage(ChatMessage message);

    /// <summary>
    /// Appends a raw update to the turn. Updates can be provider output or an engine-generated
    /// continuation input for a tool or approval round.
    /// </summary>
    /// <remarks>
    /// Implementations must preserve the update's role, message identifier, contents, conversation
    /// identifier, and additional properties so logical turn boundaries can be replayed.
    /// </remarks>
    void AppendUpdate(ChatResponseUpdate update);

    /// <summary>
    /// Commits the turn. Only completed turns may be returned by <see cref="GetUpdates"/>.
    /// </summary>
    void CompleteTurn();

    /// <summary>Returns the raw updates for all committed turns.</summary>
    IReadOnlyList<ChatResponseUpdate> GetUpdates();

    /// <summary>
    /// Removes all committed updates and resets provider conversation state.
    /// </summary>
    void Clear();
}
