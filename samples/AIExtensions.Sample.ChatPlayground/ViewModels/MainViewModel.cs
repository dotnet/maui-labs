using System.ComponentModel;
using AIExtensions.Sample.ChatPlayground.Features.Chat;
using AIExtensions.Sample.ChatPlayground.Features.Library;
using AIExtensions.Sample.ChatPlayground.Features.Recording;
using AIExtensions.Sample.ChatPlayground.Models;
using AIExtensions.Sample.ChatPlayground.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Extensions.AI;

namespace AIExtensions.Sample.ChatPlayground.ViewModels;

#pragma warning disable MEAI001 // Image-generation tools and result content are experimental in the installed SDK.
/// <summary>Coordinates real chat requests, protocol history, and response presentation.</summary>
public partial class MainViewModel : ObservableObject
{
    private readonly PlaygroundTools _tools;
    private readonly ChatLibraryService _recording;
    private readonly ChatConversation _conversation = new();
    private readonly IChatClient _replayClient;
    private IChatClient? _selectedClient;
    private CancellationTokenSource? _requestCancellation;
    // Only the active turn may apply UI changes after asynchronous provider work completes.
    private long _requestGeneration;
    private bool _replayIncomplete;

    /// <summary>Initializes the child view models and request orchestration.</summary>
    public MainViewModel(
        PlaygroundTools tools,
        ChatLibraryService recording,
        SettingsPaneViewModel settings,
        ChatAreaViewModel chat,
        ChatLibraryViewModel library)
    {
        _tools = tools;
        _recording = recording;
        Settings = settings;
        Chat = chat;
        Library = library;
        _replayClient = settings.Clients.Single(option => option.Descriptor.IsReplay).Client;
        Chat.SendCommand = new AsyncRelayCommand(SendAsync, () => Chat.CanSend);
        Chat.CancelCommand = new RelayCommand(Cancel, () => Chat.IsBusy);
        Chat.PlayReplayCommand = ReplayChatCommand;
        Chat.NextReplayCommand = NextReplayCommand;
        Chat.RestartReplayCommand = RestartReplayCommand;
        Chat.BrowseChatsCommand = BrowseChatsCommand;
        Library.OpenChatAsync = OpenLibraryChatAsync;
        Settings.PropertyChanged += SettingsPropertyChanged;
        Chat.PropertyChanged += ChatPropertyChanged;
        Library.PropertyChanged += LibraryPropertyChanged;
        _recording.Changed += (_, _) => MainThread.BeginInvokeOnMainThread(RefreshOperationCommands);
        ApplyClientSelection(Settings.SelectedClient);
    }

    /// <summary>Gets the settings-pane state.</summary>
    public SettingsPaneViewModel Settings { get; }

    /// <summary>Gets the chat-area state.</summary>
    public ChatAreaViewModel Chat { get; }

    public ChatLibraryViewModel Library { get; }

    public IAsyncRelayCommand NewChatCommand =>
        _newChatCommand ??= new AsyncRelayCommand(NewChatAsync, CanChangeChat);
    public IAsyncRelayCommand BrowseChatsCommand =>
        _browseChatsCommand ??= new AsyncRelayCommand(Library.ShowAsync,
            CanBrowseChats, AsyncRelayCommandOptions.AllowConcurrentExecutions);
    public IAsyncRelayCommand ImportFileCommand =>
        _importFileCommand ??= new AsyncRelayCommand(ImportChatFileAsync, CanChangeChat);
    public IAsyncRelayCommand<View> ExportChatCommand =>
        _exportChatCommand ??= new AsyncRelayCommand<View>(ExportChatAsync, _ => CanUseChat());
    public IAsyncRelayCommand ReplayChatCommand =>
        _replayChatCommand ??= new AsyncRelayCommand(ReplayChatAsync, CanPlayReplay);
    public IAsyncRelayCommand NextReplayCommand =>
        _nextReplayCommand ??= new AsyncRelayCommand(ReplayNextAsync, CanReplayNext);
    public IRelayCommand RestartReplayCommand =>
        _restartReplayCommand ??= new RelayCommand(RestartReplay, CanPlayReplay);

    private IAsyncRelayCommand? _newChatCommand;
    private IAsyncRelayCommand? _browseChatsCommand;
    private IAsyncRelayCommand? _importFileCommand;
    private IAsyncRelayCommand<View>? _exportChatCommand;
    private IAsyncRelayCommand? _replayChatCommand;
    private IAsyncRelayCommand? _nextReplayCommand;
    private IRelayCommand? _restartReplayCommand;

    /// <summary>Restores the auto-saved conversation after the page is displayed.</summary>
    public async Task RestoreCachedChatAsync()
    {
        if (_recording.RestoreError is { } error)
        {
            Chat.StatusMessage = error;
            return;
        }

        if (_recording.InteractionCount > 0)
        {
            Settings.SelectedClient = _replayClient;
            await ReplayAllAsync();
        }
    }

    private bool CanChangeChat() => !Chat.IsBusy && !Settings.IsBusy && !Library.IsBusy;
    // Searching must not disable the popup's anchor while its contents are in use.
    private bool CanBrowseChats() => !Chat.IsBusy && !Settings.IsBusy;
    private bool CanUseChat() => CanChangeChat() && _recording.InteractionCount > 0;
    private bool CanPlayReplay() => CanUseChat() && ReferenceEquals(_selectedClient, _replayClient);
    private bool CanReplayNext() => CanPlayReplay() && _recording.HasReplayRemaining;

    private Task NewChatAsync()
    {
        try
        {
            _recording.NewRecording();
            ClearVisibleChat();
            _replayIncomplete = false;
            Chat.StatusMessage = ReferenceEquals(_selectedClient, _replayClient)
                ? "New chat started. Select a live client to send a message."
                : $"New chat started. The previous chat is kept in Chats.{ImageAvailabilityHint}";
            RefreshOperationCommands();
        }
        catch (Exception exception)
        {
            Chat.StatusMessage = $"New chat failed: {exception.Message}";
        }
        return Task.CompletedTask;
    }

    private async Task OpenLibraryChatAsync(string id)
    {
        try
        {
            _recording.OpenChat(id);
            Library.Close();
            Settings.SelectedClient = _replayClient;
            await ReplayAllAsync();
        }
        catch (Exception exception)
        {
            Library.StatusMessage = $"Could not open saved chat: {exception.Message}";
        }
    }

    private async Task ImportChatFileAsync()
    {
        Settings.IsBusy = true;
        try
        {
            var file = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Import a saved chat",
            });
            if (file is null)
            {
                Library.StatusMessage = "Import cancelled; current chat unchanged.";
                return;
            }

            await using var input = await file.OpenReadAsync();
            await _recording.LoadFileAsync(input);
            Library.Close();
            Settings.SelectedClient = _replayClient;
            if (_recording.InteractionCount == 0)
            {
                ClearVisibleChat();
                _replayIncomplete = false;
                Chat.StatusMessage = "Imported an empty chat. Select a live client to send a message.";
            }
            else
            {
                await ReplayAllAsync();
            }
        }
        catch (Exception exception)
        {
            Library.StatusMessage = $"Import failed: {exception.Message}";
        }
        finally
        {
            Settings.IsBusy = false;
        }
    }

    private async Task ExportChatAsync(View? source)
    {
        if (source is null)
        {
            Chat.StatusMessage = "Export chat failed: the Export button is unavailable.";
            return;
        }

        Settings.IsBusy = true;
        try
        {
            var path = _recording.Export();
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Export current chat as JSON",
                File = new ShareFile(path),
                PresentationSourceBounds = GetPageBounds(source),
            });
            Chat.StatusMessage = "Chat export opened. Choose Save to Files to keep a copy.";
        }
        catch (Exception exception)
        {
            Chat.StatusMessage = $"Export chat failed: {exception.Message}";
        }
        finally
        {
            Settings.IsBusy = false;
        }
    }

    private Task ReplayChatAsync() => ReplayAllAsync();
    private Task ReplayNextAsync() => ReplayAsync(playAll: false);

    private void RestartReplay()
    {
        if (_recording.InteractionCount == 0)
        {
            Chat.StatusMessage = "There is no chat to replay.";
            return;
        }

        _recording.RestartReplay();
        ClearVisibleChat();
        _replayIncomplete = true;
        var turns = _recording.InteractionCount;
        Chat.StatusMessage = $"Ready to replay {turns} {(turns == 1 ? "turn" : "turns")}. Press Next turn or Play all.";
        RefreshOperationCommands();
    }

    private static Rect GetPageBounds(View source)
    {
        double x = 0, y = 0;
        for (Element? current = source; current is VisualElement view; current = view.Parent)
        {
            x += view.X;
            y += view.Y;
        }

        return new Rect(x, y, source.Width, source.Height);
    }

    private async Task SendAsync()
    {
        var client = _selectedClient;
        var attachment = Chat.SelectedImage;
        if (client is null)
        {
            Chat.StatusMessage = Settings.ClientStatus;
            return;
        }
        var descriptor = client.GetService<ChatClientDescriptor>()
            ?? throw new InvalidOperationException("The selected chat client did not expose a ChatClientDescriptor.");
        if (descriptor.IsReplay)
        {
            Chat.StatusMessage = "Replay is read-only. Use Play all or Next turn.";
            return;
        }
        if (string.IsNullOrWhiteSpace(Chat.Prompt) && attachment is null)
        {
            Chat.StatusMessage = "Enter a prompt or attach an image before sending.";
            return;
        }
        if (!descriptor.SupportsImageInput && _conversation.HistoryContainsImage)
        {
            Chat.StatusMessage = $"This chat contains images. {descriptor.DisplayName} cannot send them; " +
                "clear the chat or switch to an image-capable client.";
            return;
        }
        if (attachment is not null && !descriptor.SupportsImageInput)
        {
            Chat.StatusMessage = $"{descriptor.DisplayName} does not support image input. " +
                "Switch to an image-capable client or remove the image.";
            return;
        }

        // Validate options before taking ownership of the composer input for this turn.
        var tools = CreateEnabledTools();
        ChatOptions options;
        try
        {
            options = Settings.CreateChatOptions(tools);
        }
        catch (ArgumentException exception)
        {
            Chat.StatusMessage = exception.Message;
            AddError(exception.Message);
            return;
        }

        var prompt = Chat.Prompt;
        var userMessage = CreateUserMessage(prompt, attachment);
        var userText = string.IsNullOrWhiteSpace(prompt) ? $"Attached {attachment!.FileName}" : prompt.Trim();
        var useStructuredJson = Settings.UseStructuredJson;
        Chat.Prompt = string.Empty;
        Chat.ClearSelectedImage();

        var requestGeneration = ++_requestGeneration;
        var requestCancellation = new CancellationTokenSource();
        _requestCancellation = requestCancellation;
        Chat.IsBusy = true;
        Settings.IsBusy = true;
        Chat.StatusMessage = "Sending request…";

        try
        {
            // The conversation produces portable changes; only this view model applies them to MAUI state.
            await foreach (var change in _conversation.SendTurnAsync(
                client, userMessage, userText, options, Settings.UseStreaming, useStructuredJson,
                requestCancellation.Token))
            {
                if (IsCurrentRequest(requestGeneration))
                    await Chat.ApplyTranscriptChangeAsync(change);
            }

            if (IsCurrentRequest(requestGeneration))
            {
                var count = _recording.InteractionCount;
                Chat.StatusMessage = $"Response complete and auto-saved " +
                    $"({count} {(count == 1 ? "interaction" : "interactions")}).";
            }
        }
        catch (OperationCanceledException)
        {
            if (IsCurrentRequest(requestGeneration))
            {
                Chat.StatusMessage = "Request cancelled.";
                AddError("Request cancelled.");
            }
        }
        catch (Exception exception)
        {
            if (IsCurrentRequest(requestGeneration))
            {
                Chat.StatusMessage = $"Request failed: {exception.Message}";
                AddError($"Request failed: {exception.Message}");
            }
        }
        finally
        {
            requestCancellation.Dispose();
            if (ReferenceEquals(_requestCancellation, requestCancellation))
                _requestCancellation = null;
            if (IsCurrentRequest(requestGeneration))
            {
                Chat.IsBusy = false;
                Settings.IsBusy = false;
            }
        }
    }

    private Task ReplayAllAsync() => ReplayAsync(playAll: true);

    private async Task ReplayAsync(bool playAll)
    {
        if (_recording.InteractionCount == 0)
        {
            Chat.StatusMessage = "There is no chat to replay.";
            return;
        }
        if (!playAll && !_recording.HasReplayRemaining)
        {
            Chat.StatusMessage = "No more turns to replay. Press Play all to start again.";
            return;
        }

        var generation = ++_requestGeneration;
        using var cancellation = new CancellationTokenSource();
        _requestCancellation = cancellation;
        Chat.IsBusy = true;
        Settings.IsBusy = true;
        _replayIncomplete = true;
        Chat.StatusMessage = playAll ? "Replaying saved chat…" : "Replaying next turn…";

        try
        {
            if (playAll)
                _recording.RestartReplay();
            if (playAll || _recording.ReplayPosition == 0)
                ClearVisibleChat();

            // Reuse the live turn projector so saved and real responses render identically.
            do
            {
                cancellation.Token.ThrowIfCancellationRequested();
                var interaction = _recording.PeekNext();
                var options = ChatRecordingSerializer.ReadOptions(interaction.Request);
                await foreach (var change in _conversation.ReplayTurnAsync(
                    _replayClient, interaction.Request, options, interaction.IsStreaming,
                    ChatRecordingSerializer.IsStructuredJson(interaction.Request), cancellation.Token))
                {
                    if (IsCurrentRequest(generation))
                        await Chat.ApplyTranscriptChangeAsync(change);
                }
            }
            while (playAll && _recording.HasReplayRemaining);

            _replayIncomplete = _recording.HasReplayRemaining;
            Chat.StatusMessage = _replayIncomplete
                ? $"Replayed {_recording.ReplayPosition}/{_recording.InteractionCount} turns. Press Next turn or Play all."
                : $"Replayed {_recording.InteractionCount} " +
                    $"{(_recording.InteractionCount == 1 ? "turn" : "turns")}. Select a live client to continue.";
        }
        catch (OperationCanceledException)
        {
            ResetFailedReplay();
            Chat.StatusMessage = "Replay cancelled. Press Play all or Next turn to try again.";
        }
        catch (Exception exception)
        {
            ResetFailedReplay();
            Chat.StatusMessage = $"Replay failed: {exception.Message}. Press Play all to retry or start a new chat.";
        }
        finally
        {
            if (ReferenceEquals(_requestCancellation, cancellation))
                _requestCancellation = null;
            Chat.IsBusy = false;
            Settings.IsBusy = false;
        }
    }

    private void ResetFailedReplay()
    {
        ClearVisibleChat();
        _recording.RestartReplay();
    }

    private List<AITool> CreateEnabledTools()
    {
        var tools = new List<AITool>();
        if (Settings.UseDateTimeTool)
            tools.Add(_tools.CurrentLocalDateTime);
        if (Settings.UseCalculatorTool)
            tools.Add(_tools.Calculator);
        if (Settings.UseImageGenerationTool && SelectedDescriptor?.SupportsImageGeneration == true)
            tools.Add(new HostedImageGenerationTool());
        return tools;
    }

    private static ChatMessage CreateUserMessage(string prompt, ImageAttachment? attachment)
    {
        var contents = new List<AIContent>();
        if (!string.IsNullOrWhiteSpace(prompt))
            contents.Add(new TextContent(prompt.Trim()));
        if (attachment is not null)
            contents.Add(new DataContent(attachment.Bytes, attachment.MediaType));
        return new ChatMessage(ChatRole.User, contents);
    }

    private bool CanSend()
    {
        var descriptor = SelectedDescriptor;
        if (Chat.IsBusy || Settings.IsBusy ||
            descriptor is null ||
            string.IsNullOrWhiteSpace(Chat.Prompt) && !Chat.HasSelectedImage)
        {
            return false;
        }

        if (_replayIncomplete || descriptor.IsReplay ||
            (!descriptor.SupportsImageInput &&
                (Chat.HasSelectedImage || _conversation.HistoryContainsImage)))
        {
            return false;
        }

        return Settings.ToolMode != ChatToolMode.RequireAny ||
            Settings.UseDateTimeTool ||
            Settings.UseCalculatorTool ||
            (Settings.UseImageGenerationTool && descriptor.SupportsImageGeneration);
    }

    private ChatClientDescriptor? SelectedDescriptor => Settings.SelectedDescriptor;

    private string ImageAvailabilityHint => SelectedDescriptor is { IsReplay: false, SupportsImageInput: false }
        ? " Image attachments are unavailable with this client."
        : string.Empty;

    private void ApplyClientSelection(IChatClient? client)
    {
        var wasReplay = ReferenceEquals(_selectedClient, _replayClient);
        _selectedClient = client;
        var descriptor = SelectedDescriptor;
        if (descriptor is null)
        {
            Chat.IsReplayClient = false;
            Chat.IsImageSupported = false;
            Chat.StatusMessage = Settings.ClientStatus;
            RefreshOperationCommands();
            return;
        }

        var isReplay = descriptor.IsReplay;
        // Selecting Replay after finishing a recording starts from its first turn.
        if (isReplay && !wasReplay && _recording.ReplayPosition == _recording.InteractionCount)
            _recording.RestartReplay();
        Chat.IsReplayClient = isReplay;
        UpdateEmptyState();
        Chat.IsImageSupported = !isReplay && descriptor.SupportsImageInput;
        Chat.StatusMessage = GetClientStatus(descriptor);
        RefreshOperationCommands();
    }

    private string GetClientStatus(ChatClientDescriptor descriptor)
    {
        if (descriptor.IsReplay)
            return _recording.InteractionCount == 0
                ? "No current chat. Press Find chats to open one or select a live client."
                : "Replay is ready. Press Play all or Next turn.";

        if (_replayIncomplete)
            return "Finish replay before continuing with a live client, or start a new chat.";

        if (!descriptor.SupportsImageInput && Chat.HasSelectedImage)
            return $"{descriptor.Status} The pending image cannot be sent by {descriptor.DisplayName}.";

        if (!descriptor.SupportsImageInput && _conversation.HistoryContainsImage)
            return $"{descriptor.Status} This conversation contains image content " +
                $"and cannot be sent to {descriptor.DisplayName}.";

        return $"{descriptor.Status}{ImageAvailabilityHint}";
    }

    private void SettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsPaneViewModel.SelectedClient))
            ApplyClientSelection(Settings.SelectedClient);
        else if (e.PropertyName == nameof(SettingsPaneViewModel.IsBusy))
            RefreshOperationCommands();
        else if (e.PropertyName is nameof(SettingsPaneViewModel.ToolMode) or
            nameof(SettingsPaneViewModel.UseDateTimeTool) or
            nameof(SettingsPaneViewModel.UseCalculatorTool) or
            nameof(SettingsPaneViewModel.UseImageGenerationTool))
            UpdateChatCanSend();
    }

    private void ChatPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatAreaViewModel.IsBusy))
            RefreshOperationCommands();
        else if (e.PropertyName is nameof(ChatAreaViewModel.Prompt) or
            nameof(ChatAreaViewModel.HasSelectedImage))
            UpdateChatCanSend();
    }

    private void LibraryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatLibraryViewModel.IsBusy))
            RefreshOperationCommands();
    }

    private void UpdateChatCanSend() => Chat.CanSend = CanSend();

    private void RefreshOperationCommands()
    {
        // Composer commands refresh in ChatAreaViewModel; these depend on recording and busy state.
        UpdateChatCanSend();
        NewChatCommand.NotifyCanExecuteChanged();
        BrowseChatsCommand.NotifyCanExecuteChanged();
        ImportFileCommand.NotifyCanExecuteChanged();
        ExportChatCommand.NotifyCanExecuteChanged();
        ReplayChatCommand.NotifyCanExecuteChanged();
        NextReplayCommand.NotifyCanExecuteChanged();
        RestartReplayCommand.NotifyCanExecuteChanged();
    }

    private void Cancel()
    {
        _requestCancellation?.Cancel();
        Chat.StatusMessage = "Cancelling request…";
    }

    private void ClearVisibleChat()
    {
        _conversation.Clear();
        Chat.ClearConversation();
        Chat.Prompt = string.Empty;
        Chat.ClearSelectedImage();
        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        if (SelectedDescriptor?.IsReplay == true)
        {
            var hasRecording = _recording.InteractionCount > 0;
            Chat.EmptyTitle = hasRecording ? "Recording ready to replay" : "No chat to replay";
            Chat.EmptySubtitle = hasRecording
                ? "Press Next turn to step through the recording, or Play all to show the full chat."
                : "Send a live message or load a saved chat to replay.";
        }
        else
        {
            Chat.EmptyTitle = "Start a real chat request";
            Chat.EmptySubtitle = "Choose a client, configure options, and send a prompt.";
        }
    }

    private bool IsCurrentRequest(long requestGeneration) => requestGeneration == _requestGeneration;

    private void AddError(string text) =>
        Chat.Messages.Add(new ChatMessageViewModel { Text = text, IsError = true, Label = "Error" });
}
#pragma warning restore MEAI001
