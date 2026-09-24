using AIExtensions.Sample.ChatPlayground.Features.Recording;
using AIExtensions.Sample.ChatPlayground.Features.Search;
using AIExtensions.Sample.ChatPlayground.ViewModels;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.Maui.AI.Chat.Tests;

public sealed class ChatLibraryViewModelTests
{
    [Fact]
    public async Task AppleSearch_ShowsOnDeviceIndexAndCanSwitchBackFromAzure()
    {
        using var directory = new SearchDirectory();
        using var search = directory.CreateSearch(apple: true, azure: true);
        var library = new ChatLibraryViewModel(search) { Query = "cobalt", IsOpen = true };

        Assert.Equal("Apple on-device index", library.SearchIndexLabel);
        Assert.True(library.HasSemanticSearch);
        Assert.True(library.CanEnableAzure);
        Assert.True(library.SemanticSearchCommand.CanExecute(null));

        await library.SelectAzureAsync(true);
        Assert.Equal("Azure OpenAI index", library.SearchIndexLabel);
        Assert.Equal("Use Apple", library.DisableAzureLabel);
        Assert.True(library.CanDisableAzure);

        await library.SelectAzureAsync(false);
        Assert.Equal("Apple on-device index", library.SearchIndexLabel);
        Assert.False(library.CanDisableAzure);
        Assert.True(library.SemanticSearchCommand.CanExecute(null));
    }

    [Fact]
    public async Task TextOnlySearch_HidesMeaningActionUntilAzureIsSelected()
    {
        using var directory = new SearchDirectory();
        using var search = directory.CreateSearch(azure: true);
        var library = new ChatLibraryViewModel(search) { Query = "cobalt", IsOpen = true };

        Assert.Equal("Text-only search", library.SearchIndexLabel);
        Assert.False(library.HasSemanticSearch);
        Assert.False(library.SemanticSearchCommand.CanExecute(null));

        await library.SelectAzureAsync(true);
        Assert.True(library.HasSemanticSearch);
        Assert.True(library.SemanticSearchCommand.CanExecute(null));
        Assert.Equal("Text only", library.DisableAzureLabel);

        await library.SelectAzureAsync(false);
        Assert.False(library.HasSemanticSearch);
    }

    [Fact]
    public async Task UnconfiguredAzure_CannotBeSelected()
    {
        using var directory = new SearchDirectory();
        using var search = directory.CreateSearch();
        var library = new ChatLibraryViewModel(search);

        Assert.False(library.CanEnableAzure);
        Assert.False(library.HasSemanticSearch);
        await Assert.ThrowsAsync<InvalidOperationException>(() => library.SelectAzureAsync(true));
    }

    private sealed class SearchDirectory : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "chat-library-view-" + Guid.NewGuid().ToString("N"));

        public SearchDirectory() => Directory.CreateDirectory(_root);

        public ChatSearchService CreateSearch(bool apple = false, bool azure = false)
        {
            var recording = new ChatRecordingService(
                NullLogger<ChatRecordingService>.Instance, _root, Path.Combine(_root, "cache"));
            Func<IEmbeddingGenerator<string, Embedding<float>>> factory =
                () => throw new InvalidOperationException("Keyword filtering must not use embeddings.");
            return new ChatSearchService(recording, NullLogger<ChatSearchService>.Instance, _root,
                apple ? factory : null, apple ? "apple/test" : null,
                azure ? factory : null, azure ? "azure/test" : null);
        }

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
