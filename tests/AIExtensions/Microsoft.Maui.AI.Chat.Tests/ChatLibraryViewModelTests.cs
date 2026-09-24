using AIExtensions.Sample.ChatPlayground.Features.Library;
using AIExtensions.Sample.ChatPlayground.Features.Recording;
using AIExtensions.Sample.ChatPlayground.Features.Search;
using AIExtensions.Sample.ChatPlayground.ViewModels;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.Maui.AI.Chat.Tests;

public sealed class ChatLibraryViewModelTests
{
    private const string AppleId = "apple-test";
    private const string AzureId = "azure-test";

    [Fact]
    public async Task FindChats_StartsWithContainsAndIndexesOnlyTheSelectedBackend()
    {
        using var directory = new SearchDirectory();
        var apple = new TestEmbeddings(_ => [1f, 0f]);
        var azure = new TestEmbeddings(_ => [0f, 1f]);
        using var search = directory.CreateSearch(apple, azure);
        var library = new ChatLibraryViewModel(search);

        await library.ShowAsync();
        Assert.Equal(ChatSearchDescriptor.ContainsId, library.SelectedMode.Id);
        Assert.Equal("Search with: Contains", library.SearchIndexLabel);
        Assert.Equal(3, library.SearchModes.Count);
        Assert.Equal(ChatSearchDataLocation.Remote, FindMode(library, AzureId).DataLocation);
        Assert.Empty(apple.Inputs);
        Assert.Empty(azure.Inputs);

        await library.SelectModeAsync(FindMode(library, AppleId));
        Assert.Equal("Search with: Apple index", library.SearchIndexLabel);
        Assert.NotEmpty(apple.Inputs);
        Assert.Empty(azure.Inputs);

        await library.SelectModeAsync(FindMode(library, AzureId));
        Assert.Equal("Search with: Azure index", library.SearchIndexLabel);
        Assert.NotEmpty(azure.Inputs);
        library.Close();
        await library.ShowAsync();
        Assert.Equal(ChatSearchDescriptor.ContainsId, library.SelectedMode.Id);
    }

    [Fact]
    public async Task TypingRapidly_OnlySearchesTheLastBufferedQuery()
    {
        using var directory = new SearchDirectory();
        var apple = new TestEmbeddings(_ => [1f, 0f]);
        using var search = directory.CreateSearch(apple);
        var library = new ChatLibraryViewModel(search, TimeSpan.FromMilliseconds(80));
        await library.ShowAsync();
        await library.SelectModeAsync(FindMode(library, AppleId));
        apple.Inputs.Clear();

        library.Query = "pi";
        library.Query = "pig";
        library.Query = "pigment";
        await WaitForAsync(() => !library.IsBusy && library.StatusMessage.Contains("found"));

        Assert.Equal(["pigment"], apple.Inputs);
        Assert.Equal(AppleId, library.SelectedMode.Id);
    }

    [Fact]
    public async Task IndexingProgress_IsVisibleUntilTheSelectedIndexIsReady()
    {
        using var directory = new SearchDirectory();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var apple = new TestEmbeddings(_ => [1f, 0f], async cancellationToken =>
        {
            started.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
        });
        using var search = directory.CreateSearch(apple);
        var library = new ChatLibraryViewModel(search);
        await library.ShowAsync();

        var selecting = library.SelectModeAsync(FindMode(library, AppleId));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForAsync(() => library.IsIndexing);
        Assert.Contains("Indexing 1/1 chats", library.IndexProgressMessage);

        release.SetResult();
        await selecting;
        Assert.False(library.IsIndexing);
    }

    [Fact]
    public async Task FailedEmbeddingSearch_ReportsErrorAndSwitchesToLocalContains()
    {
        using var directory = new SearchDirectory();
        using var search = directory.CreateSearch(new TestEmbeddings(_ => [0f, 0f]));
        var library = new ChatLibraryViewModel(search);
        await library.ShowAsync();
        await library.SelectModeAsync(FindMode(library, AppleId));

        Assert.Equal(ChatSearchDescriptor.ContainsId, library.SelectedMode.Id);
        Assert.True(library.HasSearchError);
        Assert.Contains("search failed", library.StatusMessage);
        Assert.Contains("Switched to Contains", library.StatusMessage);
        Assert.Single(library.Results);
    }

    [Fact]
    public async Task SwitchingBackend_CancelsOldIndexingWithoutReplacingNewResults()
    {
        using var directory = new SearchDirectory();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocked = new TestEmbeddings(_ => [1f, 0f], async cancellationToken =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        });
        using var search = directory.CreateSearch(blocked);
        var library = new ChatLibraryViewModel(search);
        await library.ShowAsync();

        var selecting = library.SelectModeAsync(FindMode(library, AppleId));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await library.SelectModeAsync(ChatSearchDescriptor.Contains);
        await selecting;
        Assert.Equal(ChatSearchDescriptor.ContainsId, library.SelectedMode.Id);
        Assert.False(library.IsBusy);
        Assert.False(library.IsIndexing);
        Assert.Single(library.Results);
    }

    [Fact]
    public async Task MissingBackend_CannotBeSelected()
    {
        using var directory = new SearchDirectory();
        using var search = directory.CreateSearch();
        var library = new ChatLibraryViewModel(search);

        await library.ShowAsync();
        Assert.Single(library.SearchModes);
        await Assert.ThrowsAsync<ArgumentException>(() => library.SelectModeAsync(
            new ChatSearchDescriptor("missing", "Missing", "Not configured", "missing/model",
                ChatSearchDataLocation.OnDevice)));
        Assert.Equal(ChatSearchDescriptor.ContainsId, library.SelectedMode.Id);
    }

    private static ChatSearchDescriptor FindMode(ChatLibraryViewModel library, string id) =>
        Assert.Single(library.SearchModes, mode => mode.Id == id);

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }

    private sealed class SearchDirectory : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "chat-library-view-" + Guid.NewGuid().ToString("N"));
        private readonly ChatLibraryService _recording;

        public SearchDirectory()
        {
            Directory.CreateDirectory(_root);
            _recording = new ChatLibraryService(
                NullLogger<ChatLibraryService>.Instance, _root, Path.Combine(_root, "cache"));
            _recording.AddResponse(
                ChatRecordingSerializer.Request([new ChatMessage(ChatRole.User, "What is cobalt?")], null),
                new ChatResponse([new ChatMessage(ChatRole.Assistant, "Cobalt is a blue pigment.")]));
        }

        public ChatSearchService CreateSearch(TestEmbeddings? apple = null, TestEmbeddings? azure = null)
        {
            var providers = new List<IEmbeddingGenerator<string, Embedding<float>>>();
            if (apple is not null)
                providers.Add(new DescribedEmbeddingGenerator(() => apple,
                    new ChatSearchDescriptor(AppleId, "Apple index", "Local search",
                        "apple/test", ChatSearchDataLocation.OnDevice)));
            if (azure is not null)
                providers.Add(new DescribedEmbeddingGenerator(() => azure,
                    new ChatSearchDescriptor(AzureId, "Azure index", "Remote search",
                        "azure/test", ChatSearchDataLocation.Remote)));
            return new ChatSearchService(_recording, NullLogger<ChatSearchService>.Instance,
                _root, providers);
        }

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
