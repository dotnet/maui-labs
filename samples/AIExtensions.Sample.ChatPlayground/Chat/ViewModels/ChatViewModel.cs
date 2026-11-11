using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.AI.Chat;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>
/// Coordinates the chat tab: the one chat session that the chat area lists, the composer, the shared notes, and the
/// settings that shape each request.
/// </summary>
public partial class ChatViewModel : ObservableObject
{
    private readonly FileConversationThread _thread;
    private readonly ImageInputService _imageInput;
    private IChatClient? _selectedClient;
    private ImageAttachment? _selectedImage;
    private ConversationStatus _status;
    private bool _isRestoring;

    /// <summary>Initializes the session, its router, and the child view models.</summary>
    public ChatViewModel(PlaygroundTools tools, FileConversationThread thread, NotesFile notesFile,
        ImageInputService imageInput, SettingsPaneViewModel settings, DiagnosticsViewModel diagnostics,
        ModelReadinessViewModel readiness, ILoggerFactory loggerFactory)
    {
        _thread = thread;
        _imageInput = imageInput;
        Settings = settings;
        Diagnostics = diagnostics;
        Readiness = readiness;
        var notes = new SharedNotes(notesFile);
        var router = new PlaygroundChatClient(() => _selectedClient, settings, tools, notes.CreateRequestContext);

        // One session for the whole tab. The router reads the settings for every request, and the thread saves each
        // committed turn, so the conversation comes back when the app starts; New chat clears it.
        Session = new AgentContext(new UIAgent<NotesState>(router, options =>
        {
            options.Thread = thread;
            options.AddGeneratedToolBlocks();
            options.AddBlockHandler(new CalculatorWorksheetHandler());
            options.RegisterUIAction(ThemeAction.Create(ApplyTheme));
            notes.Register(options);
        }, loggerFactory));
        Notes = new NotesViewModel(notes, Session);

        Session.PropertyChanged += SessionPropertyChanged;
        Readiness.PropertyChanged += (_, _) =>
        {
            Settings.ModelStatus = Readiness.Status;
            RefreshComposer();
        };
        Settings.PropertyChanged += SettingsPropertyChanged;
        ApplyClientSelection(Settings.SelectedClient);
    }

    /// <summary>Gets the chat session. It lives as long as the tab; New chat clears it.</summary>
    public AgentContext Session { get; }

    /// <summary>Gets the settings-pane state.</summary>
    public SettingsPaneViewModel Settings { get; }

    public DiagnosticsViewModel Diagnostics { get; }

    public ModelReadinessViewModel Readiness { get; }

    /// <summary>Gets the notes that the user and the model share.</summary>
    public NotesViewModel Notes { get; }

    [ObservableProperty] private string statusMessage = "Choose a client and send a prompt.";
    [ObservableProperty] private string welcomeTitle = "Start a real chat request";
    [ObservableProperty] private string welcomeMessage = "Choose a client, configure options, and send a prompt.";
    [ObservableProperty] private string placeholder = "Message";
    [ObservableProperty] private IList<string> suggestions = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand), nameof(SendSuggestionCommand))]
    private bool isInputEnabled;

    /// <summary>Gets or sets the message the composer holds.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string prompt = string.Empty;

    /// <summary>Gets whether the selected client accepts images, so the composer can attach one.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ChooseImageCommand), nameof(UseSampleImageCommand))]
    private bool isImageSupported;

    /// <summary>Gets a preview of the image that goes with the next message.</summary>
    [ObservableProperty] private ImageSource? selectedImagePreview;

    /// <summary>Gets the file name of the image that goes with the next message.</summary>
    [ObservableProperty] private string selectedImageName = string.Empty;

    /// <summary>Gets whether an image goes with the next message.</summary>
    public bool HasSelectedImage => _selectedImage is not null;

    /// <summary>Gets whether a response is running, so the composer shows Stop instead of Send.</summary>
    public bool IsResponding => Session.Status is ConversationStatus.Streaming or ConversationStatus.AwaitingInput;

    /// <summary>Gets whether no response is running, so the composer shows Send.</summary>
    public bool IsIdle => !IsResponding;

    // The XAML source generator does not see the commands that [RelayCommand] generates, so the views bind to these.
    public IAsyncRelayCommand SendAction => SendCommand;
    public IAsyncRelayCommand<string> SendSuggestionAction => SendSuggestionCommand;
    public IAsyncRelayCommand StopAction => StopCommand;
    public IAsyncRelayCommand<ConversationTurn> RetryAction => RetryCommand;
    public IRelayCommand<ToolApprovalBlock> ApproveAction => ApproveCommand;
    public IRelayCommand<ToolApprovalBlock> RejectAction => RejectCommand;
    public IAsyncRelayCommand<UIActionBlock> RunAction => RunActionCommand;
    public IAsyncRelayCommand NewChatAction => NewChatCommand;
    public IAsyncRelayCommand ChooseImageAction => ChooseImageCommand;
    public IAsyncRelayCommand UseSampleImageAction => UseSampleImageCommand;
    public IRelayCommand RemoveImageAction => RemoveImageCommand;

    private ChatClientDescriptor? SelectedDescriptor => Settings.SelectedDescriptor;

    /// <summary>
    /// Shows the conversation and the notes that were saved when the app last ran, after the page is displayed.
    /// </summary>
    public async Task RestoreChatAsync()
    {
        _isRestoring = true;
        try
        {
            if (await SavedChat.RestoreAsync(Session, _thread, Notes) is { } restored)
                StatusMessage = restored;
        }
        catch (Exception exception)
        {
            StatusMessage = $"Could not restore the saved conversation: {DisplayText.OneLine(exception.Message)}";
        }
        finally
        {
            _isRestoring = false;
            UpdateWelcome();
            RefreshComposer();
        }
    }

    private bool CanSend() =>
        IsInputEnabled && !IsResponding && (!string.IsNullOrWhiteSpace(Prompt) || HasSelectedImage) &&
        (SelectedDescriptor?.SupportsImageInput == true || !HasSelectedImage && !ConversationContainsImage());

    private bool CanSendSuggestion(string? suggestion) => IsInputEnabled && !IsResponding;

    private bool CanStop() => IsResponding;

    private bool CanRetry(ConversationTurn? turn) =>
        Session.Status == ConversationStatus.Error && turn is { IsFailed: true } &&
        Session.Turns.Count > 0 && ReferenceEquals(Session.Turns[^1], turn);

    [RelayCommand(CanExecute = nameof(CanSend))]
    private Task SendAsync()
    {
        var message = CreateUserMessage(Prompt, _selectedImage);
        Prompt = string.Empty;
        ClearSelectedImage();
        return SendMessageAsync(message);
    }

    [RelayCommand(CanExecute = nameof(CanSendSuggestion))]
    private Task SendSuggestionAsync(string? suggestion) =>
        string.IsNullOrWhiteSpace(suggestion)
            ? Task.CompletedTask
            : SendMessageAsync(new ChatMessage(ChatRole.User, suggestion));

    private async Task SendMessageAsync(ChatMessage message)
    {
        try
        {
            await Session.SendMessageAsync(message);
        }
        catch (Exception exception)
        {
            StatusMessage = $"Request failed: {DisplayText.OneLine(exception.Message)}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private Task StopAsync()
    {
        StatusMessage = "Stopping the response\u2026";
        return Session.CancelAsync();
    }

    [RelayCommand(CanExecute = nameof(CanRetry))]
    private async Task RetryAsync(ConversationTurn? turn)
    {
        try
        {
            await Session.RetryAsync();
        }
        catch (Exception exception)
        {
            StatusMessage = $"Retry failed: {DisplayText.OneLine(exception.Message)}";
        }
    }

    /// <summary>Approves a call that waits for approval, so it runs.</summary>
    [RelayCommand]
    private void Approve(ToolApprovalBlock? approval)
    {
        if (approval is { IsAwaitingInput: true })
            approval.Approve();
    }

    /// <summary>Rejects a call that waits for approval; the model hears that the user declined it.</summary>
    [RelayCommand]
    private void Reject(ToolApprovalBlock? approval)
    {
        if (approval is { IsAwaitingInput: true })
            approval.Reject("The user rejected this call.");
    }

    /// <summary>Runs a manual UI action that waits for the user.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task RunActionAsync(UIActionBlock? action)
    {
        if (action is not { IsAwaitingInput: true })
            return;

        try
        {
            await action.InvokeAsync();
        }
        catch (OperationCanceledException)
        {
            // The turn ended before the action could send its result.
        }
    }

    private bool CanChangeChat() => !IsResponding;

    [RelayCommand(CanExecute = nameof(CanChangeChat))]
    private async Task NewChatAsync()
    {
        try
        {
            await SavedChat.ClearAsync(Session, Notes);
            Prompt = string.Empty;
            ClearSelectedImage();
            StatusMessage = $"New chat started.{ImageAvailabilityHint}";
        }
        catch (Exception exception)
        {
            StatusMessage = $"New chat failed: {DisplayText.OneLine(exception.Message)}";
        }
        finally
        {
            UpdateWelcome();
            RefreshComposer();
        }
    }

    private bool CanAttachImage() => IsImageSupported && !IsResponding;

    [RelayCommand(CanExecute = nameof(CanAttachImage))]
    private async Task ChooseImageAsync()
    {
        try
        {
            if (await _imageInput.PickImageAsync() is { } image)
                SetSelectedImage(image);
        }
        catch (Exception exception)
        {
            StatusMessage = $"Could not select image: {DisplayText.OneLine(exception.Message)}";
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
            StatusMessage = $"Could not load the sample image: {DisplayText.OneLine(exception.Message)}";
        }
    }

    [RelayCommand]
    private void RemoveImage() => ClearSelectedImage();

    private void SetSelectedImage(ImageAttachment image)
    {
        _selectedImage = image;
        var previewBytes = image.Bytes;
        SelectedImageName = image.FileName;
        SelectedImagePreview = ImageSource.FromStream(() => new MemoryStream(previewBytes, writable: false));
        OnPropertyChanged(nameof(HasSelectedImage));
        SendCommand.NotifyCanExecuteChanged();
        StatusMessage = $"Image ready: {SelectedImageName}";
    }

    private void ClearSelectedImage()
    {
        if (_selectedImage is null)
            return;

        _selectedImage = null;
        SelectedImageName = string.Empty;
        SelectedImagePreview = null;
        OnPropertyChanged(nameof(HasSelectedImage));
        SendCommand.NotifyCanExecuteChanged();
    }

    private static ChatMessage CreateUserMessage(string prompt, ImageAttachment? image)
    {
        var contents = new List<AIContent>();
        if (!string.IsNullOrWhiteSpace(prompt))
            contents.Add(new TextContent(prompt.Trim()));
        if (image is not null)
            contents.Add(new DataContent(image.Bytes, image.MediaType));
        return new ChatMessage(ChatRole.User, contents);
    }

    private static void ApplyTheme(AppTheme theme) =>
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (Application.Current is { } application)
                application.UserAppTheme = theme;
        });

    private string ImageAvailabilityHint => SelectedDescriptor is { SupportsImageInput: false }
        ? " Image input unavailable."
        : string.Empty;

    private void SessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AgentContext.Status))
            return;

        var previous = _status;
        var status = Session.Status;
        _status = status;
        if (!_isRestoring)
        {
            switch (status)
            {
                case ConversationStatus.Streaming when previous == ConversationStatus.AwaitingInput:
                    StatusMessage = "Continuing after your decision\u2026";
                    break;
                case ConversationStatus.Streaming:
                    StatusMessage = "Sending request\u2026";
                    break;
                case ConversationStatus.AwaitingInput:
                    StatusMessage = "Waiting for you. Approve, reject, or decide what the chat asks for to continue.";
                    break;
                case ConversationStatus.Error:
                    StatusMessage = $"Request failed: {DisplayText.OneLine(Session.Error?.Message ?? "unknown error.")}";
                    break;
                case ConversationStatus.Idle when previous is ConversationStatus.Streaming or ConversationStatus.AwaitingInput:
                    StatusMessage = Session.Turns.Count > 0 && Session.Turns[^1].IsStopped
                        ? "Request stopped."
                        : "Response complete and saved.";
                    break;
            }
        }

        OnPropertyChanged(nameof(IsResponding));
        OnPropertyChanged(nameof(IsIdle));
        UpdateBusyState();
    }

    private void ApplyClientSelection(IChatClient? client)
    {
        _selectedClient = client;
        var descriptor = SelectedDescriptor;
        Readiness.SetClient(descriptor, Settings.RequestModelId);
        IsImageSupported = descriptor?.SupportsImageInput == true;
        StatusMessage = descriptor is null ? Settings.ClientStatus : GetClientStatus(descriptor);
        RefreshComposer();
        UpdateWelcome();
    }

    private string GetClientStatus(ChatClientDescriptor descriptor)
    {
        if (!descriptor.SupportsImageInput && HasSelectedImage)
            return $"{descriptor.Description} The pending image cannot be sent by {descriptor.DisplayName}.";

        if (!descriptor.SupportsImageInput && ConversationContainsImage())
            return $"{descriptor.Description} This conversation contains image content " +
                $"and cannot be sent to {descriptor.DisplayName}.";

        return $"{descriptor.DisplayName} selected.{ImageAvailabilityHint}";
    }

    private bool ConversationContainsImage() =>
        Session.Turns.Any(static turn => turn.RequestBlocks.Concat(turn.ResponseBlocks)
            .Any(static block => block is MediaContentBlock { Content: DataContent }));

    private void UpdateWelcome()
    {
        WelcomeTitle = "Start a real chat request";
        WelcomeMessage = Suggestions.Count > 0
            ? "Choose a client, configure options, and send a prompt, or try one of these."
            : "Choose a client, configure options, and send a prompt.";
    }

    private void SettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SettingsPaneViewModel.SelectedClient):
                ApplyClientSelection(Settings.SelectedClient);
                break;
            case nameof(SettingsPaneViewModel.SelectedModelId)
                when ReferenceEquals(_selectedClient, Settings.SelectedClient):
                Readiness.SetClient(SelectedDescriptor, Settings.RequestModelId);
                break;
            case nameof(SettingsPaneViewModel.SupportsToolCalling) or
                nameof(SettingsPaneViewModel.SupportsUIActions) or
                nameof(SettingsPaneViewModel.UseCalculatorTool) or
                nameof(SettingsPaneViewModel.UseDateTimeTool) or
                nameof(SettingsPaneViewModel.UseConnectionStatusTool) or
                nameof(SettingsPaneViewModel.UseReasoningSummary) or
                nameof(SettingsPaneViewModel.UseImageGenerationTool):
                RefreshComposer();
                UpdateWelcome();
                break;
        }
    }

    private void UpdateBusyState()
    {
        Settings.IsBusy = IsResponding;
        Readiness.IsInteractionBusy = Settings.IsBusy;
        RefreshComposer();
        NewChatCommand.NotifyCanExecuteChanged();
        ChooseImageCommand.NotifyCanExecuteChanged();
        UseSampleImageCommand.NotifyCanExecuteChanged();
        SendCommand.NotifyCanExecuteChanged();
        SendSuggestionCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        RetryCommand.NotifyCanExecuteChanged();
    }

    private void RefreshComposer()
    {
        var descriptor = SelectedDescriptor;
        IsInputEnabled = descriptor is not null && !Readiness.IsSendBlocked;
        Placeholder = descriptor is null
            ? "Select a chat client in settings"
            : $"Message {descriptor.DisplayName}\u2026";
        SendCommand.NotifyCanExecuteChanged();

        var suggestions = Settings.CreateSuggestions();
        if (!suggestions.SequenceEqual(Suggestions))
            Suggestions = suggestions;
    }
}
