// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Semantic parity with Microsoft.Maui.Chat.Controls.ChatContentItem: this record captures
// the same projected-row shape the native ChatMessagesView renders (message + content +
// grouping flags), but without the MAUI BindableObject dependency. See
// Components/UPSTREAM-NOTES.md for how the projection tracks its native counterpart.

using Microsoft.Maui.Chat.Controls;

namespace Microsoft.Maui.Chat.Controls.Blazor;

/// <summary>
/// One projected row: a single <see cref="MessageContent"/> in the context of its
/// <see cref="ConversationMessage"/>, participant, and neighbours.
/// </summary>
/// <remarks>
/// <para>
/// The Blazor <see cref="ChatMessagesView"/> projects every message into one row per
/// content item, so the visible list stays flat and Blazor's diff scales linearly with the
/// number of rendered rows — never a nested list inside a bubble.
/// </para>
/// <para>
/// A row is a value: it is recreated whenever a structural change happens
/// (<c>MessageAdded</c>, <c>MessageRemoved</c>, <c>Reset</c>) or when a content is added or
/// removed from a message. <see cref="ContentVersion"/> is bumped when the underlying
/// <see cref="MessageContent"/> raised <c>ContentChanged</c> and the shell wants Blazor to
/// re-render only the affected row — the record is otherwise identity-equal so Blazor's
/// diff keeps its subtree stable.
/// </para>
/// </remarks>
/// <param name="Message">The message that owns <paramref name="Content"/>.</param>
/// <param name="Content">The content this row renders.</param>
/// <param name="IsOutgoing">Whether this row belongs to the local participant and renders trailing-aligned.</param>
/// <param name="IsFirstInMessage">Whether this is the first content of its message.</param>
/// <param name="IsLastInMessage">Whether this is the last content of its message.</param>
/// <param name="IsFirstFromParticipant">Whether this row starts a run from the same participant.</param>
/// <param name="IsLastFromParticipant">Whether this row ends a run from the same participant.</param>
/// <param name="ContentVersion">Monotonic counter that changes when the underlying content mutated in place.</param>
public sealed record ChatMessageRowModel(
    ConversationMessage Message,
    MessageContent Content,
    bool IsOutgoing,
    bool IsFirstInMessage,
    bool IsLastInMessage,
    bool IsFirstFromParticipant,
    bool IsLastFromParticipant,
    long ContentVersion)
{
    /// <summary>Gets the participant that authored <see cref="Message"/>.</summary>
    public ChatParticipant Participant => Message.Participant;

    /// <summary>Gets the timestamp of <see cref="Message"/>.</summary>
    public DateTimeOffset Timestamp => Message.CreatedAt;

    /// <summary>Gets whether this row renders leading-aligned. The inverse of <see cref="IsOutgoing"/>.</summary>
    public bool IsIncoming => !IsOutgoing;

    /// <summary>
    /// Returns a stable key that Blazor's <c>@key</c> can use so a streaming mutation reuses
    /// the same DOM subtree instead of ripping the row out and rebuilding it.
    /// </summary>
    /// <returns>A composite of the message and content identifiers.</returns>
    public string Key => $"{Message.Id}::{Content.Id}";
}
