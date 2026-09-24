using System.Text.Json.Nodes;
using AIExtensions.Sample.ChatPlayground.Features.Recording;
using AIExtensions.Sample.ChatPlayground.Features.Search;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.Maui.AI.Chat.Tests;

public sealed class ChatSearchServiceTests
{
    [Fact]
    public async Task BrowsingSavedChats_ShowsAssistantExcerptInsteadOfRepeatingTitle()
    {
        using var directory = new SearchDirectory();
        var recording = directory.CreateRecording();
        await using (var input = File.OpenRead(FixturePath("no-tools.json")))
            await recording.LoadFileAsync(input);

        using var search = directory.CreateSearch(recording);
        var result = await search.SearchAsync(string.Empty, semantic: false, useAzure: false);

        var hit = Assert.Single(result.Hits);
        Assert.StartsWith("Reply with exactly", hit.Title);
        Assert.Equal("Assistant: cobalt", hit.Snippet);
    }

    [Fact]
    public async Task SearchAsync_OnlyIndexesNewTurnText_NotPreviousHistoryOrImageBytes()
    {
        using var directory = new SearchDirectory();
        var recording = directory.CreateRecording();
        await using (var input = File.OpenRead(FixturePath("multi-turn-image-in-out.json")))
            await recording.LoadFileAsync(input);

        var generator = new TestEmbeddings(TextVector);
        using var search = directory.CreateSearch(recording, local: () => generator);
        await search.IndexChatAsync(recording.ActiveChatId, useAzure: false);

        Assert.Single(generator.Inputs, text => text.Contains("Use image generation to edit"));
        Assert.DoesNotContain(generator.Inputs, text => text.Contains("data:image", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(generator.Inputs, text => text.Contains("Describing the generated image"));
        Assert.All(generator.Inputs, text => Assert.InRange(text.Length, 1, 360));
    }

    [Fact]
    public async Task SearchAsync_FindsSemanticMatchAndReusesPersistedVectors()
    {
        using var directory = new SearchDirectory();
        var recording = directory.CreateRecording();
        await using (var input = File.OpenRead(FixturePath("no-tools.json")))
            await recording.LoadFileAsync(input);

        var generator = new TestEmbeddings(TextVector);
        using (var search = directory.CreateSearch(recording, local: () => generator))
        {
            Assert.Empty((await search.SearchAsync("pigment", semantic: false, useAzure: false)).Hits);
            var semantic = await search.SearchAsync("pigment", semantic: true, useAzure: false);
            Assert.True(semantic.IsSemantic);
            Assert.Equal(recording.ActiveChatId, Assert.Single(semantic.Hits).Id);
            Assert.Contains(generator.Inputs, text => text.Contains("cobalt"));
        }

        var restarted = new TestEmbeddings(TextVector);
        using var reloaded = directory.CreateSearch(directory.CreateRecording(), local: () => restarted);
        Assert.Single((await reloaded.SearchAsync("pigment", semantic: true, useAzure: false)).Hits);
        Assert.Equal(["pigment"], restarted.Inputs);
    }

    [Fact]
    public async Task CloudEmbeddings_RequireExplicitSelectionAndNeverReadImageBytes()
    {
        using var directory = new SearchDirectory();
        var recording = directory.CreateRecording();
        await using (var input = File.OpenRead(FixturePath("multi-turn-image-in-out.json")))
            await recording.LoadFileAsync(input);

        var azure = new TestEmbeddings(TextVector);
        using var search = directory.CreateSearch(recording, azure: () => azure);
        var privateSearch = await search.SearchAsync("watercolor", semantic: true, useAzure: false);
        Assert.False(privateSearch.IsSemantic);
        Assert.Contains("unavailable", privateSearch.Notice);
        Assert.Empty(azure.Inputs);

        var consented = await search.SearchAsync("watercolor", semantic: true, useAzure: true);
        Assert.True(consented.IsSemantic);
        Assert.NotEmpty(azure.Inputs);
        Assert.DoesNotContain(azure.Inputs, text => text.Contains("data:image", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(azure.Inputs, text => text.Contains("Describing the generated image"));
    }

    [Fact]
    public async Task DamagedIndex_IsRebuiltWithVisibleNotice_WithoutChangingRecording()
    {
        using var directory = new SearchDirectory();
        var recording = directory.CreateRecording();
        await using (var input = File.OpenRead(FixturePath("no-tools.json")))
            await recording.LoadFileAsync(input);
        var original = File.ReadAllBytes(recording.AutosavePath);

        using (var search = directory.CreateSearch(recording, local: () => new TestEmbeddings(TextVector)))
            await search.SearchAsync("pigment", semantic: true, useAzure: false);

        var indexPath = Assert.Single(Directory.GetFiles(directory.IndexDirectory,
            recording.ActiveChatId + ".json", SearchOption.AllDirectories));
        File.WriteAllText(indexPath, "{ damaged");

        using var restarted = directory.CreateSearch(directory.CreateRecording(),
            local: () => new TestEmbeddings(TextVector));
        var result = await restarted.SearchAsync("pigment", semantic: true, useAzure: false);
        Assert.Single(result.Hits);
        Assert.Contains("Rebuilt a damaged search index", result.Notice);
        Assert.Equal(original, File.ReadAllBytes(recording.AutosavePath));
    }

    [Fact]
    public async Task IndexWithNullChunk_IsRebuiltWithoutChangingRecording()
    {
        using var directory = new SearchDirectory();
        var recording = directory.CreateRecording();
        await using (var input = File.OpenRead(FixturePath("no-tools.json")))
            await recording.LoadFileAsync(input);
        var original = File.ReadAllBytes(recording.AutosavePath);

        using (var search = directory.CreateSearch(recording, local: () => new TestEmbeddings(TextVector)))
            await search.SearchAsync("pigment", semantic: true, useAzure: false);

        var indexPath = Assert.Single(Directory.GetFiles(directory.IndexDirectory,
            recording.ActiveChatId + ".json", SearchOption.AllDirectories));
        var index = JsonNode.Parse(File.ReadAllText(indexPath))!;
        index["Chunks"]!.AsArray().Add(null);
        File.WriteAllText(indexPath, index.ToJsonString());

        using var restarted = directory.CreateSearch(directory.CreateRecording(),
            local: () => new TestEmbeddings(TextVector));
        var result = await restarted.SearchAsync("pigment", semantic: true, useAzure: false);
        Assert.Single(result.Hits);
        Assert.Contains("Rebuilt a damaged search index", result.Notice);
        Assert.Equal(original, File.ReadAllBytes(recording.AutosavePath));
    }

    [Fact]
    public async Task EmptyEmbedding_LeavesRecordingIntactAndReportsError()
    {
        using var directory = new SearchDirectory();
        var recording = directory.CreateRecording();
        await using (var input = File.OpenRead(FixturePath("no-tools.json")))
            await recording.LoadFileAsync(input);

        using var search = directory.CreateSearch(recording,
            local: () => new TestEmbeddings(_ => [0f, 0f]));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            search.IndexChatAsync(recording.ActiveChatId, useAzure: false));
        Assert.Equal(1, recording.InteractionCount);
        Assert.Single((await search.SearchAsync("cobalt", semantic: false, useAzure: false)).Hits);
    }

    [Fact]
    public async Task MissingEmbedding_ReportsFailureInsteadOfIndexingInvalidAppleVector()
    {
        using var directory = new SearchDirectory();
        var recording = directory.CreateRecording();
        await using (var input = File.OpenRead(FixturePath("no-tools.json")))
            await recording.LoadFileAsync(input);

        using var search = directory.CreateSearch(recording,
            local: () => new TestEmbeddings(_ => []));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            search.IndexChatAsync(recording.ActiveChatId, useAzure: false));
        Assert.Single(recording.ListChats(), chat => chat.InteractionCount == 1);
    }

    [Fact]
    public async Task NewTurn_AppendsOnlyItsOwnVectors()
    {
        using var directory = new SearchDirectory();
        var recording = directory.CreateRecording();
        await using (var input = File.OpenRead(FixturePath("no-tools.json")))
            await recording.LoadFileAsync(input);

        var generator = new TestEmbeddings(TextVector);
        using var search = directory.CreateSearch(recording, local: () => generator);
        await search.IndexChatAsync(recording.ActiveChatId, useAzure: false);
        var originalInputs = generator.Inputs.Count;

        recording.AddResponse(
            ChatRecordingSerializer.Request([new ChatMessage(ChatRole.User, "Describe violet")], null),
            new ChatResponse([new ChatMessage(ChatRole.Assistant, "Violet is a color.")]));
        await search.IndexChatAsync(recording.ActiveChatId, useAzure: false);

        Assert.Equal(originalInputs + 2, generator.Inputs.Count);
        Assert.Single(generator.Inputs, text => text.StartsWith("Reply with exactly", StringComparison.Ordinal));
        Assert.Contains(generator.Inputs, text => text.Contains("Violet is a color."));
    }

    [Fact]
    public async Task ChangedEmbeddingDimensions_RebuildVectorsInsteadOfComparingDifferentSpaces()
    {
        using var directory = new SearchDirectory();
        var recording = directory.CreateRecording();
        await using (var input = File.OpenRead(FixturePath("no-tools.json")))
            await recording.LoadFileAsync(input);

        using (var search = directory.CreateSearch(recording, local: () => new TestEmbeddings(TextVector)))
            await search.SearchAsync("pigment", semantic: true, useAzure: false);

        var replacement = new TestEmbeddings(text => [.. TextVector(text), 0.5f]);
        using var restarted = directory.CreateSearch(directory.CreateRecording(),
            local: () => replacement);
        var result = await restarted.SearchAsync("pigment", semantic: true, useAzure: false);

        Assert.Single(result.Hits);
        Assert.Contains("Rebuilt the outdated search vectors", result.Notice);
        Assert.Contains(replacement.Inputs, text => text.Contains("cobalt"));
    }

    private static float[] TextVector(string text) =>
        text.Contains("cobalt", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("pigment", StringComparison.OrdinalIgnoreCase)
            ? [1f, 0f]
            : [0f, 1f];

    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", name);

    private sealed class TestEmbeddings(Func<string, float[]> embed)
        : IEmbeddingGenerator<string, Embedding<float>>
    {
        public List<string> Inputs { get; } = [];

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values, EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var result = new GeneratedEmbeddings<Embedding<float>>();
            foreach (var text in values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Inputs.Add(text);
                result.Add(new Embedding<float>(embed(text)));
            }
            return Task.FromResult(result);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class SearchDirectory : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "chat-search-" + Guid.NewGuid().ToString("N"));

        public string DataDirectory => Path.Combine(_root, "data");
        public string IndexDirectory => Path.Combine(DataDirectory, "chat-playground", "indexes");

        public ChatRecordingService CreateRecording() =>
            new(NullLogger<ChatRecordingService>.Instance, DataDirectory, Path.Combine(_root, "cache"));

        public ChatSearchService CreateSearch(
            ChatRecordingService recording,
            Func<IEmbeddingGenerator<string, Embedding<float>>>? local = null,
            Func<IEmbeddingGenerator<string, Embedding<float>>>? azure = null) =>
            new(recording, NullLogger<ChatSearchService>.Instance, DataDirectory,
                local, local is null ? null : "apple/natural-language/en/test",
                azure, azure is null ? null : "azure/text-embedding-3-small/test");

        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
    }
}
