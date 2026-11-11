using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.AI;
using Microsoft.Maui.Essentials.AI;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Owns provider selection and request options shown in the settings pane.</summary>
public sealed partial class SettingsPaneViewModel : ObservableObject
{
    /// <summary>The system instructions a chat starts with, which tell the model what the playground can do.</summary>
    public const string DefaultInstructions =
        "You are the assistant in a .NET MAUI chat playground. Answer in plain text, without Markdown. " +
        "Use the calculator for arithmetic, one operation per call. You can read the local date and time and this " +
        "device's connection. The notes you share with the user come with each message when they have text; when " +
        "none come, the notes are empty.";

    /// <summary>
    /// What the instructions add for a client that gets the chat's UI actions, which the app runs: the theme, and
    /// proposing changes to the notes.
    /// </summary>
    public const string UIActionInstructions =
        "You can switch the app between light, dark, and the system theme with set_theme. To change the notes, call " +
        "propose_notes with the complete new text. The user accepts or rejects the proposal, so never say the notes " +
        "changed unless the result says it was accepted.";

    private const string InstructionsWithUIActions = DefaultInstructions + " " + UIActionInstructions;

    /// <summary>Initializes the settings state.</summary>
    public SettingsPaneViewModel(IEnumerable<IChatClient> clients)
    {
        Clients = clients.Select((client, index) => new ChatClientOption(client, client.GetRequiredService<ChatClientDescriptor>(), index)).ToArray();

        if (Clients.Select(option => option.Descriptor.Id).Distinct(StringComparer.Ordinal).Count() != Clients.Count)
            throw new ArgumentException("Chat clients need distinct IDs.", nameof(clients));

        SelectedClient = Clients.FirstOrDefault()?.Client;
    }

    public IReadOnlyList<ChatClientOption> Clients { get; }

    /// <summary>Gets or sets the selected client implementation.</summary>
    [ObservableProperty] private IChatClient? selectedClient;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RequestModelId))]
    private string? selectedModelId;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SupportsToolCalling))]
    [NotifyPropertyChangedFor(nameof(SupportsUIActions))]
    private ChatModelStatus? modelStatus;

    [ObservableProperty] private bool useStreaming = true;
    [ObservableProperty] private bool useStructuredJson;
    [ObservableProperty] private bool useReasoningSummary = true;
    [ObservableProperty] private string instructions = DefaultInstructions;
    [ObservableProperty] private bool useDateTimeTool = true;
    [ObservableProperty] private bool useCalculatorTool = true;
    [ObservableProperty] private bool useConnectionStatusTool = true;
    [ObservableProperty] private bool useImageGenerationTool = true;
    [ObservableProperty] private bool requireToolApproval;

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
        ? client.GetRequiredService<ChatClientDescriptor>()
        : null;
    public bool CanEditOptions => !IsBusy && SelectedDescriptor is not null;
    public IReadOnlyList<ChatModelOption> Models => SelectedDescriptor?.Models ?? [];
    public bool HasModels => Models.Count > 0;
    public string? RequestModelId => HasModels ? SelectedModelId : null;
    public bool SupportsToolCalling => SelectedDescriptor?.SupportsToolCalling == true &&
        (ModelStatus?.Capabilities is not { } capabilities || capabilities.Contains(ChatModelCapability.ToolCalling));

    /// <summary>
    /// Gets whether the selected client hands tool calls back to the app, so the model gets the chat's UI actions.
    /// </summary>
    public bool SupportsUIActions => SupportsToolCalling && SelectedDescriptor?.SupportsUIActions == true;

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

    /// <summary>Creates request options using the current settings, the enabled tools, and the chat's UI actions.</summary>
    /// <param name="tools">The tools the settings enable, which run on this device or at the provider.</param>
    /// <param name="uiActions">The chat's UI actions, which the app runs, such as switching the theme.</param>
    public ChatOptions CreateChatOptions(IList<AITool> tools, IList<AITool>? uiActions = null)
    {
        uiActions ??= [];
        var supportsToolCalling = SupportsToolCalling;
        if (!supportsToolCalling && (tools.Count > 0 || uiActions.Count > 0))
            throw new NotSupportedException("The selected chat client does not support tool calling.");
        if (supportsToolCalling && ToolMode != ChatToolMode.Auto &&
            ToolMode != ChatToolMode.None &&
            ToolMode != ChatToolMode.RequireAny)
            throw new ArgumentOutOfRangeException(nameof(ToolMode), "Select a supported tool mode.");

        // A UI action does not count: forced to call one, the model would switch the theme or propose notes each time.
        if (supportsToolCalling && ToolMode == ChatToolMode.RequireAny && tools.Count == 0)
            throw new ArgumentException("Require any needs at least one enabled tool.");
        if (supportsToolCalling && ToolMode is RequiredChatToolMode && SelectedDescriptor?.SupportsRequiredToolMode == false)
            throw new NotSupportedException("The selected chat client does not support required tool mode. Select Auto.");

        var options = new ChatOptions
        {
            ModelId = RequestModelId,
            Instructions = string.IsNullOrWhiteSpace(Instructions) ? null : Instructions.Trim(),
            Temperature = OptionalFloat(EnableTemperature, Temperature, "Temperature"),
            TopP = OptionalFloat(EnableTopP, TopP, "Top P"),
            TopK = OptionalInt(EnableTopK, TopK, "Top K"),
            MaxOutputTokens = OptionalInt(EnableMaxOutputTokens, MaxOutputTokens, "Max output tokens", positive: true),
            FrequencyPenalty = OptionalFloat(EnableFrequencyPenalty, FrequencyPenalty, "Frequency penalty"),
            PresencePenalty = OptionalFloat(EnablePresencePenalty, PresencePenalty, "Presence penalty"),
            Seed = OptionalLong(EnableSeed, Seed, "Seed"),
            StopSequences = OptionalStopSequences(),
            Tools = supportsToolCalling ? [.. tools, .. uiActions] : null,
            AllowMultipleToolCalls = supportsToolCalling ? AllowMultipleToolCalls : null,
            ToolMode = supportsToolCalling ? ToolMode : ChatToolMode.None,
        };
        if (UseReasoningSummary && SelectedDescriptor?.SupportsReasoningSummary == true)
            options.Reasoning = new ReasoningOptions
            {
                Effort = ReasoningEffort.Medium,
                Output = ReasoningOutput.Summary,
            };
        if (UseStructuredJson && SelectedDescriptor?.SupportsStructuredOutput == true)
            options.ResponseFormat = ChatResponseFormat.ForJsonSchema<PlaygroundResponse>(PlaygroundJsonContext.Default.Options);
        return options;
    }

    /// <summary>Gets the instructions a chat starts with for the selected client, which mention only what it can do.</summary>
    public string GetDefaultInstructions() => SupportsUIActions ? InstructionsWithUIActions : DefaultInstructions;

    /// <summary>Creates the prompts the empty chat suggests, for what the selected client and the settings allow.</summary>
    public IList<string> CreateSuggestions()
    {
        if (SelectedDescriptor is not { } descriptor)
            return [];

        var suggestions = new List<string>();
        var tools = SupportsToolCalling;
        if (tools && UseCalculatorTool)
            suggestions.Add("Use the calculator: (12 \u00D7 7) + (30 \u00F7 4)");
        if (tools && UseDateTimeTool)
            suggestions.Add("What time is it here?");
        if (tools && UseConnectionStatusTool)
            suggestions.Add("Am I online?");
        if (SupportsUIActions)
        {
            suggestions.Add("Switch to dark mode");
            suggestions.Add("Add a Paris packing list to my notes");
        }
        if (UseReasoningSummary && descriptor.SupportsReasoningSummary)
            suggestions.Add("Which is larger, 9.11 or 9.9?");
        if (tools && UseImageGenerationTool && descriptor.SupportsImageGeneration)
            suggestions.Add("Generate an image of a cozy cabin");
        if (suggestions.Count == 0)
            suggestions.Add("Tell me a fun fact about .NET MAUI");
        return suggestions;
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
        ModelStatus = null;
        if (SelectedDescriptor?.SupportsRequiredToolMode == false && ToolMode is RequiredChatToolMode)
            ToolMode = ChatToolMode.Auto;
        SelectedModelId = Models.FirstOrDefault()?.Id;
        ClientStatus = SelectedDescriptor?.Description ?? "No chat client selected.";
        OnPropertyChanged(nameof(SelectedDescriptor));
        OnPropertyChanged(nameof(CanEditOptions));
        OnPropertyChanged(nameof(Models));
        OnPropertyChanged(nameof(HasModels));
        OnPropertyChanged(nameof(RequestModelId));
        OnPropertyChanged(nameof(SupportsToolCalling));
        OnPropertyChanged(nameof(SupportsUIActions));
        UpdateDefaultInstructions();
    }

    partial void OnModelStatusChanged(ChatModelStatus? value) => UpdateDefaultInstructions();

    // Instructions the user edited stay as they are.
    private void UpdateDefaultInstructions()
    {
        if (Instructions is DefaultInstructions or InstructionsWithUIActions)
            Instructions = GetDefaultInstructions();
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
