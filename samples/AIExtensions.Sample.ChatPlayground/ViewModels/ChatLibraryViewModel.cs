using System.Collections.ObjectModel;
using AIExtensions.Sample.ChatPlayground.Features.Search;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIExtensions.Sample.ChatPlayground.ViewModels;

/// <summary>Searches saved conversations without changing the active chat until one is opened.</summary>
public partial class ChatLibraryViewModel(ChatSearchService search) : ObservableObject
{
    public const string AzureConsentPreferenceKey = "chat-playground-azure-search-consent";

    private CancellationTokenSource? _searchCancellation;
    private IAsyncRelayCommand? _semanticSearchCommand;
    private IAsyncRelayCommand<ChatSearchHit>? _openChatCommand;
    private IRelayCommand? _closeCommand;
    private IAsyncRelayCommand? _importFileCommand;

    public ObservableCollection<ChatSearchHit> Results { get; } = [];

    [ObservableProperty] private bool isOpen;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool useAzureEmbeddings;
    [ObservableProperty] private string query = string.Empty;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private bool hasSearchError;

    public bool HasAppleEmbeddings => search.HasLocalEmbeddings;
    public bool HasAzureEmbeddings => search.HasAzureEmbeddings;
    public bool HasSemanticSearch => UseAzureEmbeddings ? HasAzureEmbeddings : HasAppleEmbeddings;
    public bool CanEnableAzure => HasAzureEmbeddings && !UseAzureEmbeddings;
    public bool CanDisableAzure => UseAzureEmbeddings;
    public string SearchIndexLabel => UseAzureEmbeddings
        ? "Azure OpenAI index"
        : HasAppleEmbeddings ? "Apple on-device index" : "Text-only search";
    public string DisableAzureLabel => HasAppleEmbeddings ? "Use Apple" : "Text only";
    public string DisableAzureDescription => HasAppleEmbeddings
        ? "Switch to Apple's on-device search index"
        : "Stop using Azure and filter saved chats by text only";
    public bool IsEmpty => Results.Count == 0 && !IsBusy;
    public string EmptyMessage => HasSearchError
        ? "Search could not complete. Check the status below."
        : Query.Length == 0
            ? "No saved chats yet. Import a file or start a chat."
            : HasSemanticSearch
                ? "No text matches. Try other words or Find similar."
                : "No saved chats match. Try different words.";
    public string SearchModeDescription => UseAzureEmbeddings
        ? "Text filters locally. Indexing and Find similar send saved text and queries to Azure."
        : HasAppleEmbeddings
            ? "Text filters locally. Find similar uses Apple's on-device NaturalLanguage index."
            : HasAzureEmbeddings
                ? "Type to filter locally. Enable Azure for meaning-based search."
                : "Type to filter locally. Configure AI:EmbeddingDeploymentName for meaning-based search.";

    public Func<string, Task>? OpenChatAsync { get; set; }

    public IAsyncRelayCommand? ImportFileCommand
    {
        get => _importFileCommand;
        set => SetProperty(ref _importFileCommand, value);
    }

    public IAsyncRelayCommand SemanticSearchCommand =>
        _semanticSearchCommand ??= new AsyncRelayCommand(() => RefreshAsync(semantic: true),
            () => IsOpen && !IsBusy && HasSemanticSearch && !string.IsNullOrWhiteSpace(Query));

    public IAsyncRelayCommand<ChatSearchHit> OpenChatCommand =>
        _openChatCommand ??= new AsyncRelayCommand<ChatSearchHit>(OpenAsync,
            hit => IsOpen && !IsBusy && hit is not null);

    public IRelayCommand CloseCommand => _closeCommand ??= new RelayCommand(Close);

    public async Task ShowAsync()
    {
        IsOpen = true;
        Query = string.Empty;
        await RefreshAsync(semantic: false);
    }

    public async Task SelectAzureAsync(bool enabled)
    {
        if (enabled && !HasAzureEmbeddings)
            throw new InvalidOperationException("Azure semantic search is not configured.");
        UseAzureEmbeddings = enabled;
        await RefreshAsync(semantic: false);
    }

    public void Close()
    {
        _searchCancellation?.Cancel();
        IsOpen = false;
    }

    private async Task OpenAsync(ChatSearchHit? hit)
    {
        if (hit is null)
            return;
        if (OpenChatAsync is null)
            throw new InvalidOperationException("The chat library has no conversation opener.");
        await OpenChatAsync(hit.Id);
    }

    private async Task RefreshAsync(bool semantic)
    {
        _searchCancellation?.Cancel();
        using var cancellation = new CancellationTokenSource();
        _searchCancellation = cancellation;
        StatusMessage = semantic
            ? UseAzureEmbeddings ? "Searching and indexing with Azure..." : "Searching Apple's on-device index..."
            : "Filtering saved chats...";
        IsBusy = true;
        try
        {
            var result = await search.SearchAsync(Query, semantic, UseAzureEmbeddings, cancellation.Token);
            if (!ReferenceEquals(_searchCancellation, cancellation))
                return;
            SetResults(result.Hits);
            HasSearchError = false;
            var count = result.Hits.Count;
            StatusMessage = $"{count} {(count == 1 ? "chat" : "chats")} found" +
                (result.IsSemantic ? " by meaning." : ".") +
                (result.Notice is null ? string.Empty : $" {result.Notice}");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (ReferenceEquals(_searchCancellation, cancellation))
            {
                StatusMessage = $"Chat search failed: {exception.Message}";
                if (semantic)
                {
                    try
                    {
                        var keywords = await search.SearchAsync(Query, semantic: false,
                            useAzure: false, cancellationToken: cancellation.Token);
                        if (!ReferenceEquals(_searchCancellation, cancellation))
                            return;
                        SetResults(keywords.Hits);
                        StatusMessage += " Showing keyword matches instead.";
                        HasSearchError = false;
                    }
                    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                    {
                    }
                    catch (Exception fallbackException)
                    {
                        if (!ReferenceEquals(_searchCancellation, cancellation))
                            return;
                        Results.Clear();
                        OnPropertyChanged(nameof(IsEmpty));
                        StatusMessage += $" Keyword search also failed: {fallbackException.Message}";
                        HasSearchError = true;
                    }
                }
                else
                {
                    Results.Clear();
                    OnPropertyChanged(nameof(IsEmpty));
                    HasSearchError = true;
                }
            }
        }
        finally
        {
            if (ReferenceEquals(_searchCancellation, cancellation))
            {
                _searchCancellation = null;
                IsBusy = false;
            }
        }
    }

    private void SetResults(IReadOnlyList<ChatSearchHit> hits)
    {
        Results.Clear();
        foreach (var hit in hits)
            Results.Add(hit);
        OnPropertyChanged(nameof(IsEmpty));
    }

    partial void OnQueryChanged(string value)
    {
        OnPropertyChanged(nameof(EmptyMessage));
        SemanticSearchCommand.NotifyCanExecuteChanged();
        if (IsOpen)
            _ = RefreshAsync(semantic: false);
    }

    partial void OnUseAzureEmbeddingsChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEnableAzure));
        OnPropertyChanged(nameof(CanDisableAzure));
        OnPropertyChanged(nameof(HasSemanticSearch));
        OnPropertyChanged(nameof(SearchIndexLabel));
        OnPropertyChanged(nameof(DisableAzureLabel));
        OnPropertyChanged(nameof(DisableAzureDescription));
        OnPropertyChanged(nameof(SearchModeDescription));
        OnPropertyChanged(nameof(EmptyMessage));
        SemanticSearchCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsEmpty));
        SemanticSearchCommand.NotifyCanExecuteChanged();
        OpenChatCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsOpenChanged(bool value)
    {
        SemanticSearchCommand.NotifyCanExecuteChanged();
        OpenChatCommand.NotifyCanExecuteChanged();
    }

    partial void OnHasSearchErrorChanged(bool value) => OnPropertyChanged(nameof(EmptyMessage));
}
