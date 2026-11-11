using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat;

namespace AIExtensions.Sample.ChatPlayground.Tests;

/// <summary>
/// The connection status card: a <c>[ToolBlock]</c> whose handler the source generator writes, registered with
/// <c>AddGeneratedToolBlocks()</c>.
/// </summary>
public sealed class ConnectionStatusBlockTests
{
    [Fact]
    public async Task ConnectionStatusCall_IsATypedCardThatItsResultFillsIn()
    {
        using var model = new ScriptedChatClient(
            ScriptedResponse.Stream(new ChatResponseUpdate(ChatRole.Assistant,
                [new FunctionCallContent("call-1", "get_connection_status")])),
            ScriptedResponse.Stream(new ChatResponseUpdate(ChatRole.Assistant, "You're online over Wi-Fi.")));
        using var live = new FunctionInvokingChatClient(model);
        using var harness = new PlaygroundChatHarness(live);
        var summaries = new List<string>();
        using var added = harness.Session.OnBlockAdded((_, block) =>
        {
            if (block is not ConnectionStatusBlock card)
                return;

            summaries.Add(card.Summary);
            card.PropertyChanged += (_, e) =>
            {
                // The engine notifies every property of a custom block when its handler maps the result.
                if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(ConnectionStatusBlock.Summary))
                    summaries.Add(card.Summary);
            };
        });

        await harness.Session.SendMessageAsync("Am I online?");

        var card = Assert.Single(harness.LastTurn.ResponseBlocks.OfType<ConnectionStatusBlock>());
        Assert.Equal("get_connection_status", card.ToolName);
        Assert.Equal("call-1", card.Call?.CallId);
        Assert.True(card.HasResult);
        Assert.Equal("Internet", card.NetworkAccess);
        Assert.True(card.IsOnline);
        Assert.Equal("Online", card.Summary);
        Assert.Equal("WiFi", card.ConnectionTypes);

        // The card shows the call at once, then fills in.
        Assert.Equal(string.Empty, summaries[0]);
        Assert.Equal("Online", summaries[^1]);
        Assert.Equal("You're online over Wi-Fi.",
            Assert.IsType<TextContentBlock>(harness.LastTurn.ResponseBlocks[^1]).RawText);
    }

    [Fact]
    public async Task OfflineResult_SaysSoWithoutConnectionTypes()
    {
        using var result = JsonDocument.Parse("""{"networkAccess":"None","connectionProfiles":[]}""");
        using var model = new ScriptedChatClient(ScriptedResponse.Stream(
            new ChatResponseUpdate(ChatRole.Assistant, [new FunctionCallContent("call-1", "get_connection_status")]),
            new ChatResponseUpdate(ChatRole.Tool, [new FunctionResultContent("call-1", result.RootElement.Clone())])));
        using var harness = new PlaygroundChatHarness(model);

        await harness.Session.SendMessageAsync("Am I online?");

        var card = Assert.Single(harness.LastTurn.ResponseBlocks.OfType<ConnectionStatusBlock>());
        Assert.Equal("None", card.NetworkAccess);
        Assert.False(card.IsOnline);
        Assert.Equal("Offline", card.Summary);
        Assert.Equal(string.Empty, card.ConnectionTypes);
    }

    [Fact]
    public void Card_DescribesEachNetworkAccess()
    {
        Assert.Equal(
            ["Online", "Online, with limited access", "Local network only", "Offline", "Unknown", string.Empty],
            new[] { "Internet", "ConstrainedInternet", "Local", "None", "Unknown", null }
                .Select(access => new ConnectionStatusBlock { NetworkAccess = access }.Summary));
    }
}
