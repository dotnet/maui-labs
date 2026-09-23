using CommunityToolkit.Mvvm.ComponentModel;

namespace ChatClientPlayground.ViewModels;

/// <summary>Presentation state for one visible chat message.</summary>
public sealed partial class ChatMessageViewModel : ObservableObject
{
    [ObservableProperty] private string text = string.Empty;
    [ObservableProperty] private bool isUser;
    [ObservableProperty] private bool isError;
    [ObservableProperty] private bool isSystem;
    [ObservableProperty] private bool isTool;
    [ObservableProperty] private bool isStreaming;
    [ObservableProperty] private string? label;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetails))]
    private string? detailText;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage))]
    private ImageSource? imageSource;

    /// <summary>Gets whether this bubble has supplemental details.</summary>
    public bool HasDetails => !string.IsNullOrWhiteSpace(DetailText);

    /// <summary>Gets whether this bubble displays an image.</summary>
    public bool HasImage => ImageSource is not null;
}
