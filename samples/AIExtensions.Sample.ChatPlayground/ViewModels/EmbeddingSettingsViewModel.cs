using System.Globalization;
using AIExtensions.Sample.ChatPlayground.Features.Embeddings;
using AIExtensions.Sample.ChatPlayground.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.AI;

namespace AIExtensions.Sample.ChatPlayground.ViewModels;

/// <summary>Owns generator selection and explicit document-index maintenance.</summary>
public sealed partial class EmbeddingSettingsViewModel : ObservableObject
{
    private readonly DocumentSearchService _search;

    public EmbeddingSettingsViewModel(
        DocumentSearchService search, IEnumerable<IEmbeddingGenerator<string, Embedding<float>>> generators)
    {
        ArgumentNullException.ThrowIfNull(search);
        ArgumentNullException.ThrowIfNull(generators);
        _search = search;
        Generators = generators.Select((generator, index) => new EmbeddingGeneratorOption(
            generator, generator.GetService<EmbeddingGeneratorDescriptor>()
                ?? throw new InvalidOperationException(
                    $"Embedding generator {index} did not expose its descriptor."), index)).ToArray();
        if (Generators.Select(option => option.Descriptor.Id).Distinct(StringComparer.Ordinal).Count() != Generators.Count ||
            Generators.Select(option => option.Descriptor.IndexIdentity).Distinct(StringComparer.Ordinal).Count() != Generators.Count)
            throw new ArgumentException("Embedding generators need distinct IDs and model identities.", nameof(generators));

        SelectedOption = Generators.FirstOrDefault();
        StatusMessage = SelectedOption is null
            ? "No embedding generator is registered. Import documents now; configure one to index and search."
            : $"Selected {SelectedOption.Descriptor.DisplayName}. Import and index documents to start searching.";
    }

    public event EventHandler? DocumentsCleared;
    public event EventHandler? IndexCleared;
    public IReadOnlyList<EmbeddingGeneratorOption> Generators { get; }

    [ObservableProperty] private EmbeddingGeneratorOption? selectedOption;
    [ObservableProperty] private string dimensions = string.Empty;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private string indexProgressMessage = string.Empty;

    public IEmbeddingGenerator<string, Embedding<float>>? SelectedGenerator => SelectedOption?.Generator;
    public EmbeddingGeneratorDescriptor? SelectedDescriptor => SelectedOption?.Descriptor;
    public bool HasGenerator => SelectedOption is not null;
    public bool IsIdle => !IsBusy;

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

    private bool CanManageIndex() => HasGenerator && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanManageIndex), IncludeCancelCommand = true)]
    private async Task IndexDocumentsAsync(CancellationToken cancellationToken)
    {
        var option = SelectedOption ?? throw new InvalidOperationException("Choose an embedding generator.");
        IsBusy = true;
        StatusMessage = $"Indexing documents with {option.Descriptor.DisplayName}...";
        try
        {
            var progress = new Progress<DocumentIndexProgress>(value =>
            {
                if (!IsBusy || SelectedOption != option)
                    return;
                IndexProgressMessage = value.CompletedDocuments < value.TotalDocuments
                    ? $"Indexing {value.CompletedDocuments + 1}/{value.TotalDocuments}: " +
                        $"{value.IndexedChunks}/{value.TotalChunks} excerpts from {value.DocumentName}"
                    : $"Indexed {value.TotalDocuments} documents.";
            });
            var result = await _search.IndexDocumentsAsync(
                option.Generator, option.Descriptor.IndexIdentity, SelectedDimensions, progress, cancellationToken);
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
        IsBusy = true;
        try
        {
            var cleared = await _search.ClearIndexAsync(option.Descriptor.IndexIdentity, SelectedDimensions);
            StatusMessage = cleared
                ? $"Cleared the {option.Descriptor.DisplayName} index. Imported documents were not changed."
                : $"No {option.Descriptor.DisplayName} index exists for these dimensions.";
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
            : $"Selected {value.Descriptor.DisplayName}. Index documents for this model and dimensions.";
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
