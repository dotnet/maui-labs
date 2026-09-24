using System.Text;
using System.Text.Json.Nodes;
using AIExtensions.Sample.ChatPlayground.Features.Chat;
using AIExtensions.Sample.ChatPlayground.Features.Embeddings;
using AIExtensions.Sample.ChatPlayground.Features.Recording;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.Maui.AI.Chat.Tests;

public sealed class DocumentSearchServiceTests
{
    [Fact]
    public async Task ImportMarkdown_PersistsDocumentsWithoutContactingAnEmbeddingModel()
    {
        using var directory = new DocumentDirectory();
        using (var store = directory.CreateStore())
        {
            var imported = await ImportAsync(store, "notes.md", "# Notes\nA blue bird visits the meadow.");
            Assert.Equal("notes.md", imported.Name);
            Assert.Equal(38, imported.CharacterCount);
        }

        using var restored = directory.CreateStore();
        var document = Assert.Single(await restored.ListAsync());
        Assert.Equal("notes.md", document.Name);
        Assert.False(Directory.Exists(directory.IndexDirectory));
    }

    [Theory]
    [InlineData("photo.png", "image")]
    [InlineData("empty.md", "   ")]
    public async Task ImportUnsupportedOrEmptyContent_LeavesTheDocumentStoreUnchanged(string name, string text)
    {
        using var directory = new DocumentDirectory();
        using var store = directory.CreateStore();

        await Assert.ThrowsAsync<InvalidDataException>(() => ImportAsync(store, name, text));

        Assert.Empty(await store.ListAsync());
    }

    [Fact]
    public async Task ImportInvalidUtf8OrOversizedText_RejectsTheFile()
    {
        using var directory = new DocumentDirectory();
        using var store = directory.CreateStore();
        using var invalid = new MemoryStream([0xC3, 0x28]);
        await Assert.ThrowsAnyAsync<ArgumentException>(() => store.ImportAsync("bad.txt", invalid));
        using var large = new MemoryStream(new byte[256 * 1024 + 1]);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ImportAsync("large.md", large));
        Assert.Empty(await store.ListAsync());
    }

    [Fact]
    public async Task Search_RequiresExplicitIndexWithoutCallingTheGeneratorOrReadingChats()
    {
        using var directory = new DocumentDirectory();
        using var store = directory.CreateStore();
        await ImportAsync(store, "blue.md", "A blue bird lives near the river.");
        var generator = new TestEmbeddings(_ => [1f, 0f]);
        using var search = directory.CreateSearch(store);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            search.SearchAsync("blue", generator, "apple/english"));

        Assert.Contains("Index documents", error.Message);
        Assert.Empty(generator.Inputs);
        Assert.False(Directory.Exists(directory.IndexDirectory));
    }

    [Fact]
    public async Task Ranking_UsesOnlyTheSelectedGeneratorAndItsCosineSimilarity()
    {
        using var directory = new DocumentDirectory();
        using var store = directory.CreateStore();
        var blue = await ImportAsync(store, "blue.md", "The blue bird rests near a stream.");
        var red = await ImportAsync(store, "red.md", "A red fox visits the garden.");
        var local = new TestEmbeddings(text =>
            text == "blue bird" || text.Contains("red fox", StringComparison.OrdinalIgnoreCase)
                ? [1f, 0f] : [0f, 1f]);
        var cloud = new TestEmbeddings(text =>
            text == "blue bird" || text.Contains("blue bird", StringComparison.OrdinalIgnoreCase)
                ? [1f, 0f] : [0f, 1f]);
        using var search = directory.CreateSearch(store);
        await search.IndexDocumentsAsync(local, "apple/english");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            search.SearchAsync("blue bird", cloud, "azure/v2"));
        await search.IndexDocumentsAsync(cloud, "azure/v2");

        Assert.Equal(red.Id, (await search.SearchAsync("blue bird", local, "apple/english")).Hits[0].Id);
        Assert.Equal(blue.Id, (await search.SearchAsync("blue bird", cloud, "azure/v2")).Hits[0].Id);
        Assert.Equal(2, Directory.GetDirectories(directory.IndexDirectory).Length);
        Assert.All(local.Inputs, text => Assert.DoesNotContain("chat-client-playground", text));
    }

    [Fact]
    public async Task ModelIdentityAndDimensions_KeepArbitraryVectorSpacesSeparate()
    {
        using var directory = new DocumentDirectory();
        using var store = directory.CreateStore();
        await ImportAsync(store, "notes.md", "blue river");
        var generator = new TestEmbeddings(_ => [1f, 0f]);
        using var search = directory.CreateSearch(store);
        await search.IndexDocumentsAsync(generator, "model/dimensions/3");
        await search.IndexDocumentsAsync(generator, "model", dimensions: 3);

        Assert.Equal(2, Directory.GetDirectories(directory.IndexDirectory).Length);
        Assert.Equal([null, 3], generator.RequestedDimensions);
    }

    [Fact]
    public async Task NewDocument_RequiresReindexButOldIndexedDocumentsRemainSearchable()
    {
        using var directory = new DocumentDirectory();
        using var store = directory.CreateStore();
        var first = await ImportAsync(store, "first.md", "blue bird");
        var generator = new TestEmbeddings(_ => [1f, 0f]);
        using var search = directory.CreateSearch(store);
        await search.IndexDocumentsAsync(generator, "apple/english");
        var inputs = generator.Inputs.Count;
        await ImportAsync(store, "second.txt", "orange fox");

        var result = await search.SearchAsync("bird", generator, "apple/english");

        Assert.Equal(first.Id, Assert.Single(result.Hits).Id);
        Assert.Contains("need indexing", result.Notice);
        Assert.Equal(inputs + 1, generator.Inputs.Count);
        var updated = await search.IndexDocumentsAsync(generator, "apple/english");
        Assert.Equal(1, updated.UpdatedDocuments);
        Assert.Equal(inputs + 2, generator.Inputs.Count);
    }

    [Fact]
    public async Task ClearIndex_RemovesOnlyOneModelAndPreservesDocumentsAndChat()
    {
        using var directory = new DocumentDirectory();
        var chat = new ChatSessionService(
            NullLogger<ChatSessionService>.Instance, directory.DataDirectory, directory.CacheDirectory);
        chat.AddResponse(
            ChatRecordingSerializer.Request([new ChatMessage(ChatRole.User, "Private chat")], null),
            new ChatResponse([new ChatMessage(ChatRole.Assistant, "Private reply")]));
        var chatBytes = File.ReadAllBytes(chat.AutosavePath);
        using var store = directory.CreateStore();
        await ImportAsync(store, "notes.md", "A blue bird.");
        using var search = directory.CreateSearch(store);
        var generator = new TestEmbeddings(_ => [1f, 0f]);
        await search.IndexDocumentsAsync(generator, "apple/english");
        await search.IndexDocumentsAsync(generator, "azure/v2");

        Assert.True(await search.ClearIndexAsync("apple/english"));
        Assert.False(await search.ClearIndexAsync("apple/english"));
        Assert.Single(await store.ListAsync());
        Assert.Equal(chatBytes, File.ReadAllBytes(chat.AutosavePath));
        Assert.Single((await search.SearchAsync("bird", generator, "azure/v2")).Hits);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            search.SearchAsync("bird", generator, "apple/english"));
    }

    [Fact]
    public async Task ClearingDocuments_RemovesDocumentsAndAllIndexesButLeavesChatUnchanged()
    {
        using var directory = new DocumentDirectory();
        var chat = new ChatSessionService(
            NullLogger<ChatSessionService>.Instance, directory.DataDirectory, directory.CacheDirectory);
        chat.AddResponse(
            ChatRecordingSerializer.Request([new ChatMessage(ChatRole.User, "Keep chat")], null),
            new ChatResponse([new ChatMessage(ChatRole.Assistant, "Saved")]));
        var saved = File.ReadAllBytes(chat.AutosavePath);
        using var store = directory.CreateStore();
        await ImportAsync(store, "notes.txt", "Moonlight at the observatory.");
        using var search = directory.CreateSearch(store);
        await search.IndexDocumentsAsync(new TestEmbeddings(_ => [1f, 0f]), "apple/english");

        await search.ClearDocumentsAsync();

        Assert.Empty(await store.ListAsync());
        Assert.False(Directory.Exists(directory.IndexDirectory));
        Assert.Equal(saved, File.ReadAllBytes(chat.AutosavePath));
    }

    [Fact]
    public async Task DamagedIndex_ReportsErrorAndIsRebuiltOnlyOnExplicitIndex()
    {
        using var directory = new DocumentDirectory();
        using (var store = directory.CreateStore())
        using (var search = directory.CreateSearch(store))
        {
            await ImportAsync(store, "notes.md", "A green valley.");
            await search.IndexDocumentsAsync(new TestEmbeddings(_ => [1f, 0f]), "apple/english");
        }
        var path = Assert.Single(Directory.GetFiles(directory.IndexDirectory, "*.json", SearchOption.AllDirectories));
        File.WriteAllText(path, "{ invalid");
        using var restored = directory.CreateStore();
        using var restartedSearch = directory.CreateSearch(restored);
        var generator = new TestEmbeddings(_ => [1f, 0f]);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            restartedSearch.SearchAsync("valley", generator, "apple/english"));
        Assert.Contains("Index documents", error.Message);
        Assert.Empty(generator.Inputs);
        var reindexed = await restartedSearch.IndexDocumentsAsync(generator, "apple/english");
        Assert.Contains("Rebuilt a damaged index", reindexed.Notice);
        Assert.Single((await restartedSearch.SearchAsync("valley", generator, "apple/english")).Hits);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidOrEmptyVectors_NeverPersistAnIndex(bool empty)
    {
        using var directory = new DocumentDirectory();
        using var store = directory.CreateStore();
        await ImportAsync(store, "notes.txt", "A green valley.");
        using var search = directory.CreateSearch(store);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            search.IndexDocumentsAsync(new TestEmbeddings(_ => empty ? [] : [0f, 0f]), "apple/english"));

        Assert.Empty(Directory.Exists(directory.IndexDirectory)
            ? Directory.GetFiles(directory.IndexDirectory, "*.json", SearchOption.AllDirectories)
            : []);
        Assert.Single(await store.ListAsync());
    }

    [Fact]
    public async Task CancelledIndex_DoesNotPersistPartialVectors()
    {
        using var directory = new DocumentDirectory();
        using var store = directory.CreateStore();
        await ImportAsync(store, "notes.md", "A green valley.");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocked = new TestEmbeddings(_ => [1f, 0f], async token =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        });
        using var search = directory.CreateSearch(store);
        using var cancellation = new CancellationTokenSource();
        var task = search.IndexDocumentsAsync(blocked, "apple/english", cancellationToken: cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Empty(Directory.Exists(directory.IndexDirectory)
            ? Directory.GetFiles(directory.IndexDirectory, "*.json", SearchOption.AllDirectories)
            : []);
        await search.IndexDocumentsAsync(new TestEmbeddings(_ => [1f, 0f]), "apple/english");
        Assert.Single(Directory.GetFiles(directory.IndexDirectory, "*.json", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ChangedDocumentText_RequiresAnExplicitReindex()
    {
        using var directory = new DocumentDirectory();
        using (var store = directory.CreateStore())
        using (var search = directory.CreateSearch(store))
        {
            await ImportAsync(store, "notes.md", "A green valley.");
            await search.IndexDocumentsAsync(new TestEmbeddings(_ => [1f, 0f]), "apple/english");
        }
        var path = Assert.Single(Directory.GetFiles(directory.DocumentsDirectory, "*.json"));
        var document = JsonNode.Parse(File.ReadAllText(path))!;
        document["Text"] = "A blue meadow.";
        File.WriteAllText(path, document.ToJsonString());
        using var restored = directory.CreateStore();
        using var searchRestored = directory.CreateSearch(restored);
        var generator = new TestEmbeddings(_ => [1f, 0f]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            searchRestored.SearchAsync("blue", generator, "apple/english"));
        Assert.Empty(generator.Inputs);
        Assert.Equal(1, (await searchRestored.IndexDocumentsAsync(generator, "apple/english")).UpdatedDocuments);
        Assert.Single((await searchRestored.SearchAsync("blue", generator, "apple/english")).Hits);
    }

    private static async Task<ImportedDocument> ImportAsync(DocumentStore store, string name, string text)
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(text));
        return await store.ImportAsync(name, input);
    }

    private sealed class DocumentDirectory : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "document-search-" + Guid.NewGuid().ToString("N"));
        public string DataDirectory => Path.Combine(_root, "data");
        public string CacheDirectory => Path.Combine(_root, "cache");
        public string IndexDirectory => Path.Combine(DataDirectory, "embedding-playground", "indexes");
        public string DocumentsDirectory => Path.Combine(DataDirectory, "embedding-playground", "documents");

        public DocumentStore CreateStore() => new(DataDirectory);
        public DocumentSearchService CreateSearch(DocumentStore store) =>
            new(store, NullLogger<DocumentSearchService>.Instance, DataDirectory);

        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
    }
}
