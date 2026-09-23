using System.Collections.ObjectModel;
using System.Collections.Specialized;
using ChatClientPlayground.Models;
using ChatClientPlayground.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ChatClientPlayground.ViewModels;

/// <summary>Owns chat presentation, attachment selection, and composer commands.</summary>
public sealed partial class ChatAreaViewModel : ObservableObject
{
    private readonly ImageInputService _imageInput;
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
    [ObservableProperty] private string recordingHint = string.Empty;
    [ObservableProperty] private bool isLiveClient = true;
    [ObservableProperty] private bool isImageSupported;
    [ObservableProperty] private ImageSource? selectedImagePreview;
    [ObservableProperty] private string selectedImageName = string.Empty;
    [ObservableProperty] private bool isAttachmentMenuOpen;
    [ObservableProperty] private bool canSend;

    /// <summary>Gets whether the composer has an image waiting to send.</summary>
    public bool HasSelectedImage => _selectedImage is not null;

    /// <summary>Gets whether the message list is empty.</summary>
    public bool IsEmpty => Messages.Count == 0;

    public bool IsRecordingClient => !IsLiveClient;

    /// <summary>Gets whether a request is active.</summary>
    public bool IsIdle => !IsBusy;

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

    public IRelayCommand ToggleAttachmentMenuCommand => _toggleAttachmentMenuCommand ??= new RelayCommand(ToggleAttachmentMenu, CanAttachImage);
    public IAsyncRelayCommand ChooseImageCommand => _chooseImageCommand ??= new AsyncRelayCommand(ChooseImageAsync, CanAttachImage);
    public IAsyncRelayCommand UseSampleImageCommand => _useSampleImageCommand ??= new AsyncRelayCommand(UseSampleImageAsync, CanAttachImage);
    public IRelayCommand RemoveImageCommand => _removeImageCommand ??= new RelayCommand(ClearSelectedImage);

    private IAsyncRelayCommand? _sendCommand;
    private IRelayCommand? _cancelCommand;
    private IRelayCommand? _toggleAttachmentMenuCommand;
    private IAsyncRelayCommand? _chooseImageCommand;
    private IAsyncRelayCommand? _useSampleImageCommand;
    private IRelayCommand? _removeImageCommand;

    /// <summary>Refreshes command availability after orchestration state changes.</summary>
    public void RefreshCommands()
    {
        SendCommand?.NotifyCanExecuteChanged();
        CancelCommand?.NotifyCanExecuteChanged();
        ToggleAttachmentMenuCommand.NotifyCanExecuteChanged();
        ChooseImageCommand.NotifyCanExecuteChanged();
        UseSampleImageCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Clears the pending image after it has been added to protocol history.</summary>
    public void ClearSelectedImage()
    {
        _selectedImage = null;
        SelectedImageName = string.Empty;
        SelectedImagePreview = null;
        IsAttachmentMenuOpen = false;
        OnPropertyChanged(nameof(HasSelectedImage));
        RefreshCommands();
    }

    private bool CanAttachImage() => !IsBusy && IsImageSupported;
    private void ToggleAttachmentMenu() => IsAttachmentMenuOpen = !IsAttachmentMenuOpen;

    private async Task ChooseImageAsync()
    {
        IsAttachmentMenuOpen = false;
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

    private async Task UseSampleImageAsync()
    {
        IsAttachmentMenuOpen = false;
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
        RefreshCommands();
    }

    private void MessagesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        OnPropertyChanged(nameof(IsEmpty));

    partial void OnPromptChanged(string value) => RefreshCommands();

    partial void OnIsLiveClientChanged(bool value) => OnPropertyChanged(nameof(IsRecordingClient));

    partial void OnCanSendChanged(bool value) => SendCommand?.NotifyCanExecuteChanged();

    partial void OnIsBusyChanged(bool value)
    {
        if (value)
            IsAttachmentMenuOpen = false;
        OnPropertyChanged(nameof(IsIdle));
        RefreshCommands();
    }

    partial void OnIsImageSupportedChanged(bool value)
    {
        if (!value)
            IsAttachmentMenuOpen = false;
        RefreshCommands();
    }
}
