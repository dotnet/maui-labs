using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DataIngestion;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Owns document reader selection, independently of Chat and Embeddings.</summary>
public sealed partial class DocumentSettingsViewModel : ObservableObject
{
    public DocumentSettingsViewModel(IEnumerable<IngestionDocumentReader> readers)
    {
        ArgumentNullException.ThrowIfNull(readers);
        Readers = readers.Select((reader, index) => new DocumentReaderOption(
            reader,
            reader is DescribedDocumentReader described
                ? described.Descriptor
                : throw new InvalidOperationException($"Document reader {index} did not expose its descriptor."),
            index)).ToArray();
        if (Readers.Select(option => option.Descriptor.Id).Distinct(StringComparer.Ordinal).Count() != Readers.Count)
            throw new ArgumentException("Document readers need distinct IDs.", nameof(readers));
        SelectedOption = Readers.FirstOrDefault();
    }

    public IReadOnlyList<DocumentReaderOption> Readers { get; }

    [ObservableProperty] private DocumentReaderOption? selectedOption;
    [ObservableProperty] private bool isBusy;

    public IngestionDocumentReader? SelectedReader => SelectedOption?.Reader;
    public DocumentReaderDescriptor? SelectedDescriptor => SelectedOption?.Descriptor;
    public bool HasReader => SelectedOption is not null;
    public bool IsIdle => !IsBusy;

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
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsIdle));
}

public sealed record DocumentReaderOption(
    IngestionDocumentReader Reader, DocumentReaderDescriptor Descriptor, int Index)
{
    public string AutomationId => $"DocumentReader{Index}Radio";
}
