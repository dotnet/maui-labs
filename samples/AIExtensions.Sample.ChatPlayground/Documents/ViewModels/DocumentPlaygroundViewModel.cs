using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DataIngestion;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Runs the selected real document reader against the imported file.</summary>
public sealed partial class DocumentPlaygroundViewModel : ObservableObject
{
    private readonly DocumentInputService _input;
    private readonly DocumentReadingService _reading;
    private readonly DocumentExtractionService _extraction;
    private SelectedDocument? _selected;
    private string _normalizedOutput = string.Empty;
    private string _rawOutput = string.Empty;

    public DocumentPlaygroundViewModel(
        DocumentReadingService reading, DocumentExtractionService extraction,
        DocumentInputService input, DocumentSettingsViewModel settings)
    {
        _reading = reading;
        _extraction = extraction;
        _input = input;
        Settings = settings;
        Settings.PropertyChanged += SettingsPropertyChanged;
        StatusMessage = Settings.HasSelection
            ? $"Selected {Settings.CurrentName}. Choose a document."
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
    public bool HasRawResult => _rawOutput.Length > 0;
    public bool CanInspect => !IsBusy && HasResult;
    public bool IsIdle => !IsBusy;
    public bool IsCloudSelected => Settings.CurrentIsCloud;
    public bool CanRead => !IsBusy && HasDocument && Settings.HasSelection;
    public string OperationLabel => Settings.OperationLabel;
    public IAsyncRelayCommand ChooseDocument => ChooseDocumentCommand;
    public IAsyncRelayCommand UseSamplePdf => UseSamplePdfCommand;
    public IAsyncRelayCommand UseSampleImage => UseSampleImageCommand;
    public IAsyncRelayCommand ReadDocument => ReadDocumentCommand;
    public IRelayCommand RemoveDocumentAction => RemoveDocumentCommand;
    public System.Windows.Input.ICommand CancelReading => ReadDocumentCancelCommand;
    public IRelayCommand InspectNormalized => InspectNormalizedResultCommand;
    public IRelayCommand InspectRaw => InspectRawResultCommand;

    [RelayCommand(CanExecute = nameof(CanInspect))]
    private void InspectNormalizedResult() => ResultOutput = _normalizedOutput;

    private bool CanInspectRaw() => CanInspect && HasRawResult;

    [RelayCommand(CanExecute = nameof(CanInspectRaw))]
    private void InspectRawResult() => ResultOutput = _rawOutput;

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
        ClearResult();
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
            ClearResult();
            ResultHeading = "DOCUMENT RESULT";
            StatusMessage = $"Selected {file.FileName}. Choose an API and provider in settings.";
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
        var file = _selected ?? throw new InvalidOperationException("Choose a document first.");
        var name = Settings.CurrentName;
        IsBusy = true;
        Settings.IsBusy = true;
        ClearResult();
        StatusMessage = $"Processing {file.FileName} with {name}...";
        try
        {
            int pages;
            if (Settings.IsExtractionMode)
            {
                var client = Settings.SelectedClientOption?.Client
                    ?? throw new InvalidOperationException("Choose a document extraction client.");
                var progress = new Progress<DocumentExtractionProgress>(update =>
                {
                    if (IsBusy)
                        StatusMessage = $"Extracted {update.PagesProcessed?.ToString() ?? "?"}/{update.TotalPages?.ToString() ?? "?"} pages.";
                });
                var result = await _extraction.ExtractAsync(
                    client, file, Settings.StreamPages, Settings.CreateOptions(), progress, cancellationToken);
                ResultOutput = result.Output;
                _rawOutput = result.RawOutput;
                pages = result.Result.Pages.Count;
            }
            else
            {
                var reader = Settings.IsReaderMode
                    ? Settings.SelectedReader ?? throw new InvalidOperationException("Choose a document reader.")
                    : new OcrDocumentReader(
                        Settings.SelectedClientOption?.Client ?? throw new InvalidOperationException("Choose a document extraction client."),
                        Settings.CreateOptions());
                var result = await _reading.ReadAsync(reader, file, cancellationToken);
                ResultOutput = result.Output;
                pages = result.PageCount;
            }
            cancellationToken.ThrowIfCancellationRequested();
            _normalizedOutput = ResultOutput;
            OnPropertyChanged(nameof(HasRawResult));
            ResultHeading = name.ToUpperInvariant() + " RESULT";
            StatusMessage = $"Processed {pages} page(s) with {name} using {Settings.Mode}.";
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            StatusMessage = Settings.CurrentIsCloud
                ? "Document reading timed out; a submitted Azure operation may continue server-side."
                : "Document reading timed out.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = Settings.CurrentIsCloud
                ? "Document reading cancelled; a submitted Azure operation may continue server-side."
                : "Document reading cancelled.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Document processing failed with {name}: {exception.Message}";
        }
        finally
        {
            Settings.IsBusy = false;
            IsBusy = false;
        }
    }

    private void SettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DocumentSettingsViewModel.SelectedOption) ||
            e.PropertyName == nameof(DocumentSettingsViewModel.SelectedClientOption) ||
            e.PropertyName == nameof(DocumentSettingsViewModel.Mode))
        {
            ClearResult();
            ResultHeading = "DOCUMENT RESULT";
            StatusMessage = Settings.HasSelection
                ? $"Selected {Settings.CurrentName}. Choose a file, then {Settings.OperationLabel.ToLowerInvariant()}."
                : "No document provider selected for this API.";
            OnPropertyChanged(nameof(IsCloudSelected));
            OnPropertyChanged(nameof(CanRead));
            OnPropertyChanged(nameof(OperationLabel));
            ReadDocumentCommand.NotifyCanExecuteChanged();
        }
    }

    private void ClearResult()
    {
        ResultOutput = string.Empty;
        _normalizedOutput = string.Empty;
        _rawOutput = string.Empty;
        OnPropertyChanged(nameof(HasRawResult));
    }

    partial void OnResultOutputChanged(string value)
    {
        OnPropertyChanged(nameof(HasResult));
        OnPropertyChanged(nameof(CanInspect));
        InspectNormalizedResultCommand.NotifyCanExecuteChanged();
        InspectRawResultCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(CanRead));
        ChooseDocumentCommand.NotifyCanExecuteChanged();
        UseSamplePdfCommand.NotifyCanExecuteChanged();
        UseSampleImageCommand.NotifyCanExecuteChanged();
        RemoveDocumentCommand.NotifyCanExecuteChanged();
        ReadDocumentCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanInspect));
        InspectNormalizedResultCommand.NotifyCanExecuteChanged();
        InspectRawResultCommand.NotifyCanExecuteChanged();
    }
}
