using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.AI;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Owns generator selection and explicit document-index maintenance.</summary>
public sealed partial class EmbeddingSettingsViewModel : ObservableObject
{
    private readonly DocumentSearchService _search;

    public EmbeddingSettingsViewModel(DocumentSearchService search, IEnumerable<IEmbeddingGenerator<string, Embedding<float>>> generators)
    {
        ArgumentNullException.ThrowIfNull(search);
        ArgumentNullException.ThrowIfNull(generators);
        _search = search;
        Generators = generators.Select((generator, index) => new EmbeddingGeneratorOption(
            generator, generator.GetService<EmbeddingGeneratorDescriptor>()
                ?? throw new InvalidOperationException(
                    $"Embedding generator {index} did not expose its descriptor."), index)).ToArray();
        if (Generators.Select(option => option.Descriptor.Id).Distinct(StringComparer.Ordinal).Count() != Generators.Count)
            throw new ArgumentException("Embedding generators need distinct model IDs.", nameof(generators));

        SelectedOption = Generators.FirstOrDefault();
        StatusMessage = SelectedOption is null
            ? "No embedding generator is registered. Import documents now; configure one to index and search."
            : $"Selected {SelectedOption.Descriptor.DisplayName}. Import and index documents to start searching.";
    }

    public event EventHandler? DocumentsCleared;
    public event EventHandler? IndexCleared;
    public event EventHandler? ResultsInvalidated;
    public IReadOnlyList<EmbeddingGeneratorOption> Generators { get; }
    public IReadOnlyList<DocumentChunkingMode> ChunkingModes { get; } = Enum.GetValues<DocumentChunkingMode>();

    [ObservableProperty] private EmbeddingGeneratorOption? selectedOption;
    [ObservableProperty] private string dimensions = string.Empty;
    [ObservableProperty] private DocumentChunkingMode chunkingMode = DocumentChunkingMode.FixedCharacters;
    [ObservableProperty] private string chunkSize = "360";
    [ObservableProperty] private string chunkOverlap = "0";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private string indexProgressMessage = string.Empty;

    public IEmbeddingGenerator<string, Embedding<float>>? SelectedGenerator => SelectedOption?.Generator;
    public EmbeddingGeneratorDescriptor? SelectedDescriptor => SelectedOption?.Descriptor;
    public bool HasGenerator => SelectedOption is not null;
    public bool IsIdle => !IsBusy;
    public bool HasChunkParameters => ChunkingMode != DocumentChunkingMode.WholeDocument;
    public string ChunkSizeLabel => ChunkingMode == DocumentChunkingMode.Sentences
        ? "CHUNK SIZE (SENTENCES)" : "CHUNK SIZE (UTF-16 UNITS)";
    public string ChunkOverlapLabel => ChunkingMode == DocumentChunkingMode.Sentences
        ? "OVERLAP (SENTENCES)" : "OVERLAP (UTF-16 UNITS)";
    public string ChunkingDescription => ChunkingMode switch
    {
        DocumentChunkingMode.WholeDocument => "Embed each normalized document as one chunk. Provider input limits still apply.",
        DocumentChunkingMode.Sentences => "Portable punctuation-based splitting, not NLTokenizer. Splits after . ! ? followed by whitespace/end. Dots inside identifiers and decimals stay intact; abbreviations and closing quotes are not language-aware.",
        _ => "Normalize whitespace and prefer word boundaries. Size and overlap count UTF-16 units, not tokens. Surrogate pairs stay intact (a pair can exceed a size of one).",
    };

    // Declared aliases let MAUI compile XAML bindings to toolkit-generated commands.
    public IAsyncRelayCommand IndexDocuments => IndexDocumentsCommand;
    public IAsyncRelayCommand ClearIndex => ClearIndexCommand;
    public IAsyncRelayCommand ClearDocuments => ClearDocumentsCommand;
    public System.Windows.Input.ICommand CancelIndex => IndexDocumentsCancelCommand;

    public int? SelectedDimensions
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Dimensions))
                return null;
            if (!int.TryParse(Dimensions, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value <= 0)
                throw new ArgumentException("Dimensions must be a positive integer, or blank to use the model default.");
            return value;
        }
    }

    public DocumentChunkingOptions SelectedChunking
    {
        get
        {
            if (ChunkingMode == DocumentChunkingMode.WholeDocument)
                return new(DocumentChunkingMode.WholeDocument);
            if (!int.TryParse(ChunkSize, NumberStyles.None, CultureInfo.InvariantCulture, out var size))
                throw new ArgumentException("Chunk size must be an integer from 1 to 262144.");
            if (!int.TryParse(ChunkOverlap, NumberStyles.None, CultureInfo.InvariantCulture, out var overlap))
                throw new ArgumentException("Chunk overlap must be zero or greater and smaller than chunk size.");
            var options = new DocumentChunkingOptions(ChunkingMode, size, overlap);
            DocumentChunker.ValidateOptions(options);
            return options;
        }
    }

    private bool CanManageIndex() => HasGenerator && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanManageIndex), IncludeCancelCommand = true)]
    private async Task IndexDocumentsAsync(CancellationToken cancellationToken)
    {
        var option = SelectedOption ?? throw new InvalidOperationException("Choose an embedding generator.");
        ResultsInvalidated?.Invoke(this, EventArgs.Empty);
        IsBusy = true;
        IndexProgressMessage = string.Empty;
        StatusMessage = $"Indexing documents with {option.Descriptor.DisplayName}...";
        try
        {
            var progress = new Progress<DocumentIndexProgress>(value =>
            {
                if (!IsBusy || SelectedOption != option)
                    return;
                IndexProgressMessage = value.CompletedDocuments < value.TotalDocuments
                    ? $"Indexing {value.CompletedDocuments + 1}/{value.TotalDocuments}: " +
                        $"{value.IndexedChunks}/{value.TotalChunks} chunks from {value.DocumentName}"
                    : $"Indexed {value.TotalDocuments} documents.";
            });
            var result = await _search.IndexDocumentsAsync(
                option.Generator, option.Descriptor.Id, SelectedDimensions, progress, cancellationToken, SelectedChunking);
            StatusMessage = $"{result.TotalDocuments} {(result.TotalDocuments == 1 ? "document" : "documents")} indexed " +
                $"with {option.Descriptor.DisplayName} ({result.UpdatedDocuments} updated)." +
                (result.Notice is null ? string.Empty : $" {result.Notice}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusMessage = "Indexing cancelled.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Document indexing failed: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanManageIndex))]
    private async Task ClearIndexAsync()
    {
        var option = SelectedOption ?? throw new InvalidOperationException("Choose an embedding generator.");
        ResultsInvalidated?.Invoke(this, EventArgs.Empty);
        IsBusy = true;
        try
        {
            var cleared = await _search.ClearIndexAsync(option.Descriptor.Id, SelectedDimensions,
                chunking: SelectedChunking);
            StatusMessage = cleared
                ? $"Cleared the {option.Descriptor.DisplayName} index. Imported documents were not changed."
                : $"No {option.Descriptor.DisplayName} index exists for these dimensions and chunking settings.";
            IndexCleared?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            StatusMessage = $"Could not clear index: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanClearDocuments() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanClearDocuments))]
    private async Task ClearDocumentsAsync()
    {
        ResultsInvalidated?.Invoke(this, EventArgs.Empty);
        IsBusy = true;
        try
        {
            await _search.ClearDocumentsAsync();
            DocumentsCleared?.Invoke(this, EventArgs.Empty);
            StatusMessage = "Cleared imported documents and all derived indexes. Chat recordings were not changed.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Could not clear documents: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSelectedOptionChanging(EmbeddingGeneratorOption? value)
    {
        if (value is not null && !Generators.Contains(value))
            throw new ArgumentException("The selected embedding generator is not registered.", nameof(value));
    }

    partial void OnSelectedOptionChanged(EmbeddingGeneratorOption? value)
    {
        OnPropertyChanged(nameof(SelectedGenerator));
        OnPropertyChanged(nameof(SelectedDescriptor));
        OnPropertyChanged(nameof(HasGenerator));
        IndexDocumentsCommand.NotifyCanExecuteChanged();
        ClearIndexCommand.NotifyCanExecuteChanged();
        StatusMessage = value is null
            ? "No embedding generator is registered."
            : $"Selected {value.Descriptor.DisplayName}. Index documents for this model, dimensions and chunking.";
    }

    partial void OnChunkingModeChanged(DocumentChunkingMode value)
    {
        ChunkSize = value == DocumentChunkingMode.Sentences ? "1" : "360";
        ChunkOverlap = "0";
        OnPropertyChanged(nameof(HasChunkParameters));
        OnPropertyChanged(nameof(ChunkSizeLabel));
        OnPropertyChanged(nameof(ChunkOverlapLabel));
        OnPropertyChanged(nameof(ChunkingDescription));
        ConfigurationChanged();
    }

    partial void OnChunkSizeChanged(string value) => ConfigurationChanged();
    partial void OnChunkOverlapChanged(string value) => ConfigurationChanged();
    partial void OnDimensionsChanged(string value) => ConfigurationChanged();

    private void ConfigurationChanged()
    {
        ResultsInvalidated?.Invoke(this, EventArgs.Empty);
        try
        {
            _ = SelectedDimensions;
            _ = SelectedChunking;
            StatusMessage = "Settings changed. Index documents for this model, dimensions and chunking, then search.";
        }
        catch (ArgumentException exception)
        {
            StatusMessage = $"Invalid embedding settings: {exception.Message}";
        }
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsIdle));
        IndexDocumentsCommand.NotifyCanExecuteChanged();
        ClearIndexCommand.NotifyCanExecuteChanged();
        ClearDocumentsCommand.NotifyCanExecuteChanged();
    }
}

public sealed record EmbeddingGeneratorOption(
    IEmbeddingGenerator<string, Embedding<float>> Generator, EmbeddingGeneratorDescriptor Descriptor, int Index)
{
    public string AutomationId => $"EmbeddingGenerator{Index}Radio";
}
