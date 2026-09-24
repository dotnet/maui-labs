using System.Globalization;
using System.Numerics.Tensors;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AIExtensions.Sample.ChatPlayground.Features.Library;
using AIExtensions.Sample.ChatPlayground.Features.Recording;
using AIExtensions.Sample.ChatPlayground.Features.Storage;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AIExtensions.Sample.ChatPlayground.Features.Search;

public sealed record ChatSearchProgress(
    int CompletedChats, int TotalChats, string ChatTitle,
    int IndexedChunks, int TotalChunks, bool IsIndexing);

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
    private const int IndexVersion = 2;
    private const int MaxChunkCharacters = 360;
    private const int MaxSnippetCharacters = 180;
    private readonly IChatLibrary _library;
    private readonly ILogger<ChatSearchService> _logger;
    private readonly string _indexDirectory;
    private readonly Dictionary<string, ChatEmbeddingProvider> _providers;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, StoredIndex> _cache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IEmbeddingGenerator<string, Embedding<float>>> _generators =
        new(StringComparer.Ordinal);

    public ChatSearchService(
        IChatLibrary library,
        ILogger<ChatSearchService> logger,
        string dataDirectory,
        IEnumerable<ChatEmbeddingProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentNullException.ThrowIfNull(providers);
        var registered = providers.ToArray();
        if (registered.Any(provider => provider is null) ||
            registered.Select(provider => provider.Descriptor.Id).Distinct(StringComparer.Ordinal).Count() != registered.Length ||
            registered.Select(provider => provider.Descriptor.IndexIdentity).Distinct(StringComparer.Ordinal).Count() != registered.Length)
            throw new ArgumentException("Embedding providers must have distinct IDs and model identities.", nameof(providers));

        _library = library;
        _logger = logger;
        _indexDirectory = Path.Combine(dataDirectory, "chat-playground", "indexes");
        _providers = registered.ToDictionary(provider => provider.Descriptor.Id, StringComparer.Ordinal);
        SearchModes = [ChatSearchDescriptor.Contains, .. registered.Select(provider => provider.Descriptor)];
    }

    public IReadOnlyList<ChatSearchDescriptor> SearchModes { get; }

    /// <summary>Searches using the selected backend, building only that backend's missing index.</summary>
    public async Task<ChatSearchResults> SearchAsync(
        string query, string searchModeId, CancellationToken cancellationToken = default,
        IProgress<ChatSearchProgress>? progress = null)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            query = query.Trim();
            var (modelKey, generator) = GetProvider(searchModeId);
            var notices = new List<string>();

            var chats = _library.ListChats().Where(chat => chat.InteractionCount > 0).ToArray();
            var indexes = new List<(SavedChat Chat, StoredIndex Index)>(chats.Length);
            for (var i = 0; i < chats.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var chat = chats[i];
                var completed = i;
                indexes.Add((chat, await EnsureIndexAsync(
                    chat, modelKey, generator, notices, cancellationToken,
                    (chunks, total) => progress?.Report(new ChatSearchProgress(
                        completed, chats.Length, chat.Title, chunks, total, IsIndexing: true)))
                    .ConfigureAwait(false)));
                progress?.Report(new ChatSearchProgress(
                    i + 1, chats.Length, chat.Title, 0, 0, IsIndexing: false));
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
                    var completed = i;
                    indexes[i] = (chat, await EnsureIndexAsync(
                        chat, modelKey, generator, notices, cancellationToken,
                        (chunks, total) => progress?.Report(new ChatSearchProgress(
                            completed, chats.Length, chat.Title, chunks, total, IsIndexing: true)),
                        rebuild: true)
                        .ConfigureAwait(false));
                    progress?.Report(new ChatSearchProgress(
                        i + 1, chats.Length, chat.Title, 0, 0, IsIndexing: false));
                }
            }

            var hits = new List<ChatSearchHit>();
            foreach (var (chat, index) in indexes)
            {
                var titleMatches = query.Length > 0 &&
                    chat.Title.Contains(query, StringComparison.OrdinalIgnoreCase);
                var bestScore = titleMatches ? 0.5f : float.NegativeInfinity;
                var snippet = index.Chunks.LastOrDefault(chunk => !chunk.IsUser) ??
                    index.Chunks.FirstOrDefault();

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
                        $"{(snippet.IsUser ? "You" : "Assistant")}: {Excerpt(snippet.Text, query)}";
                    hits.Add(new ChatSearchHit(chat.Id, chat.Title, excerpt,
                        chat.InteractionCount, chat.UpdatedAtUtc,
                        query.Length == 0 ? 0 : bestScore));
                }
            }

            return new ChatSearchResults(
                hits.OrderByDescending(hit => hit.Score).ThenByDescending(hit => hit.UpdatedAtUtc)
                    .Take(40).ToArray(),
                generator is not null && query.Length > 0,
                notices.Count == 0 ? null : string.Join(" ", notices.Distinct()));
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string Excerpt(string text, string query)
    {
        if (text.Length <= MaxSnippetCharacters)
            return text;
        var match = query.Length == 0 ? -1 : text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        var start = match < 0 ? 0 : Math.Min(Math.Max(0, match - 40), text.Length - MaxSnippetCharacters);
        return (start == 0 ? string.Empty : "...") +
            text.Substring(start, MaxSnippetCharacters) +
            (start + MaxSnippetCharacters == text.Length ? string.Empty : "...");
    }

    private (string ModelKey, IEmbeddingGenerator<string, Embedding<float>>? Generator) GetProvider(
        string searchModeId)
    {
        if (searchModeId == ChatSearchDescriptor.ContainsId)
            return (ChatSearchDescriptor.Contains.IndexIdentity, null);
        if (!_providers.TryGetValue(searchModeId, out var provider))
            throw new ArgumentException("The selected search method is not registered.", nameof(searchModeId));
        if (!_generators.TryGetValue(searchModeId, out var generator))
        {
            generator = provider.CreateGenerator()
                ?? throw new InvalidOperationException(
                    $"The {provider.Descriptor.DisplayName} embedding provider returned no generator.");
            _generators.Add(searchModeId, generator);
        }
        return (provider.Descriptor.IndexIdentity, generator);
    }

    private async Task<StoredIndex> EnsureIndexAsync(
        SavedChat chat, string modelKey, IEmbeddingGenerator<string, Embedding<float>>? generator,
        List<string> notices, CancellationToken cancellationToken,
        Action<int, int>? indexProgress = null, bool rebuild = false)
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
            var recording = _library.ReadChat(chat.Id);
            if (recording.Interactions.Count != chat.InteractionCount)
                throw new InvalidDataException($"The chat library metadata for {chat.Title} is out of date.");
            return Extract(recording, index.InteractionCount, generator is not null);
        }, cancellationToken).ConfigureAwait(false);
        indexProgress?.Invoke(0, chunks.Count);
        var updated = new StoredIndex
        {
            ModelKey = key,
            InteractionCount = chat.InteractionCount,
            Dimension = index.Dimension,
            Chunks = [.. index.Chunks],
        };
        if (generator is not null)
        {
            var indexed = 0;
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
                indexed += batch.Length;
                indexProgress?.Invoke(indexed, chunks.Count);
            }
        }

        updated.Chunks.AddRange(chunks);
        await Task.Run(() =>
            AtomicFile.WriteAllText(path, JsonSerializer.Serialize(updated)), cancellationToken)
            .ConfigureAwait(false);
        _cache[cacheKey] = updated;
        if (generator is null)
            indexProgress?.Invoke(chunks.Count, chunks.Count);
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

    private static List<SearchChunk> Extract(ChatRecording recording, int start, bool forEmbeddings)
    {
        var chunks = new List<SearchChunk>();
        for (var turn = start; turn < recording.Interactions.Count; turn++)
        {
            var interaction = recording.Interactions[turn];
            var alreadyIndexed = new Dictionary<(bool IsUser, string Text), int>();
            if (turn > 0)
            {
                foreach (var text in RequestTexts(recording.Interactions[turn - 1]))
                    Count(alreadyIndexed, text);
                foreach (var text in AssistantTexts(recording.Interactions[turn - 1]))
                    Count(alreadyIndexed, (false, text));
            }

            var seenInRequest = new Dictionary<(bool IsUser, string Text), int>();
            foreach (var text in RequestTexts(interaction))
            {
                if (Count(seenInRequest, text) > alreadyIndexed.GetValueOrDefault(text))
                    AddText(text.Text, text.IsUser);
            }
            foreach (var text in AssistantTexts(interaction))
                AddText(text, isUser: false);

            void AddText(string text, bool isUser)
            {
                foreach (var part in forEmbeddings ? SplitText(text) : [text])
                    chunks.Add(new SearchChunk { Turn = turn, IsUser = isUser, Text = part });
            }
        }
        return chunks;
    }

    private static int Count(
        Dictionary<(bool IsUser, string Text), int> counts, (bool IsUser, string Text) message)
    {
        var count = counts.GetValueOrDefault(message) + 1;
        counts[message] = count;
        return count;
    }

    private static IEnumerable<(bool IsUser, string Text)> RequestTexts(RecordedInteraction interaction)
    {
        foreach (var message in ChatRecordingSerializer.ReadRequestMessages(interaction.Request))
        {
            if (message.Role != ChatRole.User && message.Role != ChatRole.Assistant)
                continue;
            var text = NormalizeText(message.Role == ChatRole.User
                ? string.Join(" ", message.Contents.OfType<TextContent>().Select(content => content.Text))
                : string.Concat(message.Contents.OfType<TextContent>().Select(content => content.Text)));
            if (text.Length > 0)
                yield return (message.Role == ChatRole.User, text);
        }
    }

#pragma warning disable MEAI001 // Image-tool boundaries must not merge unrelated assistant messages.
    private static IEnumerable<string> AssistantTexts(RecordedInteraction interaction)
    {
        if (!interaction.IsStreaming)
        {
            foreach (var message in ChatRecordingSerializer.ReadResponse(interaction.Response!).Messages)
            {
                if (message.Role != ChatRole.Assistant)
                    continue;
                var text = NormalizeText(string.Concat(
                    message.Contents.OfType<TextContent>().Select(content => content.Text)));
                if (text.Length > 0)
                    yield return text;
            }
            yield break;
        }

        var current = new StringBuilder();
        foreach (var update in interaction.Updates.Select(ChatRecordingSerializer.ReadUpdate))
        {
            foreach (var content in update.Contents)
            {
                if (content is TextContent text &&
                    (update.Role is null || update.Role == ChatRole.Assistant))
                    current.Append(text.Text);
                else if (content is FunctionCallContent or FunctionResultContent or
                    ImageGenerationToolCallContent || update.Role == ChatRole.Tool ||
                    update.Role == ChatRole.System || update.Role == ChatRole.User)
                {
                    var message = NormalizeText(current.ToString());
                    if (message.Length > 0)
                        yield return message;
                    current.Clear();
                }
            }
        }
        var last = NormalizeText(current.ToString());
        if (last.Length > 0)
            yield return last;
    }
#pragma warning restore MEAI001

    private static string NormalizeText(string value) =>
        Regex.Replace(value, @"\s+", " ").Trim();

    private static IEnumerable<string> SplitText(string normalized)
    {
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
        foreach (var generator in _generators.Values.Distinct<IEmbeddingGenerator<string, Embedding<float>>>(ReferenceEqualityComparer.Instance))
            generator.Dispose();
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
