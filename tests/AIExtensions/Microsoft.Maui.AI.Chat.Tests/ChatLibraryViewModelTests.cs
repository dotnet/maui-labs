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
    public async Task FindChats_UsesEmbeddingPageSelectionWithoutChangingIt()
    {
        using var directory = new SearchDirectory();
        var apple = new TestEmbeddings(_ => [1f, 0f]);
        var azure = new TestEmbeddings(_ => [0f, 1f]);
        using var search = directory.CreateSearch(apple, azure);
        var settings = new ChatSearchSettings(search.SearchModes);
        var library = new ChatLibraryViewModel(search, settings);

        await library.ShowAsync();
        Assert.Equal(ChatSearchDescriptor.ContainsId, settings.SelectedMode.Id);
        Assert.Equal("Using Contains", library.SearchIndexLabel);
        Assert.Equal(3, settings.SearchModes.Count);
        Assert.Equal(ChatSearchDataLocation.Remote, FindMode(settings, AzureId).DataLocation);
        Assert.Empty(apple.Inputs);
        Assert.Empty(azure.Inputs);

        library.Close();
        settings.SelectedMode = FindMode(settings, AppleId);
        await library.ShowAsync();
        Assert.Equal("Using Apple index", library.SearchIndexLabel);
        Assert.NotEmpty(apple.Inputs);
        Assert.Empty(azure.Inputs);

        library.Close();
        settings.SelectedMode = FindMode(settings, AzureId);
        await library.ShowAsync();
        Assert.Equal("Using Azure index", library.SearchIndexLabel);
        Assert.NotEmpty(azure.Inputs);
        Assert.Equal(AzureId, settings.SelectedMode.Id);
    }

    [Fact]
    public async Task TypingRapidly_OnlySearchesTheLastBufferedQuery()
    {
        using var directory = new SearchDirectory();
        var apple = new TestEmbeddings(_ => [1f, 0f]);
        using var search = directory.CreateSearch(apple);
        var settings = new ChatSearchSettings(search.SearchModes)
        {
            SelectedMode = search.SearchModes.Single(mode => mode.Id == AppleId),
        };
        var library = new ChatLibraryViewModel(search, settings, TimeSpan.FromMilliseconds(80));
        await library.ShowAsync();
        apple.Inputs.Clear();

        library.Query = "pi";
        library.Query = "pig";
        library.Query = "pigment";
        await WaitForAsync(() => !library.IsBusy && library.StatusMessage.Contains("found"));

        Assert.Equal(["pigment"], apple.Inputs);
        Assert.Equal(AppleId, settings.SelectedMode.Id);
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
        var settings = new ChatSearchSettings(search.SearchModes)
        {
            SelectedMode = search.SearchModes.Single(mode => mode.Id == AppleId),
        };
        var library = new ChatLibraryViewModel(search, settings);

        var selecting = library.ShowAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForAsync(() => library.IsIndexing);
        Assert.Contains("Indexing 1/1 chats", library.IndexProgressMessage);

        release.SetResult();
        await selecting;
        Assert.False(library.IsIndexing);
    }

    [Fact]
    public async Task FailedEmbeddingSearch_ReportsErrorWithoutChangingTheMethod()
    {
        using var directory = new SearchDirectory();
        using var search = directory.CreateSearch(new TestEmbeddings(_ => [0f, 0f]));
        var settings = new ChatSearchSettings(search.SearchModes)
        {
            SelectedMode = search.SearchModes.Single(mode => mode.Id == AppleId),
        };
        var library = new ChatLibraryViewModel(search, settings);
        await library.ShowAsync();

        Assert.Equal(AppleId, settings.SelectedMode.Id);
        Assert.True(library.HasSearchError);
        Assert.Contains("search failed", library.StatusMessage);
        Assert.Empty(library.Results);
    }

    [Fact]
    public async Task ClosingFind_CancelsIndexingAndReopeningUsesCurrentSettings()
    {
        using var directory = new SearchDirectory();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocked = new TestEmbeddings(_ => [1f, 0f], async cancellationToken =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        });
        using var search = directory.CreateSearch(blocked);
        var settings = new ChatSearchSettings(search.SearchModes)
        {
            SelectedMode = search.SearchModes.Single(mode => mode.Id == AppleId),
        };
        var library = new ChatLibraryViewModel(search, settings);

        var selecting = library.ShowAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        library.Close();
        settings.SelectedMode = ChatSearchDescriptor.Contains;
        await selecting;
        await library.ShowAsync();
        Assert.Equal(ChatSearchDescriptor.ContainsId, settings.SelectedMode.Id);
        Assert.False(library.IsBusy);
        Assert.False(library.IsIndexing);
        Assert.Single(library.Results);
    }

    [Fact]
    public void MissingBackend_CannotBeSelectedInSharedSettings()
    {
        using var directory = new SearchDirectory();
        using var search = directory.CreateSearch();
        var settings = new ChatSearchSettings(search.SearchModes);

        Assert.Single(settings.SearchModes);
        Assert.Throws<ArgumentException>(() => settings.SelectedMode =
            new ChatSearchDescriptor("missing", "Missing", "Not configured", "missing/model",
                ChatSearchDataLocation.OnDevice));
        Assert.Equal(ChatSearchDescriptor.ContainsId, settings.SelectedMode.Id);
    }

    private static ChatSearchDescriptor FindMode(ChatSearchSettings settings, string id) =>
        Assert.Single(settings.SearchModes, mode => mode.Id == id);

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
