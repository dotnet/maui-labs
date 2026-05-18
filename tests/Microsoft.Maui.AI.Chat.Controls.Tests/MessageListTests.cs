using Microsoft.AspNetCore.Components.AI;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat.Controls.Tests.TestHelpers;

namespace Microsoft.Maui.AI.Chat.Controls.Tests;

/// <summary>
/// Mirrors: Blazor.Tests/Components/MessageListTests.cs
/// Tests that the CopilotChatView correctly surfaces blocks from the AgentContext
/// as ContentContext items in its observable collection.
/// </summary>
public class MessageListTests
{
    [Fact]
    public async Task SendMessage_ProducesUserAndAssistantBlocks()
    {
        var session = SessionFactory.Create("Hello!");
        var blocks = new List<ContentBlock>();
        session.RegisterOnBlockAdded((_, b) => blocks.Add(b));

        await session.SendMessageAsync("Hi");

        Assert.Contains(blocks, b => b.Role == ChatRole.User);
        Assert.Contains(blocks, b => b.Role == ChatRole.Assistant);
    }

    [Fact]
    public async Task MultipleTurns_AllBlocksAccumulate()
    {
        int callCount = 0;
        var client = new TestChatClient((msgs, _, _) =>
        {
            callCount++;
            return Task.FromResult(new ChatResponse(
                [new ChatMessage(ChatRole.Assistant, $"Response {callCount}")]));
        });
        var session = SessionFactory.Create(client);
        var blocks = new List<ContentBlock>();
        session.RegisterOnBlockAdded((_, b) => blocks.Add(b));

        await session.SendMessageAsync("First");
        await session.SendMessageAsync("Second");

        // 2 user blocks + 2 assistant blocks = 4 minimum
        Assert.True(blocks.Count >= 4, $"Expected >=4 blocks, got {blocks.Count}");
        var assistantBlocks = blocks.Where(b => b.Role == ChatRole.Assistant).ToList();
        Assert.Equal(2, assistantBlocks.Count);
    }

    [Fact]
    public async Task MessageContent_IsAccessibleViaRichContentBlock()
    {
        var session = SessionFactory.Create("World!");
        await session.SendMessageAsync("Hello");

        var turn = session.Turns[0];
        var userBlock = turn.RequestBlocks.OfType<RichContentBlock>().First();
        var assistantBlock = turn.ResponseBlocks.OfType<RichContentBlock>().First();

        Assert.Equal("Hello", userBlock.RawText);
        Assert.Equal("World!", assistantBlock.RawText);
    }

    [Fact]
    public async Task TypingIndicator_IsActive_DuringStreaming()
    {
        var statuses = new List<ConversationStatus>();
        var session = SessionFactory.Create("Done");
        session.RegisterOnStatusChanged(s => statuses.Add(s));

        await session.SendMessageAsync("Hi");

        Assert.Contains(ConversationStatus.Streaming, statuses);
        Assert.Equal(ConversationStatus.Idle, session.Status);
    }

    [Fact]
    public async Task ConversationHistory_IsPassedToClient_OnSubsequentTurns()
    {
        var client = new TestChatClient((msgs, _, _) =>
            Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "ok")])));
        var session = SessionFactory.Create(client);

        await session.SendMessageAsync("First");
        await session.SendMessageAsync("Second");

        // The second call should include messages from the first turn
        var secondCallMessages = client.SentMessages[1];
        Assert.True(secondCallMessages.Count >= 3,
            $"Second call should have history (got {secondCallMessages.Count} messages)");
    }
}
