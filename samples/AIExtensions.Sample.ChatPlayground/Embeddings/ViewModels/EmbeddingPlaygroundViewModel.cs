using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Imports text documents and searches their separate, model-specific indexes.</summary>
public sealed partial class EmbeddingPlaygroundViewModel : ObservableObject
{
    private readonly DocumentStore _documents;
    private readonly DocumentSearchService _search;
    private int _configurationVersion;

    public EmbeddingPlaygroundViewModel(DocumentStore documents, DocumentSearchService search,
        EmbeddingSettingsViewModel settings, DiagnosticsViewModel diagnostics)
    {
        _documents = documents;
        _search = search;
        Settings = settings;
        Diagnostics = diagnostics;
        Settings.PropertyChanged += SettingsPropertyChanged;
        Settings.ResultsInvalidated += (_, _) => ClearResults();
        Settings.IndexCleared += (_, _) =>
        {
            ClearResults();
            StatusMessage = "Index cleared. Imported documents are unchanged; index them to search again.";
        };
        Settings.DocumentsCleared += (_, _) =>
        {
            Documents.Clear();
            ClearResults();
            StatusMessage = "Imported documents and indexes cleared.";
        };
        StatusMessage = "Import text or load sample documents, then index them in settings.";
    }

    public EmbeddingSettingsViewModel Settings { get; }
    public DiagnosticsViewModel Diagnostics { get; }
    public ObservableCollection<ImportedDocument> Documents { get; } = [];
    public ObservableCollection<DocumentSearchHit> Results { get; } = [];

    [ObservableProperty] private string query = string.Empty;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private bool showingSearchResults;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private DocumentSearchHit? selectedResult;

    public bool IsIdle => !IsBusy;
    public bool CanManageDocuments => !IsBusy && !Settings.IsBusy;
    public string ResultsHeading => ShowingSearchResults ? "SEARCH RESULTS" : "IMPORTED DOCUMENTS";
    public string ChunksHeading => SelectedResult is { } hit ? $"RANKED CHUNKS · {hit.Name}" : "RANKED CHUNKS";
    public IReadOnlyList<DocumentChunkHit> SelectedChunks => SelectedResult?.Chunks ?? [];
    public IAsyncRelayCommand Search => SearchDocumentsCommand;
    public IAsyncRelayCommand LoadSampleDocuments => LoadSampleDocumentsCommand;
    public System.Windows.Input.ICommand CancelSearch => SearchDocumentsCancelCommand;

    public async Task LoadDocumentsAsync()
    {
        try
        {
            var documents = await _documents.ListAsync();
            Documents.Clear();
            foreach (var document in documents)
                Documents.Add(document);
            if (!ShowingSearchResults)
                StatusMessage = $"{documents.Count} imported {(documents.Count == 1 ? "document" : "documents")}." +
                    (Settings.HasGenerator ? " Index documents in settings to search." : string.Empty);
        }
        catch (Exception exception)
        {
            ClearResults();
            StatusMessage = $"Could not load documents: {exception.Message}";
        }
    }

    public async Task ImportDocumentAsync(string fileName, Stream source, CancellationToken cancellationToken = default)
    {
        if (IsBusy || Settings.IsBusy)
        {
            StatusMessage = "Wait for the current document operation to finish before importing.";
            return;
        }
        ClearResults();
        IsBusy = true;
        Settings.IsBusy = true;
        try
        {
            var document = await _documents.ImportAsync(fileName, source, cancellationToken);
            Documents.Insert(0, document);
            StatusMessage = $"Imported {document.Name}. Index documents in settings to search it.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusMessage = "Document import cancelled.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Document import failed: {exception.Message}";
        }
        finally
        {
            Settings.IsBusy = false;
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanManageDocuments))]
    private async Task LoadSampleDocumentsAsync()
    {
        ClearResults();
        IsBusy = true;
        Settings.IsBusy = true;
        try
        {
            foreach (var sample in EmbeddingSampleDocuments.All)
            {
                using var input = new MemoryStream(Encoding.UTF8.GetBytes(sample.Text));
                var document = await _documents.ImportAsync(sample.Id + ".md", input);
                Documents.Insert(0, document);
            }
            StatusMessage = "Loaded 3 sample documents locally. Existing documents were kept. Index documents in settings to search.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Could not load sample documents: {exception.Message}";
        }
        finally
        {
            Settings.IsBusy = false;
            IsBusy = false;
        }
    }

    public void ReportImportProblem(string message)
    {
        ClearResults();
        StatusMessage = message;
    }

    private void ClearResults()
    {
        SelectedResult = null;
        Results.Clear();
        ShowingSearchResults = false;
    }

    private bool CanSearch() => !IsBusy && !Settings.IsBusy &&
        Settings.HasGenerator && !string.IsNullOrWhiteSpace(Query);

    [RelayCommand(CanExecute = nameof(CanSearch), IncludeCancelCommand = true)]
    private async Task SearchDocumentsAsync(CancellationToken cancellationToken)
    {
        var option = Settings.SelectedOption ?? throw new InvalidOperationException("Choose an embedding generator.");
        var configurationVersion = _configurationVersion;
        ClearResults();
        IsBusy = true;
        Settings.IsBusy = true;
        StatusMessage = $"Searching with {option.Descriptor.DisplayName}...";
        try
        {
            var result = await _search.SearchAsync(
                Query, option.Generator, option.Descriptor.Id, Settings.SelectedDimensions,
                cancellationToken, Settings.SelectedChunking);
            if (configurationVersion != _configurationVersion)
            {
                StatusMessage = "Settings changed during search. Search again with the current configuration.";
                return;
            }
            foreach (var hit in result.Hits)
                Results.Add(hit);
            ShowingSearchResults = true;
            SelectedResult = Results.FirstOrDefault();
            StatusMessage = $"{result.Hits.Count} closest {(result.Hits.Count == 1 ? "document" : "documents")}" +
                (result.QueryDimensions > 0 ? $" ({result.QueryDimensions}-dimensional query)." : ".") +
                (string.IsNullOrEmpty(result.Notice) ? string.Empty : $" {result.Notice}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusMessage = "Search cancelled.";
        }
        catch (Exception exception)
        {
            ClearResults();
            StatusMessage = $"Search failed: {exception.Message}";
        }
        finally
        {
            Settings.IsBusy = false;
            IsBusy = false;
        }
    }

    private void SettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EmbeddingSettingsViewModel.SelectedOption) or
            nameof(EmbeddingSettingsViewModel.Dimensions) or
            nameof(EmbeddingSettingsViewModel.ChunkingMode) or
            nameof(EmbeddingSettingsViewModel.ChunkSize) or
            nameof(EmbeddingSettingsViewModel.ChunkOverlap))
        {
            _configurationVersion++;
            ClearResults();
            StatusMessage = Settings.HasGenerator
                ? "Index documents for the selected model, dimensions and chunking, then search."
                : "Configure an embedding generator to search imported documents.";
        }
        if (e.PropertyName is nameof(EmbeddingSettingsViewModel.SelectedOption) or
            nameof(EmbeddingSettingsViewModel.IsBusy))
        {
            SearchDocumentsCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(CanManageDocuments));
            LoadSampleDocumentsCommand.NotifyCanExecuteChanged();
        }
    }

    partial void OnQueryChanged(string value) => SearchDocumentsCommand.NotifyCanExecuteChanged();

    partial void OnShowingSearchResultsChanged(bool value) => OnPropertyChanged(nameof(ResultsHeading));

    partial void OnSelectedResultChanged(DocumentSearchHit? value)
    {
        OnPropertyChanged(nameof(SelectedChunks));
        OnPropertyChanged(nameof(ChunksHeading));
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(CanManageDocuments));
        SearchDocumentsCommand.NotifyCanExecuteChanged();
        LoadSampleDocumentsCommand.NotifyCanExecuteChanged();
    }
}
