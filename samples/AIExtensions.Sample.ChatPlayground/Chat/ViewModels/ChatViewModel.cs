using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.AI.Chat;
using Microsoft.Maui.ApplicationModel.DataTransfer;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>
/// Coordinates the chat tab: the one chat session shown by <c>AIChatView</c>, the settings that shape each request,
/// and recording, import, export, and replay.
/// </summary>
public partial class ChatViewModel : ObservableObject
{
    private readonly ChatSessionService _recording;
    private readonly IChatClient _replayClient;
    private readonly PlaygroundChatClient _router;
    private readonly ChatReplayDriver _replay;
    private IChatClient? _selectedClient;
    private CancellationTokenSource? _replayCancellation;
    private ConversationStatus _status;
    private bool _replayIncomplete;
    private bool _receivingResponse;
    private int _operations;

    /// <summary>Initializes the session, its router, and the child view models.</summary>
    public ChatViewModel(PlaygroundTools tools, ChatSessionService recording, SettingsPaneViewModel settings,
        DiagnosticsViewModel diagnostics, ModelReadinessViewModel readiness, ILoggerFactory loggerFactory)
    {
        _recording = recording;
        Settings = settings;
        Diagnostics = diagnostics;
        Readiness = readiness;
        _replayClient = settings.Clients.Single(option => option.Descriptor.IsReplay).Client;
        _router = new PlaygroundChatClient(() => _selectedClient, _replayClient, settings, tools);

        // One session for the whole tab. The router reads the settings for every request, so the session's own
        // options stay empty; New chat clears the session rather than replacing it.
        Session = new AgentContext(new UIAgent(_router, new ChatOptions(), loggerFactory));
        _replay = new ChatReplayDriver(Session, _router, recording);

        Session.RegisterOnStatusChanged(OnSessionStatusChanged);
        Session.RegisterOnBlockAdded(OnSessionBlockAdded);
        _router.LiveRequestStarting += (_, options) => SystemInstructions = options.Instructions;
        _replay.InteractionStarting += (_, interaction) =>
            SystemInstructions = ChatRecordingSerializer.ReadInstructions(interaction.Request);
        Readiness.PropertyChanged += (_, _) =>
        {
            Settings.ModelStatus = Readiness.Status;
            RefreshComposer();
        };
        Settings.PropertyChanged += SettingsPropertyChanged;
        _recording.Changed += (_, _) => MainThread.BeginInvokeOnMainThread(RefreshOperationCommands);
        ApplyClientSelection(Settings.SelectedClient);
    }

    /// <summary>Gets the chat session. It lives as long as the tab; New chat clears it.</summary>
    public AgentContext Session { get; }

    /// <summary>Gets the settings-pane state.</summary>
    public SettingsPaneViewModel Settings { get; }

    public DiagnosticsViewModel Diagnostics { get; }

    public ModelReadinessViewModel Readiness { get; }

    [ObservableProperty] private string statusMessage = "Choose a client and send a prompt.";
    [ObservableProperty] private string welcomeTitle = "Start a real chat request";
    [ObservableProperty] private string welcomeMessage = "Choose a client, configure options, and send a prompt.";
    [ObservableProperty] private string placeholder = "Message";
    [ObservableProperty] private IList<string> suggestions = [];
    [ObservableProperty] private bool allowAttachments;
    [ObservableProperty] private bool isInputEnabled;
    [ObservableProperty] private bool isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLiveClient))]
    private bool isReplayClient;

    /// <summary>Gets the system instructions sent with the latest request, shown above the conversation.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSystemInstructions))]
    private string? systemInstructions;

    /// <summary>Gets whether the composer is shown: always, except for Replay, which shows the replay controls.</summary>
    public bool IsLiveClient => !IsReplayClient;

    public bool HasSystemInstructions => !string.IsNullOrWhiteSpace(SystemInstructions);

    public IAsyncRelayCommand NewChatAction => NewChatCommand;
    public IAsyncRelayCommand ImportFileAction => ImportChatFileCommand;
    public IAsyncRelayCommand LoadSampleAction => LoadSampleChatCommand;
    public IAsyncRelayCommand<View> ExportChatAction => ExportChatCommand;
    public IAsyncRelayCommand ReplayChatAction => ReplayChatCommand;
    public IAsyncRelayCommand NextReplayAction => ReplayNextCommand;
    public IAsyncRelayCommand RestartReplayAction => RestartReplayCommand;
    public IRelayCommand StopReplayAction => StopReplayCommand;

    private ChatClientDescriptor? SelectedDescriptor => Settings.SelectedDescriptor;

    private bool IsSessionBusy => Session.Status is ConversationStatus.Streaming or ConversationStatus.AwaitingInput;

    private string ImageAvailabilityHint => SelectedDescriptor is { IsReplay: false, SupportsImageInput: false }
        ? " Image input unavailable."
        : string.Empty;

    /// <summary>Restores the auto-saved conversation after the page is displayed.</summary>
    public async Task RestoreCachedChatAsync()
    {
        if (_recording.RestoreError is { } error)
        {
            StatusMessage = error;
            return;
        }

        if (_recording.InteractionCount > 0)
            await ReplayAsync(playAll: true);
    }

    private bool CanChangeChat() => !IsBusy && _operations == 0;
    private bool CanUseChat() => CanChangeChat() && _recording.InteractionCount > 0;
    private bool CanExportChat(View? _) => CanUseChat();
    private bool CanPlayReplay() => CanUseChat() && ReferenceEquals(_selectedClient, _replayClient);
    private bool CanReplayNext() => CanPlayReplay() && _recording.HasReplayRemaining;
    private bool CanStopReplay() => _replay.IsReplaying;

    [RelayCommand(CanExecute = nameof(CanChangeChat))]
    private async Task NewChatAsync()
    {
        try
        {
            _recording.NewRecording();
            await Session.ClearAsync();
            SystemInstructions = null;
            _replayIncomplete = false;
            StatusMessage = IsReplayClient
                ? "New chat started. Select a live client to send a message."
                : $"New chat started. Export before starting another chat to keep a copy.{ImageAvailabilityHint}";
        }
        catch (Exception exception)
        {
            StatusMessage = $"New chat failed: {OneLine(exception.Message)}";
        }
        finally
        {
            UpdateWelcome();
            RefreshComposer();
            RefreshOperationCommands();
        }
    }

    [RelayCommand(CanExecute = nameof(CanChangeChat))]
    private async Task ImportChatFileAsync()
    {
        BeginOperation();
        try
        {
            var file = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Import a saved chat",
            });
            if (file is null)
            {
                StatusMessage = "Import cancelled; current chat unchanged.";
                return;
            }

            await using var input = await file.OpenReadAsync();
            await LoadAndReplayChatAsync(input);
        }
        catch (Exception exception)
        {
            StatusMessage = $"Could not import or replay the chat: {OneLine(exception.Message)}";
        }
        finally
        {
            EndOperation();
        }
    }

    [RelayCommand(CanExecute = nameof(CanChangeChat))]
    private async Task LoadSampleChatAsync()
    {
        BeginOperation();
        try
        {
            await using var input = await FileSystem.OpenAppPackageFileAsync("playground_chat.json");
            await LoadAndReplayChatAsync(input);
        }
        catch (Exception exception)
        {
            StatusMessage = $"Could not load or replay the example chat: {OneLine(exception.Message)}";
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task LoadAndReplayChatAsync(Stream input)
    {
        await _recording.LoadFileAsync(input);
        Settings.SelectedClient = _replayClient;
        if (_recording.InteractionCount == 0)
        {
            await Session.ClearAsync();
            SystemInstructions = null;
            _replayIncomplete = false;
            StatusMessage = "Imported an empty chat. Select a live client to send a message.";
            UpdateWelcome();
        }
        else
        {
            await ReplayAsync(playAll: true);
        }
    }

    [RelayCommand(CanExecute = nameof(CanExportChat))]
    private async Task ExportChatAsync(View? source)
    {
        if (source is null)
        {
            StatusMessage = "Export chat failed: the Export button is unavailable.";
            return;
        }

        BeginOperation();
        try
        {
            var path = _recording.Export();
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Export current chat as JSON",
                File = new ShareFile(path),
                PresentationSourceBounds = GetPageBounds(source),
            });
            StatusMessage = "Current chat export opened. Choose where to save or share the JSON file.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Export chat failed: {OneLine(exception.Message)}";
        }
        finally
        {
            EndOperation();
        }
    }

    [RelayCommand(CanExecute = nameof(CanPlayReplay))]
    private Task ReplayChatAsync() => ReplayAsync(playAll: true);

    [RelayCommand(CanExecute = nameof(CanReplayNext))]
    private Task ReplayNextAsync() => ReplayAsync(playAll: false);

    [RelayCommand(CanExecute = nameof(CanPlayReplay))]
    private async Task RestartReplayAsync()
    {
        if (_recording.InteractionCount == 0)
        {
            StatusMessage = "There is no chat to replay.";
            return;
        }

        _recording.RestartReplay();
        await Session.ClearAsync();
        SystemInstructions = null;
        _replayIncomplete = true;
        StatusMessage = $"Ready to replay {DescribeInteractions(_recording.InteractionCount)}. Press Next turn or Play all.";
        UpdateWelcome();
        RefreshComposer();
        RefreshOperationCommands();
    }

    [RelayCommand(CanExecute = nameof(CanStopReplay))]
    private void StopReplay()
    {
        _replayCancellation?.Cancel();
        StatusMessage = "Stopping replay\u2026";
    }

    private async Task ReplayAsync(bool playAll)
    {
        if (_recording.InteractionCount == 0)
        {
            StatusMessage = "There is no chat to replay.";
            return;
        }
        if (!playAll && !_recording.HasReplayRemaining)
        {
            StatusMessage = "No more turns to replay. Press Play all to start again.";
            return;
        }

        using var cancellation = new CancellationTokenSource();
        _replayCancellation = cancellation;
        _replayIncomplete = true;
        StatusMessage = playAll ? "Replaying saved chat\u2026" : "Replaying next turn\u2026";
        var replay = _replay.ReplayAsync(playAll, cancellation.Token);
        UpdateBusyState();
        try
        {
            var result = await replay;
            _replayIncomplete = _recording.HasReplayRemaining;
            StatusMessage = DescribeReplay(result);
        }
        catch (OperationCanceledException)
        {
            await ResetFailedReplayAsync();
            Settings.SelectedClient = _replayClient;
            StatusMessage = "Replay cancelled. Press Play all or Next turn to try again.";
        }
        catch (Exception exception)
        {
            await ResetFailedReplayAsync();
            Settings.SelectedClient = _replayClient;
            StatusMessage = $"Replay failed: {OneLine(exception.Message).TrimEnd('.')}. Press Play all to retry or start a new chat.";
        }
        finally
        {
            if (ReferenceEquals(_replayCancellation, cancellation))
                _replayCancellation = null;
            UpdateWelcome();
            UpdateBusyState();
        }
    }

    private string DescribeReplay(ChatReplayResult result)
    {
        var unanswered = result.UnansweredApprovals.Count == 0
            ? string.Empty
            : $" The recording never answered the approval for {string.Join(", ", result.UnansweredApprovals.Distinct())}, " +
                "so that turn is shown stopped.";
        if (_replayIncomplete)
        {
            return $"Replayed {_recording.ReplayPosition} of {DescribeInteractions(_recording.InteractionCount)}. " +
                $"Press Next turn or Play all.{unanswered}";
        }

        return $"Replayed {DescribeInteractions(_recording.InteractionCount)}. " +
            (IsReplayClient
                ? "Select a live client to continue."
                : $"{SelectedDescriptor?.DisplayName ?? "No client"} selected.") +
            unanswered;
    }

    // The status line shows at most two lines, so a provider error that spans several lines is joined into one.
    private static string OneLine(string text) =>
        string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string DescribeInteractions(int count) =>
        $"{count} {(count == 1 ? "interaction" : "interactions")}";

    private async Task ResetFailedReplayAsync()
    {
        await Session.ClearAsync();
        SystemInstructions = null;
        _recording.RestartReplay();
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

    private void OnSessionStatusChanged(ConversationStatus status)
    {
        var previous = _status;
        _status = status;
        if (!_replay.IsReplaying)
        {
            switch (status)
            {
                case ConversationStatus.Streaming when previous == ConversationStatus.AwaitingInput:
                    StatusMessage = "Continuing after your decision\u2026";
                    break;
                case ConversationStatus.Streaming:
                    _receivingResponse = false;
                    StatusMessage = "Sending request\u2026";
                    break;
                case ConversationStatus.AwaitingInput:
                    StatusMessage = "Waiting for your decision. Approve or reject the tool call to continue.";
                    break;
                case ConversationStatus.Error:
                    StatusMessage = $"Request failed: {OneLine(Session.Error?.Message ?? "unknown error.")}";
                    break;
                case ConversationStatus.Idle when previous is ConversationStatus.Streaming or ConversationStatus.AwaitingInput:
                    StatusMessage = Session.Turns.Count > 0 && Session.Turns[^1].IsStopped
                        ? "Request stopped."
                        : $"Response complete and auto-saved ({DescribeInteractions(_recording.InteractionCount)}).";
                    break;
            }
        }

        UpdateBusyState();
    }

    private void OnSessionBlockAdded(ConversationTurn turn, ContentBlock block)
    {
        if (_replay.IsReplaying || _receivingResponse || Session.Status != ConversationStatus.Streaming ||
            !turn.ResponseBlocks.Contains(block))
        {
            return;
        }

        _receivingResponse = true;
        StatusMessage = "Receiving response\u2026";
    }

    private void ApplyClientSelection(IChatClient? client)
    {
        var wasReplay = ReferenceEquals(_selectedClient, _replayClient);
        _selectedClient = client;
        var descriptor = SelectedDescriptor;
        Readiness.SetClient(descriptor, Settings.RequestModelId);
        IsReplayClient = descriptor?.IsReplay == true;
        if (descriptor is null)
        {
            StatusMessage = Settings.ClientStatus;
        }
        else
        {
            // Selecting Replay after finishing a recording starts from its first turn.
            if (descriptor.IsReplay && !wasReplay && _recording.ReplayPosition == _recording.InteractionCount)
                _recording.RestartReplay();
            StatusMessage = GetClientStatus(descriptor);
        }

        RefreshComposer();
        UpdateWelcome();
        RefreshOperationCommands();
    }

    private string GetClientStatus(ChatClientDescriptor descriptor)
    {
        if (descriptor.IsReplay)
            return _recording.InteractionCount == 0
                ? "No current chat. Import a recording using More, or select a live client."
                : "Replay is ready. Press Play all or Next turn.";

        if (_replayIncomplete)
            return "Finish replay before continuing with a live client, or start a new chat.";

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
        if (SelectedDescriptor?.IsReplay == true)
        {
            var hasRecording = _recording.InteractionCount > 0;
            WelcomeTitle = hasRecording ? "Recording ready to replay" : "No chat to replay";
            WelcomeMessage = hasRecording
                ? "Press Play all or Next turn below to replay it without contacting a model."
                : "Send a live message or import a recording to replay.";
        }
        else
        {
            WelcomeTitle = "Start a real chat request";
            WelcomeMessage = Suggestions.Count > 0
                ? "Choose a client, configure options, and send a prompt, or try one of these."
                : "Choose a client, configure options, and send a prompt.";
        }
    }

    private IList<string> CreateSuggestions(ChatClientDescriptor? descriptor)
    {
        if (descriptor is null or { IsReplay: true })
            return [];

        var suggestions = new List<string>();
        if (Settings.SupportsToolCalling && Settings.UseCalculatorTool)
            suggestions.Add("What is 12 \u00D7 7? Use the calculator.");
        if (Settings.SupportsToolCalling && Settings.UseDateTimeTool)
            suggestions.Add("What time is it here?");
        suggestions.Add("Show a Markdown table of the planets");
        if (Settings.SupportsToolCalling && Settings.UseImageGenerationTool && descriptor.SupportsImageGeneration)
            suggestions.Add("Generate an image of a cozy cabin");
        return suggestions;
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
            case nameof(SettingsPaneViewModel.IsBusy):
                RefreshOperationCommands();
                break;
            case nameof(SettingsPaneViewModel.SupportsToolCalling) or
                nameof(SettingsPaneViewModel.UseCalculatorTool) or
                nameof(SettingsPaneViewModel.UseDateTimeTool) or
                nameof(SettingsPaneViewModel.UseImageGenerationTool):
                RefreshComposer();
                UpdateWelcome();
                break;
        }
    }

    private void BeginOperation()
    {
        _operations++;
        UpdateBusyState();
    }

    private void EndOperation()
    {
        _operations--;
        UpdateBusyState();
    }

    private void UpdateBusyState()
    {
        IsBusy = IsSessionBusy || _replay.IsReplaying;
        Settings.IsBusy = IsBusy || _operations > 0;
        Readiness.IsInteractionBusy = Settings.IsBusy;
        RefreshComposer();
        RefreshOperationCommands();
    }

    private void RefreshComposer()
    {
        var descriptor = SelectedDescriptor;
        IsInputEnabled = descriptor is { IsReplay: false } &&
            !Readiness.IsSendBlocked &&
            !_replayIncomplete &&
            _recording.RestoreError is null &&
            _operations == 0 &&
            !_replay.IsReplaying;
        AllowAttachments = descriptor is { IsReplay: false, SupportsImageInput: true };
        Placeholder = descriptor switch
        {
            null => "Select a chat client in settings",
            { IsReplay: true } => "Replay is read-only",
            _ => $"Message {descriptor.DisplayName}\u2026",
        };

        var suggestions = CreateSuggestions(descriptor);
        if (!suggestions.SequenceEqual(Suggestions))
            Suggestions = suggestions;
    }

    private void RefreshOperationCommands()
    {
        NewChatCommand.NotifyCanExecuteChanged();
        ImportChatFileCommand.NotifyCanExecuteChanged();
        LoadSampleChatCommand.NotifyCanExecuteChanged();
        ExportChatCommand.NotifyCanExecuteChanged();
        ReplayChatCommand.NotifyCanExecuteChanged();
        ReplayNextCommand.NotifyCanExecuteChanged();
        RestartReplayCommand.NotifyCanExecuteChanged();
        StopReplayCommand.NotifyCanExecuteChanged();
    }
}
