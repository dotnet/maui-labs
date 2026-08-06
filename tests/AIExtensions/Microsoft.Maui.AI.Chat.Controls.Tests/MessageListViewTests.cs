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
        Assert.Empty(view.Items);
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
        Assert.True(view.Items.Count >= 2, $"Expected >= 2 items, got {view.Items.Count}");
    }

    [Fact]
    public async Task SettingSession_RaisesItemsCollectionChanged()
    {
        var session = SessionFactory.Create("Hello!");
        await session.SendMessageAsync("Hi");

        var view = new MessageListView();
        var raised = 0;
        ((System.Collections.Specialized.INotifyCollectionChanged)view.Items).CollectionChanged += (_, _) => raised++;

        view.Session = session;

        Assert.True(raised > 0);
        Assert.True(view.Items.Count > 0);
    }

    [Fact]
    public async Task ClearingSession_EmptiesItems()
    {
        var session = SessionFactory.Create("Hello!");
        await session.SendMessageAsync("Hi");

        var view = new MessageListView { Session = session };
        Assert.True(view.Items.Count > 0);

        view.Session = null;

        Assert.Empty(view.Items);
    }

    [Fact]
    public async Task CompletedTurn_HasNoThinkingOrErrorItems()
    {
        var session = SessionFactory.Create("Hello!");
        await session.SendMessageAsync("Hi");

        var view = new MessageListView { Session = session };

        // After a successful turn, only real content is rendered — no transient status items.
        Assert.DoesNotContain(view.Items, c => c.Block is ThinkingContentBlock);
        Assert.DoesNotContain(view.Items, c => c.Block is ErrorContentBlock);
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

        var error = Assert.Single(view.Items.Select(c => c.Block).OfType<ErrorContentBlock>());
        Assert.Equal(ErrorContentBlock.DefaultUserMessage, error.Message);
        Assert.DoesNotContain("boom", error.Message, StringComparison.Ordinal);
        Assert.Equal("boom", session.Error!.Message);

        // ...but the engine's turns never contained an error block (thread stays clean).
        Assert.DoesNotContain(session.Turns.SelectMany(t => t.ResponseBlocks), b => b is ErrorContentBlock);
    }

    [Fact]
    public async Task CancellationReconciliation_PreservesPriorStickyError()
    {
        var client = new TestChatClient((_, _, _) =>
            throw new InvalidOperationException("diagnostic"));
        var session = SessionFactory.Create(client);
        await session.SendMessageAsync("Hi");
        var view = new MessageListView { Session = session };
        var error = Assert.Single(view.Items, item => item.Block is ErrorContentBlock);

        session.Turns[0].ClearResponseBlocks();
        view.ReconcileItemsWithSession();

        Assert.Contains(error, view.Items);
        Assert.DoesNotContain(
            view.Items,
            item => item.Block.Role == Microsoft.Extensions.AI.ChatRole.Assistant
                && item.Block is not ErrorContentBlock);
    }
}
