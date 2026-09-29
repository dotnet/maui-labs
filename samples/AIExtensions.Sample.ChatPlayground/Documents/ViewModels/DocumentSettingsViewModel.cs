using CommunityToolkit.Mvvm.ComponentModel;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Provider status, request options, and selected/result metadata for Documents.</summary>
public sealed partial class DocumentSettingsViewModel : ObservableObject
{
    private readonly DocumentInputService _input;

    public DocumentSettingsViewModel(
        IEnumerable<IDocumentExtractionProvider> providers,
        DocumentInputService input)
    {
        _input = input;
        Providers = providers
            .Select(static provider => new DocumentProviderOption(provider, provider.Descriptor))
            .ToArray();
        SelectedProvider = Providers.FirstOrDefault(static option => option.Descriptor.IsAvailable)
            ?? Providers.FirstOrDefault();
        SelectedDocumentDetails = "No document selected.";
        ResultDetails = "No extraction result.";
    }

    public IReadOnlyList<DocumentProviderOption> Providers { get; }

    public string ProviderName => SelectedProvider?.Descriptor.DisplayName ?? "No provider";

    public string AvailabilityMessage =>
        SelectedProvider?.Descriptor.Description ?? "No document extraction provider is registered.";

    public bool IsSupported => SelectedProvider?.Descriptor.IsAvailable == true;

    public bool CanScan =>
        SelectedProvider?.Descriptor.Id == "apple-vision" &&
        IsSupported &&
        _input.CanScan;

    public bool CanEditOptions => IsSupported && !IsBusy;

    public bool SendsDocumentOffDevice =>
        SelectedProvider?.Descriptor.SendsDocumentOffDevice == true;

    public IReadOnlyList<string> DisplayModes { get; } = ["Images", "JSON", "Both"];

    public IReadOnlyList<string> LayoutModes { get; } = ["Horizontal", "Vertical"];

    [ObservableProperty] private bool detectBarcodes = true;
    [ObservableProperty] private bool automaticallyDetectLanguage = true;
    [ObservableProperty] private bool includeImages;
    [ObservableProperty] private DocumentProviderOption? selectedProvider;
    [ObservableProperty] private string selectedDisplayMode = "Both";
    [ObservableProperty] private string selectedLayoutMode = "Horizontal";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string selectedDocumentDetails;
    [ObservableProperty] private string resultDetails;

    public DocumentExtractionSettings CreateSettings() =>
        new(DetectBarcodes, AutomaticallyDetectLanguage, IncludeImages);

    partial void OnSelectedProviderChanged(DocumentProviderOption? value)
    {
        OnPropertyChanged(nameof(ProviderName));
        OnPropertyChanged(nameof(AvailabilityMessage));
        OnPropertyChanged(nameof(IsSupported));
        OnPropertyChanged(nameof(CanScan));
        OnPropertyChanged(nameof(CanEditOptions));
        OnPropertyChanged(nameof(SendsDocumentOffDevice));
    }

    partial void OnIsBusyChanged(bool value) =>
        OnPropertyChanged(nameof(CanEditOptions));
}
