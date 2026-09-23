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
    [ObservableProperty] private RecordingMode mode;
    [ObservableProperty] private string recordingStatus = "Live requests are not recorded.";
    [ObservableProperty] private int interactionCount;
    [ObservableProperty] private int replayPosition;
    [ObservableProperty] private string storagePath = string.Empty;

    /// <summary>Gets whether provider selection is allowed.</summary>
    public bool CanSelectClient => !IsBusy;
    public bool IsLive { get => Mode == RecordingMode.Live; set { if (value) Mode = RecordingMode.Live; } }
    public bool IsRecord { get => Mode == RecordingMode.Record; set { if (value) Mode = RecordingMode.Record; } }
    public bool IsReplay { get => Mode == RecordingMode.Replay; set { if (value) Mode = RecordingMode.Replay; } }
    public bool CanRestartReplay => InteractionCount > 0;
    public string TapeSummary => $"{InteractionCount} interaction{(InteractionCount == 1 ? string.Empty : "s")} · replay {ReplayPosition}/{InteractionCount}";
    public string StorageInfo => $"Recordings are saved app-locally. Path: {StoragePath}";
    public bool IsLocalSelected { get => SelectedClient == ChatClientKind.Local; set { if (value) SelectedClient = ChatClientKind.Local; } }
    public bool IsCloudSelected { get => SelectedClient == ChatClientKind.Cloud; set { if (value) SelectedClient = ChatClientKind.Cloud; } }
    public bool IsToolModeAuto { get => ToolMode == "Auto"; set { if (value) ToolMode = "Auto"; } }
    public bool IsToolModeNone { get => ToolMode == "None"; set { if (value) ToolMode = "None"; } }
    public bool IsToolModeRequireAny { get => ToolMode == "RequireAny"; set { if (value) ToolMode = "RequireAny"; } }
    public bool IsMultipleToolCallsDefault { get => MultipleToolCallsMode == Models.MultipleToolCallsMode.Default; set { if (value) MultipleToolCallsMode = Models.MultipleToolCallsMode.Default; } }
    public bool IsMultipleToolCallsAllowed { get => MultipleToolCallsMode == Models.MultipleToolCallsMode.Allow; set { if (value) MultipleToolCallsMode = Models.MultipleToolCallsMode.Allow; } }
    public bool IsMultipleToolCallsDisallowed { get => MultipleToolCallsMode == Models.MultipleToolCallsMode.Disallow; set { if (value) MultipleToolCallsMode = Models.MultipleToolCallsMode.Disallow; } }
    public string LocalProviderInfo => _chatClients.GetClient(ChatClientKind.Local).Descriptor.Status;
    public string CloudProviderInfo => _chatClients.GetClient(ChatClientKind.Cloud).Descriptor.Status;
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
    }

    [RelayCommand]
    private void NewRecording()
    {
        _recording.NewRecording();
        RecordingStatus = "New in-memory recording created.";
    }

    [RelayCommand]
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

    [RelayCommand]
    private void Load()
    {
        try
        {
            _recording.Load();
            RecordingStatus = $"Loaded {InteractionCount} interaction(s); replay is ready.";
        }
        catch (Exception exception)
        {
            RecordingStatus = $"Load failed: {exception.Message}";
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
        Mode = _recording.Mode;
        InteractionCount = _recording.InteractionCount;
        ReplayPosition = _recording.ReplayPosition;
        StoragePath = _recording.Path;
        OnPropertyChanged(nameof(TapeSummary));
        OnPropertyChanged(nameof(StorageInfo));
        RestartReplayCommand.NotifyCanExecuteChanged();
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
        OnPropertyChanged(nameof(IsLocalSelected));
        OnPropertyChanged(nameof(IsCloudSelected));
    }

    partial void OnModeChanged(RecordingMode value)
    {
        _recording.SetMode(value);
        RecordingStatus = value switch
        {
            RecordingMode.Live => "Live requests are not recorded.",
            RecordingMode.Record => "Requests will be recorded in memory.",
            _ => InteractionCount == 0
                ? "Replay needs a loaded or recorded interaction."
                : $"Replay is ready at interaction {ReplayPosition + 1} of {InteractionCount}.",
        };
        OnPropertyChanged(nameof(IsLive));
        OnPropertyChanged(nameof(IsRecord));
        OnPropertyChanged(nameof(IsReplay));
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
