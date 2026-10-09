using AIExtensions.Sample.ChatPlayground;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat.Tests;

public sealed class SettingsPaneViewModelTests
{
    [Fact]
    public void SystemDescriptor_DoesNotEnableUnsupportedReasoningOrImageOptions()
    {
        using var client = new DescribedChatClient(new StubChatClient(),
            new ChatClientDescriptor("core-ai-chat", "Core AI", "Not loaded.", SupportsToolCalling: true));
        var settings = new SettingsPaneViewModel([client]) { UseReasoningSummary = true };

        var options = settings.CreateChatOptions([]);

        Assert.Null(options.Reasoning);
        Assert.False(settings.SelectedDescriptor!.SupportsImageInput);
        Assert.False(settings.SelectedDescriptor.SupportsReasoningSummary);
        Assert.Null(options.TopK);
        Assert.Null(options.Seed);
        Assert.Same(ChatToolMode.Auto, options.ToolMode);
    }

    [Fact]
    public void CoreAIDescriptor_UsesFullReasoningAndProviderDefaultEffort()
    {
        using var client = new DescribedChatClient(new StubChatClient(),
            new ChatClientDescriptor("core-ai-chat", "Core AI", "Not loaded.",
                SupportsToolCalling: true, SupportsFullReasoning: true));
        var settings = new SettingsPaneViewModel([client]);
        var options = settings.CreateChatOptions([]);
        Assert.True(settings.SelectedDescriptor!.SupportsReasoning);
        Assert.Equal("Full reasoning", settings.SelectedDescriptor.ReasoningLabel);
        Assert.False(settings.SelectedDescriptor.SupportsReasoningSummary);
        Assert.False(settings.SelectedDescriptor.SupportsImageInput);
        Assert.Equal(ReasoningOutput.Full, options.Reasoning?.Output);
        Assert.Null(options.Reasoning?.Effort);
        settings.UseReasoningSummary = false;
        var hidden = settings.CreateChatOptions([]);
        Assert.Equal(ReasoningOutput.None, hidden.Reasoning?.Output);
        Assert.Null(hidden.Reasoning?.Effort);
    }

    [Fact]
    public void AzureDescriptor_KeepsSummaryAndMediumEffortSemantics()
    {
        using var client = new DescribedChatClient(new StubChatClient(),
            new ChatClientDescriptor("azure", "Azure", "Ready.", SupportsReasoningSummary: true));
        var settings = new SettingsPaneViewModel([client]);
        var options = settings.CreateChatOptions([]);
        Assert.Equal("Reasoning summary", settings.SelectedDescriptor!.ReasoningLabel);
        Assert.Equal(ReasoningOutput.Summary, options.Reasoning?.Output);
        Assert.Equal(ReasoningEffort.Medium, options.Reasoning?.Effort);
        settings.UseReasoningSummary = false;
        Assert.Null(settings.CreateChatOptions([]).Reasoning);
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
