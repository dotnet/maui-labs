using System.Globalization;
using System.Text;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>What changed between two versions of a text, word by word.</summary>
public static class TextDiff
{
    // Above this many word pairs a full comparison costs too much; the change is then shown as a whole.
    private const long MaxComparisons = 4_000_000;

    /// <summary>Compares two texts.</summary>
    /// <returns>
    /// The segments that read as the new text with the removed words kept in place, so each version joins back exactly.
    /// A word followed by different whitespace, such as the last word of a paragraph that now continues, stays
    /// unchanged; only the whitespace after it is marked.
    /// </returns>
    public static IReadOnlyList<DiffSegment> Compute(string? before, string? after)
    {
        var oldWords = Tokenize(before ?? string.Empty);
        var newWords = Tokenize(after ?? string.Empty);
        var oldKeys = oldWords.ConvertAll(static word => word.TrimEnd());
        var newKeys = newWords.ConvertAll(static word => word.TrimEnd());
        var segments = new List<DiffSegment>();
        if ((long)oldWords.Count * newWords.Count > MaxComparisons)
        {
            Add(segments, DiffKind.Removed, before ?? string.Empty);
            Add(segments, DiffKind.Added, after ?? string.Empty);
            return segments;
        }

        // Longest common subsequence of words, read from the end so the walk below goes forwards.
        var lengths = new int[oldWords.Count + 1, newWords.Count + 1];
        for (var i = oldWords.Count - 1; i >= 0; i--)
        {
            for (var j = newWords.Count - 1; j >= 0; j--)
            {
                lengths[i, j] = oldKeys[i] == newKeys[j]
                    ? lengths[i + 1, j + 1] + 1
                    : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
            }
        }

        int oldIndex = 0, newIndex = 0;
        while (oldIndex < oldWords.Count && newIndex < newWords.Count)
        {
            if (oldKeys[oldIndex] == newKeys[newIndex])
            {
                var word = newKeys[newIndex];
                var oldSpace = oldWords[oldIndex++][word.Length..];
                var newSpace = newWords[newIndex++][word.Length..];
                Add(segments, DiffKind.Unchanged, word);
                if (oldSpace == newSpace)
                {
                    Add(segments, DiffKind.Unchanged, newSpace);
                }
                else
                {
                    Add(segments, DiffKind.Removed, oldSpace);
                    Add(segments, DiffKind.Added, newSpace);
                }
            }
            else if (lengths[oldIndex + 1, newIndex] >= lengths[oldIndex, newIndex + 1])
            {
                Add(segments, DiffKind.Removed, oldWords[oldIndex++]);
            }
            else
            {
                Add(segments, DiffKind.Added, newWords[newIndex++]);
            }
        }

        while (oldIndex < oldWords.Count)
            Add(segments, DiffKind.Removed, oldWords[oldIndex++]);
        while (newIndex < newWords.Count)
            Add(segments, DiffKind.Added, newWords[newIndex++]);
        return segments;
    }

    /// <summary>
    /// Splits text into words and punctuation marks, each with the whitespace that follows it, so the pieces join back
    /// exactly and a word still matches when a comma or full stop is added after it. It reads whole text elements, so
    /// an emoji, also with a skin tone or as a joined sequence, is never split, and an accent stays with its letter.
    /// </summary>
    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var start = 0;
        var previous = CharacterKind.Space;
        for (var index = 0; index < text.Length; index += StringInfo.GetNextTextElementLength(text, index))
        {
            var kind = GetKind(text, index);
            if (index > start && StartsToken(previous, kind))
            {
                tokens.Add(text[start..index]);
                start = index;
            }

            previous = kind;
        }

        if (start < text.Length)
            tokens.Add(text[start..]);
        return tokens;
    }

    // A token starts after whitespace and at each punctuation mark, and a word starts after a punctuation mark.
    private static bool StartsToken(CharacterKind previous, CharacterKind current) =>
        current != CharacterKind.Space &&
        (previous != CharacterKind.Word || current != CharacterKind.Word);

    // A text element is what its first character is: a letter or digit, which its combining marks follow, an
    // apostrophe, which belongs to its word, whitespace, or anything else, such as punctuation or an emoji.
    private static CharacterKind GetKind(string text, int index)
    {
        Rune.DecodeFromUtf16(text.AsSpan(index), out var rune, out _);
        if (Rune.IsWhiteSpace(rune))
            return CharacterKind.Space;
        return Rune.IsLetterOrDigit(rune) || rune.Value is '\'' or '\u2019' ? CharacterKind.Word : CharacterKind.Mark;
    }

    private static void Add(List<DiffSegment> segments, DiffKind kind, string text)
    {
        if (text.Length == 0)
            return;
        if (segments.Count > 0 && segments[^1].Kind == kind)
            segments[^1] = segments[^1] with { Text = segments[^1].Text + text };
        else
            segments.Add(new DiffSegment(kind, text));
    }

    private enum CharacterKind
    {
        Space,
        Word,
        Mark,
    }
}

/// <summary>Whether a piece of text was kept, added or removed.</summary>
public enum DiffKind
{
    Unchanged,
    Added,
    Removed,
}

/// <summary>A piece of text and whether it was kept, added or removed.</summary>
public sealed record DiffSegment(DiffKind Kind, string Text);
