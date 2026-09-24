using System.Globalization;
using System.Numerics.Tensors;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AIExtensions.Sample.ChatPlayground.Features.Recording;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AIExtensions.Sample.ChatPlayground.Features.Search;

/// <summary>A saved chat and the turn excerpt that best matches a search.</summary>
public sealed record ChatSearchHit(
    string Id, string Title, string Snippet, int InteractionCount, DateTimeOffset UpdatedAtUtc, float Score)
{
    public string Details => $"{InteractionCount} {(InteractionCount == 1 ? "turn" : "turns")} - " +
        UpdatedAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
}

public sealed record ChatSearchResults(IReadOnlyList<ChatSearchHit> Hits, bool IsSemantic, string? Notice);

/// <summary>Keeps replaceable, model-specific text indexes outside portable chat recordings.</summary>
public sealed class ChatSearchService : IDisposable
{
    private const int IndexVersion = 1;
    private const int MaxChunkCharacters = 360;
    private readonly ChatRecordingService _recording;
    private readonly ILogger<ChatSearchService> _logger;
    private readonly string _indexDirectory;
    private readonly Func<IEmbeddingGenerator<string, Embedding<float>>>? _localFactory;
    private readonly Func<IEmbeddingGenerator<string, Embedding<float>>>? _azureFactory;
    private readonly string? _localModelKey;
    private readonly string? _azureModelKey;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, StoredIndex> _cache = new(StringComparer.Ordinal);
    private IEmbeddingGenerator<string, Embedding<float>>? _local;
    private IEmbeddingGenerator<string, Embedding<float>>? _azure;

    public ChatSearchService(
        ChatRecordingService recording,
        ILogger<ChatSearchService> logger,
        string dataDirectory,
        Func<IEmbeddingGenerator<string, Embedding<float>>>? localFactory = null,
        string? localModelKey = null,
        Func<IEmbeddingGenerator<string, Embedding<float>>>? azureFactory = null,
        string? azureModelKey = null)
    {
        if ((localFactory is null) != (localModelKey is null) ||
            (azureFactory is null) != (azureModelKey is null))
            throw new ArgumentException("Each embedding provider needs both a factory and a model identity.");

        _recording = recording;
        _logger = logger;
        _indexDirectory = Path.Combine(dataDirectory, "chat-playground", "indexes");
        _localFactory = localFactory;
        _azureFactory = azureFactory;
        _localModelKey = localModelKey;
        _azureModelKey = azureModelKey;
    }

    public bool HasLocalEmbeddings => _localFactory is not null;
    public bool HasAzureEmbeddings => _azureFactory is not null;

    /// <summary>Indexes an archived chat without holding up recording or replacing its data.</summary>
    public async Task<string?> IndexChatAsync(string chatId, bool useAzure, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var chat = _recording.ListChats().Single(item => item.Id == chatId);
            if (chat.InteractionCount == 0)
                return null;
            var notices = new List<string>();
            var (modelKey, generator) = GetProvider(useAzure, semantic: true);
            await EnsureIndexAsync(chat, modelKey, generator, notices, cancellationToken)
                .ConfigureAwait(false);
            return notices.Count == 0 ? null : string.Join(" ", notices.Distinct());
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Filters locally while typing; semantic search runs only on explicit request.</summary>
    public async Task<ChatSearchResults> SearchAsync(
        string query, bool semantic, bool useAzure, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            query = query.Trim();
            var (modelKey, generator) = GetProvider(useAzure, semantic);
            var notices = new List<string>();
            if (semantic && generator is null)
                notices.Add("Semantic search is unavailable; showing keyword matches.");

            var chats = _recording.ListChats().Where(chat => chat.InteractionCount > 0).ToArray();
            var indexes = new List<(SavedChat Chat, StoredIndex Index)>(chats.Length);
            foreach (var chat in chats)
            {
                cancellationToken.ThrowIfCancellationRequested();
                indexes.Add((chat, await EnsureIndexAsync(
                    chat, modelKey, generator, notices, cancellationToken).ConfigureAwait(false)));
            }

            ReadOnlyMemory<float> queryVector = default;
            if (query.Length > 0 && generator is not null && indexes.Any(item => item.Index.Chunks.Count > 0))
            {
                queryVector = await Task.Run(
                    () => generator.GenerateVectorAsync(query, cancellationToken: cancellationToken),
                    cancellationToken).ConfigureAwait(false);
                ValidateVector(queryVector.Span, dimension: 0);
                for (var i = 0; i < indexes.Count; i++)
                {
                    var (chat, index) = indexes[i];
                    if (index.Chunks.Count == 0 || index.Dimension == queryVector.Length)
                        continue;

                    notices.Add($"Rebuilt the outdated search vectors for {chat.Title}.");
                    indexes[i] = (chat, await EnsureIndexAsync(
                        chat, modelKey, generator, notices, cancellationToken, rebuild: true)
                        .ConfigureAwait(false));
                }
            }

            var hits = new List<ChatSearchHit>();
            foreach (var (chat, index) in indexes)
            {
                var titleMatches = query.Length > 0 &&
                    chat.Title.Contains(query, StringComparison.OrdinalIgnoreCase);
                var bestScore = titleMatches ? 0.5f : float.NegativeInfinity;
                var snippet = index.Chunks.FirstOrDefault();

                foreach (var chunk in index.Chunks)
                {
                    var keywordMatch = query.Length > 0 &&
                        chunk.Text.Contains(query, StringComparison.OrdinalIgnoreCase);
                    if (query.Length == 0)
                        break;
                    if (queryVector.IsEmpty && !keywordMatch)
                        continue;

                    var score = queryVector.IsEmpty ? 0f :
                        TensorPrimitives.CosineSimilarity(queryVector.Span, chunk.Vector!);
                    if (keywordMatch)
                        score += 0.6f;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        snippet = chunk;
                    }
                }

                if (query.Length == 0 || bestScore > float.NegativeInfinity)
                {
                    var excerpt = snippet is null ? chat.Title :
                        $"{(snippet.IsUser ? "You" : "Assistant")}: {snippet.Text}";
                    hits.Add(new ChatSearchHit(chat.Id, chat.Title, excerpt,
                        chat.InteractionCount, chat.UpdatedAtUtc,
                        query.Length == 0 ? 0 : bestScore));
                }
            }

            return new ChatSearchResults(
                hits.OrderByDescending(hit => hit.Score).ThenByDescending(hit => hit.UpdatedAtUtc)
                    .Take(40).ToArray(),
                generator is not null && semantic,
                notices.Count == 0 ? null : string.Join(" ", notices.Distinct()));
        }
        finally
        {
            _gate.Release();
        }
    }

    private (string ModelKey, IEmbeddingGenerator<string, Embedding<float>>? Generator) GetProvider(
        bool useAzure, bool semantic)
    {
        if (!semantic)
            return ("text-v1", null);
        if (useAzure)
        {
            if (_azureFactory is null)
                throw new InvalidOperationException(
                    "Azure semantic search needs AI:EmbeddingDeploymentName in local user secrets.");
            return (_azureModelKey!, _azure ??= _azureFactory());
        }
        return _localFactory is null
            ? ("text-v1", null)
            : (_localModelKey!, _local ??= _localFactory());
    }

    private async Task<StoredIndex> EnsureIndexAsync(
        SavedChat chat, string modelKey, IEmbeddingGenerator<string, Embedding<float>>? generator,
        List<string> notices, CancellationToken cancellationToken, bool rebuild = false)
    {
        // Keep endpoint and model identity out of filenames and the persisted index.
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(modelKey)));
        var cacheKey = key + chat.Id;
        var path = Path.Combine(_indexDirectory, key, chat.Id + ".json");
        StoredIndex? index = null;
        if (!rebuild && !_cache.TryGetValue(cacheKey, out index))
        {
            if (File.Exists(path))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
                    index = JsonSerializer.Deserialize<StoredIndex>(json)
                        ?? throw new InvalidDataException("The search index is empty.");
                    if (index.Version != IndexVersion || index.ModelKey != key ||
                        index.InteractionCount < 0 || index.InteractionCount > chat.InteractionCount ||
                        index.Chunks is null || index.Dimension < 0 ||
                        index.Chunks.Any(chunk => chunk is null || chunk.Text is null ||
                            chunk.Turn < 0 || chunk.Turn >= index.InteractionCount ||
                            chunk.Vector is { Length: 0 } ||
                            generator is not null && chunk.Vector?.Length != index.Dimension))
                        throw new InvalidDataException("The search index is incompatible with its recording.");
                }
                catch (Exception exception) when (exception is JsonException or InvalidDataException)
                {
                    _logger.LogWarning(exception, "Rebuilding the damaged search index for chat {ChatId}.", chat.Id);
                    notices.Add($"Rebuilt a damaged search index for {chat.Title}.");
                    index = null;
                }
            }
        }
        index ??= new StoredIndex { ModelKey = key };

        if (index.InteractionCount == chat.InteractionCount)
        {
            _cache[cacheKey] = index;
            return index;
        }

        var chunks = await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var recording = _recording.ReadChat(chat.Id);
            if (recording.Interactions.Count != chat.InteractionCount)
                throw new InvalidDataException($"The chat library metadata for {chat.Title} is out of date.");
            return Extract(recording, index.InteractionCount);
        }, cancellationToken).ConfigureAwait(false);
        var updated = new StoredIndex
        {
            ModelKey = key,
            InteractionCount = chat.InteractionCount,
            Dimension = index.Dimension,
            Chunks = [.. index.Chunks],
        };
        if (generator is not null)
        {
            foreach (var batch in chunks.Chunk(32))
            {
                var embeddings = await Task.Run(
                    () => generator.GenerateAsync(
                        batch.Select(chunk => chunk.Text), cancellationToken: cancellationToken),
                    cancellationToken).ConfigureAwait(false);
                if (embeddings.Count != batch.Length)
                    throw new InvalidDataException("The embedding provider returned the wrong number of vectors.");
                for (var i = 0; i < batch.Length; i++)
                {
                    var vector = embeddings[i].Vector.ToArray();
                    ValidateVector(vector, updated.Dimension);
                    updated.Dimension = vector.Length;
                    batch[i].Vector = vector;
                }
            }
        }

        updated.Chunks.AddRange(chunks);
        await Task.Run(() =>
            ChatLibraryStore.WriteAtomic(path, JsonSerializer.Serialize(updated)), cancellationToken)
            .ConfigureAwait(false);
        _cache[cacheKey] = updated;
        return updated;
    }

    private static void ValidateVector(ReadOnlySpan<float> vector, int dimension)
    {
        if (vector.IsEmpty)
            throw new InvalidDataException("The embedding provider returned an empty vector.");
        var length = TensorPrimitives.Norm(vector);
        if (dimension != 0 && vector.Length != dimension ||
            length <= 0 || !float.IsFinite(length))
            throw new InvalidDataException("The embedding provider returned an empty, invalid, or incompatible vector.");
    }

    private static List<SearchChunk> Extract(ChatRecording recording, int start)
    {
        var chunks = new List<SearchChunk>();
        for (var turn = start; turn < recording.Interactions.Count; turn++)
        {
            var interaction = recording.Interactions[turn];
            // Requests include all earlier turns; only the latest user message belongs to this one.
            var user = ChatRecordingSerializer.ReadRequestMessages(interaction.Request)
                .LastOrDefault(message => message.Role == ChatRole.User);
            if (user is not null)
                AddText(user.Contents.OfType<TextContent>().Select(content => content.Text), isUser: true);

            var assistantText = interaction.IsStreaming
                ? interaction.Updates.Select(ChatRecordingSerializer.ReadUpdate)
                    .SelectMany(update => update.Contents).OfType<TextContent>()
                    .Select(content => content.Text)
                : ChatRecordingSerializer.ReadResponse(interaction.Response!).Messages
                    .Where(message => message.Role == ChatRole.Assistant)
                    .SelectMany(message => message.Contents).OfType<TextContent>()
                    .Select(content => content.Text);
            AddText([string.Concat(assistantText)], isUser: false);

            void AddText(IEnumerable<string?> parts, bool isUser)
            {
                foreach (var text in SplitText(string.Join(" ", parts)))
                    chunks.Add(new SearchChunk { Turn = turn, IsUser = isUser, Text = text });
            }
        }
        return chunks;
    }

    private static IEnumerable<string> SplitText(string value)
    {
        var normalized = Regex.Replace(value, @"\s+", " ").Trim();
        var paragraph = new StringBuilder();
        foreach (var sentence in Regex.Split(normalized, @"(?<=[.!?])\s+"))
        {
            if (sentence.Length == 0)
                continue;
            if (paragraph.Length > 0 && paragraph.Length + sentence.Length + 1 > MaxChunkCharacters)
            {
                yield return paragraph.ToString();
                paragraph.Clear();
            }
            if (sentence.Length > MaxChunkCharacters)
            {
                if (paragraph.Length > 0)
                {
                    yield return paragraph.ToString();
                    paragraph.Clear();
                }
                var remaining = sentence;
                while (remaining.Length > MaxChunkCharacters)
                {
                    var end = remaining.LastIndexOf(' ', MaxChunkCharacters, MaxChunkCharacters);
                    if (end <= 0)
                        end = MaxChunkCharacters;
                    yield return remaining[..end];
                    remaining = remaining[end..].TrimStart();
                }
                if (remaining.Length > 0)
                    paragraph.Append(remaining);
            }
            else
            {
                if (paragraph.Length > 0)
                    paragraph.Append(' ');
                paragraph.Append(sentence);
            }
        }
        if (paragraph.Length > 0)
            yield return paragraph.ToString();
    }

    public void Dispose()
    {
        _local?.Dispose();
        _azure?.Dispose();
        _gate.Dispose();
    }

    private sealed class StoredIndex
    {
        public int Version { get; set; } = IndexVersion;
        public string ModelKey { get; set; } = string.Empty;
        public int InteractionCount { get; set; }
        public int Dimension { get; set; }
        public List<SearchChunk> Chunks { get; set; } = [];
    }

    private sealed class SearchChunk
    {
        public int Turn { get; set; }
        public bool IsUser { get; set; }
        public string Text { get; set; } = string.Empty;
        public float[]? Vector { get; set; }
    }
}
