using AIExtensions.Sample.ChatPlayground;
using Microsoft.Extensions.AI;
using Microsoft.Maui.Essentials.AI;

namespace Microsoft.Maui.AI.Chat.Tests;

public sealed class SettingsPaneViewModelTests
{
    [Fact]
    public void ModelCapabilities_EnableToolsOnlyForTheCapableSelectionWithoutLosingToolChoices()
    {
        using var client = new DescribedChatClient(new StubChatClient(),
            new ChatClientDescriptor("nano", "Nano", "Ready", SupportsToolCalling: true,
                SupportsRequiredToolMode: false));
        var settings = new SettingsPaneViewModel([client]);
        var tool = AIFunctionFactory.Create(() => "secret", "lookup");
        settings.ModelStatus = new(ChatModelAvailability.Available, [ChatModelCapability.Text]);
        Assert.False(settings.SupportsToolCalling);
        Assert.Null(settings.CreateChatOptions([]).Tools);
        Assert.Same(ChatToolMode.None, settings.CreateChatOptions([]).ToolMode);
        Assert.True(settings.UseCalculatorTool);

        settings.ModelStatus = new(ChatModelAvailability.Available, [ChatModelCapability.Text, ChatModelCapability.ToolCalling]);
        Assert.True(settings.SupportsToolCalling);
        Assert.Same(tool, Assert.Single(settings.CreateChatOptions([tool]).Tools!));
        settings.ToolMode = ChatToolMode.RequireAny;
        Assert.Throws<NotSupportedException>(() => settings.CreateChatOptions([tool]));
    }

    [Fact]
    public void ChatDescriptor_ToolCallingIsOptIn()
    {
        Assert.False(new ChatClientDescriptor("test", "Test", "Ready").SupportsToolCalling);
    }

    [Fact]
    public void SelectedClient_ExposesActualClientAndItsDescriptor()
    {
        using var live = CreateClient("Live", isReplay: false);
        using var replay = CreateClient("Replay", isReplay: true);
        var settings = new SettingsPaneViewModel([live, replay]);

        Assert.Same(live, settings.SelectedClient);
        Assert.True(settings.CanEditOptions);
        Assert.Equal("Live ready", settings.ClientStatus);
        Assert.Equal("ChatClient0Radio", settings.Clients[0].AutomationId);

        settings.SelectedClient = replay;

        Assert.Same(replay, settings.SelectedClient);
        Assert.True(settings.SelectedDescriptor?.IsReplay);
        Assert.False(settings.CanEditOptions);
        Assert.Equal("Replay ready", settings.ClientStatus);
        Assert.Equal("ChatClient1Radio", settings.Clients[1].AutomationId);
    }

    [Fact]
    public void Constructor_ClientWithoutDescriptor_Throws()
    {
        using var client = new StubChatClient();

        Assert.Throws<InvalidOperationException>(() => new SettingsPaneViewModel([client]));
    }

    [Fact]
    public void Constructor_DuplicateClientIds_Throws()
    {
        using var first = CreateClient("Live", isReplay: false);
        using var second = CreateClient("Live", isReplay: false);

        Assert.Throws<ArgumentException>(() => new SettingsPaneViewModel([first, second]));
    }

    [Fact]
    public void Constructor_OnlyReplayClient_SelectsReplayWithoutLiveProvider()
    {
        using var replay = CreateClient("Replay", isReplay: true);

        var settings = new SettingsPaneViewModel([replay]);

        Assert.Same(replay, settings.SelectedClient);
        Assert.False(settings.CanEditOptions);
        Assert.Equal("Replay ready", settings.ClientStatus);
    }

    [Fact]
    public void Constructor_ReplayBeforeLive_SelectsFirstLiveClient()
    {
        using var replay = CreateClient("Replay", isReplay: true);
        using var live = CreateClient("Live", isReplay: false);
        var settings = new SettingsPaneViewModel([replay, live]);

        Assert.Same(live, settings.SelectedClient);
        Assert.Equal("ChatClient1Radio", settings.Clients[1].AutomationId);
    }

    [Fact]
    public void ModelSelection_OverridesEachRequestWithoutReplacingTheClient()
    {
        using var client = new DescribedChatClient(new StubChatClient(),
            new ChatClientDescriptor("models", "Models", "Ready", Models:
            [
                new("gemini-nano", "Default"),
                new("gemini-nano-preview-fast", "Preview fast"),
            ]));
        using var other = CreateClient("Other", isReplay: false);
        var settings = new SettingsPaneViewModel([client, other]);

        Assert.True(settings.HasModels);
        Assert.Equal("gemini-nano", settings.SelectedModelId);
        Assert.Equal("ChatModel-gemini-nano-preview-fast", settings.Models[1].AutomationId);
        var originalOptions = settings.CreateChatOptions([]);
        Assert.Equal("gemini-nano", originalOptions.ModelId);

        settings.SelectedModelId = "gemini-nano-preview-fast";

        Assert.Same(client, settings.SelectedClient);
        Assert.Equal("gemini-nano-preview-fast", settings.CreateChatOptions([]).ModelId);
        Assert.Equal("gemini-nano", originalOptions.ModelId);

        settings.IsBusy = true;
        Assert.False(settings.CanEditOptions);
        settings.IsBusy = false;
        settings.SelectedClient = other;

        Assert.False(settings.HasModels);
        Assert.Null(settings.RequestModelId);
        Assert.Null(settings.CreateChatOptions([]).ModelId);

        settings.SelectedClient = client;
        Assert.Equal("gemini-nano", settings.SelectedModelId);
    }

    [Fact]
    public void CreateChatOptions_UsesBuiltInToolModesAndRejectsUnsupportedModes()
    {
        using var client = CreateClient("Live", isReplay: false);
        var settings = new SettingsPaneViewModel([client]);

        Assert.True(settings.UseConnectionStatusTool);
        Assert.Same(ChatToolMode.Auto, settings.CreateChatOptions([]).ToolMode);

        settings.ToolMode = ChatToolMode.None;
        Assert.Same(ChatToolMode.None, settings.CreateChatOptions([]).ToolMode);

        settings.ToolMode = ChatToolMode.RequireAny;
        Assert.Throws<ArgumentException>(() => settings.CreateChatOptions([]));

        settings.ToolMode = ChatToolMode.RequireSpecific("test");
        Assert.Throws<ArgumentOutOfRangeException>(() => settings.CreateChatOptions([]));
    }

    [Fact]
    public void MultipleToolCalls_MapsNullableBoolDirectlyToChatOptions()
    {
        using var client = CreateClient("Live", isReplay: false);
        var settings = new SettingsPaneViewModel([client]);

        Assert.True(settings.IsMultipleToolCallsDefault);
        Assert.Null(settings.CreateChatOptions([]).AllowMultipleToolCalls);

        settings.IsMultipleToolCallsAllowed = true;
        Assert.True(settings.CreateChatOptions([]).AllowMultipleToolCalls);

        settings.IsMultipleToolCallsDisallowed = true;
        Assert.False(settings.CreateChatOptions([]).AllowMultipleToolCalls);

        settings.IsMultipleToolCallsDefault = true;
        Assert.Null(settings.CreateChatOptions([]).AllowMultipleToolCalls);
    }

    [Fact]
    public void CreateChatOptions_ClientWithoutToolCalling_DoesNotForwardHiddenToolSettings()
    {
        using var live = CreateClient("Live", isReplay: false);
        using var noTools = new DescribedChatClient(
            new StubChatClient(),
            new ChatClientDescriptor(
                "no-tools", "No tools", "Ready", SupportsImageInput: true, SupportsToolCalling: false));
        var settings = new SettingsPaneViewModel([live, noTools]);
        settings.ToolMode = ChatToolMode.RequireAny;
        settings.AllowMultipleToolCalls = true;
        settings.SelectedClient = noTools;

        var options = settings.CreateChatOptions([]);
        Assert.Null(options.Tools);
        Assert.Same(ChatToolMode.None, options.ToolMode);
        Assert.Null(options.AllowMultipleToolCalls);

        var tool = AIFunctionFactory.Create(() => "unexpected");
        Assert.Throws<NotSupportedException>(() => settings.CreateChatOptions([tool]));

        settings.SelectedClient = live;
        Assert.Same(ChatToolMode.RequireAny, settings.CreateChatOptions([tool]).ToolMode);
    }

    [Fact]
    public void CreateChatOptions_SwitchingFromStructuredProvider_DoesNotSendStaleJsonSettings()
    {
        using var live = CreateClient("Live", isReplay: false);
        using var textOnly = new DescribedChatClient(new StubChatClient(),
            new ChatClientDescriptor("text-only", "Text only", "Plain text",
                SupportsStructuredOutput: false));
        var settings = new SettingsPaneViewModel([live, textOnly]) { UseStructuredJson = true };
        Assert.IsType<ChatResponseFormatJson>(settings.CreateChatOptions([]).ResponseFormat);

        settings.SelectedClient = textOnly;
        Assert.True(settings.UseStructuredJson);
        Assert.Null(settings.CreateChatOptions([]).ResponseFormat);

        settings.SelectedClient = live;
        Assert.IsType<ChatResponseFormatJson>(settings.CreateChatOptions([]).ResponseFormat);

        settings.UseStructuredJson = false;
        Assert.Null(settings.CreateChatOptions([]).ResponseFormat);
    }

    [Fact]
    public void CreateChatOptions_TextOnlyDescriptor_DoesNotApplyUnsupportedSettings()
    {
        using var client = new DescribedChatClient(
            new StubChatClient(),
            new ChatClientDescriptor(
                "text-only", "Text only", "The first request confirms availability.",
                SupportsImageInput: true, SupportsToolCalling: false, SupportsStructuredOutput: false));
        var settings = new SettingsPaneViewModel([client]) { UseStructuredJson = true };

        var options = settings.CreateChatOptions([]);

        Assert.Same(client, settings.SelectedClient);
        Assert.Same(ChatToolMode.None, options.ToolMode);
        Assert.Null(options.Tools);
        Assert.Null(options.AllowMultipleToolCalls);
        Assert.Null(options.Reasoning);
        Assert.Null(options.ResponseFormat);
        Assert.True(settings.UseStructuredJson);
        Assert.True(settings.SelectedDescriptor!.SupportsImageInput);
        Assert.False(settings.SelectedDescriptor.SupportsReasoningSummary);
        Assert.False(settings.SelectedDescriptor.SupportsImageGeneration);
        Assert.False(settings.SelectedDescriptor.SupportsStructuredOutput);
        Assert.Contains("first request", settings.ClientStatus);
    }

    [Fact]
    public void StructuredOutput_DisablesUnsupportedToolCombinationWithoutLosingSelections()
    {
        using var client = new DescribedChatClient(new StubChatClient(),
            new ChatClientDescriptor("nano", "Nano", "Ready", SupportsToolCalling: true,
                SupportsStructuredOutputWithTools: false));
        var settings = new SettingsPaneViewModel([client])
        {
            ModelStatus = new(ChatModelAvailability.Available,
                [ChatModelCapability.StructuredOutput, ChatModelCapability.ToolCalling]),
            UseStructuredJson = true,
        };
        Assert.True(settings.SupportsStructuredOutput);
        Assert.False(settings.SupportsToolCalling);
        Assert.True(settings.UseCalculatorTool);
        var options = settings.CreateChatOptions([]);
        Assert.IsType<ChatResponseFormatJson>(options.ResponseFormat);
        Assert.Null(options.Tools);
        Assert.Same(ChatToolMode.None, options.ToolMode);

        settings.UseStructuredJson = false;
        Assert.True(settings.SupportsToolCalling);
        Assert.True(settings.UseCalculatorTool);
        var tool = AIFunctionFactory.Create(() => "value", "lookup");
        Assert.Same(tool, Assert.Single(settings.CreateChatOptions([tool]).Tools!));
    }

    [Fact]
    public void ModelCapabilities_ClearUnavailableStructuredSelectionAndEnableCapableProfiles()
    {
        using var client = CreateClient("Live", isReplay: false);
        var settings = new SettingsPaneViewModel([client]) { UseStructuredJson = true };
        settings.ModelStatus = new(ChatModelAvailability.Available, [ChatModelCapability.Text]);
        Assert.False(settings.SupportsStructuredOutput);
        Assert.False(settings.UseStructuredJson);
        Assert.Null(settings.CreateChatOptions([]).ResponseFormat);

        settings.ModelStatus = new(ChatModelAvailability.Available,
            [ChatModelCapability.Text, ChatModelCapability.StructuredOutput]);
        settings.UseStructuredJson = true;
        Assert.True(settings.SupportsStructuredOutput);
        Assert.IsType<ChatResponseFormatJson>(settings.CreateChatOptions([]).ResponseFormat);
    }

    private static IChatClient CreateClient(string name, bool isReplay) =>
        new DescribedChatClient(
            new StubChatClient(),
            new ChatClientDescriptor(
                name.ToLowerInvariant(), name, $"{name} ready",
                IsReplay: isReplay, SupportsToolCalling: !isReplay));

    private sealed class StubChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceKey is null && serviceType == typeof(IChatClient) ? this : null;

        public void Dispose() { }
    }
}
