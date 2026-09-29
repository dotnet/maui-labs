using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DocumentExtraction;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Owns document-client selection and request options.</summary>
public sealed partial class DocumentSettingsViewModel : ObservableObject
{
    private const string AppleVisionClientId = "apple-vision";
    private const string MistralDocumentClientId = "foundry-mistral-document";

    private readonly DocumentBooleanOption _detectBarcodes = new(
        "apple.vision.barcodeDetectionEnabled",
        "Detect barcodes",
        "Ask Apple Vision to return barcode blocks.",
        "DocumentDetectBarcodesCheckBox",
        defaultValue: true);
    private readonly DocumentBooleanOption _detectLanguage = new(
        "apple.vision.automaticallyDetectLanguage",
        "Detect language",
        "Ask Apple Vision to identify recognized languages.",
        "DocumentAutomaticLanguageCheckBox",
        defaultValue: true);
    private readonly DocumentBooleanOption _includeImages = new(
        "mistral.includeImages",
        "Include figure images",
        "Request extracted figure bytes. This can substantially increase response size.",
        "DocumentIncludeImagesCheckBox",
        defaultValue: false);

    public DocumentSettingsViewModel(IEnumerable<IDocumentExtractionClient> clients)
    {
        Clients = clients
            .Select(static (client, index) => new DocumentClientOption(
                client,
                client.GetService<DocumentExtractionClientDescriptor>()
                    ?? throw new InvalidOperationException($"Document client {index} did not expose its descriptor."),
                index))
            .ToArray();

        if (Clients.Select(static option => option.Descriptor.Id).Distinct(StringComparer.Ordinal).Count() != Clients.Count)
        {
            throw new ArgumentException("Document clients need distinct IDs.", nameof(clients));
        }
        SelectedOption = Clients.FirstOrDefault();
        SelectedOption = Clients.FirstOrDefault();
    }

    public IReadOnlyList<DocumentClientOption> Clients { get; }

    public ObservableCollection<DocumentBooleanOption> RequestOptions { get; } = [];

    public IDocumentExtractionClient? SelectedClient => SelectedOption?.Client;

    public DocumentExtractionClientDescriptor? SelectedDescriptor => SelectedOption?.Descriptor;

    public string ClientName => SelectedDescriptor?.Name ?? "No client";

    public string AvailabilityMessage => SelectedDescriptor?.Description ?? "No document extraction client is registered.";

    public bool HasClient => SelectedClient is not null;

    public bool CanSelectClient => !IsBusy;

    public bool CanEditOptions => HasClient && !IsBusy;

    public bool HasRequestOptions => RequestOptions.Count > 0;

    public bool DetectBarcodes
    {
        get => _detectBarcodes.Value;
        set => _detectBarcodes.Value = value;
    }

    public bool AutomaticallyDetectLanguage
    {
        get => _detectLanguage.Value;
        set => _detectLanguage.Value = value;
    }

    public bool IncludeImages
    {
        get => _includeImages.Value;
        set => _includeImages.Value = value;
    }

    [ObservableProperty] private DocumentClientOption? selectedOption;
    [ObservableProperty] private bool isBusy;

    public DocumentExtractionOptions CreateOptions()
    {
        if (RequestOptions.Count == 0)
            return new DocumentExtractionOptions();

        return new DocumentExtractionOptions
        {
            AdditionalProperties = new AdditionalPropertiesDictionary(
                RequestOptions.ToDictionary(static option => option.Key, static option => (object?)option.Value)),
        };
    }

    partial void OnSelectedOptionChanged(DocumentClientOption? value)
    {
        UpdateRequestOptions();

        OnPropertyChanged(nameof(SelectedClient));
        OnPropertyChanged(nameof(SelectedDescriptor));
        OnPropertyChanged(nameof(ClientName));
        OnPropertyChanged(nameof(AvailabilityMessage));
        OnPropertyChanged(nameof(HasClient));
        OnPropertyChanged(nameof(CanEditOptions));
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSelectClient));
        OnPropertyChanged(nameof(CanEditOptions));
    }

    private void UpdateRequestOptions()
    {
        RequestOptions.Clear();

        switch (SelectedDescriptor?.Id)
        {
            case AppleVisionClientId:
                RequestOptions.Add(_detectBarcodes);
                RequestOptions.Add(_detectLanguage);
                break;
            case MistralDocumentClientId:
                RequestOptions.Add(_includeImages);
                break;
        }

        OnPropertyChanged(nameof(HasRequestOptions));
    }
}

public sealed partial class DocumentBooleanOption(
    string key,
    string name,
    string description,
    string automationId,
    bool defaultValue) : ObservableObject
{
    public string Key { get; } = key;
    public string Name { get; } = name;
    public string Description { get; } = description;
    public string AutomationId { get; } = automationId;

    [ObservableProperty] private bool value = defaultValue;
}
