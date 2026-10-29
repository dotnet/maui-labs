using System.Text;
using AIExtensions.Sample.ChatPlayground;

namespace Microsoft.Maui.AI.Chat.Tests;

public sealed class DocumentChunkerTests
{
    [Fact]
    public void WholeDocument_NormalizesWhitespaceAndIgnoresValidSizeAndOverlap()
    {
        var chunks = DocumentChunker.Chunk(" \tOne.\n\n Two.  Three! ",
            new(DocumentChunkingMode.WholeDocument, 2, 1));

        Assert.Equal(["One. Two. Three!"], chunks);
    }

    [Theory]
    [InlineData(DocumentChunkingMode.WholeDocument)]
    [InlineData(DocumentChunkingMode.FixedCharacters)]
    [InlineData(DocumentChunkingMode.Sentences)]
    public void EmptyText_ProducesNoChunks(DocumentChunkingMode mode) =>
        Assert.Empty(DocumentChunker.Chunk(" \n\t ", new(mode)));

    [Fact]
    public void FixedCharacters_DefaultsPreserveTheOriginal360UnitWordBoundaryPolicy()
    {
        var text = string.Join(" ", Enumerable.Repeat("123456789", 80));

        var chunks = DocumentChunker.Chunk(text, new());

        Assert.Equal([359, 359, 79], chunks.Select(chunk => chunk.Length));
        Assert.Equal(text, string.Join(" ", chunks));
    }

    [Fact]
    public void FixedCharacters_UsesUtf16UnitsAndOverlapsWithoutLosingTheTail()
    {
        Assert.Equal(["abcde", "defgh", "ghijk"],
            DocumentChunker.Chunk("abcdefghijk", new(Size: 5, Overlap: 2)));
        Assert.Equal(["abc", "bcd", "cde", "def"],
            DocumentChunker.Chunk("abcdef", new(Size: 3, Overlap: 2)));
    }

    [Fact]
    public void FixedCharacters_PrefersWordBoundariesButNeverReturnsWhitespaceChunks()
    {
        var chunks = DocumentChunker.Chunk("one two three four five", new(Size: 10, Overlap: 9));

        Assert.NotEmpty(chunks);
        Assert.All(chunks, chunk => Assert.False(string.IsNullOrWhiteSpace(chunk)));
        Assert.EndsWith("five", chunks[^1]);
        Assert.InRange(chunks.Count, 1, 23);
    }

    [Fact]
    public void FixedCharacters_NonBmpAndNearTotalOverlapAlwaysAdvanceWithoutSplittingPairs()
    {
        const string text = "A😀BC🚲D𐐀EF🌳G";
        var strictEncoding = new UTF8Encoding(false, true);
        for (var size = 1; size <= 12; size++)
        {
            for (var overlap = 0; overlap < size; overlap++)
            {
                var chunks = DocumentChunker.Chunk(text, new(Size: size, Overlap: overlap));
                Assert.InRange(chunks.Count, 1, text.Length);
                Assert.All(chunks, chunk =>
                {
                    Assert.NotEmpty(chunk);
                    Assert.False(char.IsLowSurrogate(chunk[0]));
                    Assert.False(char.IsHighSurrogate(chunk[^1]));
                    Assert.NotEmpty(strictEncoding.GetBytes(chunk));
                    Assert.InRange(chunk.Length, 1, Math.Max(2, size));
                });
                Assert.EndsWith("G", chunks[^1]);
                if (overlap == 0)
                    Assert.Equal(text, string.Concat(chunks));
            }
        }
    }

    [Fact]
    public void Sentences_PreservesIdentifiersDecimalsAndTerminalPunctuation()
    {
        var chunks = DocumentChunker.Chunk(
            "Microsoft.Maui reads 3.14 units.\n Cache.exe retries?!\tLast sentence without punctuation",
            new(DocumentChunkingMode.Sentences, 1));

        Assert.Equal([
            "Microsoft.Maui reads 3.14 units.",
            "Cache.exe retries?!",
            "Last sentence without punctuation",
        ], chunks);
    }

    [Fact]
    public void Sentences_CountsSizeAndOverlapInSentencesAndStopsAtTheFinalWindow()
    {
        Assert.Equal(["First. Second!", "Second! Third?", "Third? Fourth."],
            DocumentChunker.Chunk("First. Second! Third? Fourth.",
                new(DocumentChunkingMode.Sentences, 2, 1)));
        Assert.Equal(["First. Second!", "Third?"],
            DocumentChunker.Chunk("First. Second! Third?",
                new(DocumentChunkingMode.Sentences, 2)));
    }

    [Fact]
    public void FixedCharacters_MaximumDocumentWithNearTotalOverlap_RejectsExpansionBeforeLargeAllocation()
    {
        var text = new string('a', 256 * 1024);
        var before = GC.GetAllocatedBytesForCurrentThread();

        var error = Assert.Throws<InvalidDataException>(() =>
            DocumentChunker.Chunk(text, new(Size: 131072, Overlap: 131071)));

        Assert.Contains("4 Mi UTF-16", error.Message);
        Assert.Contains("Reduce overlap or increase chunk size", error.Message);
        Assert.InRange(GC.GetAllocatedBytesForCurrentThread() - before, 0, 16 * 1024 * 1024);
    }

    [Fact]
    public void Sentences_MaximumDocumentWithNearTotalOverlap_RejectsExpansionBeforeLargeAllocation()
    {
        var text = string.Concat(Enumerable.Repeat("A. ", 87381));
        Assert.Equal(256 * 1024 - 1, text.Length);
        var before = GC.GetAllocatedBytesForCurrentThread();

        var error = Assert.Throws<InvalidDataException>(() =>
            DocumentChunker.Chunk(text, new(DocumentChunkingMode.Sentences, 43690, 43689)));

        Assert.Contains("4 Mi UTF-16", error.Message);
        Assert.Contains("Reduce overlap or increase chunk size", error.Message);
        Assert.InRange(GC.GetAllocatedBytesForCurrentThread() - before, 0, 24 * 1024 * 1024);
    }

    [Theory]
    [InlineData(DocumentChunkingMode.FixedCharacters)]
    [InlineData(DocumentChunkingMode.Sentences)]
    public void TooManySmallChunks_RejectsExpansionEvenWhenCharacterBudgetWouldAllowIt(DocumentChunkingMode mode)
    {
        var text = mode == DocumentChunkingMode.Sentences
            ? string.Join(" ", Enumerable.Repeat("A.", 4097))
            : new string('a', 4097);

        var error = Assert.Throws<InvalidDataException>(() =>
            DocumentChunker.Chunk(text, new(mode, 1)));

        Assert.Contains("4096 chunks", error.Message);
    }

    [Fact]
    public void FixedCharacters_MaximumDocumentWithDefaultPolicy_StillSucceeds()
    {
        var text = new string('a', 256 * 1024);

        var chunks = DocumentChunker.Chunk(text, new());

        Assert.Equal(text, string.Concat(chunks));
        Assert.Equal(729, chunks.Count);
    }

    [Fact]
    public void Sentences_MaximumValidParameters_StopAtFinalWindowWithoutOverflow()
    {
        var text = string.Join(" ", Enumerable.Repeat("A.", 4096));

        Assert.Equal([text], DocumentChunker.Chunk(text,
            new(DocumentChunkingMode.Sentences, 262144, 262143)));
        Assert.Equal(4096, DocumentChunker.Chunk(text, new(DocumentChunkingMode.Sentences, 1)).Count);
    }

    [Theory]
    [InlineData(DocumentChunkingMode.FixedCharacters, 0, 0)]
    [InlineData(DocumentChunkingMode.FixedCharacters, -1, 0)]
    [InlineData(DocumentChunkingMode.FixedCharacters, 262145, 0)]
    [InlineData(DocumentChunkingMode.FixedCharacters, 5, -1)]
    [InlineData(DocumentChunkingMode.FixedCharacters, 5, 5)]
    [InlineData(DocumentChunkingMode.Sentences, 1, 1)]
    [InlineData(DocumentChunkingMode.WholeDocument, 0, 0)]
    [InlineData(DocumentChunkingMode.WholeDocument, 5, 6)]
    [InlineData((DocumentChunkingMode)999, 5, 0)]
    public void InvalidOptions_ThrowExplicitlyEvenForEmptyText(DocumentChunkingMode mode, int size, int overlap) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DocumentChunker.Chunk(string.Empty, new(mode, size, overlap)));

    [Fact]
    public void NullInputs_ThrowExplicitly()
    {
        Assert.Throws<ArgumentNullException>(() => DocumentChunker.Chunk(null!, new()));
        Assert.Throws<ArgumentNullException>(() => DocumentChunker.Chunk("text", null!));
    }
}
