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

    public DocumentSettingsViewModel(IEnumerable<IngestionDocumentReader> readers, IEnumerable<DocumentExtractionClient> clients)
    {
        Readers = readers.Select((reader, index) => new DocumentReaderOption(reader, index)).ToArray();
        Clients = clients.Select((client, index) => new DocumentExtractionClientOption(client, index)).ToArray();
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
    [ObservableProperty] private string extractorName = string.Empty;
    [ObservableProperty] private string appleRecognitionLanguages = string.Empty;

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
    public bool CanSelectAppleOptions => IsClientMode && SelectedClientOption?.Descriptor.Id == "apple-vision-document";
    public string OperationLabel => IsExtractionMode ? "Extract" : "Read";

    public DocumentExtractionOptions CreateOptions()
    {
        var options = new DocumentExtractionOptions
        {
            ExtractorName = CanSelectModel && !string.IsNullOrWhiteSpace(ExtractorName) ? ExtractorName.Trim() : null,
        };
        if (CanSelectAppleOptions && !string.IsNullOrWhiteSpace(AppleRecognitionLanguages))
            options.AdditionalProperties["apple.vision.recognitionLanguages"] =
                AppleRecognitionLanguages.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return options;
    }

    partial void OnSelectedOptionChanged(DocumentReaderOption? value)
    {
        OnPropertyChanged(nameof(SelectedReader));
        OnPropertyChanged(nameof(SelectedDescriptor));
        OnPropertyChanged(nameof(HasReader));
        NotifySelectionChanged();
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsIdle));

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
        OnPropertyChanged(nameof(CanSelectAppleOptions));
    }
}
