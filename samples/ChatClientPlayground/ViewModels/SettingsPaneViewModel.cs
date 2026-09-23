using System.Globalization;
using System.Text.Json;
using ChatClientPlayground.Models;
using ChatClientPlayground.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.AI;

namespace ChatClientPlayground.ViewModels;

/// <summary>Owns provider selection and request options shown in the settings pane.</summary>
public sealed partial class SettingsPaneViewModel : ObservableObject
{
    private static JsonSerializerOptions StructuredJson => PlaygroundJsonContext.Default.Options;
    private readonly ChatClientService _chatClients;

    /// <summary>Initializes the settings state.</summary>
    public SettingsPaneViewModel(ChatClientService chatClients, RecordingViewModel recording)
    {
        _chatClients = chatClients;
        Recording = recording;
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

    /// <summary>Gets whether provider selection is allowed.</summary>
    public bool CanSelectClient => !IsBusy;
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
    /// <summary>Gets the contained recording control state.</summary>
    public RecordingViewModel Recording { get; }

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
