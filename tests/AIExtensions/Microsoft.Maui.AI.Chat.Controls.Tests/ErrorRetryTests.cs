using Microsoft.Maui.AI.Chat.Controls.Tests.TestHelpers;
using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;

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

    [Fact]
    public async Task RetryStreaming_RemovesStalePartialResponseBeforeNewBlocksArrive()
    {
        var session = new AgentContext(new UIAgent(new PartialFailureClient()));
        await session.SendMessageAsync("Try");
        var list = new MessageListView { Session = session };
        var contexts = list.Items.OfType<ContentContext>().ToArray();

        Assert.Equal(3, list.Items.Count);
        Assert.Contains(contexts, item =>
            item.Block is TextContentBlock { RawText: "partial" });
        Assert.Contains(contexts, item => item.Block is ErrorContentBlock);
        ContentContext[]? itemsWhenRetryStarted = null;
        session.RegisterOnStatusChanged(status =>
        {
            if (status == ConversationStatus.Streaming
                && itemsWhenRetryStarted is null)
            {
                itemsWhenRetryStarted = list.Items
                    .OfType<ContentContext>()
                    .ToArray();
            }
        });

        await session.RetryAsync();

        var retryItems = itemsWhenRetryStarted
            ?? throw new Xunit.Sdk.XunitException("Retry never entered streaming.");
        var remaining = Assert.Single(
            retryItems,
            item => item.Block is not ThinkingContentBlock);
        Assert.True(remaining.IsRequest);
        Assert.DoesNotContain(retryItems, item =>
            item.Block is TextContentBlock { RawText: "partial" });
        Assert.DoesNotContain(
            retryItems,
            item => item.Block is ErrorContentBlock);
    }

    private sealed class PartialFailureClient : IChatClient
    {
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                MessageId = "partial-message",
                Contents = [new TextContent("partial")],
            };
            await Task.Yield();
            throw new InvalidOperationException("failure");
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
