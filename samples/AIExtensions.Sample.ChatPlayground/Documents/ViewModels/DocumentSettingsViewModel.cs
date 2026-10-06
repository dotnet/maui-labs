using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DataIngestion;
using Microsoft.Extensions.DocumentExtraction;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Owns document reader selection, independently of Chat and Embeddings.</summary>
public sealed partial class DocumentSettingsViewModel : ObservableObject
{
    public DocumentSettingsViewModel(IEnumerable<IngestionDocumentReader> readers) : this(readers, [])
    {
    }

    public DocumentSettingsViewModel(IEnumerable<IngestionDocumentReader> readers, IEnumerable<IDocumentExtractionClient> clients)
    {
        ArgumentNullException.ThrowIfNull(readers);
        ArgumentNullException.ThrowIfNull(clients);
        Readers = readers.Select((reader, index) => new DocumentReaderOption(reader, index)).ToArray();
        Clients = clients.Select((client, index) => new DocumentExtractionClientOption(client, index)).ToArray();
        if (Readers.Select(option => option.Descriptor.Id).Distinct(StringComparer.Ordinal).Count() != Readers.Count)
            throw new ArgumentException("Document readers need distinct IDs.", nameof(readers));
        if (Clients.Select(option => option.Descriptor.Id).Distinct(StringComparer.Ordinal).Count() != Clients.Count)
            throw new ArgumentException("Document extraction clients need distinct IDs.", nameof(clients));
        SelectedOption = Readers.FirstOrDefault();
        SelectedClientOption = Clients.FirstOrDefault();
        Mode = Clients.Count > 0 ? DocumentOperationMode.Extraction : DocumentOperationMode.Reader;
    }

    public IReadOnlyList<DocumentReaderOption> Readers { get; }
    public IReadOnlyList<DocumentExtractionClientOption> Clients { get; }

    [ObservableProperty] private DocumentReaderOption? selectedOption;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private DocumentExtractionClientOption? selectedClientOption;
    [ObservableProperty] private DocumentOperationMode mode;
    [ObservableProperty] private bool streamPages = true;
    [ObservableProperty] private string modelId = string.Empty;

    public IngestionDocumentReader? SelectedReader => SelectedOption?.Reader;
    public DocumentReaderDescriptor? SelectedDescriptor => SelectedOption?.Descriptor;
    public bool HasReader => SelectedOption is not null;
    public bool IsIdle => !IsBusy;
    public bool IsReaderMode => Mode == DocumentOperationMode.Reader;
    public bool IsClientMode => !IsReaderMode;
    public bool IsExtractionMode => Mode == DocumentOperationMode.Extraction;
    public bool HasSelection => IsReaderMode ? HasReader : SelectedClientOption is not null;
    public string CurrentName => IsReaderMode ? SelectedDescriptor?.DisplayName ?? "No reader" :
        SelectedClientOption?.Descriptor.DisplayName ?? "No client";
    public bool CurrentIsCloud => IsReaderMode ? SelectedDescriptor?.IsCloud == true : SelectedClientOption?.Descriptor.IsCloud == true;
    public bool CanSelectModel => IsClientMode && SelectedClientOption?.Descriptor.IsCloud == true;
    public string OperationLabel => IsExtractionMode ? "Extract" : "Read";

    public DocumentExtractionOptions? CreateOptions() =>
        !CanSelectModel || string.IsNullOrWhiteSpace(ModelId) ? null : new DocumentExtractionOptions { ModelId = ModelId.Trim() };

    partial void OnSelectedOptionChanging(DocumentReaderOption? value)
    {
        if (value is not null && !Readers.Contains(value))
            throw new ArgumentException("The selected document reader is not registered.", nameof(value));
    }

    partial void OnSelectedOptionChanged(DocumentReaderOption? value)
    {
        OnPropertyChanged(nameof(SelectedReader));
        OnPropertyChanged(nameof(SelectedDescriptor));
        OnPropertyChanged(nameof(HasReader));
        NotifySelectionChanged();
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsIdle));

    partial void OnSelectedClientOptionChanging(DocumentExtractionClientOption? value)
    {
        if (value is not null && !Clients.Contains(value))
            throw new ArgumentException("The selected document extraction client is not registered.", nameof(value));
    }

    partial void OnSelectedClientOptionChanged(DocumentExtractionClientOption? value) => NotifySelectionChanged();

    partial void OnModeChanged(DocumentOperationMode value)
    {
        OnPropertyChanged(nameof(IsReaderMode));
        OnPropertyChanged(nameof(IsClientMode));
        OnPropertyChanged(nameof(IsExtractionMode));
        OnPropertyChanged(nameof(OperationLabel));
        NotifySelectionChanged();
    }

    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CurrentName));
        OnPropertyChanged(nameof(CurrentIsCloud));
        OnPropertyChanged(nameof(CanSelectModel));
    }
}

public enum DocumentOperationMode { Reader, Extraction, OcrReader }

public sealed record DocumentReaderOption(IngestionDocumentReader Reader, int Index)
{
    public DocumentReaderDescriptor Descriptor { get; } =
        Reader is DescribedDocumentReader described
            ? described.Descriptor
            : throw new InvalidOperationException($"Document reader {Index} did not expose its descriptor.");

    public string AutomationId => $"DocumentReader{Index}Radio";
}
