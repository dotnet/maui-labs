namespace AIExtensions.Sample.ChatPlayground;

public sealed record ImportedDocument(
    string Id,
    string Name,
    int CharacterCount,
    DateTimeOffset ImportedAtUtc)
{
    public string Details =>
        $"{CharacterCount} characters - imported {ImportedAtUtc.ToLocalTime():g}";
}

internal sealed record StoredDocument(
    string Id,
    string Name,
    string Text,
    DateTimeOffset ImportedAtUtc)
{
    public ImportedDocument Summary =>
        new(Id, Name, Text.Length, ImportedAtUtc);
}

public sealed record DocumentIndexProgress(
    int CompletedDocuments,
    int TotalDocuments,
    string DocumentName,
    int IndexedChunks,
    int TotalChunks);

public sealed record DocumentIndexResult(
    int TotalDocuments,
    int UpdatedDocuments,
    string? Notice);

public sealed record DocumentSearchHit(
    string Id,
    string Name,
    string Snippet,
    float Score,
    DateTimeOffset ImportedAtUtc,
    IReadOnlyList<DocumentChunkHit>? RankedChunks = null)
{
    public IReadOnlyList<DocumentChunkHit> Chunks => RankedChunks ?? [];
    public string AutomationId => $"EmbeddingDocument_{Id}";
    public string Details =>
        $"Max chunk cosine {Score:F4} - {Chunks.Count} chunks";
}

public sealed record DocumentChunkHit(
    string DocumentId, string DocumentName, int Ordinal, string Text, float Score)
{
    public string Details => $"Chunk {Ordinal} · cosine {Score:F4}";
    public string AutomationId => $"EmbeddingChunk_{DocumentId}_{Ordinal}";
}

public sealed record DocumentSearchResults(
    IReadOnlyList<DocumentSearchHit> Hits,
    int QueryDimensions,
    string? Notice);
