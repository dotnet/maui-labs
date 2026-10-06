using System.ComponentModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DataIngestion;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Runs the selected real document reader against the imported file.</summary>
public sealed partial class DocumentPlaygroundViewModel : ObservableObject
{
    private readonly DocumentInputService _input;
    private SelectedDocument? _selected;
    private int _selectionVersion;

    public DocumentPlaygroundViewModel(DocumentInputService input, DocumentSettingsViewModel settings)
    {
        _input = input;
        Settings = settings;
        Settings.PropertyChanged += SettingsPropertyChanged;
        StatusMessage = Settings.HasReader
            ? $"Selected {Settings.SelectedDescriptor!.DisplayName}. Choose a document to read."
            : "No document reader available. On Apple, use iOS or Mac Catalyst 26+; for cloud, configure Document Intelligence.";
    }

    public DocumentSettingsViewModel Settings { get; }

    [ObservableProperty] private string selectedName = "No document selected.";
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private string resultHeading = "DOCUMENT RESULT";
    [ObservableProperty] private string resultOutput = string.Empty;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private ImageSource? selectedPreview;

    public int SelectionVersion => _selectionVersion;
    public bool HasDocument => _selected is not null;
    public bool HasResult => ResultOutput.Length > 0;
    public bool IsIdle => !IsBusy;
    public bool IsCloudSelected => Settings.SelectedDescriptor?.IsCloud == true;
    public bool CanRead => !IsBusy && HasDocument && Settings.HasReader;
    public IAsyncRelayCommand ChooseDocument => ChooseDocumentCommand;
    public IAsyncRelayCommand UseSamplePdf => UseSamplePdfCommand;
    public IAsyncRelayCommand UseSampleImage => UseSampleImageCommand;
    public IAsyncRelayCommand ReadDocument => ReadDocumentCommand;
    public System.Windows.Input.ICommand RemoveDocumentAction => RemoveDocumentCommand;
    public System.Windows.Input.ICommand CancelReading => ReadDocumentCancelCommand;

    private bool CanChooseDocument() => !IsBusy;
    private bool CanReadDocument() => CanRead;

    [RelayCommand(CanExecute = nameof(CanChooseDocument))]
    private Task ChooseDocumentAsync() => SelectDocumentAsync(() => _input.PickAsync());

    [RelayCommand(CanExecute = nameof(CanChooseDocument))]
    private Task UseSamplePdfAsync() => SelectDocumentAsync(async () => await _input.LoadSamplePdfAsync());

    [RelayCommand(CanExecute = nameof(CanChooseDocument))]
    private Task UseSampleImageAsync() => SelectDocumentAsync(async () => await _input.LoadSampleImageAsync());

    [RelayCommand(CanExecute = nameof(CanChooseDocument))]
    private void RemoveDocument()
    {
        _selected = null;
        _selectionVersion++;
        SelectedName = "No document selected.";
        SelectedPreview = null;
        ResultOutput = string.Empty;
        ResultHeading = "DOCUMENT RESULT";
        StatusMessage = "Choose a PDF or image using the + menu.";
        OnPropertyChanged(nameof(HasDocument));
        OnPropertyChanged(nameof(CanRead));
        ReadDocumentCommand.NotifyCanExecuteChanged();
    }

    private async Task SelectDocumentAsync(Func<Task<SelectedDocument?>> load)
    {
        IsBusy = true;
        Settings.IsBusy = true;
        try
        {
            var file = await load();
            if (file is null)
            {
                StatusMessage = "File selection cancelled.";
                return;
            }
            _selected = file;
            _selectionVersion++;
            SelectedName = file.FileName;
            SelectedPreview = file.MediaType.StartsWith("image/", StringComparison.Ordinal)
                ? ImageSource.FromStream(() => new MemoryStream(file.Bytes, writable: false))
                : ImageSource.FromFile("folder.png");
            ResultOutput = string.Empty;
            ResultHeading = "DOCUMENT RESULT";
            StatusMessage = $"Selected {file.FileName}. Choose a reader in settings, then select Read.";
            OnPropertyChanged(nameof(HasDocument));
            OnPropertyChanged(nameof(CanRead));
            ReadDocumentCommand.NotifyCanExecuteChanged();
        }
        catch (Exception exception)
        {
            StatusMessage = $"Could not select document: {exception.Message}";
        }
        finally
        {
            Settings.IsBusy = false;
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanReadDocument), IncludeCancelCommand = true)]
    private async Task ReadDocumentAsync(CancellationToken cancellationToken)
    {
        var option = Settings.SelectedOption ?? throw new InvalidOperationException("Choose a document reader.");
        var file = _selected ?? throw new InvalidOperationException("Choose a document first.");
        IsBusy = true;
        Settings.IsBusy = true;
        ResultOutput = string.Empty;
        StatusMessage = $"Reading {file.FileName} with {option.Descriptor.DisplayName}...";
        try
        {
            using var stream = new MemoryStream(file.Bytes, writable: false);
            var result = await option.Reader.ReadAsync(stream, file.FileName, file.MediaType, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            ResultOutput = Format(result);
            ResultHeading = option.Descriptor.DisplayName.ToUpperInvariant() + " RESULT";
            StatusMessage = $"Read {result.Sections.Count} page(s) with {option.Descriptor.DisplayName}.";
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            StatusMessage = option.Descriptor.IsCloud
                ? "Document reading timed out; a submitted Azure operation may continue server-side."
                : "Document reading timed out.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = option.Descriptor.IsCloud
                ? "Document reading cancelled; a submitted Azure operation may continue server-side."
                : "Document reading cancelled.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Document reading failed with {option.Descriptor.DisplayName}: {exception.Message}";
        }
        finally
        {
            Settings.IsBusy = false;
            IsBusy = false;
        }
    }

    private void SettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DocumentSettingsViewModel.SelectedOption))
        {
            ResultOutput = string.Empty;
            ResultHeading = "DOCUMENT RESULT";
            StatusMessage = Settings.HasReader
                ? $"Selected {Settings.SelectedDescriptor!.DisplayName}. " +
                    (HasDocument ? "Read the selected document with this reader." : "Choose a document to read.")
                : "No document reader selected.";
            OnPropertyChanged(nameof(IsCloudSelected));
            OnPropertyChanged(nameof(CanRead));
            ReadDocumentCommand.NotifyCanExecuteChanged();
        }
    }

    partial void OnResultOutputChanged(string value) => OnPropertyChanged(nameof(HasResult));

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(CanRead));
        ChooseDocumentCommand.NotifyCanExecuteChanged();
        UseSamplePdfCommand.NotifyCanExecuteChanged();
        UseSampleImageCommand.NotifyCanExecuteChanged();
        RemoveDocumentCommand.NotifyCanExecuteChanged();
        ReadDocumentCommand.NotifyCanExecuteChanged();
    }

    private static string Format(IngestionDocument document)
    {
        var output = new StringBuilder().AppendLine($"Document: {document.Identifier}");
        foreach (var section in document.Sections)
        {
            output.AppendLine().AppendLine($"Page {section.PageNumber}");
            foreach (var element in section.Elements)
            {
                var type = element switch
                {
                    IngestionDocumentHeader => "Header",
                    IngestionDocumentTable => "Table",
                    IngestionDocumentParagraph => "Paragraph",
                    _ => "Element",
                };
                output.Append(type).Append(": ").AppendLine(element.Text ?? element.GetMarkdown());
                output.AppendLine("Markdown:").AppendLine(element.GetMarkdown()).AppendLine();
            }
        }
        return output.ToString();
    }
}
