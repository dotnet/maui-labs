using Microsoft.Maui.AI.Chat.Controls.Tests.TestHelpers;

namespace Microsoft.Maui.AI.Chat.Controls.Tests;

public class TurnContextTests
{
    [Fact]
    public async Task MessageItems_ExposeTurnIdentityAndPosition()
    {
        var session = SessionFactory.Create("Assistant response");
        await session.SendMessageAsync("User request");
        var list = new MessageListView { Session = session };
        var contexts = list.Items.OfType<ContentContext>().ToArray();

        Assert.Equal(2, contexts.Length);
        var request = contexts[0];
        var response = contexts[1];
        var turn = Assert.Single(session.Turns);

        Assert.Same(turn, request.Turn);
        Assert.Same(turn, response.Turn);
        Assert.Equal(turn.Id, request.TurnId);
        Assert.Equal(turn.Id, response.TurnId);
        Assert.True(request.IsRequest);
        Assert.False(response.IsRequest);
        Assert.True(request.IsFirstInTurn);
        Assert.False(request.IsLastInTurn);
        Assert.False(response.IsFirstInTurn);
        Assert.True(response.IsLastInTurn);
    }

    [Fact]
    public void TransientContent_HasNoTurnMetadata()
    {
        var context = new ContentContext(
            SessionFactory.Create(),
            new ThinkingContentBlock());

        Assert.Null(context.Turn);
        Assert.Null(context.TurnId);
        Assert.False(context.IsRequest);
        Assert.False(context.IsFirstInTurn);
        Assert.False(context.IsLastInTurn);
    }
}
