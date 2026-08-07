using Microsoft.Maui.AI.Chat.Controls.Tests.TestHelpers;

namespace Microsoft.Maui.AI.Chat.Controls.Tests;

public class ErrorRetryTests
{
    [Fact]
    public async Task RetryAsync_RetriesFailedTurnAndHidesRetryButton()
    {
        var attempts = 0;
        var client = new TestChatClient((_, _, _) =>
        {
            attempts++;
            return attempts == 1
                ? Task.FromException<Microsoft.Extensions.AI.ChatResponse>(
                    new InvalidOperationException("diagnostic detail"))
                : Task.FromResult(new Microsoft.Extensions.AI.ChatResponse(
                    [new Microsoft.Extensions.AI.ChatMessage(
                        Microsoft.Extensions.AI.ChatRole.Assistant,
                        "Recovered")]));
        });
        var session = SessionFactory.Create(client);
        await session.SendMessageAsync("Try");
        var view = new ErrorMessageView();
        view.ApplyContentContext(new ContentContext(
            session,
            new ErrorContentBlock(ErrorContentBlock.DefaultUserMessage)));

        Assert.True(view.RetryButton.IsVisible);
        await view.RetryAsync();

        Assert.Equal(ConversationStatus.Idle, session.Status);
        Assert.Null(session.Error);
        Assert.Equal(2, attempts);
        Assert.False(view.RetryButton.IsVisible);
    }
}
