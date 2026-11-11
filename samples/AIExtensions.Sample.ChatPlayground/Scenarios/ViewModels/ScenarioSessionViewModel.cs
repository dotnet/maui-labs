using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.AI.Chat;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>
/// Runs one scenario in the Agentic tab: its session, the router client and recording behind it, the bundled example,
/// and the status line.
/// </summary>
/// <remarks>
/// Like the chat tab, the scenario talks to the model through <see cref="PlaygroundChatClient"/>, which sends each
/// request to the client selected for the scenarios with the options the scenario builds, records the exchange, and
/// answers from the recording while the example replays.
/// </remarks>
public partial class ScenarioSessionViewModel : ObservableObject
{
    private readonly SettingsPaneViewModel _settings;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ChatSessionService _recording;
    private readonly PlaygroundChatClient _router;
    private readonly List<IDisposable> _sessionRegistrations = [];
    private ChatReplayDriver _replay;
    private CancellationTokenSource? _replayCancellation;
    private ConversationStatus _status;
    private bool _receivingResponse;
    private bool _opened;

    /// <summary>Initializes the scenario's session.</summary>
    public ScenarioSessionViewModel(
        PlaygroundScenario scenario,
        SettingsPaneViewModel settings,
        ILoggerFactory loggerFactory,
        ILogger<ChatSessionService> recordingLogger,
        string dataDirectory,
        string exportDirectory)
    {
        Scenario = scenario;
        _settings = settings;
        _loggerFactory = loggerFactory;
        _recording = new ChatSessionService(recordingLogger, dataDirectory, exportDirectory, "scenario-" + scenario.Id);

        // Each launch starts a new conversation, so the recording starts over too. The persistence scenario restores
        // its conversation from its own thread instead.
        _recording.NewRecording();
        _router = new PlaygroundChatClient(
            () => _settings.SelectedClient,
            new ReplayChatClient(_recording),
            settings,
            engineOptions => Scenario.CreateRequestOptions(engineOptions, Capabilities),
            _recording,
            messages => Scenario.PrepareMessages(messages));
        _replay = CreateSession();
        _settings.PropertyChanged += SettingsPropertyChanged;
        Refresh();
    }

    /// <summary>Raised after a new session replaced the previous one, so panels can follow the new agent.</summary>
    public event EventHandler? SessionCreated;

    /// <summary>Gets the scenario.</summary>
    public PlaygroundScenario Scenario { get; }

    /// <summary>Gets the scenario's conversation.</summary>
    [ObservableProperty] private AgentContext session = null!;

    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private bool isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotReplaying))]
    private bool isReplaying;
    [ObservableProperty] private bool isInputEnabled;
    [ObservableProperty] private string welcomeMessage = string.Empty;
    [ObservableProperty] private IList<string> suggestions = [];

    public string Title => Scenario.Title;

    /// <summary>Gets whether the example is not replaying, so it can be played.</summary>
    public bool IsNotReplaying => !IsReplaying;

    public string Placeholder => Scenario.Placeholder;

    /// <summary>Gets whether the scenario's session is streaming or waiting for the user.</summary>
    public bool IsSessionBusy => Session.Status is ConversationStatus.Streaming or ConversationStatus.AwaitingInput;

    private ScenarioClientCapabilities Capabilities => new(
        _settings.RequestModelId,
        _settings.SupportsToolCalling,
        _settings.SelectedDescriptor?.SupportsReasoningSummary == true);

    private ChatClientDescriptor? Client => _settings.SelectedDescriptor is { IsReplay: false } descriptor ? descriptor : null;

    private bool ClientCanRunScenario => Client is not null && (!Scenario.RequiresToolCalling || _settings.SupportsToolCalling);

    /// <summary>Restores what the scenario keeps between launches, the first time it is shown.</summary>
    public async Task OpenAsync()
    {
        if (_opened)
            return;
        _opened = true;
        if (Scenario is PersistenceScenario)
            await RestoreSavedConversationAsync();
    }

    /// <summary>Reads the saved conversation again with a new agent and session, as starting the app does.</summary>
    public async Task ReloadAsync()
    {
        if (IsBusy)
            return;

        _replay = CreateSession();
        await RestoreSavedConversationAsync();
    }

    private bool CanChangeConversation() => !IsBusy;

    private bool CanStopReplay() => IsReplaying;

    public IAsyncRelayCommand NewConversationAction => NewConversationCommand;
    public IAsyncRelayCommand PlayExampleAction => PlayExampleCommand;
    public IRelayCommand StopReplayAction => StopReplayCommand;

    [RelayCommand(CanExecute = nameof(CanChangeConversation))]
    private async Task NewConversationAsync()
    {
        try
        {
            _recording.NewRecording();
            await Session.ClearAsync();
            StatusMessage = Scenario is PersistenceScenario
                ? "New conversation started; the saved conversation was cleared."
                : $"New conversation started. {DescribeClient()}";
        }
        catch (Exception exception)
        {
            StatusMessage = $"New conversation failed: {OneLine(exception.Message)}";
        }
        finally
        {
            Refresh();
        }
    }

    [RelayCommand(CanExecute = nameof(CanChangeConversation))]
    private async Task PlayExampleAsync()
    {
        using var cancellation = new CancellationTokenSource();
        _replayCancellation = cancellation;
        IsReplaying = true;
        StatusMessage = "Replaying the recorded example\u2026";
        UpdateBusyState();
        try
        {
            await using (var input = await FileSystem.OpenAppPackageFileAsync(ScenarioCatalog.ExamplePath(Scenario.Id)))
                await _recording.LoadFileAsync(input, cancellation.Token);

            var result = await _replay.ReplayAsync(playAll: true, cancellation.Token);
            var unanswered = result.UnansweredApprovals.Count == 0
                ? string.Empty
                : $" The example never answered {string.Join(", ", result.UnansweredApprovals.Distinct())}, so that turn is stopped.";
            var saved = Scenario is PersistenceScenario ? " It is now the saved conversation." : string.Empty;
            StatusMessage = $"Replayed the example ({result.Turns} {(result.Turns == 1 ? "turn" : "turns")}) without " +
                $"contacting a model.{saved} {DescribeClient()}{unanswered}";
        }
        catch (OperationCanceledException)
        {
            await ResetAfterFailedReplayAsync();
            StatusMessage = "Replay stopped. Press Example to play it again.";
        }
        catch (Exception exception)
        {
            await ResetAfterFailedReplayAsync();
            StatusMessage = $"Could not replay the example: {OneLine(exception.Message)}";
        }
        finally
        {
            if (ReferenceEquals(_replayCancellation, cancellation))
                _replayCancellation = null;
            IsReplaying = false;
            UpdateBusyState();
        }
    }

    [RelayCommand(CanExecute = nameof(CanStopReplay))]
    private void StopReplay()
    {
        _replayCancellation?.Cancel();
        StatusMessage = "Stopping the replay\u2026";
    }

    private async Task ResetAfterFailedReplayAsync()
    {
        _recording.NewRecording();
        await Session.ClearAsync();
    }

    private async Task RestoreSavedConversationAsync()
    {
        if (Scenario is not PersistenceScenario { Thread: { } thread })
            return;

        try
        {
            await Session.RestoreAsync();
            StatusMessage = thread.LoadError is { } error
                ? $"{error} Starting a new conversation."
                : Session.Turns.Count == 0
                    ? $"No saved conversation yet. Each turn you send is saved. {DescribeClient()}"
                    : $"Restored {Session.Turns.Count} {(Session.Turns.Count == 1 ? "turn" : "turns")} from the saved conversation. {DescribeClient()}";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Could not restore the saved conversation: {OneLine(exception.Message)}";
        }
        finally
        {
            Refresh();
        }
    }

    private ChatReplayDriver CreateSession()
    {
        foreach (var registration in _sessionRegistrations)
            registration.Dispose();
        _sessionRegistrations.Clear();
        var previous = Session;

        Session = new AgentContext(Scenario.CreateAgent(_router, _loggerFactory));
        _sessionRegistrations.Add(Session.RegisterOnStatusChanged(OnSessionStatusChanged));
        _sessionRegistrations.Add(Session.RegisterOnBlockAdded(OnSessionBlockAdded));
        previous?.Dispose();
        _status = Session.Status;

        var replay = new ChatReplayDriver(Session, _router, _recording);
        SessionCreated?.Invoke(this, EventArgs.Empty);
        return replay;
    }

    private void OnSessionStatusChanged(ConversationStatus status)
    {
        var previous = _status;
        _status = status;
        if (!IsReplaying)
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
                    StatusMessage = "Waiting for you: decide in the chat to continue.";
                    break;
                case ConversationStatus.Error:
                    StatusMessage = $"Request failed: {OneLine(Session.Error?.Message ?? "unknown error.")}";
                    break;
                case ConversationStatus.Idle when previous is ConversationStatus.Streaming or ConversationStatus.AwaitingInput:
                    StatusMessage = Session.Turns.Count > 0 && Session.Turns[^1].IsStopped
                        ? "Request stopped."
                        : Scenario is PersistenceScenario
                            ? "Response complete and saved to the conversation file."
                            : "Response complete.";
                    break;
            }
        }

        UpdateBusyState();
    }

    private void OnSessionBlockAdded(ConversationTurn turn, ContentBlock block)
    {
        if (IsReplaying || _receivingResponse || Session.Status != ConversationStatus.Streaming ||
            !turn.ResponseBlocks.Contains(block))
        {
            return;
        }

        _receivingResponse = true;
        StatusMessage = "Receiving response\u2026";
    }

    private void SettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SettingsPaneViewModel.SelectedClient) or
            nameof(SettingsPaneViewModel.SupportsToolCalling))
        {
            if (!IsBusy)
                StatusMessage = DescribeClient();
            Refresh();
        }
    }

    private void UpdateBusyState()
    {
        IsBusy = IsSessionBusy || IsReplaying;
        Refresh();
    }

    private void Refresh()
    {
        IsInputEnabled = ClientCanRunScenario && !IsReplaying;
        WelcomeMessage = Scenario.Summary + ". " + (ClientCanRunScenario
            ? "Try one of these, or press Example to replay a recorded run."
            : "Press Example to replay a recorded run. " + DescribeClient());
        IList<string> suggestions = ClientCanRunScenario ? [.. Scenario.Suggestions] : [];
        if (!suggestions.SequenceEqual(Suggestions))
            Suggestions = suggestions;
        if (string.IsNullOrEmpty(StatusMessage))
            StatusMessage = DescribeClient();

        NewConversationCommand.NotifyCanExecuteChanged();
        PlayExampleCommand.NotifyCanExecuteChanged();
        StopReplayCommand.NotifyCanExecuteChanged();
    }

    private string DescribeClient() => Client switch
    {
        null => "Select a live client in the sidebar to chat.",
        _ when Scenario.RequiresToolCalling && !_settings.SupportsToolCalling =>
            $"{Client.DisplayName} cannot call tools; select a tool-capable client, such as Azure OpenAI.",
        _ when Scenario.UsesReasoning && !Client.SupportsReasoningSummary =>
            $"{Client.DisplayName} selected; it does not return a reasoning summary.",
        _ => $"{Client.DisplayName} selected.",
    };

    // The status line shows at most two lines, so a provider error that spans several lines is joined into one.
    private static string OneLine(string text) =>
        string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
