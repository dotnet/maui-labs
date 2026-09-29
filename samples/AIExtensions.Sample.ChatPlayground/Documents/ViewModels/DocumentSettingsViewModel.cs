using CommunityToolkit.Mvvm.ComponentModel;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Provider status, request options, and selected/result metadata for Documents.</summary>
public sealed partial class DocumentSettingsViewModel : ObservableObject
{
    private readonly DocumentExtractionRunner _runner;
    private readonly DocumentInputService _input;

    public DocumentSettingsViewModel(
        DocumentExtractionRunner runner,
        DocumentInputService input)
    {
        _runner = runner;
        _input = input;
        SelectedDocumentDetails = "No document selected.";
        ResultDetails = "No extraction result.";
    }

    public string ProviderName => _runner.Provider.DisplayName;

    public string AvailabilityMessage => _runner.Provider.Description;

    public bool IsSupported => _runner.Provider.IsAvailable;

    public bool CanScan => IsSupported && _input.CanScan;

    public bool CanEditOptions => IsSupported && !IsBusy;

    public IReadOnlyList<string> DisplayModes { get; } = ["Images", "JSON", "Both"];

    public IReadOnlyList<string> LayoutModes { get; } = ["Horizontal", "Vertical"];

    [ObservableProperty] private bool detectBarcodes = true;
    [ObservableProperty] private bool automaticallyDetectLanguage = true;
    [ObservableProperty] private string selectedDisplayMode = "Both";
    [ObservableProperty] private string selectedLayoutMode = "Horizontal";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string selectedDocumentDetails;
    [ObservableProperty] private string resultDetails;

    public DocumentExtractionSettings CreateSettings() =>
        new(DetectBarcodes, AutomaticallyDetectLanguage);

    partial void OnIsBusyChanged(bool value) =>
        OnPropertyChanged(nameof(CanEditOptions));
}
