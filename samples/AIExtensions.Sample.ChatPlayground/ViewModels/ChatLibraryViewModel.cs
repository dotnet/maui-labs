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

    public bool HasAzureEmbeddings => search.HasAzureEmbeddings;
    public bool CanEnableAzure => HasAzureEmbeddings && !UseAzureEmbeddings;
    public bool IsEmpty => Results.Count == 0 && !IsBusy;
    public string EmptyMessage => HasSearchError
        ? "Search could not complete. Check the status below."
        : Query.Length == 0
            ? "No saved chats yet. Import a file or start a chat."
            : "No saved chats match. Try different words or press Search.";
    public string SearchModeDescription => UseAzureEmbeddings
        ? "Azure semantic search sends saved chat text and search terms to your configured deployment."
        : search.HasLocalEmbeddings
            ? "Text filters locally. Press Search for private on-device semantic matches."
            : search.HasAzureEmbeddings
                ? "Text filters locally. Enable Azure to search by meaning."
                : "Text filters locally. Configure AI:EmbeddingDeploymentName to enable semantic search.";

    public Func<string, Task>? OpenChatAsync { get; set; }

    public IAsyncRelayCommand? ImportFileCommand
    {
        get => _importFileCommand;
        set => SetProperty(ref _importFileCommand, value);
    }

    public IAsyncRelayCommand SemanticSearchCommand =>
        _semanticSearchCommand ??= new AsyncRelayCommand(() => RefreshAsync(semantic: true),
            () => IsOpen && !IsBusy && !string.IsNullOrWhiteSpace(Query));

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
        OnPropertyChanged(nameof(SearchModeDescription));
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
