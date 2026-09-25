using System.Text;
using AIExtensions.Sample.ChatPlayground.Features.Embeddings.Services;
using AIExtensions.Sample.ChatPlayground.Features.Embeddings.ViewModels;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.Maui.AI.Chat.Tests;

public sealed class DocumentViewModelTests
{
    [Fact]
    public async Task ImportIndexSearchAndClear_UseOneSelectedGeneratorAndRemainIndependentOfChat()
    {
        using var directory = new DocumentDirectory();
        using var store = directory.CreateStore();
        using var search = directory.CreateSearch(store);
        var generator = new TestEmbeddings(_ => [1f, 0f]);
        var described = Describe("apple", "apple/english", generator);
        var settings = new EmbeddingSettingsViewModel(search, [described]) { Dimensions = "3" };
        var playground = new EmbeddingPlaygroundViewModel(store, search, settings) { Query = "blue bird" };
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("# Field notes\nBlue birds sing."));
        await playground.ImportDocumentAsync("notes.md", input);

        Assert.Single(playground.Documents);
        Assert.False(playground.ShowingSearchResults);
        Assert.Empty(generator.Inputs);
        await playground.Search.ExecuteAsync(null);
        Assert.Contains("Index documents", playground.StatusMessage);
        Assert.Empty(generator.Inputs);

        await settings.IndexDocuments.ExecuteAsync(null);
        Assert.Contains("1 document indexed", settings.StatusMessage);
        await playground.Search.ExecuteAsync(null);
        Assert.Single(playground.Results);
        Assert.True(playground.ShowingSearchResults);
        Assert.Contains("2-dimensional query", playground.StatusMessage);
        Assert.All(generator.RequestedDimensions, dimension => Assert.Equal(3, dimension));
        Assert.Same(described, settings.SelectedGenerator);

        await settings.ClearIndex.ExecuteAsync(null);
        Assert.Single(playground.Documents);
        Assert.Empty(playground.Results);
        Assert.False(playground.ShowingSearchResults);
        await playground.Search.ExecuteAsync(null);
        Assert.Contains("Index documents", playground.StatusMessage);
        Assert.False(playground.ShowingSearchResults);
        await settings.ClearDocuments.ExecuteAsync(null);
        Assert.Empty(playground.Documents);
        Assert.False(playground.ShowingSearchResults);
        Assert.Empty(await store.ListAsync());
    }

    [Fact]
    public async Task NoGenerator_AllowsImportAndClearButDoesNotPretendSearchWorks()
    {
        using var directory = new DocumentDirectory();
        using var store = directory.CreateStore();
        using var search = directory.CreateSearch(store);
        var settings = new EmbeddingSettingsViewModel(search, []);
        var playground = new EmbeddingPlaygroundViewModel(store, search, settings) { Query = "sunrise" };
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("Sunrise over the meadow."));

        await playground.ImportDocumentAsync("notes.txt", input);

        Assert.Single(playground.Documents);
        Assert.False(playground.Search.CanExecute(null));
        Assert.False(settings.IndexDocuments.CanExecute(null));
        Assert.True(settings.ClearDocuments.CanExecute(null));
        await settings.ClearDocuments.ExecuteAsync(null);
        Assert.Empty(playground.Documents);
    }

    [Fact]
    public async Task InvalidDimensionsAndDocuments_ReportErrorsWithoutContactingModel()
    {
        using var directory = new DocumentDirectory();
        using var store = directory.CreateStore();
        using var search = directory.CreateSearch(store);
        var generator = new TestEmbeddings(_ => [1f, 0f]);
        var settings = new EmbeddingSettingsViewModel(search, [Describe("apple", "apple/english", generator)])
        {
            Dimensions = "invalid",
        };
        var playground = new EmbeddingPlaygroundViewModel(store, search, settings) { Query = "blue" };
        using var invalid = new MemoryStream(Encoding.UTF8.GetBytes("Not a document format"));
        await playground.ImportDocumentAsync("image.png", invalid);
        Assert.Contains("Import a Markdown", playground.StatusMessage);
        Assert.Empty(playground.Documents);

        using var valid = new MemoryStream(Encoding.UTF8.GetBytes("A blue bird."));
        await playground.ImportDocumentAsync("notes.md", valid);
        await settings.IndexDocuments.ExecuteAsync(null);
        await playground.Search.ExecuteAsync(null);

        Assert.Contains("Dimensions must be a positive integer", settings.StatusMessage);
        Assert.Contains("Dimensions must be a positive integer", playground.StatusMessage);
        Assert.Empty(generator.Inputs);
    }

    [Fact]
    public void Settings_RejectsDuplicateDescriptorsForTheSameModel()
    {
        using var directory = new DocumentDirectory();
        using var store = directory.CreateStore();
        using var search = directory.CreateSearch(store);

        Assert.Throws<ArgumentException>(() => new EmbeddingSettingsViewModel(search,
        [
            Describe("local", "apple/english", new TestEmbeddings(_ => [1f, 0f])),
            Describe("local-two", "apple/english", new TestEmbeddings(_ => [1f, 0f])),
        ]));
    }

    [Fact]
    public async Task IndexCancelButton_CancelsInFlightEmbeddingWithoutPersistingAnIndex()
    {
        using var directory = new DocumentDirectory();
        using var store = directory.CreateStore();
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("An on-device indexing test."));
        await store.ImportAsync("notes.md", input);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocked = new TestEmbeddings(_ => [1f, 0f], async token =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        });
        using var search = directory.CreateSearch(store);
        var settings = new EmbeddingSettingsViewModel(search, [Describe("apple", "apple/english", blocked)]);

        var indexing = settings.IndexDocuments.ExecuteAsync(null);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        settings.CancelIndex.Execute(null);
        await indexing.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("Indexing cancelled.", settings.StatusMessage);
        Assert.False(Directory.Exists(directory.IndexDirectory));
    }

    private static DescribedEmbeddingGenerator Describe(string id, string identity, TestEmbeddings generator) =>
        new(() => generator, new EmbeddingGeneratorDescriptor(id, id, "Test embedding provider", identity));

    private sealed class DocumentDirectory : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "document-vm-" + Guid.NewGuid().ToString("N"));
        public string IndexDirectory => Path.Combine(_root, "embedding-playground", "indexes");
        public DocumentStore CreateStore() => new(_root);
        public DocumentSearchService CreateSearch(DocumentStore store) =>
            new(store, NullLogger<DocumentSearchService>.Instance, _root);
        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
    }
}
