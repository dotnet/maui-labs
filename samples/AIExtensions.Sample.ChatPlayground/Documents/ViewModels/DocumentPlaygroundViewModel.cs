using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DocumentExtraction;

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
        StatusMessage = settings.IsSupported
            ? "Choose an image or PDF, or load the sample document."
            : settings.AvailabilityMessage;
    }

    public event EventHandler<DocumentTextRequestedEventArgs>? TextRequested;

    public DocumentSettingsViewModel Settings { get; }

    public ObservableCollection<DocumentResultNode> Nodes { get; } = [];
    public ObservableCollection<DocumentPagePreviewViewModel> PreviewPages { get; } = [];

    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private string selectedFileName = "No document selected";
    [ObservableProperty] private DocumentResultNode? selectedNode;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private double progress;
    [ObservableProperty] private bool isProgressVisible;

    public bool IsIdle => !IsBusy;
    public bool IsSupported => Settings.IsSupported;
    public bool CanScan => Settings.CanScan && !IsBusy;
    public bool HasSelection => _selectedInput is not null;
    public bool HasPreviewPages => PreviewPages.Count > 0;
    public bool HasResult => _result is not null;

    public IAsyncRelayCommand ChooseDocument => ChooseDocumentCommand;
    public IAsyncRelayCommand UseSampleDocument => UseSampleDocumentCommand;
    public IAsyncRelayCommand ScanDocument => ScanDocumentCommand;
    public IAsyncRelayCommand ExtractDocument => ExtractDocumentCommand;
    public System.Windows.Input.ICommand CancelExtraction => ExtractDocumentCancelCommand;
    public IRelayCommand ShowCapabilities => OpenCapabilitiesCommand;
    public IRelayCommand ShowNormalizedJson => OpenNormalizedJsonCommand;
    public IRelayCommand ShowRawJson => OpenRawJsonCommand;

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
        Settings.ResultDetails = "Extraction in progress.";
        OnPropertyChanged(nameof(HasResult));
        RefreshCommands();
        StatusMessage = $"Recognizing {input.FileName}...";

        var pageProgress = new Progress<DocumentExtractionProgress>(OnProgress);
        try
        {
            var result = await Task.Run(
                () => _runner.ExtractAsync(
                    input,
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
            Settings.ResultDetails =
                $"{result.Pages.Count} pages - {projected.Count:N0} structured nodes" +
                (projected.Count > MaximumDisplayedNodes
                    ? $" - showing the first {MaximumDisplayedNodes:N0}"
                    : string.Empty);
            StatusMessage = BuildCompletionStatus(result, projected.Count);
            OnPropertyChanged(nameof(HasResult));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusMessage = "Document extraction cancelled.";
            Settings.ResultDetails = "No extraction result.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Document extraction failed: {exception.Message}";
            Settings.ResultDetails = "No extraction result.";
        }
        finally
        {
            Settings.IsBusy = false;
            IsBusy = false;
            IsProgressVisible = false;
            RefreshCommands();
        }
    }

    [RelayCommand]
    private void OpenCapabilities()
    {
        try
        {
            RequestText("Document provider capabilities", _runner.GetCapabilitiesSummary());
        }
        catch (Exception exception)
        {
            StatusMessage = $"Could not read provider capabilities: {exception.Message}";
        }
    }

    private bool CanShowResult() => _result is not null && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanShowResult))]
    private void OpenNormalizedJson()
    {
        if (_result is { } result)
            RequestText("Normalized document JSON", DocumentRawJson.SerializeNormalized(result));
    }

    [RelayCommand(CanExecute = nameof(CanShowResult))]
    private void OpenRawJson()
    {
        if (_result is { } result)
            RequestText("Raw Apple Vision JSON", DocumentRawJson.SerializePages(result));
    }

    public void ShowNodeJson(DocumentResultNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.HasRawJson)
            RequestText($"Raw JSON - {node.Title}", node.GetRawJson());
    }

    private void SelectInput(DocumentInput input)
    {
        _selectedInput = input;
        _result = null;
        SelectedFileName = input.FileName;
        Settings.SelectedDocumentDetails = input.Details;
        Settings.ResultDetails = "Not extracted yet.";
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

    private void RequestText(string title, string content) =>
        TextRequested?.Invoke(this, new(title, content));

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsIdle));
        OnPropertyChanged(nameof(CanScan));
    }

    partial void OnSelectedNodeChanged(DocumentResultNode? value)
    {
        foreach (var preview in PreviewPages)
            preview.SelectedNode = value?.PageNumber == preview.PageNumber ? value : null;
    }

    private void RefreshCommands()
    {
        ChooseDocumentCommand.NotifyCanExecuteChanged();
        UseSampleDocumentCommand.NotifyCanExecuteChanged();
        ScanDocumentCommand.NotifyCanExecuteChanged();
        ExtractDocumentCommand.NotifyCanExecuteChanged();
        OpenNormalizedJsonCommand.NotifyCanExecuteChanged();
        OpenRawJsonCommand.NotifyCanExecuteChanged();
    }
}

public sealed record DocumentTextRequestedEventArgs(string Title, string Content);
