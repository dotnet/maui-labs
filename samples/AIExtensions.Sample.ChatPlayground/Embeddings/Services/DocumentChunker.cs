using System.Text.RegularExpressions;

namespace AIExtensions.Sample.ChatPlayground;

public enum DocumentChunkingMode
{
    WholeDocument,
    FixedCharacters,
    Sentences,
}

public sealed record DocumentChunkingOptions(
    DocumentChunkingMode Mode = DocumentChunkingMode.FixedCharacters, int Size = 360, int Overlap = 0);

/// <summary>Portable, deterministic whitespace-normalized chunking; no model or native tokenizer is used.</summary>
public static class DocumentChunker
{
    private const int MaxEmittedCharacters = 4 * 1024 * 1024;
    private const int MaxChunks = 4096;

    public static string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    public static IReadOnlyList<string> Chunk(string text, DocumentChunkingOptions options)
    {
        ArgumentNullException.ThrowIfNull(text);
        ValidateOptions(options);
        var normalized = Normalize(text);
        if (normalized.Length == 0)
            return [];
        var emittedCharacters = 0;
        if (options.Mode == DocumentChunkingMode.WholeDocument)
        {
            ReserveOutput(normalized.Length, 0, ref emittedCharacters);
            return [normalized];
        }
        if (options.Mode == DocumentChunkingMode.Sentences)
        {
            // Deliberately simple: abbreviations and punctuation followed by closing quotes
            // are not language-aware. Dots inside identifiers and decimals stay intact.
            var sentences = Regex.Split(normalized, @"(?<=[.!?])\s+");
            var chunks = new List<string>();
            for (var start = 0; start < sentences.Length;)
            {
                var remaining = sentences.Length - start;
                var count = Math.Min(options.Size, remaining);
                long chunkLength = count - 1;
                for (var i = 0; i < count; i++)
                    chunkLength += sentences[start + i].Length;
                ReserveOutput(chunkLength, chunks.Count, ref emittedCharacters);
                chunks.Add(string.Join(" ", sentences, start, count));
                if (count == remaining)
                    break;
                // Non-final windows have count == Size and count < remaining, so this cannot overflow.
                start += count - options.Overlap;
            }
            return chunks;
        }

        var result = new List<string>();
        for (var start = 0; start < normalized.Length;)
        {
            var end = start + Math.Min(options.Size, normalized.Length - start);
            if (end < normalized.Length && char.IsLowSurrogate(normalized[end]) &&
                char.IsHighSurrogate(normalized[end - 1]))
                end--;
            // A surrogate pair is indivisible, even when the requested size is one UTF-16 unit.
            if (end == start)
                end = start + Math.Min(2, normalized.Length - start);
            if (end < normalized.Length)
            {
                var space = normalized.LastIndexOf(' ', end - 1, end - start);
                if (space > start + options.Size / 2)
                    end = space;
            }
            var chunk = normalized.AsSpan(start, end - start).Trim();
            if (chunk.Length > 0)
            {
                ReserveOutput(chunk.Length, result.Count, ref emittedCharacters);
                result.Add(chunk.ToString());
            }
            if (end == normalized.Length)
                break;

            var next = end - options.Overlap;
            if (next <= start)
                next = start + (char.IsHighSurrogate(normalized[start]) &&
                    start + 1 < normalized.Length && char.IsLowSurrogate(normalized[start + 1]) ? 2 : 1);
            if (next < normalized.Length && char.IsLowSurrogate(normalized[next]) &&
                char.IsHighSurrogate(normalized[next - 1]))
                next++;
            while (next < normalized.Length && normalized[next] == ' ')
                next++;
            start = next;
        }
        return result;
    }

    private static void ReserveOutput(long chunkLength, int chunkCount, ref int emittedCharacters)
    {
        if (chunkCount >= MaxChunks || chunkLength > MaxEmittedCharacters - emittedCharacters)
            throw new InvalidDataException(
                "Chunking exceeds the per-document budget of 4096 chunks or 4 Mi UTF-16 units of total chunk text. " +
                "Reduce overlap or increase chunk size, then index documents again.");
        emittedCharacters += (int)chunkLength;
    }

    internal static void ValidateOptions(DocumentChunkingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!Enum.IsDefined(options.Mode))
            throw new ArgumentOutOfRangeException(nameof(options), "Choose a valid chunking mode.");
        if (options.Size is <= 0 or > 262144)
            throw new ArgumentOutOfRangeException(nameof(options), "Chunk size must be an integer from 1 to 262144.");
        if (options.Overlap < 0 || options.Overlap >= options.Size)
            throw new ArgumentOutOfRangeException(nameof(options), "Chunk overlap must be zero or greater and smaller than chunk size.");
    }
}
