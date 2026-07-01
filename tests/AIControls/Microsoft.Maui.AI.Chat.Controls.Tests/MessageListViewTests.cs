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
    public void NewView_ShowToolCallsAndResults_DefaultToTrue()
    {
        var view = new MessageListView();
        Assert.True(view.ShowToolCalls);
        Assert.True(view.ShowToolResults);
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
    public async Task TogglingShowToolResults_RebuildsAndRaisesItemsChanged()
    {
        var session = SessionFactory.Create("Hello!");
        await session.SendMessageAsync("Hi");

        var view = new MessageListView { Session = session };
        var raised = 0;
        view.ItemsChanged += (_, _) => raised++;

        view.ShowToolResults = false;

        Assert.True(raised > 0);
    }
}
