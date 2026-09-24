using System.Globalization;
using System.Numerics.Tensors;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AIExtensions.Sample.ChatPlayground.Features.Storage;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AIExtensions.Sample.ChatPlayground.Features.Embeddings;

public sealed record DocumentIndexProgress(
    int CompletedDocuments, int TotalDocuments, string DocumentName, int IndexedChunks, int TotalChunks);

public sealed record DocumentIndexResult(int TotalDocuments, int UpdatedDocuments, string? Notice);

public sealed record DocumentSearchHit(
    string Id, string Name, string Snippet, float Score, DateTimeOffset ImportedAtUtc)
{
    public string Details => $"Similarity {Score:F2} - imported {ImportedAtUtc.ToLocalTime():g}";
}

public sealed record DocumentSearchResults(IReadOnlyList<DocumentSearchHit> Hits, int QueryDimensions, string? Notice);

/// <summary>Indexes imported text with the selected generator and searches only that generator's vectors.</summary>
public sealed class DocumentSearchService : IDisposable
{
    private const int IndexVersion = 1;
    private const int MaxChunkCharacters = 360;
    private readonly DocumentStore _documents;
    private readonly ILogger<DocumentSearchService> _logger;
    private readonly string _indexDirectory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, StoredIndex> _cache = new(StringComparer.Ordinal);

    public DocumentSearchService(
        DocumentStore documents, ILogger<DocumentSearchService> logger, string dataDirectory)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _documents = documents;
        _logger = logger;
        _indexDirectory = Path.Combine(dataDirectory, "embedding-playground", "indexes");
    }

    public async Task<DocumentIndexResult> IndexDocumentsAsync(
        IEmbeddingGenerator<string, Embedding<float>> generator, string modelIdentity,
        int? dimensions = null, IProgress<DocumentIndexProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(generator);
        var key = GetIndexKey(modelIdentity, dimensions);
        var options = dimensions is { } count ? new EmbeddingGenerationOptions { Dimensions = count } : null;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var documents = await _documents.ListAsync(cancellationToken).ConfigureAwait(false);
            var notices = new List<string>();
            var updated = 0;
            for (var i = 0; i < documents.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var info = documents[i];
                var document = await _documents.ReadAsync(info.Id, cancellationToken).ConfigureAwait(false);
                if (await EnsureIndexAsync(document, key, generator, options, notices,
                    (chunks, total) => progress?.Report(new DocumentIndexProgress(
                        i, documents.Count, info.Name, chunks, total)), cancellationToken).ConfigureAwait(false))
                    updated++;
                progress?.Report(new DocumentIndexProgress(i + 1, documents.Count, info.Name, 0, 0));
            }
            return new DocumentIndexResult(documents.Count, updated,
                notices.Count == 0 ? null : string.Join(" ", notices.Distinct()));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<DocumentSearchResults> SearchAsync(
        string query, IEmbeddingGenerator<string, Embedding<float>> generator,
        string modelIdentity, int? dimensions = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentNullException.ThrowIfNull(generator);
        var key = GetIndexKey(modelIdentity, dimensions);
        var options = dimensions is { } count ? new EmbeddingGenerationOptions { Dimensions = count } : null;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var documents = await _documents.ListAsync(cancellationToken).ConfigureAwait(false);
            var indexed = new List<(ImportedDocument Info, StoredIndex Index)>(documents.Count);
            foreach (var info in documents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                StoredIndex? index;
                try
                {
                    index = await LoadIndexAsync(info.Id, key, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is JsonException or InvalidDataException)
                {
                    _logger.LogWarning(exception, "Could not read the search index for document {DocumentId}.", info.Id);
                    throw new InvalidDataException(
                        $"The index for {info.Name} is damaged. Use Index documents in Embeddings settings to rebuild it.",
                        exception);
                }
                if (index is not null)
                {
                    var document = await _documents.ReadAsync(info.Id, cancellationToken).ConfigureAwait(false);
                    if (index.TextHash == HashText(document.Text))
                        indexed.Add((info, index));
                }
            }

            if (indexed.Count == 0 && documents.Count > 0)
                throw new InvalidOperationException(
                    "No documents are indexed for this generator. Use Index documents in Embeddings settings first.");
            if (indexed.Count == 0)
                return new DocumentSearchResults([], 0, null);

            var queryVector = await Task.Run(
                () => generator.GenerateVectorAsync(query.Trim(), options, cancellationToken),
                cancellationToken).ConfigureAwait(false);
            ValidateVector(queryVector.Span, 0);
            var hits = new List<DocumentSearchHit>(indexed.Count);
            foreach (var (info, index) in indexed)
            {
                if (index.Dimension != queryVector.Length)
                    throw new InvalidDataException(
                        $"The vectors for {info.Name} do not match the selected generator. " +
                        "Update its model identity, or clear this index and then use Index documents.");

                var best = index.Chunks
                    .Select(chunk => (chunk.Text, Score: TensorPrimitives.CosineSimilarity(queryVector.Span, chunk.Vector)))
                    .OrderByDescending(chunk => chunk.Score).First();
                hits.Add(new DocumentSearchHit(info.Id, info.Name,
                    best.Text.Length > 180 ? best.Text[..180] + "..." : best.Text,
                    best.Score, info.ImportedAtUtc));
            }
            return new DocumentSearchResults(
                hits.OrderByDescending(hit => hit.Score).ThenByDescending(hit => hit.ImportedAtUtc)
                    .Take(40).ToArray(),
                queryVector.Length, indexed.Count == documents.Count ? null :
                    $"{documents.Count - indexed.Count} new or changed documents need indexing.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> ClearIndexAsync(
        string modelIdentity, int? dimensions = null, CancellationToken cancellationToken = default)
    {
        var key = GetIndexKey(modelIdentity, dimensions);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var path = Path.Combine(_indexDirectory, key);
            foreach (var cached in _cache.Keys.Where(value => value.StartsWith(key, StringComparison.Ordinal)).ToArray())
                _cache.Remove(cached);
            if (!Directory.Exists(path))
                return false;
            await Task.Run(() => Directory.Delete(path, recursive: true), cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearDocumentsAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _documents.ClearAsync(cancellationToken).ConfigureAwait(false);
            if (Directory.Exists(_indexDirectory))
                await Task.Run(() => Directory.Delete(_indexDirectory, recursive: true), cancellationToken)
                    .ConfigureAwait(false);
            _cache.Clear();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool> EnsureIndexAsync(
        StoredDocument document, string key, IEmbeddingGenerator<string, Embedding<float>> generator,
        EmbeddingGenerationOptions? options, List<string> notices,
        Action<int, int>? report, CancellationToken cancellationToken)
    {
        StoredIndex? index = null;
        try
        {
            index = await LoadIndexAsync(document.Id, key, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            _logger.LogWarning(exception, "Rebuilding the damaged index for document {DocumentId}.", document.Id);
            notices.Add($"Rebuilt a damaged index for {document.Name}.");
            _cache.Remove(key + document.Id);
        }

        var hash = HashText(document.Text);
        if (index?.TextHash == hash)
            return false;
        var texts = Chunk(document.Text).ToArray();
        report?.Invoke(0, texts.Length);
        var updated = new StoredIndex { ModelKey = key, TextHash = hash };
        foreach (var batch in texts.Chunk(32))
        {
            var embeddings = await Task.Run(
                () => generator.GenerateAsync(batch, options, cancellationToken),
                cancellationToken).ConfigureAwait(false);
            if (embeddings.Count != batch.Length)
                throw new InvalidDataException("The embedding generator returned the wrong number of vectors.");
            for (var i = 0; i < batch.Length; i++)
            {
                var vector = embeddings[i].Vector.ToArray();
                ValidateVector(vector, updated.Dimension);
                updated.Dimension = vector.Length;
                updated.Chunks.Add(new IndexedChunk { Text = batch[i], Vector = vector });
            }
            report?.Invoke(updated.Chunks.Count, texts.Length);
        }
        var path = Path.Combine(_indexDirectory, key, document.Id + ".json");
        await Task.Run(() => AtomicFile.WriteAllText(path, JsonSerializer.Serialize(updated)), cancellationToken)
            .ConfigureAwait(false);
        _cache[key + document.Id] = updated;
        return true;
    }

    private async Task<StoredIndex?> LoadIndexAsync(string id, string key, CancellationToken cancellationToken)
    {
        var cacheKey = key + id;
        if (_cache.TryGetValue(cacheKey, out var index))
            return index;
        var path = Path.Combine(_indexDirectory, key, id + ".json");
        if (!File.Exists(path))
            return null;

        index = JsonSerializer.Deserialize<StoredIndex>(
            await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false))
            ?? throw new InvalidDataException("The document index is empty.");
        if (index.Version != IndexVersion || index.ModelKey != key ||
            string.IsNullOrWhiteSpace(index.TextHash) || index.Dimension <= 0 ||
            index.Chunks is not { Count: > 0 } ||
            index.Chunks.Any(chunk => chunk is null || string.IsNullOrWhiteSpace(chunk.Text) ||
                chunk.Vector?.Length != index.Dimension))
            throw new InvalidDataException("The document index is invalid.");
        foreach (var chunk in index.Chunks)
            ValidateVector(chunk.Vector, index.Dimension);
        _cache[cacheKey] = index;
        return index;
    }

    private static IEnumerable<string> Chunk(string text)
    {
        var normalized = Regex.Replace(text, @"\s+", " ").Trim();
        for (var start = 0; start < normalized.Length;)
        {
            var end = Math.Min(start + MaxChunkCharacters, normalized.Length);
            if (end < normalized.Length)
            {
                var space = normalized.LastIndexOf(' ', end - 1, end - start);
                if (space > start + MaxChunkCharacters / 2)
                    end = space;
            }
            yield return normalized[start..end].Trim();
            start = end;
            while (start < normalized.Length && normalized[start] == ' ')
                start++;
        }
    }

    private static void ValidateVector(ReadOnlySpan<float> vector, int dimension)
    {
        if (vector.IsEmpty)
            throw new InvalidDataException("The embedding generator returned an empty vector.");
        var norm = TensorPrimitives.Norm(vector);
        if (dimension != 0 && vector.Length != dimension || norm <= 0 || !float.IsFinite(norm))
            throw new InvalidDataException("The embedding generator returned an invalid or incompatible vector.");
    }

    private static string HashText(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static string GetIndexKey(string modelIdentity, int? dimensions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelIdentity);
        if (dimensions is <= 0)
            throw new ArgumentOutOfRangeException(nameof(dimensions), "Embedding dimensions must be positive.");
        var identity = modelIdentity.Length.ToString(CultureInfo.InvariantCulture) + ":" +
            modelIdentity + "\0" + (dimensions?.ToString(CultureInfo.InvariantCulture) ?? "default");
        return HashText(identity);
    }

    public void Dispose() => _gate.Dispose();

    private sealed class StoredIndex
    {
        public int Version { get; set; } = IndexVersion;
        public string ModelKey { get; set; } = string.Empty;
        public string TextHash { get; set; } = string.Empty;
        public int Dimension { get; set; }
        public List<IndexedChunk> Chunks { get; set; } = [];
    }

    private sealed class IndexedChunk
    {
        public string Text { get; set; } = string.Empty;
        public float[] Vector { get; set; } = [];
    }
}
