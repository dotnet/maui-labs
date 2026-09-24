using System.Collections.ObjectModel;
using AIExtensions.Sample.ChatPlayground.Features.Search;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIExtensions.Sample.ChatPlayground.ViewModels;

/// <summary>Searches saved conversations without changing the active chat until one is opened.</summary>
public partial class ChatLibraryViewModel : ObservableObject
{
    private static readonly TimeSpan DefaultSearchDelay = TimeSpan.FromMilliseconds(400);

    private readonly ChatSearchService _search;
    private readonly TimeSpan _searchDelay;
    private CancellationTokenSource? _searchCancellation;
    private IAsyncRelayCommand? _searchNowCommand;
    private IAsyncRelayCommand<ChatSearchHit>? _openChatCommand;
    private IRelayCommand? _closeCommand;
    private IAsyncRelayCommand? _importFileCommand;

    public ChatLibraryViewModel(ChatSearchService search, TimeSpan? searchDelay = null)
    {
        _search = search;
        _searchDelay = searchDelay ?? DefaultSearchDelay;
        if (_searchDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(searchDelay));
    }

    public ObservableCollection<ChatSearchHit> Results { get; } = [];
    public IReadOnlyList<ChatSearchDescriptor> SearchModes => _search.SearchModes;
    public double SearchPickerHeight => Math.Min(300, SearchModes.Count * 88);
    public long VisitId { get; private set; }

    [ObservableProperty] private bool isOpen;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isIndexing;
    [ObservableProperty] private ChatSearchDescriptor selectedMode = ChatSearchDescriptor.Contains;
    [ObservableProperty] private string query = string.Empty;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private string indexProgressMessage = string.Empty;
    [ObservableProperty] private bool hasSearchError;

    public string SearchIndexLabel => $"Search with: {SelectedMode.DisplayName}";
    public bool IsEmpty => Results.Count == 0 && !IsBusy;
    public string EmptyMessage => HasSearchError
        ? "Search failed. Check the status below or try another method."
        : Query.Length == 0
            ? "No saved chats yet. Import a file or start a chat."
            : SelectedMode.Id == ChatSearchDescriptor.ContainsId
                ? "No saved chats contain that text. Try different words."
                : "No similar chats found. Try different words or Contains.";
    public string SearchModeDescription => SelectedMode.Description;

    public Func<string, Task>? OpenChatAsync { get; set; }

    public IAsyncRelayCommand? ImportFileCommand
    {
        get => _importFileCommand;
        set => SetProperty(ref _importFileCommand, value);
    }

    public IAsyncRelayCommand SearchNowCommand =>
        _searchNowCommand ??= new AsyncRelayCommand(() => RefreshAsync(debounce: false));

    public IAsyncRelayCommand<ChatSearchHit> OpenChatCommand =>
        _openChatCommand ??= new AsyncRelayCommand<ChatSearchHit>(OpenAsync,
            hit => IsOpen && !IsBusy && hit is not null);

    public IRelayCommand CloseCommand => _closeCommand ??= new RelayCommand(Close);

    public async Task ShowAsync()
    {
        SelectedMode = ChatSearchDescriptor.Contains;
        Query = string.Empty;
        VisitId++;
        IsOpen = true;
        await RefreshAsync(debounce: false);
    }

    public async Task SelectModeAsync(ChatSearchDescriptor selected)
    {
        ArgumentNullException.ThrowIfNull(selected);
        if (!SearchModes.Contains(selected))
            throw new ArgumentException("The selected search method is not registered.", nameof(selected));
        if (!IsOpen)
            return;
        SelectedMode = selected;
        await RefreshAsync(debounce: false);
    }

    public void Close()
    {
        _searchCancellation?.Cancel();
        IsOpen = false;
        IsIndexing = false;
    }

    private async Task OpenAsync(ChatSearchHit? hit)
    {
        if (hit is null)
            return;
        if (OpenChatAsync is null)
            throw new InvalidOperationException("The chat library has no conversation opener.");
        await OpenChatAsync(hit.Id);
    }

    private async Task RefreshAsync(bool debounce)
    {
        if (!IsOpen)
            return;
        _searchCancellation?.Cancel();
        using var cancellation = new CancellationTokenSource();
        _searchCancellation = cancellation;
        var selected = SelectedMode;
        var text = Query;
        StatusMessage = selected.Id == ChatSearchDescriptor.ContainsId
            ? "Filtering saved chats..."
            : $"Searching {selected.DisplayName}...";
        IsBusy = true;
        IsIndexing = false;
        try
        {
            if (debounce)
                await Task.Delay(_searchDelay, cancellation.Token);
            var progress = new Progress<ChatSearchProgress>(value =>
            {
                if (!ReferenceEquals(_searchCancellation, cancellation) || !IsOpen || SelectedMode != selected)
                    return;
                IsIndexing = value.IsIndexing;
                if (value.IsIndexing)
                    IndexProgressMessage = $"Indexing {value.CompletedChats + 1}/{value.TotalChats} chats " +
                        $"({value.IndexedChunks}/{value.TotalChunks} excerpts): {value.ChatTitle}";
            });
            var result = await _search.SearchAsync(text, selected.Id, cancellation.Token, progress);
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
                StatusMessage = $"{selected.DisplayName} search failed: {exception.Message}";
                HasSearchError = true;
                IsIndexing = false;
                if (selected.Id != ChatSearchDescriptor.ContainsId)
                {
                    try
                    {
                        var keywords = await _search.SearchAsync(
                            text, ChatSearchDescriptor.ContainsId, cancellation.Token);
                        if (!ReferenceEquals(_searchCancellation, cancellation))
                            return;
                        SelectedMode = ChatSearchDescriptor.Contains;
                        SetResults(keywords.Hits);
                        StatusMessage += " Switched to Contains and showing local text matches.";
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
                        StatusMessage += $" Contains search also failed: {fallbackException.Message}";
                    }
                }
                else
                {
                    Results.Clear();
                    OnPropertyChanged(nameof(IsEmpty));
                }
            }
        }
        finally
        {
            if (ReferenceEquals(_searchCancellation, cancellation))
            {
                _searchCancellation = null;
                IsBusy = false;
                IsIndexing = false;
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
        if (IsOpen)
            _ = RefreshAsync(debounce: true);
    }

    partial void OnSelectedModeChanged(ChatSearchDescriptor value)
    {
        OnPropertyChanged(nameof(SearchIndexLabel));
        OnPropertyChanged(nameof(SearchModeDescription));
        OnPropertyChanged(nameof(EmptyMessage));
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsEmpty));
        OpenChatCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsOpenChanged(bool value)
    {
        OpenChatCommand.NotifyCanExecuteChanged();
    }

    partial void OnHasSearchErrorChanged(bool value) => OnPropertyChanged(nameof(EmptyMessage));
}
