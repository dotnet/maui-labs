using System.Collections.ObjectModel;
using System.Globalization;
using AIExtensions.Sample.ChatPlayground.Features.Search;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.AI;

namespace AIExtensions.Sample.ChatPlayground.ViewModels;

/// <summary>Exercises the selected embedding generator and indexes saved chats with that same model.</summary>
public sealed partial class EmbeddingPlaygroundViewModel : ObservableObject
{
    private readonly ChatSearchService _search;
    private readonly ChatSearchSettings _settings;
    private readonly IReadOnlyDictionary<string, IEmbeddingGenerator<string, Embedding<float>>> _generators;
    private CancellationTokenSource? _cancellation;

    public EmbeddingPlaygroundViewModel(
        ChatSearchService search, ChatSearchSettings settings,
        IEnumerable<IEmbeddingGenerator<string, Embedding<float>>> generators)
    {
        _search = search;
        _settings = settings;
        _generators = generators.ToDictionary(
            generator => generator.GetService<ChatSearchDescriptor>()!.Id, StringComparer.Ordinal);
    }

    public IReadOnlyList<ChatSearchDescriptor> SearchModes => _settings.SearchModes;
    public ObservableCollection<ChatSearchHit> Results { get; } = [];

    public ChatSearchDescriptor SelectedMode
    {
        get => _settings.SelectedMode;
        set
        {
            if (_settings.SelectedMode == value)
                return;
            _settings.SelectedMode = value;
            Results.Clear();
            VectorPreview = string.Empty;
            VectorSummary = string.Empty;
            StatusMessage = $"Selected {value.DisplayName}. Index or search saved chats with this method.";
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsEmbeddingSelected));
            OnPropertyChanged(nameof(SelectedModeDescription));
            GenerateVectorCommand.NotifyCanExecuteChanged();
        }
    }

    public string Dimensions
    {
        get => _settings.Dimensions;
        set
        {
            if (_settings.Dimensions == value)
                return;
            _settings.Dimensions = value;
            Results.Clear();
            VectorPreview = string.Empty;
            VectorSummary = string.Empty;
            OnPropertyChanged();
        }
    }

    public bool IsEmbeddingSelected => SelectedMode.Id != ChatSearchDescriptor.ContainsId;
    public string SelectedModeDescription => SelectedMode.Description;

    [ObservableProperty] private string vectorText = string.Empty;
    [ObservableProperty] private string query = string.Empty;
    [ObservableProperty] private string statusMessage = "Select a method, then index or search your saved chats.";
    [ObservableProperty] private string vectorSummary = string.Empty;
    [ObservableProperty] private string vectorPreview = string.Empty;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isIndexing;
    [ObservableProperty] private string indexProgressMessage = string.Empty;

    public bool IsIdle => !IsBusy;

    public IAsyncRelayCommand GenerateVectorCommand =>
        _generateVectorCommand ??= new AsyncRelayCommand(GenerateVectorAsync, () => !IsBusy && IsEmbeddingSelected);
    public IAsyncRelayCommand IndexChatsCommand =>
        _indexChatsCommand ??= new AsyncRelayCommand(IndexChatsAsync, () => !IsBusy);
    public IAsyncRelayCommand SearchChatsCommand =>
        _searchChatsCommand ??= new AsyncRelayCommand(SearchChatsAsync, () => !IsBusy);
    public IRelayCommand CancelCommand =>
        _cancelCommand ??= new RelayCommand(() => _cancellation?.Cancel(), () => IsBusy);

    private IAsyncRelayCommand? _generateVectorCommand;
    private IAsyncRelayCommand? _indexChatsCommand;
    private IAsyncRelayCommand? _searchChatsCommand;
    private IRelayCommand? _cancelCommand;

    private Task IndexChatsAsync() => RunAsync(async cancellationToken =>
    {
        var result = await _search.SearchAsync(string.Empty, SelectedMode.Id, cancellationToken,
            CreateProgress(cancellationToken), _settings.SelectedDimensions);
        StatusMessage = $"Saved-chat index is ready for {SelectedMode.DisplayName}." +
            (result.Notice is null ? string.Empty : $" {result.Notice}");
    });

    private Task SearchChatsAsync() => RunAsync(async cancellationToken =>
    {
        var result = await _search.SearchAsync(Query, SelectedMode.Id, cancellationToken,
            CreateProgress(cancellationToken), _settings.SelectedDimensions);
        Results.Clear();
        foreach (var hit in result.Hits)
            Results.Add(hit);
        StatusMessage = $"{result.Hits.Count} {(result.Hits.Count == 1 ? "chat" : "chats")} found" +
            (result.IsSemantic ? " by meaning." : ".") +
            (result.Notice is null ? string.Empty : $" {result.Notice}");
    });

    private Task GenerateVectorAsync() => RunAsync(async cancellationToken =>
    {
        if (string.IsNullOrWhiteSpace(VectorText))
            throw new ArgumentException("Enter text to embed.");
        if (!_generators.TryGetValue(SelectedMode.Id, out var generator))
            throw new InvalidOperationException("Choose an embedding generator, not Contains.");

        var options = _settings.SelectedDimensions is { } dimensions
            ? new EmbeddingGenerationOptions { Dimensions = dimensions }
            : null;
        var vector = await generator.GenerateVectorAsync(VectorText.Trim(), options, cancellationToken);
        if (vector.IsEmpty)
            throw new InvalidDataException("The generator returned no embedding.");
        VectorSummary = $"{vector.Length} dimensions from {SelectedMode.DisplayName}";
        VectorPreview = string.Join(", ", vector.Span[..Math.Min(12, vector.Length)]
            .ToArray().Select(value => value.ToString("G5", CultureInfo.InvariantCulture))) +
            (vector.Length > 12 ? ", ..." : string.Empty);
        StatusMessage = "Embedding generated. Index and search saved chats to compare vectors.";
    });

    private IProgress<ChatSearchProgress> CreateProgress(CancellationToken cancellationToken) =>
        new Progress<ChatSearchProgress>(progress =>
    {
        if (!IsBusy || _cancellation?.Token != cancellationToken)
            return;
        IsIndexing = progress.IsIndexing;
        if (progress.IsIndexing)
            IndexProgressMessage = $"Indexing {progress.CompletedChats + 1}/{progress.TotalChats}: " +
                $"{progress.IndexedChunks}/{progress.TotalChunks} excerpts from {progress.ChatTitle}";
    });

    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        IsBusy = true;
        try
        {
            await action(cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            StatusMessage = "Operation cancelled.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Operation failed: {exception.Message}";
        }
        finally
        {
            _cancellation = null;
            IsIndexing = false;
            IsBusy = false;
        }
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsIdle));
        GenerateVectorCommand.NotifyCanExecuteChanged();
        IndexChatsCommand.NotifyCanExecuteChanged();
        SearchChatsCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }
}
