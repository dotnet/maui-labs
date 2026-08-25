// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using Microsoft.Maui.Chat.Controls;

namespace Microsoft.Maui.Chat.Controls.Blazor;

/// <summary>
/// Projects a <see cref="ChatConversation"/> into a flat list of <see cref="ChatMessageRowModel"/>
/// values, computing every row's grouping flags in one pass.
/// </summary>
/// <remarks>
/// <para>
/// Grouping semantics deliberately mirror
/// <c>Microsoft.Maui.Chat.Controls.ChatContentItem</c>'s so the neutral Blazor layer and
/// the native XAML layer agree on what "first in message", "last from participant", and
/// "outgoing" mean.
/// </para>
/// </remarks>
public static class ChatRowProjection
{
    /// <summary>Projects every content of every message into a row.</summary>
    /// <param name="conversation">The conversation to project. <see langword="null"/> yields an empty list.</param>
    /// <returns>The projected rows in display order.</returns>
    public static IReadOnlyList<ChatMessageRowModel> Project(ChatConversation? conversation)
    {
        if (conversation is null)
        {
            return Array.Empty<ChatMessageRowModel>();
        }

        var messages = conversation.Messages;
        if (messages.Count == 0)
        {
            return Array.Empty<ChatMessageRowModel>();
        }

        var rows = new List<ChatMessageRowModel>(capacity: messages.Count);
        for (var m = 0; m < messages.Count; m++)
        {
            var message = messages[m];
            var contents = message.Contents;
            if (contents.Count == 0)
            {
                continue;
            }

            var isOutgoing = ChatParticipantHelpers.IsOutgoingFor(conversation, message.Participant);
            var startsRun = m == 0 || !ChatParticipantHelpers.AreSameParticipant(
                messages[m - 1].Participant, message.Participant);
            var endsRun = m == messages.Count - 1 || !ChatParticipantHelpers.AreSameParticipant(
                messages[m + 1].Participant, message.Participant);

            for (var c = 0; c < contents.Count; c++)
            {
                var content = contents[c];
                var isFirstInMessage = c == 0;
                var isLastInMessage = c == contents.Count - 1;

                rows.Add(new ChatMessageRowModel(
                    Message: message,
                    Content: content,
                    IsOutgoing: isOutgoing,
                    IsFirstInMessage: isFirstInMessage,
                    IsLastInMessage: isLastInMessage,
                    IsFirstFromParticipant: startsRun && isFirstInMessage,
                    IsLastFromParticipant: endsRun && isLastInMessage,
                    ContentVersion: 0));
            }
        }

        return rows;
    }
}

/// <summary>
/// Small participant helpers that match <c>ChatContentItem.IsOutgoingFor</c>'s semantics
/// so the two projection paths cannot drift.
/// </summary>
internal static class ChatParticipantHelpers
{
    internal static bool IsOutgoingFor(ChatConversation? conversation, ChatParticipant? participant)
    {
        if (participant is null)
        {
            return false;
        }

        var local = conversation?.LocalParticipant;
        return local is not null
            ? string.Equals(local.Id, participant.Id, StringComparison.Ordinal)
            : participant.IsLocal;
    }

    internal static bool AreSameParticipant(ChatParticipant? a, ChatParticipant? b)
    {
        if (a is null || b is null)
        {
            return ReferenceEquals(a, b);
        }

        return string.Equals(a.Id, b.Id, StringComparison.Ordinal);
    }
}
