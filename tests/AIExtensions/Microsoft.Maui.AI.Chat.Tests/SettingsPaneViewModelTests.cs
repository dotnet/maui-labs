using AIExtensions.Sample.ChatPlayground.Services;
using AIExtensions.Sample.ChatPlayground.ViewModels;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat.Tests;

public sealed class SettingsPaneViewModelTests
{
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
    public void Constructor_OnlyReplayClient_SelectsReplayWithoutLiveProvider()
    {
        using var replay = CreateClient("Replay", isReplay: true);

        var settings = new SettingsPaneViewModel([replay]);

        Assert.Same(replay, settings.SelectedClient);
        Assert.False(settings.CanEditOptions);
        Assert.Equal("Replay ready", settings.ClientStatus);
    }

    [Fact]
    public void CreateChatOptions_UsesBuiltInToolModesAndRejectsUnsupportedModes()
    {
        using var client = CreateClient("Live", isReplay: false);
        var settings = new SettingsPaneViewModel([client]);

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

    private static IChatClient CreateClient(string name, bool isReplay) =>
        new StubChatClient().AsBuilder()
            .UseDescriptor(new ChatClientDescriptor(
                name, $"{name} ready", SupportsImageInput: false, IsReplay: isReplay))
            .Build();

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
