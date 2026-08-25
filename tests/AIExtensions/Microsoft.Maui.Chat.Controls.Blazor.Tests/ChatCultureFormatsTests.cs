// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;

namespace Microsoft.Maui.Chat.Controls.Blazor.Tests;

/// <summary>Verifies the "N is typing…" and timestamp string helpers used by the shell.</summary>
public class ChatCultureFormatsTests
{
    [Fact]
    public void FormatTypingText_Empty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, ChatCultureFormats.FormatTypingText(participants: null));
        Assert.Equal(string.Empty, ChatCultureFormats.FormatTypingText(Array.Empty<ChatParticipant>()));
    }

    [Fact]
    public void FormatTypingText_One_UsesSingular()
    {
        var alice = new ChatParticipant("a", "Alice");

        var text = ChatCultureFormats.FormatTypingText(new[] { alice });

        Assert.Equal("Alice is typing…", text);
    }

    [Fact]
    public void FormatTypingText_Two_UsesAnd()
    {
        var alice = new ChatParticipant("a", "Alice");
        var bob = new ChatParticipant("b", "Bob");

        var text = ChatCultureFormats.FormatTypingText(new[] { alice, bob });

        Assert.Equal("Alice and Bob are typing…", text);
    }

    [Fact]
    public void FormatTypingText_Three_UsesOneOther()
    {
        var alice = new ChatParticipant("a", "Alice");
        var bob = new ChatParticipant("b", "Bob");
        var carol = new ChatParticipant("c", "Carol");

        var text = ChatCultureFormats.FormatTypingText(new[] { alice, bob, carol });

        Assert.Equal("Alice, Bob, and 1 other are typing…", text);
    }

    [Fact]
    public void FormatTypingText_Many_UsesPluralOthers()
    {
        var participants = Enumerable.Range(0, 5)
            .Select(i => new ChatParticipant("p" + i, "Person " + i))
            .ToArray();

        var text = ChatCultureFormats.FormatTypingText(participants);

        Assert.Equal("Person 0, Person 1, and 3 others are typing…", text);
    }

    [Fact]
    public void FormatTypingText_DeduplicatesByName()
    {
        var alice = new ChatParticipant("a", "Alice");
        var alsoAlice = new ChatParticipant("b", "Alice");

        var text = ChatCultureFormats.FormatTypingText(new[] { alice, alsoAlice });

        Assert.Equal("Alice is typing…", text);
    }

    [Fact]
    public void FormatTimestamp_SameDay_ReturnsShortTime()
    {
        // Force a known culture so t-format is stable.
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("en-US");
        try
        {
            var now = new DateTimeOffset(2024, 3, 15, 14, 30, 0, TimeSpan.Zero);
            var when = new DateTimeOffset(2024, 3, 15, 9, 5, 0, TimeSpan.Zero);

            var formatted = ChatCultureFormats.FormatTimestamp(when, now);

            Assert.Contains("AM", formatted);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void FormatTimestamp_WithinWeek_ReturnsWeekdayAndTime()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("en-US");
        try
        {
            var now = new DateTimeOffset(2024, 3, 15, 14, 30, 0, TimeSpan.Zero);
            var when = new DateTimeOffset(2024, 3, 12, 9, 5, 0, TimeSpan.Zero);

            var formatted = ChatCultureFormats.FormatTimestamp(when, now);

            // Culture-neutral assertion: three-letter weekday appears somewhere in the string.
            Assert.Contains("Tue", formatted);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void FormatTimestamp_Older_ReturnsShortDate()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("en-US");
        try
        {
            var now = new DateTimeOffset(2024, 3, 15, 14, 30, 0, TimeSpan.Zero);
            var when = new DateTimeOffset(2024, 2, 1, 9, 5, 0, TimeSpan.Zero);

            var formatted = ChatCultureFormats.FormatTimestamp(when, now);

            Assert.Contains("2/1/2024", formatted);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
