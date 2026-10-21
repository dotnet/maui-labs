using System.Text;
using AIExtensions.Sample.ChatPlayground;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.Maui.AI.Chat.Tests;

public sealed class DocumentViewModelTests
{
    [Fact]
    public async Task ImportIndexSearchAndClear_UseOneSelectedGeneratorAndRemainIndependentOfChat()
    {
        using var directory = new DocumentDirectory();
        using var diagnostics = new ChatDiagnostics();
        using var store = directory.CreateStore();
        using var search = directory.CreateSearch(store);
        var generator = new TestEmbeddings(_ => [1f, 0f]);
        var described = Describe("apple/english", generator);
        var settings = new EmbeddingSettingsViewModel(search, [described]) { Dimensions = "3" };
        var playground = new EmbeddingPlaygroundViewModel(store, search, settings, new(diagnostics)) { Query = "blue bird" };
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
    public async Task SavedIndex_RemainsSearchableAfterRestartWithSameGeneratorId()
    {
        const string generatorId = "apple-natural-language";
        using var diagnostics = new ChatDiagnostics();
        using var directory = new DocumentDirectory();
        using var store = directory.CreateStore();
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("A blue bird."));
        var document = await store.ImportAsync("notes.md", input);
        using (var originalSearch = directory.CreateSearch(store))
        {
            await originalSearch.IndexDocumentsAsync(new TestEmbeddings(_ => [1f, 0f]), generatorId);
        }

        using var restoredSearch = directory.CreateSearch(store);
        var generator = new TestEmbeddings(_ => [1f, 0f]);
        var settings = new EmbeddingSettingsViewModel(restoredSearch, [Describe(generatorId, generator)]);
        var playground = new EmbeddingPlaygroundViewModel(store, restoredSearch, settings, new(diagnostics)) { Query = "bird" };

        await playground.Search.ExecuteAsync(null);

        Assert.Equal(document.Id, Assert.Single(playground.Results).Id);
        Assert.Single(generator.Inputs);
        await settings.ClearIndex.ExecuteAsync(null);
        Assert.Empty(Directory.GetFiles(directory.IndexDirectory, "*.json", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task NoGenerator_AllowsImportAndClearButDoesNotPretendSearchWorks()
    {
        using var diagnostics = new ChatDiagnostics();
        using var directory = new DocumentDirectory();
        using var store = directory.CreateStore();
        using var search = directory.CreateSearch(store);
        var settings = new EmbeddingSettingsViewModel(search, []);
        var playground = new EmbeddingPlaygroundViewModel(store, search, settings, new(diagnostics)) { Query = "sunrise" };
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
        using var diagnostics = new ChatDiagnostics();
        using var directory = new DocumentDirectory();
        using var store = directory.CreateStore();
        using var search = directory.CreateSearch(store);
        var generator = new TestEmbeddings(_ => [1f, 0f]);
        var settings = new EmbeddingSettingsViewModel(search, [Describe("apple/english", generator)])
        {
            Dimensions = "invalid",
        };
        var playground = new EmbeddingPlaygroundViewModel(store, search, settings, new(diagnostics)) { Query = "blue" };
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
    public void Settings_RejectsDuplicateModelIds()
    {
        using var directory = new DocumentDirectory();
        using var store = directory.CreateStore();
        using var search = directory.CreateSearch(store);

        Assert.Throws<ArgumentException>(() => new EmbeddingSettingsViewModel(search,
        [
            Describe("apple/english", new TestEmbeddings(_ => [1f, 0f])),
            Describe("apple/english", new TestEmbeddings(_ => [1f, 0f])),
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
        var settings = new EmbeddingSettingsViewModel(search, [Describe("apple/english", blocked)]);

        var indexing = settings.IndexDocuments.ExecuteAsync(null);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        settings.CancelIndex.Execute(null);
        await indexing.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("Indexing cancelled.", settings.StatusMessage);
        Assert.False(Directory.Exists(directory.IndexDirectory));
    }

    [Fact]
    public async Task Search_SelectsFirstDocumentAndSelectionShowsOnlyThatDocumentsRankedFullChunks()
    {
        using var directory = new DocumentDirectory();
        using var diagnostics = new ChatDiagnostics();
        using var store = directory.CreateStore();
        using var search = directory.CreateSearch(store);
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("Weak. Strong. Same score."));
        var first = await store.ImportAsync("first.md", input);
        using var otherInput = new MemoryStream(Encoding.UTF8.GetBytes("Other. Different."));
        var second = await store.ImportAsync("second.md", otherInput);
        var generator = new TestEmbeddings(text => text is "query" or "Strong." or "Same score."
            ? [1f, 0f] : [0f, 1f]);
        var settings = new EmbeddingSettingsViewModel(search, [Describe("model", generator)])
        {
            ChunkingMode = DocumentChunkingMode.Sentences,
        };
        var playground = new EmbeddingPlaygroundViewModel(store, search, settings, new(diagnostics)) { Query = "query" };
        await settings.IndexDocuments.ExecuteAsync(null);

        await playground.Search.ExecuteAsync(null);

        Assert.Equal(first.Id, playground.SelectedResult?.Id);
        Assert.Same(playground.Results[0], playground.SelectedResult);
        Assert.Equal(["Strong.", "Same score.", "Weak."], playground.SelectedChunks.Select(chunk => chunk.Text));
        Assert.Equal([2, 3, 1], playground.SelectedChunks.Select(chunk => chunk.Ordinal));
        Assert.Contains("first.md", playground.ChunksHeading);
        playground.SelectedResult = playground.Results.Single(hit => hit.Id == second.Id);
        Assert.Equal(["Other.", "Different."], playground.SelectedChunks.Select(chunk => chunk.Text));
        Assert.All(playground.SelectedChunks, chunk => Assert.Equal(second.Id, chunk.DocumentId));
        playground.SelectedResult = null;
        Assert.Empty(playground.SelectedChunks);
        Assert.Equal("RANKED CHUNKS", playground.ChunksHeading);
    }

    [Theory]
    [InlineData("model")]
    [InlineData("dimensions")]
    [InlineData("mode")]
    [InlineData("size")]
    [InlineData("overlap")]
    public async Task ChangingConfiguration_ClearsSelectedDocumentAndChunks(string setting)
    {
        using var directory = new DocumentDirectory();
        using var diagnostics = new ChatDiagnostics();
        using var store = directory.CreateStore();
        using var search = directory.CreateSearch(store);
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("One. Two."));
        await store.ImportAsync("notes.md", input);
        var generator = new TestEmbeddings(_ => [1f, 0f]);
        var settings = new EmbeddingSettingsViewModel(search,
            [Describe("one", generator), Describe("two", generator)]);
        var playground = new EmbeddingPlaygroundViewModel(store, search, settings, new(diagnostics)) { Query = "query" };
        await settings.IndexDocuments.ExecuteAsync(null);
        await playground.Search.ExecuteAsync(null);
        Assert.NotEmpty(playground.SelectedChunks);

        switch (setting)
        {
            case "model": settings.SelectedOption = settings.Generators[1]; break;
            case "dimensions": settings.Dimensions = "3"; break;
            case "mode": settings.ChunkingMode = DocumentChunkingMode.Sentences; break;
            case "size": settings.ChunkSize = "100"; break;
            case "overlap": settings.ChunkOverlap = "10"; break;
        }

        Assert.Null(playground.SelectedResult);
        Assert.Empty(playground.SelectedChunks);
        Assert.Empty(playground.Results);
        Assert.False(playground.ShowingSearchResults);
    }

    [Fact]
    public async Task ImportAndIndexFailures_DoNotKeepStaleChunkDetails()
    {
        using var directory = new DocumentDirectory();
        using var diagnostics = new ChatDiagnostics();
        using var store = directory.CreateStore();
        using var search = directory.CreateSearch(store);
        var fail = false;
        var generator = new TestEmbeddings(_ => fail ? throw new InvalidOperationException("Model unavailable") : [1f, 0f]);
        var settings = new EmbeddingSettingsViewModel(search, [Describe("model", generator)]);
        var playground = new EmbeddingPlaygroundViewModel(store, search, settings, new(diagnostics)) { Query = "query" };
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("One."));
        await playground.ImportDocumentAsync("notes.md", input);
        await settings.IndexDocuments.ExecuteAsync(null);
        await playground.Search.ExecuteAsync(null);
        Assert.NotEmpty(playground.SelectedChunks);

        using var invalid = new MemoryStream(Encoding.UTF8.GetBytes("image"));
        await playground.ImportDocumentAsync("image.png", invalid);
        Assert.Contains("import failed", playground.StatusMessage);
        Assert.Null(playground.SelectedResult);
        Assert.Empty(playground.SelectedChunks);

        await playground.Search.ExecuteAsync(null);
        Assert.NotEmpty(playground.SelectedChunks);
        fail = true;
        await playground.Search.ExecuteAsync(null);
        Assert.Contains("Search failed", playground.StatusMessage);
        Assert.Null(playground.SelectedResult);
        Assert.Empty(playground.SelectedChunks);

        fail = false;
        await playground.Search.ExecuteAsync(null);
        using var newInput = new MemoryStream(Encoding.UTF8.GetBytes("Unindexed."));
        await store.ImportAsync("new.md", newInput);
        fail = true;
        await settings.IndexDocuments.ExecuteAsync(null);
        Assert.Contains("indexing failed", settings.StatusMessage);
        Assert.Null(playground.SelectedResult);
        Assert.Empty(playground.SelectedChunks);
        Assert.Empty(playground.Results);
    }

    [Fact]
    public async Task EmptySearch_HasNoSelectedDocumentOrChunks()
    {
        using var directory = new DocumentDirectory();
        using var diagnostics = new ChatDiagnostics();
        using var store = directory.CreateStore();
        using var search = directory.CreateSearch(store);
        var generator = new TestEmbeddings(_ => [1f, 0f]);
        var settings = new EmbeddingSettingsViewModel(search, [Describe("model", generator)]);
        var playground = new EmbeddingPlaygroundViewModel(store, search, settings, new(diagnostics)) { Query = "query" };

        await playground.Search.ExecuteAsync(null);

        Assert.True(playground.ShowingSearchResults);
        Assert.Null(playground.SelectedResult);
        Assert.Empty(playground.SelectedChunks);
        Assert.Empty(generator.Inputs);
    }

    [Fact]
    public async Task SampleLoading_IsExplicitAdditiveLocalAndStillRequiresExplicitIndexing()
    {
        using var directory = new DocumentDirectory();
        using var diagnostics = new ChatDiagnostics();
        using var store = directory.CreateStore();
        using var search = directory.CreateSearch(store);
        var generator = new TestEmbeddings(_ => [1f, 0f]);
        var settings = new EmbeddingSettingsViewModel(search, [Describe("model", generator)]);
        var playground = new EmbeddingPlaygroundViewModel(store, search, settings, new(diagnostics)) { Query = "ferry" };
        await playground.LoadDocumentsAsync();
        Assert.Empty(playground.Documents);
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("Keep my imported document."));
        await playground.ImportDocumentAsync("mine.txt", input);

        await playground.LoadSampleDocuments.ExecuteAsync(null);

        Assert.Equal(4, playground.Documents.Count);
        Assert.Contains(playground.Documents, document => document.Name == "mine.txt");
        Assert.Equal(3, EmbeddingSampleDocuments.All.Count);
        Assert.Equal(3, EmbeddingSampleDocuments.All.Select(sample => sample.Id).Distinct().Count());
        foreach (var sample in EmbeddingSampleDocuments.All)
        {
            var document = playground.Documents.Single(item => item.Name == sample.Id + ".md");
            Assert.Equal(sample.Text, (await store.ReadAsync(document.Id, CancellationToken.None)).Text);
        }
        Assert.Empty(generator.Inputs);
        Assert.False(Directory.Exists(directory.IndexDirectory));
        await playground.Search.ExecuteAsync(null);
        Assert.Contains("Index documents", playground.StatusMessage);
        Assert.Empty(generator.Inputs);
        await settings.IndexDocuments.ExecuteAsync(null);
        await playground.Search.ExecuteAsync(null);
        Assert.Equal(4, playground.Results.Count);
        await playground.LoadSampleDocuments.ExecuteAsync(null);
        Assert.Equal(7, playground.Documents.Count);
        Assert.Null(playground.SelectedResult);
        Assert.Empty(playground.SelectedChunks);
    }

    [Fact]
    public void ChunkingSettings_UseModeSpecificDefaultsUnitsAndWholeDocumentWithoutParameters()
    {
        using var directory = new DocumentDirectory();
        using var store = directory.CreateStore();
        using var search = directory.CreateSearch(store);
        var settings = new EmbeddingSettingsViewModel(search, []);
        Assert.Equal(new DocumentChunkingOptions(), settings.SelectedChunking);
        Assert.Contains("UTF-16", settings.ChunkSizeLabel);
        settings.ChunkSize = "40";
        settings.ChunkOverlap = "5";

        settings.ChunkingMode = DocumentChunkingMode.Sentences;

        Assert.Equal(new DocumentChunkingOptions(DocumentChunkingMode.Sentences, 1), settings.SelectedChunking);
        Assert.Contains("SENTENCES", settings.ChunkSizeLabel);
        Assert.Contains("SENTENCES", settings.ChunkOverlapLabel);
        settings.ChunkingMode = DocumentChunkingMode.WholeDocument;
        settings.ChunkSize = "ignored";
        settings.ChunkOverlap = "ignored";
        Assert.False(settings.HasChunkParameters);
        Assert.Equal(new DocumentChunkingOptions(DocumentChunkingMode.WholeDocument), settings.SelectedChunking);
        settings.ChunkingMode = DocumentChunkingMode.FixedCharacters;
        Assert.True(settings.HasChunkParameters);
        Assert.Equal(new DocumentChunkingOptions(), settings.SelectedChunking);
    }

    [Theory]
    [InlineData("", "0", "Chunk size")]
    [InlineData("0", "0", "Chunk size")]
    [InlineData("262145", "0", "Chunk size")]
    [InlineData("999999999999999", "0", "Chunk size")]
    [InlineData("no", "0", "Chunk size")]
    [InlineData("10", "", "Chunk overlap")]
    [InlineData("10", "-1", "Chunk overlap")]
    [InlineData("10", "10", "Chunk overlap")]
    [InlineData("10", "no", "Chunk overlap")]
    public async Task InvalidChunkingSettings_ReportExplicitErrorsForIndexSearchAndClearWithoutModelCalls(
        string size, string overlap, string message)
    {
        using var directory = new DocumentDirectory();
        using var diagnostics = new ChatDiagnostics();
        using var store = directory.CreateStore();
        using var search = directory.CreateSearch(store);
        var generator = new TestEmbeddings(_ => [1f, 0f]);
        var settings = new EmbeddingSettingsViewModel(search, [Describe("model", generator)])
        {
            ChunkSize = size,
            ChunkOverlap = overlap,
        };
        var playground = new EmbeddingPlaygroundViewModel(store, search, settings, new(diagnostics)) { Query = "query" };
        Assert.Contains(message, settings.StatusMessage);

        await settings.IndexDocuments.ExecuteAsync(null);
        Assert.Contains(message, settings.StatusMessage);
        await playground.Search.ExecuteAsync(null);
        Assert.Contains(message, playground.StatusMessage);
        await settings.ClearIndex.ExecuteAsync(null);
        Assert.Contains(message, settings.StatusMessage);
        Assert.Empty(generator.Inputs);
    }

    [Fact]
    public async Task IndexSearchAndClear_AllTargetTheSameSelectedChunkingConfiguration()
    {
        using var directory = new DocumentDirectory();
        using var diagnostics = new ChatDiagnostics();
        using var store = directory.CreateStore();
        using var search = directory.CreateSearch(store);
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("First. Second. Third."));
        await store.ImportAsync("notes.md", input);
        var generator = new TestEmbeddings(_ => [1f, 0f]);
        var settings = new EmbeddingSettingsViewModel(search, [Describe("model", generator)])
        {
            ChunkingMode = DocumentChunkingMode.Sentences,
            ChunkSize = "2",
            ChunkOverlap = "1",
        };
        var playground = new EmbeddingPlaygroundViewModel(store, search, settings, new(diagnostics)) { Query = "query" };
        await settings.IndexDocuments.ExecuteAsync(null);
        await playground.Search.ExecuteAsync(null);
        Assert.Equal(["First. Second.", "Second. Third."], playground.SelectedChunks.Select(chunk => chunk.Text));
        settings.ChunkSize = "1";
        settings.ChunkOverlap = "0";
        await playground.Search.ExecuteAsync(null);
        Assert.Contains("Index documents", playground.StatusMessage);
        await settings.IndexDocuments.ExecuteAsync(null);
        await playground.Search.ExecuteAsync(null);
        Assert.Equal(3, playground.SelectedChunks.Count);
        await settings.ClearIndex.ExecuteAsync(null);
        Assert.Empty(playground.SelectedChunks);
        Assert.Null(playground.SelectedResult);
        await playground.Search.ExecuteAsync(null);
        Assert.Contains("Index documents", playground.StatusMessage);
        settings.ChunkSize = "2";
        settings.ChunkOverlap = "1";
        await playground.Search.ExecuteAsync(null);
        Assert.Equal(2, playground.SelectedChunks.Count);
        await settings.ClearDocuments.ExecuteAsync(null);
        Assert.Empty(playground.SelectedChunks);
        Assert.Null(playground.SelectedResult);
    }

    [Fact]
    public async Task BusyIndex_DisablesDocumentCommandsAndSearch()
    {
        using var directory = new DocumentDirectory();
        using var diagnostics = new ChatDiagnostics();
        using var store = directory.CreateStore();
        using var search = directory.CreateSearch(store);
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("First."));
        await store.ImportAsync("notes.md", input);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var generator = new TestEmbeddings(_ => [1f, 0f], async token =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        });
        var settings = new EmbeddingSettingsViewModel(search, [Describe("model", generator)]);
        var playground = new EmbeddingPlaygroundViewModel(store, search, settings, new(diagnostics)) { Query = "query" };

        var indexing = settings.IndexDocuments.ExecuteAsync(null);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(settings.IsIdle);
        Assert.False(playground.CanManageDocuments);
        Assert.False(playground.LoadSampleDocuments.CanExecute(null));
        Assert.False(playground.Search.CanExecute(null));
        Assert.False(settings.ClearIndex.CanExecute(null));
        settings.CancelIndex.Execute(null);
        await indexing.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(settings.IsIdle);
        Assert.True(playground.LoadSampleDocuments.CanExecute(null));
    }

    [Fact]
    public async Task SettingsChangedDuringSearch_DoesNotPublishOldConfigurationResults()
    {
        using var directory = new DocumentDirectory();
        using var diagnostics = new ChatDiagnostics();
        using var store = directory.CreateStore();
        using var search = directory.CreateSearch(store);
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("First."));
        await store.ImportAsync("notes.md", input);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var block = false;
        var generator = new TestEmbeddings(_ => [1f, 0f], async token =>
        {
            if (block)
            {
                started.TrySetResult();
                await finish.Task.WaitAsync(token);
            }
        });
        var settings = new EmbeddingSettingsViewModel(search, [Describe("model", generator)]);
        var playground = new EmbeddingPlaygroundViewModel(store, search, settings, new(diagnostics)) { Query = "query" };
        await settings.IndexDocuments.ExecuteAsync(null);
        block = true;

        var searching = playground.Search.ExecuteAsync(null);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        settings.ChunkSize = "100";
        finish.TrySetResult();
        await searching.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(playground.Results);
        Assert.Null(playground.SelectedResult);
        Assert.Empty(playground.SelectedChunks);
        Assert.Contains("Settings changed during search", playground.StatusMessage);
    }

    [Theory]
    [InlineData(DocumentChunkingMode.FixedCharacters)]
    [InlineData(DocumentChunkingMode.Sentences)]
    public async Task ChunkOutputBudget_IsReportedByIndexSettingsBeforeContactingModelAndAllowsRecovery(
        DocumentChunkingMode mode)
    {
        using var directory = new DocumentDirectory();
        using var diagnostics = new ChatDiagnostics();
        using var store = directory.CreateStore();
        using var search = directory.CreateSearch(store);
        var text = mode == DocumentChunkingMode.FixedCharacters
            ? new string('a', 256 * 1024)
            : string.Concat(Enumerable.Repeat("A. ", 87381));
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(text));
        await store.ImportAsync("maximum.txt", input);
        var generator = new TestEmbeddings(_ => [1f, 0f]);
        var settings = new EmbeddingSettingsViewModel(search, [Describe("model", generator)])
        {
            ChunkingMode = mode,
            ChunkSize = mode == DocumentChunkingMode.FixedCharacters ? "131072" : "43690",
            ChunkOverlap = mode == DocumentChunkingMode.FixedCharacters ? "131071" : "43689",
        };
        var playground = new EmbeddingPlaygroundViewModel(store, search, settings, new(diagnostics)) { Query = "query" };

        await settings.IndexDocuments.ExecuteAsync(null);

        Assert.Contains("indexing failed", settings.StatusMessage);
        Assert.Contains("Reduce overlap or increase chunk size", settings.StatusMessage);
        Assert.Empty(generator.Inputs);
        Assert.False(Directory.Exists(directory.IndexDirectory));
        Assert.Null(playground.SelectedResult);
        Assert.Empty(playground.SelectedChunks);
        Assert.True(settings.IsIdle);
        settings.ChunkOverlap = "0";
        await settings.IndexDocuments.ExecuteAsync(null);
        Assert.Contains("1 document indexed", settings.StatusMessage);
        await playground.Search.ExecuteAsync(null);
        Assert.Single(playground.Results);
        Assert.NotEmpty(playground.SelectedChunks);
    }

    private static DescribedEmbeddingGenerator Describe(string id, TestEmbeddings generator) =>
        new(generator, new EmbeddingGeneratorDescriptor(id, id, "Test embedding provider"));

    private sealed class DocumentDirectory : IDisposable
    {
        private readonly string _root = Path.Combine(Directory.GetCurrentDirectory(), "document-vm-" + Guid.NewGuid().ToString("N"));
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
