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
            .Select(static (provider, index) =>
                new DocumentProviderOption(provider, provider.Descriptor, index))
            .ToArray();
        if (Providers.Select(static option => option.Descriptor.Id)
            .Distinct(StringComparer.Ordinal).Count() != Providers.Count)
        {
            throw new ArgumentException(
                "Document providers need distinct IDs.",
                nameof(providers));
        }

        SelectedProvider = Providers.FirstOrDefault(static option => option.Descriptor.IsAvailable)
            ?? Providers.FirstOrDefault();
    }

    public IReadOnlyList<DocumentProviderOption> Providers { get; }

    public DocumentProviderDescriptor? SelectedDescriptor => SelectedProvider?.Descriptor;

    public string ProviderName => SelectedProvider?.Descriptor.DisplayName ?? "No provider";

    public string AvailabilityMessage =>
        SelectedProvider?.Descriptor.Description ?? "No document extraction provider is registered.";

    public bool IsSupported => SelectedProvider?.Descriptor.IsAvailable == true;

    public bool CanSelectProvider => !IsBusy;

    public bool CanScan =>
        SelectedProvider?.Descriptor.Id == "apple-vision" &&
        IsSupported &&
        _input.CanScan;

    public bool CanEditOptions => IsSupported && !IsBusy;

    public bool SendsDocumentOffDevice =>
        SelectedProvider?.Descriptor.SendsDocumentOffDevice == true;

    [ObservableProperty] private bool detectBarcodes = true;
    [ObservableProperty] private bool automaticallyDetectLanguage = true;
    [ObservableProperty] private bool includeImages;
    [ObservableProperty] private DocumentProviderOption? selectedProvider;
    [ObservableProperty] private bool isBusy;

    public DocumentExtractionSettings CreateSettings() =>
        new(DetectBarcodes, AutomaticallyDetectLanguage, IncludeImages);

    partial void OnSelectedProviderChanged(DocumentProviderOption? value)
    {
        OnPropertyChanged(nameof(SelectedDescriptor));
        OnPropertyChanged(nameof(ProviderName));
        OnPropertyChanged(nameof(AvailabilityMessage));
        OnPropertyChanged(nameof(IsSupported));
        OnPropertyChanged(nameof(CanScan));
        OnPropertyChanged(nameof(CanEditOptions));
        OnPropertyChanged(nameof(SendsDocumentOffDevice));
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSelectProvider));
        OnPropertyChanged(nameof(CanEditOptions));
    }
}
