using System.Collections.ObjectModel;
using System.Collections.Specialized;
using AIExtensions.Sample.ChatPlayground.Features.Chat;
using AIExtensions.Sample.ChatPlayground.Models;
using AIExtensions.Sample.ChatPlayground.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIExtensions.Sample.ChatPlayground.ViewModels;

/// <summary>Owns chat presentation, attachment selection, and composer commands.</summary>
public sealed partial class ChatAreaViewModel : ObservableObject
{
    private readonly ImageInputService _imageInput;
    private readonly Dictionary<long, ChatMessageViewModel> _entriesById = [];
    private ImageAttachment? _selectedImage;

    /// <summary>Initializes chat presentation and attachment state.</summary>
    public ChatAreaViewModel(ImageInputService imageInput)
    {
        _imageInput = imageInput;
        Messages.CollectionChanged += MessagesCollectionChanged;
    }

    /// <summary>Gets the visible chat messages.</summary>
    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];

    [ObservableProperty] private string prompt = string.Empty;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = "Choose a client and send a prompt.";
    [ObservableProperty] private string emptyTitle = "Start a real chat request";
    [ObservableProperty] private string emptySubtitle = "Choose a client, configure options, and send a prompt.";
    [ObservableProperty] private bool isImageSupported;
    [ObservableProperty] private ImageSource? selectedImagePreview;
    [ObservableProperty] private string selectedImageName = string.Empty;
    [ObservableProperty] private bool canSend;
    [ObservableProperty] private bool isReplayClient;

    /// <summary>Gets whether the composer has an image waiting to send.</summary>
    public bool HasSelectedImage => _selectedImage is not null;

    /// <summary>Gets whether the message list is empty.</summary>
    public bool IsEmpty => Messages.Count == 0;

    /// <summary>Gets whether a request is active.</summary>
    public bool IsIdle => !IsBusy;

    public bool IsLiveClient => !IsReplayClient;

    /// <summary>Gets the selected image bytes for request orchestration.</summary>
    public ImageAttachment? SelectedImage => _selectedImage;

    /// <summary>Gets or sets the request command coordinated by the parent view model.</summary>
    public IAsyncRelayCommand? SendCommand
    {
        get => _sendCommand;
        set => SetProperty(ref _sendCommand, value);
    }

    /// <summary>Gets or sets the cancellation command coordinated by the parent view model.</summary>
    public IRelayCommand? CancelCommand
    {
        get => _cancelCommand;
        set => SetProperty(ref _cancelCommand, value);
    }

    public IAsyncRelayCommand ChooseImageAction => ChooseImageCommand;
    public IAsyncRelayCommand UseSampleImageAction => UseSampleImageCommand;
    public IRelayCommand RemoveImageAction => RemoveImageCommand;

    private IAsyncRelayCommand? _sendCommand;
    private IRelayCommand? _cancelCommand;

    /// <summary>Clears the pending image after it has been added to protocol history.</summary>
    public void ClearSelectedImage()
    {
        _selectedImage = null;
        SelectedImageName = string.Empty;
        SelectedImagePreview = null;
        OnPropertyChanged(nameof(HasSelectedImage));
    }

    private bool CanAttachImage() => !IsBusy && IsImageSupported;

    private void RefreshAttachmentCommands()
    {
        ChooseImageCommand.NotifyCanExecuteChanged();
        UseSampleImageCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanAttachImage))]
    private async Task ChooseImageAsync()
    {
        try
        {
            var image = await _imageInput.PickImageAsync();
            if (image is not null)
                SetSelectedImage(image);
        }
        catch (Exception exception)
        {
            StatusMessage = $"Could not select image: {exception.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanAttachImage))]
    private async Task UseSampleImageAsync()
    {
        try
        {
            SetSelectedImage(await _imageInput.LoadSampleImageAsync());
        }
        catch (Exception exception)
        {
            StatusMessage = $"Could not load the sample image: {exception.Message}";
        }
    }

    private void SetSelectedImage(ImageAttachment image)
    {
        _selectedImage = image;
        var previewBytes = image.Bytes;
        SelectedImageName = image.FileName;
        SelectedImagePreview = ImageSource.FromStream(() => new MemoryStream(previewBytes, writable: false));
        OnPropertyChanged(nameof(HasSelectedImage));
        StatusMessage = $"Image ready: {SelectedImageName}";
    }

    [RelayCommand]
    private void RemoveImage() => ClearSelectedImage();

    private void MessagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        OnPropertyChanged(nameof(IsEmpty));

    partial void OnCanSendChanged(bool value) => SendCommand?.NotifyCanExecuteChanged();

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsIdle));
        CancelCommand?.NotifyCanExecuteChanged();
        RefreshAttachmentCommands();
    }

    partial void OnIsImageSupportedChanged(bool value) => RefreshAttachmentCommands();

    partial void OnIsReplayClientChanged(bool value)
    {
        OnPropertyChanged(nameof(IsLiveClient));
    }

    /// <summary>Awaits UI-thread delivery so streaming changes stay ordered.</summary>
    public Task ApplyTranscriptChangeAsync(TranscriptChange change) =>
        MainThread.InvokeOnMainThreadAsync(() => ApplyTranscriptChange(change));

    public void ClearConversation()
    {
        _entriesById.Clear();
        Messages.Clear();
    }

    private void ApplyTranscriptChange(TranscriptChange change)
    {
        switch (change)
        {
            case TranscriptChange.Cleared:
                ClearConversation();
                break;

            case TranscriptChange.EntryAdded added:
                var message = new ChatMessageViewModel
                {
                    IsUser = added.EntryKind == TranscriptEntryKind.User,
                    IsSystem = added.EntryKind == TranscriptEntryKind.System,
                    IsTool = added.EntryKind == TranscriptEntryKind.Tool,
                    Label = added.Label,
                    Text = added.Text,
                    DetailText = added.Details,
                    IsStreaming = added.IsStreaming,
                    ImageSource = added.ImageBytes is { } bytes
                        ? ImageSource.FromStream(() => new MemoryStream(bytes, writable: false))
                        : null,
                };
                _entriesById.Add(added.EntryId, message);
                if (added.EntryKind == TranscriptEntryKind.System)
                    Messages.Insert(0, message);
                else
                    Messages.Add(message);
                break;

            case TranscriptChange.EntryTextChanged text:
                GetEntry(text.EntryId).Text = text.Text;
                break;

            case TranscriptChange.ToolCallResolved result:
                var tool = GetEntry(result.EntryId);
                tool.Label = result.Label;
                tool.DetailText = result.Details;
                break;

            case TranscriptChange.EntryStreamingStopped ended:
                GetEntry(ended.EntryId).IsStreaming = false;
                break;

            case TranscriptChange.EntryRemoved removed:
                Messages.Remove(GetEntry(removed.EntryId));
                _entriesById.Remove(removed.EntryId);
                break;

            default:
                throw new NotSupportedException($"Unrecognized transcript change: {change.GetType().Name}.");
        }
    }

    private ChatMessageViewModel GetEntry(long entryId) =>
        _entriesById.TryGetValue(entryId, out var message)
            ? message
            : throw new InvalidOperationException($"No visible transcript entry with ID {entryId}.");
}
