using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DocumentExtraction;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Owns document-client selection and request options.</summary>
public sealed partial class DocumentSettingsViewModel : ObservableObject
{
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
    }

    public IReadOnlyList<DocumentClientOption> Clients { get; }

    public IDocumentExtractionClient? SelectedClient => SelectedOption?.Client;

    public DocumentExtractionClientDescriptor? SelectedDescriptor => SelectedOption?.Descriptor;

    public string ClientName => SelectedDescriptor?.Name ?? "No client";

    public string AvailabilityMessage => SelectedDescriptor?.Description ?? "No document extraction client is registered.";

    public bool HasClient => SelectedClient is not null;

    public bool CanSelectClient => !IsBusy;

    public bool CanEditOptions => HasClient && !IsBusy;

    [ObservableProperty] private bool detectBarcodes = true;
    [ObservableProperty] private bool automaticallyDetectLanguage = true;
    [ObservableProperty] private bool includeImages;
    [ObservableProperty] private DocumentClientOption? selectedOption;
    [ObservableProperty] private bool isBusy;

    public DocumentExtractionOptions CreateOptions() =>
        new()
        {
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["apple.vision.barcodeDetectionEnabled"] = DetectBarcodes,
                ["apple.vision.automaticallyDetectLanguage"] = AutomaticallyDetectLanguage,
                ["mistral.includeImages"] = IncludeImages,
            },
        };

    partial void OnSelectedOptionChanged(DocumentClientOption? value)
    {
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
}
