using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DocumentExtraction;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Owns extraction client selection and provider-neutral per-call options.</summary>
public sealed partial class DocumentSettingsViewModel : ObservableObject
{
    public DocumentSettingsViewModel(IEnumerable<DocumentExtractionClient> clients)
    {
        Clients = clients.Select((client, index) => new DocumentExtractionClientOption(client, index)).ToArray();
        SelectedClientOption = Clients.FirstOrDefault();
    }

    public IReadOnlyList<DocumentExtractionClientOption> Clients { get; }

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private DocumentExtractionClientOption? selectedClientOption;
    [ObservableProperty] private string extractorName = string.Empty;

    public bool IsIdle => !IsBusy;
    public bool HasSelection => SelectedClientOption is not null;
    public string CurrentName => SelectedClientOption?.Descriptor.DisplayName ?? "No client";

    public DocumentExtractionOptions CreateOptions() => new()
    {
        ExtractorName = string.IsNullOrWhiteSpace(ExtractorName) ? null : ExtractorName.Trim(),
    };

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsIdle));

    partial void OnSelectedClientOptionChanged(DocumentExtractionClientOption? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CurrentName));
    }
}
