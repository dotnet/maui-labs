using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Runs the selected real document reader against the imported file.</summary>
public sealed partial class DocumentPlaygroundViewModel : ObservableObject
{
    private readonly DocumentInputService _input;
    private readonly DocumentReadingService _reading;
    private SelectedDocument? _selected;

    public DocumentPlaygroundViewModel(
        DocumentReadingService reading, DocumentInputService input, DocumentSettingsViewModel settings)
    {
        _reading = reading;
        _input = input;
        Settings = settings;
        Settings.PropertyChanged += SettingsPropertyChanged;
        StatusMessage = Settings.HasReader
            ? $"Selected {Settings.SelectedDescriptor!.DisplayName}. Choose a document to read."
            : "No document reader available. On Apple, use iOS or Mac Catalyst 26+; for cloud, configure Document Intelligence or Foundry.";
    }

    public DocumentSettingsViewModel Settings { get; }

    [ObservableProperty] private string selectedName = "No document selected.";
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private string resultHeading = "DOCUMENT RESULT";
    [ObservableProperty] private string resultOutput = string.Empty;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private ImageSource? selectedPreview;

    public bool HasDocument => _selected is not null;
    public bool HasResult => ResultOutput.Length > 0;
    public bool IsIdle => !IsBusy;
    public bool IsCloudSelected => Settings.SelectedDescriptor?.IsCloud == true;
    public bool CanRead => !IsBusy && HasDocument && Settings.HasReader;
    public IAsyncRelayCommand ChooseDocument => ChooseDocumentCommand;
    public IAsyncRelayCommand UseSamplePdf => UseSamplePdfCommand;
    public IAsyncRelayCommand UseSampleImage => UseSampleImageCommand;
    public IAsyncRelayCommand ReadDocument => ReadDocumentCommand;
    public IRelayCommand RemoveDocumentAction => RemoveDocumentCommand;
    public System.Windows.Input.ICommand CancelReading => ReadDocumentCancelCommand;

    private bool CanChooseDocument() => !IsBusy;
    private bool CanReadDocument() => CanRead;

    [RelayCommand(CanExecute = nameof(CanChooseDocument))]
    private Task ChooseDocumentAsync() => SelectDocumentAsync(() => _input.PickAsync());

    [RelayCommand(CanExecute = nameof(CanChooseDocument))]
    private Task UseSamplePdfAsync() => SelectDocumentAsync(async () => await _input.LoadSamplePdfAsync());

    [RelayCommand(CanExecute = nameof(CanChooseDocument))]
    private Task UseSampleImageAsync() => SelectDocumentAsync(async () => await _input.LoadSampleImageAsync());

    private bool CanRemoveDocument() => !IsBusy && HasDocument;

    [RelayCommand(CanExecute = nameof(CanRemoveDocument))]
    private void RemoveDocument()
    {
        _selected = null;
        SelectedName = "No document selected.";
        SelectedPreview = null;
        ResultOutput = string.Empty;
        ResultHeading = "DOCUMENT RESULT";
        StatusMessage = "Choose a PDF or image using the + menu.";
        OnPropertyChanged(nameof(HasDocument));
        OnPropertyChanged(nameof(CanRead));
        ReadDocumentCommand.NotifyCanExecuteChanged();
        RemoveDocumentCommand.NotifyCanExecuteChanged();
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
            RemoveDocumentCommand.NotifyCanExecuteChanged();
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
            var result = await _reading.ReadAsync(option.Reader, file, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            ResultOutput = result.Output;
            ResultHeading = option.Descriptor.DisplayName.ToUpperInvariant() + " RESULT";
            StatusMessage = $"Read {result.PageCount} page(s) with {option.Descriptor.DisplayName}.";
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
}
