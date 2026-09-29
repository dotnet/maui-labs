using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DocumentExtraction;
using Microsoft.Maui.ApplicationModel.DataTransfer;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Coordinates file input, Apple Vision extraction, and document result inspection.</summary>
public sealed partial class DocumentPlaygroundViewModel : ObservableObject
{
    private const int MaximumDisplayedNodes = 2000;

    private readonly DocumentExtractionRunner _runner;
    private readonly DocumentInputService _inputService;
    private DocumentInput? _selectedInput;
    private DocumentExtractionResult? _result;

    public DocumentPlaygroundViewModel(
        DocumentExtractionRunner runner,
        DocumentInputService inputService,
        DocumentSettingsViewModel settings)
    {
        _runner = runner;
        _inputService = inputService;
        Settings = settings;
        Settings.PropertyChanged += SettingsPropertyChanged;
        StatusMessage = settings.IsSupported
            ? "Choose an image or PDF, or load the sample document."
            : settings.AvailabilityMessage;
    }

    public DocumentSettingsViewModel Settings { get; }

    public ObservableCollection<DocumentResultNode> Nodes { get; } = [];
    public ObservableCollection<DocumentPagePreviewViewModel> PreviewPages { get; } = [];

    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private string selectedFileName = "No document selected";
    [ObservableProperty] private string selectedDocumentDetails = "No document selected.";
    [ObservableProperty] private string resultDetails = "No extraction result.";
    [ObservableProperty] private DocumentResultNode? selectedNode;
    [ObservableProperty] private DocumentInspectionMode selectedInspectionMode;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private double progress;
    [ObservableProperty] private bool isProgressVisible;

    public bool IsIdle => !IsBusy;
    public bool IsSupported => Settings.IsSupported;
    public bool CanScan => Settings.CanScan && !IsBusy;
    public bool HasSelection => _selectedInput is not null;
    public bool HasPreviewPages => PreviewPages.Count > 0;
    public bool HasResult => _result is not null;
    public bool CanInspectSelectedNode => SelectedNode?.HasRawJson == true;
    public string ComposerHint => HasSelection
        ? $"Extract with {Settings.ProviderName}"
        : "Add an image or PDF to extract";
    public string InspectionTitle => SelectedInspectionMode switch
    {
        DocumentInspectionMode.Capabilities => "Provider capabilities",
        DocumentInspectionMode.NormalizedJson => "Normalized document JSON",
        DocumentInspectionMode.RawJson => $"Raw {Settings.ProviderName} JSON",
        DocumentInspectionMode.SelectedNodeJson =>
            SelectedNode is { } node ? $"Raw JSON - {node.Title}" : "Selected node JSON",
        _ => "Document inspection",
    };
    public string InspectionContent => GetInspectionContent();

    public IAsyncRelayCommand ChooseDocument => ChooseDocumentCommand;
    public IAsyncRelayCommand UseSampleDocument => UseSampleDocumentCommand;
    public IAsyncRelayCommand ScanDocument => ScanDocumentCommand;
    public IAsyncRelayCommand ExtractDocument => ExtractDocumentCommand;
    public System.Windows.Input.ICommand CancelExtraction => ExtractDocumentCancelCommand;
    public IRelayCommand RemoveDocument => RemoveSelectedDocumentCommand;
    public IAsyncRelayCommand CopyInspectionText => CopyInspectionTextCommand;

    private bool CanChooseDocument() => Settings.IsSupported && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanChooseDocument))]
    private async Task ChooseDocumentAsync()
    {
        try
        {
            if (await _inputService.PickAsync() is { } input)
                SelectInput(input);
        }
        catch (Exception exception)
        {
            StatusMessage = $"Could not select a document: {exception.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanChooseDocument))]
    private async Task UseSampleDocumentAsync()
    {
        try
        {
            SelectInput(await _inputService.LoadSampleAsync());
        }
        catch (Exception exception)
        {
            StatusMessage = $"Could not load the sample document: {exception.Message}";
        }
    }

    private bool CanScanDocument() => Settings.CanScan && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanScanDocument))]
    private async Task ScanDocumentAsync()
    {
        try
        {
            if (await _inputService.ScanAsync() is { } input)
                SelectInput(input);
        }
        catch (Exception exception)
        {
            StatusMessage = $"Could not scan a document: {exception.Message}";
        }
    }

    private bool CanExtractDocument() =>
        Settings.IsSupported && _selectedInput is not null && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanExtractDocument), IncludeCancelCommand = true)]
    private async Task ExtractDocumentAsync(CancellationToken cancellationToken)
    {
        var input = _selectedInput
            ?? throw new InvalidOperationException("Choose an image or PDF first.");

        IsBusy = true;
        Settings.IsBusy = true;
        IsProgressVisible = true;
        Progress = 0;
        Nodes.Clear();
        _result = null;
        SelectedNode = null;
        foreach (var preview in PreviewPages)
            preview.SetExtraction(null, []);
        ResultDetails = "Extraction in progress.";
        OnPropertyChanged(nameof(HasResult));
        NotifyInspectionChanged();
        RefreshCommands();
        StatusMessage = $"Recognizing {input.FileName}...";

        var pageProgress = new Progress<DocumentExtractionProgress>(OnProgress);
        try
        {
            var provider = Settings.SelectedProvider?.Provider
                ?? throw new InvalidOperationException("Choose a document provider.");
            var result = await Task.Run(
                () => _runner.ExtractAsync(
                    input,
                    provider,
                    Settings.CreateSettings(),
                    pageProgress,
                    cancellationToken),
                cancellationToken);
            _result = result;

            var projected = DocumentResultProjector.Project(result);
            foreach (var node in projected.Take(MaximumDisplayedNodes))
                Nodes.Add(node);

            foreach (var preview in PreviewPages)
            {
                preview.SetExtraction(
                    result.Pages.FirstOrDefault(page => page.PageNumber == preview.PageNumber),
                    projected.Where(node => node.PageNumber == preview.PageNumber));
            }
            SelectedNode = projected.FirstOrDefault(static node => node.BoundingRegion is not null);
            ResultDetails =
                $"{result.Pages.Count} pages - {projected.Count:N0} structured nodes" +
                (projected.Count > MaximumDisplayedNodes
                    ? $" - showing the first {MaximumDisplayedNodes:N0}"
                    : string.Empty);
            StatusMessage = BuildCompletionStatus(result, projected.Count);
            OnPropertyChanged(nameof(HasResult));
            NotifyInspectionChanged();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusMessage = "Document extraction cancelled.";
            ResultDetails = "No extraction result.";
            NotifyInspectionChanged();
        }
        catch (Exception exception)
        {
            StatusMessage = $"Document extraction failed: {exception.Message}";
            ResultDetails = "No extraction result.";
            NotifyInspectionChanged();
        }
        finally
        {
            Settings.IsBusy = false;
            IsBusy = false;
            IsProgressVisible = false;
            RefreshCommands();
        }
    }

    private bool CanRemoveDocument() => _selectedInput is not null && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRemoveDocument))]
    private void RemoveSelectedDocument()
    {
        _selectedInput = null;
        _result = null;
        SelectedFileName = "No document selected";
        SelectedDocumentDetails = "No document selected.";
        ResultDetails = "No extraction result.";
        Nodes.Clear();
        SelectedNode = null;
        PreviewPages.Clear();
        Progress = 0;
        StatusMessage = Settings.IsSupported
            ? "Choose an image or PDF, or load the sample document."
            : Settings.AvailabilityMessage;
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasPreviewPages));
        OnPropertyChanged(nameof(HasResult));
        OnPropertyChanged(nameof(ComposerHint));
        NotifyInspectionChanged();
        RefreshCommands();
    }

    [RelayCommand]
    private async Task CopyInspectionTextAsync()
    {
        await Clipboard.Default.SetTextAsync(InspectionContent);
        StatusMessage = $"Copied {InspectionTitle.ToLowerInvariant()}.";
    }

    private string GetInspectionContent()
    {
        try
        {
            return SelectedInspectionMode switch
            {
                DocumentInspectionMode.Capabilities =>
                    Settings.SelectedProvider is { } option
                        ? _runner.GetCapabilitiesSummary(option.Provider)
                        : "No document provider is registered.",
                DocumentInspectionMode.NormalizedJson =>
                    _result is { } normalizedResult
                        ? DocumentRawJson.SerializeNormalized(normalizedResult)
                        : "Extract a document to inspect normalized JSON.",
                DocumentInspectionMode.RawJson =>
                    _result is { } rawResult
                        ? DocumentRawJson.SerializePages(rawResult)
                        : "Extract a document to inspect provider raw JSON.",
                DocumentInspectionMode.SelectedNodeJson =>
                    SelectedNode?.HasRawJson == true
                        ? SelectedNode.GetRawJson()
                        : "Select a structured result that exposes provider raw JSON.",
                _ => string.Empty,
            };
        }
        catch (Exception exception)
        {
            return $"Could not inspect the document: {exception.Message}";
        }
    }

    private void SelectInput(DocumentInput input)
    {
        _selectedInput = input;
        _result = null;
        SelectedFileName = input.FileName;
        SelectedDocumentDetails = input.Details;
        ResultDetails = "Not extracted yet.";
        Nodes.Clear();
        SelectedNode = null;
        PreviewPages.Clear();
        foreach (var preview in input.PreviewPages)
        {
            PreviewPages.Add(new(
                preview,
                node => SelectedNode = node));
        }
        Progress = 0;
        StatusMessage = $"Ready to extract {input.FileName}.";
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasPreviewPages));
        OnPropertyChanged(nameof(HasResult));
        OnPropertyChanged(nameof(ComposerHint));
        NotifyInspectionChanged();
        RefreshCommands();
    }

    private void OnProgress(DocumentExtractionProgress update)
    {
        StatusMessage = update.TotalPages is { } total
            ? $"Processed page {update.PagesProcessed}/{total}..."
            : $"Processed page {update.PagesProcessed}...";
        if (update.PagesProcessed is { } processed &&
            update.TotalPages is { } totalPages and > 0)
        {
            Progress = (double)processed / totalPages;
        }
    }

    private static string BuildCompletionStatus(
        DocumentExtractionResult result,
        int nodeCount)
    {
        var pruned = result.Pages.Sum(static page =>
            page.AdditionalProperties?.TryGetValue(
                "apple.vision.repeatedContainersPruned",
                out var value) == true &&
            value is long count
                ? count
                : 0);
        return $"Extracted {result.Pages.Count} page(s) and {nodeCount:N0} structured nodes." +
            (pruned > 0 ? $" Pruned {pruned:N0} repeated provider traversal(s)." : string.Empty);
    }

    private void SettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DocumentSettingsViewModel.SelectedProvider))
            return;

        _result = null;
        Nodes.Clear();
        SelectedNode = null;
        foreach (var preview in PreviewPages)
            preview.SetExtraction(null, []);
        ResultDetails = HasSelection
            ? "Not extracted with this provider."
            : "No extraction result.";
        StatusMessage = Settings.IsSupported
            ? $"Selected {Settings.ProviderName}. Choose or extract a document."
            : Settings.AvailabilityMessage;
        OnPropertyChanged(nameof(IsSupported));
        OnPropertyChanged(nameof(CanScan));
        OnPropertyChanged(nameof(HasResult));
        OnPropertyChanged(nameof(ComposerHint));
        NotifyInspectionChanged();
        RefreshCommands();
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(CanScan));
        OnPropertyChanged(nameof(ComposerHint));
    }

    partial void OnSelectedInspectionModeChanged(DocumentInspectionMode value) =>
        NotifyInspectionChanged();

    partial void OnSelectedNodeChanged(DocumentResultNode? value)
    {
        foreach (var preview in PreviewPages)
            preview.SelectedNode = value?.PageNumber == preview.PageNumber ? value : null;
        OnPropertyChanged(nameof(CanInspectSelectedNode));
        NotifyInspectionChanged();
    }

    private void RefreshCommands()
    {
        ChooseDocumentCommand.NotifyCanExecuteChanged();
        UseSampleDocumentCommand.NotifyCanExecuteChanged();
        ScanDocumentCommand.NotifyCanExecuteChanged();
        ExtractDocumentCommand.NotifyCanExecuteChanged();
        RemoveSelectedDocumentCommand.NotifyCanExecuteChanged();
    }

    private void NotifyInspectionChanged()
    {
        OnPropertyChanged(nameof(InspectionTitle));
        OnPropertyChanged(nameof(InspectionContent));
    }
}

public enum DocumentInspectionMode
{
    Capabilities,
    NormalizedJson,
    RawJson,
    SelectedNodeJson,
}
