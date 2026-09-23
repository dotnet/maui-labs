using System.Globalization;
using System.Text.Json;
using AIExtensions.Sample.ChatPlayground.Models;
using AIExtensions.Sample.ChatPlayground.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.AI;

namespace AIExtensions.Sample.ChatPlayground.ViewModels;

/// <summary>Owns provider selection and request options shown in the settings pane.</summary>
public sealed partial class SettingsPaneViewModel : ObservableObject
{
    private static JsonSerializerOptions StructuredJson => PlaygroundJsonContext.Default.Options;

    /// <summary>Initializes the settings state.</summary>
    public SettingsPaneViewModel(IEnumerable<IChatClient> clients)
    {
        Clients = clients.Select((client, index) => new ChatClientOption(
            client,
            client.GetService<ChatClientDescriptor>()
                ?? throw new InvalidOperationException($"Chat client {index} did not expose a ChatClientDescriptor."),
            index)).ToArray();
        SelectedClient = Clients.FirstOrDefault(option => !option.Descriptor.IsReplay)?.Client
            ?? throw new InvalidOperationException("At least one real chat client must be registered.");
    }

    public IReadOnlyList<ChatClientOption> Clients { get; }

    /// <summary>Gets or sets the selected client implementation.</summary>
    [ObservableProperty] private IChatClient? selectedClient;
    [ObservableProperty] private bool isBusy;

    [ObservableProperty] private bool useStreaming = true;
    [ObservableProperty] private bool useStructuredJson;
    [ObservableProperty] private bool useReasoningSummary = true;
    [ObservableProperty] private string instructions = string.Empty;
    [ObservableProperty] private bool useDateTimeTool = true;
    [ObservableProperty] private bool useCalculatorTool = true;
    [ObservableProperty] private bool useImageGenerationTool = true;

    // Store tool choices in the same types that ChatOptions accepts.
    [ObservableProperty] private ChatToolMode toolMode = ChatToolMode.Auto;
    [ObservableProperty] private bool? allowMultipleToolCalls;

    // Disabled generation controls leave the provider's corresponding option unset.
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
    public ChatClientDescriptor? SelectedDescriptor => SelectedClient is { } client
        ? client.GetService<ChatClientDescriptor>()
            ?? throw new InvalidOperationException("The selected chat client did not expose a ChatClientDescriptor.")
        : null;
    public bool CanEditOptions => !IsBusy && SelectedDescriptor is { IsReplay: false };

    public bool IsMultipleToolCallsDefault
    {
        get => AllowMultipleToolCalls is null;
        set { if (value) AllowMultipleToolCalls = null; }
    }

    public bool IsMultipleToolCallsAllowed
    {
        get => AllowMultipleToolCalls == true;
        set { if (value) AllowMultipleToolCalls = true; }
    }

    public bool IsMultipleToolCallsDisallowed
    {
        get => AllowMultipleToolCalls == false;
        set { if (value) AllowMultipleToolCalls = false; }
    }

    /// <summary>Creates request options using the current settings and supplied real tools.</summary>
    public ChatOptions CreateChatOptions(IList<AITool> tools)
    {
        if (ToolMode != ChatToolMode.Auto &&
            ToolMode != ChatToolMode.None &&
            ToolMode != ChatToolMode.RequireAny)
            throw new ArgumentOutOfRangeException(nameof(ToolMode), "Select a supported tool mode.");
        if (ToolMode == ChatToolMode.RequireAny && tools.Count == 0)
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
            AllowMultipleToolCalls = AllowMultipleToolCalls,
            ToolMode = ToolMode,
        };
        if (UseReasoningSummary && SelectedDescriptor?.SupportsReasoningSummary == true)
            options.Reasoning = new ReasoningOptions
            {
                Effort = ReasoningEffort.Medium,
                Output = ReasoningOutput.Summary,
            };
        if (UseStructuredJson)
            options.ResponseFormat = ChatResponseFormat.ForJsonSchema<PlaygroundResponse>(StructuredJson);
        return options;
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
            throw new ArgumentException(positive
                ? $"{name} must be a positive integer."
                : $"{name} must be an integer.");
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

    partial void OnSelectedClientChanged(IChatClient? value)
    {
        ClientStatus = SelectedDescriptor?.Status ?? "No chat client selected.";
        OnPropertyChanged(nameof(SelectedDescriptor));
        OnPropertyChanged(nameof(CanEditOptions));
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSelectClient));
        OnPropertyChanged(nameof(CanEditOptions));
    }

    partial void OnAllowMultipleToolCallsChanged(bool? value)
    {
        OnPropertyChanged(nameof(IsMultipleToolCallsDefault));
        OnPropertyChanged(nameof(IsMultipleToolCallsAllowed));
        OnPropertyChanged(nameof(IsMultipleToolCallsDisallowed));
    }
}

/// <summary>Supplies descriptor bindings and a stable row ID for the client selector.</summary>
public sealed record ChatClientOption(IChatClient Client, ChatClientDescriptor Descriptor, int Index)
{
    public string AutomationId => $"ChatClient{Index}Radio";
}
