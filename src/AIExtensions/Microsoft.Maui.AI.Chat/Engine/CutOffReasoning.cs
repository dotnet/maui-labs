// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat;

/// <summary>
/// Leaves reasoning that a stop cut off out of a stopped turn's history. It is still shown and stored in
/// the thread, but providers can reject a reasoning item or thinking block that no output follows, or an
/// assistant message with nothing else in it.
/// </summary>
internal static class CutOffReasoning
{
    /// <summary>
    /// Removes the reasoning that ends the turn's last message, and the message when nothing but usage is
    /// left in it.
    /// </summary>
    /// <param name="messages">The turn's messages in history order.</param>
    internal static void Remove(List<ChatMessage> messages)
    {
        if (messages.Count == 0 || messages[^1] is not { Role: var role } last || role != ChatRole.Assistant)
            return;

        var contents = last.Contents;
        var lastOutput = contents.Count - 1;
        while (lastOutput >= 0 && contents[lastOutput] is TextReasoningContent or UsageContent)
            lastOutput--;

        var cutOff = false;
        for (var i = lastOutput + 1; i < contents.Count; i++)
            cutOff |= contents[i] is TextReasoningContent;

        if (!cutOff)
            return;

        if (lastOutput < 0)
        {
            messages.RemoveAt(messages.Count - 1);
            return;
        }

        var kept = last.Clone();
        kept.Contents = contents
            .Where((content, index) => index <= lastOutput || content is not TextReasoningContent)
            .ToList();
        messages[^1] = kept;
    }
}
