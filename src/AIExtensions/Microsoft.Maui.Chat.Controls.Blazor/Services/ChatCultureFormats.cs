// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Maui.Chat.Controls;

namespace Microsoft.Maui.Chat.Controls.Blazor;

/// <summary>
/// Small formatting helpers shared by the shell components. Kept factored out so tests can
/// exercise the exact strings the shell renders.
/// </summary>
public static class ChatCultureFormats
{
    /// <summary>Returns the "N is/are typing…" summary for a list of participants.</summary>
    /// <param name="participants">The participants currently composing.</param>
    /// <returns>A user-facing summary, or the empty string when the list is empty.</returns>
    public static string FormatTypingText(IEnumerable<ChatParticipant>? participants)
    {
        if (participants is null)
        {
            return string.Empty;
        }

        var names = participants
            .Where(static p => p is not null)
            .Select(static p => p.DisplayName)
            .Where(static n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return names.Length switch
        {
            0 => string.Empty,
            1 => $"{names[0]} is typing…",
            2 => $"{names[0]} and {names[1]} are typing…",
            _ => $"{names[0]}, {names[1]}, and {names.Length - 2} other{(names.Length == 3 ? string.Empty : "s")} are typing…",
        };
    }

    /// <summary>Formats a message timestamp as a short, culture-aware label for the delivery footer.</summary>
    /// <param name="timestamp">The timestamp of the message.</param>
    /// <param name="now">The current time. Injected for deterministic tests.</param>
    /// <returns>A short label — a time for messages within 24 hours, otherwise a short date.</returns>
    public static string FormatTimestamp(DateTimeOffset timestamp, DateTimeOffset? now = null)
    {
        var reference = now ?? DateTimeOffset.Now;
        var local = timestamp.LocalDateTime;
        var referenceLocal = reference.LocalDateTime;

        if (local.Date == referenceLocal.Date)
        {
            return local.ToString("t", CultureInfo.CurrentCulture);
        }

        if (local >= referenceLocal.AddDays(-6))
        {
            return local.ToString("ddd t", CultureInfo.CurrentCulture);
        }

        return local.ToString("d", CultureInfo.CurrentCulture);
    }
}
