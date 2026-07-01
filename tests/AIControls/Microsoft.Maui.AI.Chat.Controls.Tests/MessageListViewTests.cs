using System.Linq;
using Microsoft.Maui.AI.Chat;
using Microsoft.Maui.AI.Chat.Controls.Tests.TestHelpers;

namespace Microsoft.Maui.AI.Chat.Controls.Tests;

/// <summary>
/// Tests for <see cref="MessageListView"/> — the messages-only view extracted from
/// <see cref="CopilotChatView"/>. Verifies it surfaces an <see cref="AgentContext"/>'s blocks
/// as rendered items and raises change notifications the host relies on.
/// </summary>
public class MessageListViewTests
{
    [Fact]
    public void NewView_HasNoItems()
    {
        var view = new MessageListView();
        Assert.Equal(0, view.ItemCount);
    }

    [Fact]
    public void ContentTemplates_IsTheContentProperty_AndMutable()
    {
        var view = new MessageListView();
        Assert.Empty(view.ContentTemplates);

        view.ContentTemplates.Add(new TextContentTemplate());
        view.ContentTemplates.Add(new FunctionInvocationTemplate());

        Assert.Equal(2, view.ContentTemplates.Count);
    }

    [Fact]
    public async Task SettingSession_WithCompletedTurn_PopulatesItems()
    {
        var session = SessionFactory.Create("Hello!");
        await session.SendMessageAsync("Hi");

        var view = new MessageListView { Session = session };

        // At minimum the user prompt block and the assistant response block are rendered.
        Assert.True(view.ItemCount >= 2, $"Expected >= 2 items, got {view.ItemCount}");
    }

    [Fact]
    public async Task SettingSession_RaisesItemsChanged()
    {
        var session = SessionFactory.Create("Hello!");
        await session.SendMessageAsync("Hi");

        var view = new MessageListView();
        var raised = 0;
        view.ItemsChanged += (_, _) => raised++;

        view.Session = session;

        Assert.True(raised > 0);
        Assert.True(view.ItemCount > 0);
    }

    [Fact]
    public async Task ClearingSession_EmptiesItems()
    {
        var session = SessionFactory.Create("Hello!");
        await session.SendMessageAsync("Hi");

        var view = new MessageListView { Session = session };
        Assert.True(view.ItemCount > 0);

        view.Session = null;

        Assert.Equal(0, view.ItemCount);
    }

    [Fact]
    public async Task CompletedTurn_HasNoThinkingOrErrorItems()
    {
        var session = SessionFactory.Create("Hello!");
        await session.SendMessageAsync("Hi");

        var view = new MessageListView { Session = session };

        // After a successful turn, only real content is rendered — no transient status items.
        Assert.DoesNotContain(view.RenderedBlocks, b => b is ThinkingContentBlock);
        Assert.DoesNotContain(view.RenderedBlocks, b => b is ErrorContentBlock);
    }

    [Fact]
    public async Task FailedTurn_RendersErrorItem_NotInEngineTurns()
    {
        // A client that throws so the session enters the error state.
        var client = new TestChatClient((_, _, _) =>
            throw new InvalidOperationException("boom"));
        var session = SessionFactory.Create(client);
        await session.SendMessageAsync("Hi");

        Assert.Equal(ConversationStatus.Error, session.Status);

        // A view bound to the failed session re-projects the error as a UI-only item.
        var view = new MessageListView { Session = session };

        var error = Assert.Single(view.RenderedBlocks.OfType<ErrorContentBlock>());
        Assert.Contains("boom", error.Message);

        // ...but the engine's turns never contained an error block (thread stays clean).
        Assert.DoesNotContain(session.Turns.SelectMany(t => t.ResponseBlocks), b => b is ErrorContentBlock);
    }
}
