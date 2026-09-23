using System.Globalization;
using System.Text.Json;
using ChatClientPlayground.Models;
using ChatClientPlayground.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.AI;

namespace ChatClientPlayground.ViewModels;

/// <summary>Owns provider selection and request options shown in the settings pane.</summary>
public sealed partial class SettingsPaneViewModel : ObservableObject
{
    private static JsonSerializerOptions StructuredJson => PlaygroundJsonContext.Default.Options;
    private readonly ChatClientService _chatClients;
    private readonly ChatRecordingService _recording;

    /// <summary>Initializes the settings state.</summary>
    public SettingsPaneViewModel(ChatClientService chatClients, ChatRecordingService recording)
    {
        _chatClients = chatClients;
        _recording = recording;
        _recording.Changed += (_, _) => MainThread.BeginInvokeOnMainThread(RefreshRecording);
        RefreshRecording();
        if (_recording.CacheLoadError is { } error)
            RecordingStatus = error;
        RefreshClientStatus();
    }

    /// <summary>Gets or sets the selected client implementation.</summary>
    [ObservableProperty] private ChatClientKind selectedClient = ChatClientKind.Local;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool useStreaming = true;
    [ObservableProperty] private bool useStructuredJson;
    [ObservableProperty] private string instructions = string.Empty;
    [ObservableProperty] private bool useDateTimeTool = true;
    [ObservableProperty] private bool useCalculatorTool = true;
    [ObservableProperty] private string toolMode = "Auto";
    [ObservableProperty] private MultipleToolCallsMode multipleToolCallsMode;
    [ObservableProperty] private bool enableTemperature;
    [ObservableProperty] private string temperature = "0.7";
    [ObservableProperty] private bool enableTopP;
    [ObservableProperty] private string topP = "1";
    [ObservableProperty] private bool enableTopK;
    [ObservableProperty] private string topK = "40";
    [ObservableProperty] private bool enableMaxOutputTokens;
    [ObservableProperty] private string maxOutputTokens = "512";
    [ObservableProperty] private bool enableFrequencyPenalty;
    [ObservableProperty] private string frequencyPenalty = "0";
    [ObservableProperty] private bool enablePresencePenalty;
    [ObservableProperty] private string presencePenalty = "0";
    [ObservableProperty] private bool enableSeed;
    [ObservableProperty] private string seed = "42";
    [ObservableProperty] private bool enableStopSequences;
    [ObservableProperty] private string stopSequences = string.Empty;
    [ObservableProperty] private string clientStatus = string.Empty;
    [ObservableProperty] private bool recordRequests;
    [ObservableProperty] private string recordingStatus = "Live requests are not recorded.";
    [ObservableProperty] private int interactionCount;
    [ObservableProperty] private int replayPosition;

    /// <summary>Gets whether provider selection is allowed.</summary>
    public bool CanSelectClient => !IsBusy;
    public bool IsRealClientSelected => SelectedClient != ChatClientKind.Recording;
    public bool IsRecordingSelected { get => SelectedClient == ChatClientKind.Recording; set { if (value) SelectedClient = ChatClientKind.Recording; } }
    public bool HasRecording => InteractionCount > 0;
    public bool CanStartRecording => IsRealClientSelected && !RecordRequests;
    public bool CanStopRecording => IsRealClientSelected && RecordRequests;
    public bool CanChooseRecordingFile => IsRecordingSelected && !HasRecording;
    public bool CanClearRecording => IsRecordingSelected && HasRecording;
    public bool CanRestartReplay => HasRecording;
    public bool CanSaveRecording => HasRecording;
    public string TapeSummary => InteractionCount == 0
        ? "No recorded interactions yet"
        : $"{InteractionCount} auto-saved interaction{(InteractionCount == 1 ? string.Empty : "s")}";
    public string PlaybackSummary => InteractionCount == 0
        ? "No recording loaded."
        : $"{InteractionCount} interaction{(InteractionCount == 1 ? string.Empty : "s")} · replay {ReplayPosition}/{InteractionCount}";
    public string StorageInfo => $"Completed interactions and loaded files auto-save to {_recording.CachePath}. Save copies the recording to {_recording.Path}. The OS may clear app cache.";
    public bool IsLocalSelected { get => SelectedClient == ChatClientKind.Local; set { if (value) SelectedClient = ChatClientKind.Local; } }
    public bool IsCloudSelected { get => SelectedClient == ChatClientKind.Cloud; set { if (value) SelectedClient = ChatClientKind.Cloud; } }
    public IAsyncRelayCommand? PlayRecordingCommand
    {
        get => _playRecordingCommand;
        set => SetProperty(ref _playRecordingCommand, value);
    }
    public bool IsToolModeAuto { get => ToolMode == "Auto"; set { if (value) ToolMode = "Auto"; } }
    public bool IsToolModeNone { get => ToolMode == "None"; set { if (value) ToolMode = "None"; } }
    public bool IsToolModeRequireAny { get => ToolMode == "RequireAny"; set { if (value) ToolMode = "RequireAny"; } }
    public bool IsMultipleToolCallsDefault { get => MultipleToolCallsMode == Models.MultipleToolCallsMode.Default; set { if (value) MultipleToolCallsMode = Models.MultipleToolCallsMode.Default; } }
    public bool IsMultipleToolCallsAllowed { get => MultipleToolCallsMode == Models.MultipleToolCallsMode.Allow; set { if (value) MultipleToolCallsMode = Models.MultipleToolCallsMode.Allow; } }
    public bool IsMultipleToolCallsDisallowed { get => MultipleToolCallsMode == Models.MultipleToolCallsMode.Disallow; set { if (value) MultipleToolCallsMode = Models.MultipleToolCallsMode.Disallow; } }
    public string LocalProviderInfo => _chatClients.GetClient(ChatClientKind.Local).Descriptor.Status;
    public string CloudProviderInfo => _chatClients.GetClient(ChatClientKind.Cloud).Descriptor.Status;
    public string RecordingProviderInfo => _chatClients.GetClient(ChatClientKind.Recording).Descriptor.Status;
    public IAsyncRelayCommand ChooseRecordingFileCommand => _chooseRecordingFileCommand ??= new AsyncRelayCommand(ChooseRecordingFileAsync);

    private IAsyncRelayCommand? _chooseRecordingFileCommand;
    private IAsyncRelayCommand? _playRecordingCommand;

    /// <summary>Creates request options using the current settings and supplied real tools.</summary>
    public ChatOptions CreateChatOptions(IList<AITool> tools)
    {
        if (ToolMode == "RequireAny" && tools.Count == 0)
            throw new ArgumentException("Require any needs at least one enabled tool.");

        var options = new ChatOptions
        {
            Instructions = string.IsNullOrWhiteSpace(Instructions) ? null : Instructions.Trim(),
            Temperature = OptionalFloat(EnableTemperature, Temperature, "Temperature"),
            TopP = OptionalFloat(EnableTopP, TopP, "Top P"),
            TopK = OptionalInt(EnableTopK, TopK, "Top K"),
            MaxOutputTokens = OptionalInt(EnableMaxOutputTokens, MaxOutputTokens, "Max output tokens", positive: true),
            FrequencyPenalty = OptionalFloat(EnableFrequencyPenalty, FrequencyPenalty, "Frequency penalty"),
            PresencePenalty = OptionalFloat(EnablePresencePenalty, PresencePenalty, "Presence penalty"),
            Seed = OptionalLong(EnableSeed, Seed, "Seed"),
            StopSequences = OptionalStopSequences(),
            Tools = tools,
            AllowMultipleToolCalls = MultipleToolCallsMode switch
            {
                Models.MultipleToolCallsMode.Allow => true,
                Models.MultipleToolCallsMode.Disallow => false,
                _ => null,
            },
            ToolMode = ToolMode switch
            {
                "None" => ChatToolMode.None,
                "RequireAny" => ChatToolMode.RequireAny,
                _ => ChatToolMode.Auto,
            },
        };
        if (UseStructuredJson)
            options.ResponseFormat = ChatResponseFormat.ForJsonSchema<PlaygroundResponse>(StructuredJson);
        return options;
    }

    private void RefreshClientStatus()
    {
        ClientStatus = _chatClients.GetClient(SelectedClient).Descriptor.Status;
        OnPropertyChanged(nameof(LocalProviderInfo));
        OnPropertyChanged(nameof(CloudProviderInfo));
        OnPropertyChanged(nameof(RecordingProviderInfo));
    }

    [RelayCommand]
    private void ToggleRecording() => RecordRequests = !RecordRequests;

    [RelayCommand]
    private void NewRecording() => ResetRecording("New recording started.", "New recording");

    [RelayCommand]
    private void ClearRecording() => ResetRecording("Recording cleared.", "Clear recording");

    private void ResetRecording(string successMessage, string action)
    {
        try
        {
            _recording.NewRecording();
            RecordingStatus = successMessage;
        }
        catch (Exception exception)
        {
            RecordingStatus = $"{action} failed: {exception.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanSaveRecording))]
    private void Save()
    {
        try
        {
            _recording.Save();
            RecordingStatus = $"Saved {InteractionCount} interaction(s).";
        }
        catch (Exception exception)
        {
            RecordingStatus = $"Save failed: {exception.Message}";
        }
    }

    private async Task ChooseRecordingFileAsync()
    {
        IsBusy = true;
        try
        {
            var file = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Select a chat recording",
            });
            if (file is null)
            {
                RecordingStatus = "File selection cancelled.";
                return;
            }

            await using var input = await file.OpenReadAsync();
            await _recording.LoadFileAsync(input);
            RecordingStatus = $"Loaded '{file.FileName}' ({_recording.InteractionCount} interactions).";
        }
        catch (Exception exception)
        {
            RecordingStatus = $"Load file failed: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRestartReplay))]
    private void RestartReplay()
    {
        _recording.RestartReplay();
        RecordingStatus = "Replay restarted at interaction 1.";
    }

    private void RefreshRecording()
    {
        RecordRequests = _recording.IsRecordingEnabled;
        InteractionCount = _recording.InteractionCount;
        ReplayPosition = _recording.ReplayPosition;
        OnPropertyChanged(nameof(TapeSummary));
        OnPropertyChanged(nameof(PlaybackSummary));
        RestartReplayCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        PlayRecordingCommand?.NotifyCanExecuteChanged();
        RefreshClientStatus();
    }

    private IList<string>? OptionalStopSequences()
    {
        if (!EnableStopSequences)
            return null;

        var values = StopSequences.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return values.Length > 0
            ? values
            : throw new ArgumentException("Enter at least one comma-separated stop sequence or clear its checkbox.");
    }

    private static float? OptionalFloat(bool enabled, string value, string name)
    {
        if (!enabled)
            return null;
        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            throw new ArgumentException($"{name} must be a number using '.' as the decimal separator.");
        return parsed;
    }

    private static int? OptionalInt(bool enabled, string value, string name, bool positive = false)
    {
        if (!enabled)
            return null;
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ||
            (positive && parsed <= 0))
            throw new ArgumentException(positive ? $"{name} must be a positive integer." : $"{name} must be an integer.");
        return parsed;
    }

    private static long? OptionalLong(bool enabled, string value, string name)
    {
        if (!enabled)
            return null;
        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            throw new ArgumentException($"{name} must be an integer.");
        return parsed;
    }

    partial void OnSelectedClientChanged(ChatClientKind value)
    {
        RefreshClientStatus();
        if (_recording.CacheLoadError is null)
            RecordingStatus = value == ChatClientKind.Recording
                ? "Playback never invokes a model."
                : _recording.IsRecordingEnabled
                    ? "Real-client responses will be auto-saved."
                    : "Live requests are not recorded.";
        OnPropertyChanged(nameof(IsLocalSelected));
        OnPropertyChanged(nameof(IsCloudSelected));
        OnPropertyChanged(nameof(IsRealClientSelected));
        OnPropertyChanged(nameof(IsRecordingSelected));
        OnPropertyChanged(nameof(CanStartRecording));
        OnPropertyChanged(nameof(CanStopRecording));
        OnPropertyChanged(nameof(CanChooseRecordingFile));
        OnPropertyChanged(nameof(CanClearRecording));
    }

    partial void OnRecordRequestsChanged(bool value)
    {
        _recording.SetRecordingEnabled(value);
        OnPropertyChanged(nameof(CanStartRecording));
        OnPropertyChanged(nameof(CanStopRecording));
        RecordingStatus = value ? "Recording real-client responses." : "Recording stopped.";
    }

    partial void OnInteractionCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasRecording));
        OnPropertyChanged(nameof(CanChooseRecordingFile));
        OnPropertyChanged(nameof(CanClearRecording));
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanSelectClient));

    partial void OnToolModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsToolModeAuto));
        OnPropertyChanged(nameof(IsToolModeNone));
        OnPropertyChanged(nameof(IsToolModeRequireAny));
    }

    partial void OnMultipleToolCallsModeChanged(MultipleToolCallsMode value)
    {
        OnPropertyChanged(nameof(IsMultipleToolCallsDefault));
        OnPropertyChanged(nameof(IsMultipleToolCallsAllowed));
        OnPropertyChanged(nameof(IsMultipleToolCallsDisallowed));
    }
}
