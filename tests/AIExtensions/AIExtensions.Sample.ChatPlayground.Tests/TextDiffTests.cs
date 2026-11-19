using System.Text;

namespace AIExtensions.Sample.ChatPlayground.Tests;

/// <summary>The word diff that marks what a notes proposal changes.</summary>
public sealed class TextDiffTests
{
    [Fact]
    public void Compute_KeepsBothVersionsInItsSegments()
    {
        const string before = "The cat sat on the mat.\n\nIt was warm.";
        const string after = "The black cat sat on the mat.\n\nIt was very warm.";

        var segments = TextDiff.Compute(before, after);

        Assert.Equal(after, string.Concat(segments.Where(static s => s.Kind != DiffKind.Removed).Select(static s => s.Text)));
        Assert.Equal(before, string.Concat(segments.Where(static s => s.Kind != DiffKind.Added).Select(static s => s.Text)));
        Assert.Equal(["black ", "very "], segments.Where(static s => s.Kind == DiffKind.Added).Select(static s => s.Text));
        Assert.DoesNotContain(segments, static s => s.Kind == DiffKind.Removed);
        Assert.Equal([new DiffSegment(DiffKind.Added, "New text")], TextDiff.Compute(null, "New text"));
    }

    [Fact]
    public void Compute_KeepsAWordWhoseOnlyChangeIsTheWhitespaceAfterIt()
    {
        // A new paragraph after the last one: the old last word gains a line break but is the same word.
        var segments = TextDiff.Compute("They were safe.", "They were safe.\n\nThe end.");

        Assert.Equal(
            [new DiffSegment(DiffKind.Unchanged, "They were safe."), new DiffSegment(DiffKind.Added, "\n\nThe end.")],
            segments);
    }

    [Fact]
    public void Compute_MarksRemovedWordsInPlace()
    {
        var segments = TextDiff.Compute("Pack a raincoat and boots.", "Pack boots.");

        Assert.Equal(
            [
                new DiffSegment(DiffKind.Unchanged, "Pack "),
                new DiffSegment(DiffKind.Removed, "a raincoat and "),
                new DiffSegment(DiffKind.Unchanged, "boots."),
            ],
            segments);
    }

    [Fact]
    public void Compute_KeepsAWordThatGainsPunctuationAfterIt()
    {
        var segments = TextDiff.Compute("Pack: passport, walking shoes", "Pack: passport, walking shoes, toothbrush");

        Assert.Equal(
            [
                new DiffSegment(DiffKind.Unchanged, "Pack: passport, walking shoes"),
                new DiffSegment(DiffKind.Added, ", toothbrush"),
            ],
            segments);
        Assert.Equal(
            [
                new DiffSegment(DiffKind.Unchanged, "Pack a raincoat"),
                new DiffSegment(DiffKind.Added, " and boots"),
                new DiffSegment(DiffKind.Unchanged, "."),
            ],
            TextDiff.Compute("Pack a raincoat.", "Pack a raincoat and boots."));
    }

    [Theory]
    [InlineData("Bring \U0001F457", "Bring \U0001F45F", "Bring ", "\U0001F457", "\U0001F45F", "")]
    [InlineData("Thanks \U0001F44D", "Thanks \U0001F44D\U0001F3FD", "Thanks ", "\U0001F44D", "\U0001F44D\U0001F3FD", "")]
    [InlineData("Family: \U0001F468\u200D\U0001F469\u200D\U0001F467", "Family: \U0001F468\u200D\U0001F469\u200D\U0001F466", "Family: ", "\U0001F468\u200D\U0001F469\u200D\U0001F467", "\U0001F468\u200D\U0001F469\u200D\U0001F466", "")]
    [InlineData("Meet at the cafe.", "Meet at the cafe\u0301.", "Meet at the ", "cafe", "cafe\u0301", ".")]
    [InlineData("A naive plan", "A nai\u0308ve plan", "A ", "naive ", "nai\u0308ve ", "plan")]
    public void Compute_KeepsEmojiAndAccentsWhole(string before, string after, string kept, string removed, string added, string end)
    {
        var segments = TextDiff.Compute(before, after);

        // An emoji with its skin tone or joined family, and a letter with its accent, is changed as a whole.
        DiffSegment[] expected =
        [
            new(DiffKind.Unchanged, kept),
            new(DiffKind.Removed, removed),
            new(DiffKind.Added, added),
            new(DiffKind.Unchanged, end),
        ];
        Assert.Equal(expected.Where(static s => s.Text.Length > 0), segments);
        Assert.All(segments, static s => Assert.DoesNotContain(Rune.ReplacementChar, s.Text.EnumerateRunes()));
    }
}
