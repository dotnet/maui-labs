using System.Collections.ObjectModel;
using System.ComponentModel;
using AIExtensions.Sample.ChatPlayground.Features.Embeddings.Models;
using AIExtensions.Sample.ChatPlayground.Features.Embeddings.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIExtensions.Sample.ChatPlayground.Features.Embeddings.ViewModels;

/// <summary>Imports text documents and searches their separate, model-specific indexes.</summary>
public sealed partial class EmbeddingPlaygroundViewModel : ObservableObject
{
    private readonly DocumentStore _documents;
    private readonly DocumentSearchService _search;

    public EmbeddingPlaygroundViewModel(
        DocumentStore documents, DocumentSearchService search, EmbeddingSettingsViewModel settings)
    {
        _documents = documents;
        _search = search;
        Settings = settings;
        Settings.PropertyChanged += SettingsPropertyChanged;
        Settings.IndexCleared += (_, _) =>
        {
            Results.Clear();
            ShowingSearchResults = false;
            StatusMessage = "Index cleared. Imported documents are unchanged; index them to search again.";
        };
        Settings.DocumentsCleared += (_, _) =>
        {
            Documents.Clear();
            Results.Clear();
            ShowingSearchResults = false;
            StatusMessage = "Imported documents and indexes cleared.";
        };
        StatusMessage = "Import a Markdown or text document, then index it in settings.";
    }

    public EmbeddingSettingsViewModel Settings { get; }
    public ObservableCollection<ImportedDocument> Documents { get; } = [];
    public ObservableCollection<DocumentSearchHit> Results { get; } = [];

    [ObservableProperty] private string query = string.Empty;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private bool showingSearchResults;
    [ObservableProperty] private bool isBusy;

    public bool IsIdle => !IsBusy;
    public string ResultsHeading => ShowingSearchResults ? "SEARCH RESULTS" : "IMPORTED DOCUMENTS";
    public IAsyncRelayCommand Search => SearchDocumentsCommand;
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
        IsBusy = true;
        Settings.IsBusy = true;
        try
        {
            var document = await _documents.ImportAsync(fileName, source, cancellationToken);
            Documents.Insert(0, document);
            Results.Clear();
            ShowingSearchResults = false;
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

    private bool CanSearch() => !IsBusy && !Settings.IsBusy &&
        Settings.HasGenerator && !string.IsNullOrWhiteSpace(Query);

    [RelayCommand(CanExecute = nameof(CanSearch), IncludeCancelCommand = true)]
    private async Task SearchDocumentsAsync(CancellationToken cancellationToken)
    {
        var option = Settings.SelectedOption ?? throw new InvalidOperationException("Choose an embedding generator.");
        IsBusy = true;
        Settings.IsBusy = true;
        StatusMessage = $"Searching with {option.Descriptor.DisplayName}...";
        try
        {
            var result = await _search.SearchAsync(
                Query, option.Generator, option.Descriptor.IndexIdentity, Settings.SelectedDimensions,
                cancellationToken);
            Results.Clear();
            foreach (var hit in result.Hits)
                Results.Add(hit);
            ShowingSearchResults = true;
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
            Results.Clear();
            ShowingSearchResults = false;
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
            nameof(EmbeddingSettingsViewModel.Dimensions))
        {
            Results.Clear();
            ShowingSearchResults = false;
            StatusMessage = Settings.HasGenerator
                ? "Index documents for the selected model and dimensions, then search."
                : "Configure an embedding generator to search imported documents.";
        }
        if (e.PropertyName is nameof(EmbeddingSettingsViewModel.SelectedOption) or
            nameof(EmbeddingSettingsViewModel.IsBusy))
            SearchDocumentsCommand.NotifyCanExecuteChanged();
    }

    partial void OnQueryChanged(string value) => SearchDocumentsCommand.NotifyCanExecuteChanged();

    partial void OnShowingSearchResultsChanged(bool value) => OnPropertyChanged(nameof(ResultsHeading));

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsIdle));
        SearchDocumentsCommand.NotifyCanExecuteChanged();
    }
}
