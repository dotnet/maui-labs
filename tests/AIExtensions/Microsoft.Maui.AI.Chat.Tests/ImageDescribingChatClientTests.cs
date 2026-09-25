using System.Runtime.CompilerServices;
using AIExtensions.Sample.ChatPlayground.Features.Chat.Services;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat.Tests;

public sealed class ImageDescribingChatClientTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImageDescription_MultiTurnAndMixedContents_PreservesOriginalMessages(bool streaming)
    {
        var image = new DataContent(new byte[] { 1, 2, 3 }, "image/png");
        var first = new ChatMessage(ChatRole.User,
            [new TextContent("What is this?"), image, new TextContent("Be brief.")])
        {
            MessageId = "image-turn",
            AuthorName = "user",
        };
        var second = new ChatMessage(ChatRole.Assistant, "A photo.");
        var third = new ChatMessage(ChatRole.User, "And the weather?");
        var messages = new[] { first, second, third };
        var inner = new CapturingChatClient();
        var descriptions = 0;
        using var client = inner.AsBuilder()
            .UseImageDescriptions((content, cancellationToken) =>
            {
                Assert.Same(image, content);
                descriptions++;
                return Task.FromResult("A bridge over a river");
            })
            .Build();

        if (streaming)
        {
            await foreach (var _ in client.GetStreamingResponseAsync(messages)) { }
        }
        else
        {
            await client.GetResponseAsync(messages);
        }

        Assert.Equal(1, descriptions);
        Assert.Equal(3, inner.Messages.Count);
        Assert.Equal("image-turn", inner.Messages[0].MessageId);
        Assert.Equal("user", inner.Messages[0].AuthorName);
        Assert.Equal(ChatRole.User, inner.Messages[0].Role);
        Assert.Equal(
            ["What is this?", "[Image: A bridge over a river]", "Be brief."],
            inner.Messages[0].Contents.Select(content => Assert.IsType<TextContent>(content).Text));
        Assert.Same(second, inner.Messages[1]);
        Assert.Same(third, inner.Messages[2]);
        Assert.Same(image, first.Contents[1]);
        Assert.Equal(3, first.Contents.Count);
    }

    [Fact]
    public async Task NoImages_DoesNotRequestDescriptions()
    {
        var inner = new CapturingChatClient();
        using var client = new ImageDescribingChatClient(inner,
            (_, _) => throw new InvalidOperationException("Unexpected description."));
        var message = new ChatMessage(ChatRole.User, "Hello");

        await client.GetResponseAsync([message]);

        Assert.Same(message, Assert.Single(inner.Messages));
    }

    [Fact]
    public async Task EmptyDescription_FailsWithoutCallingInnerClient()
    {
        var inner = new CapturingChatClient();
        using var client = new ImageDescribingChatClient(inner, (_, _) => Task.FromResult(" "));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.GetResponseAsync([new ChatMessage(ChatRole.User,
                [new DataContent(new byte[] { 1 }, "image/png")])]));

        Assert.Empty(inner.Messages);
    }

    private sealed class CapturingChatClient : IChatClient
    {
        public IReadOnlyList<ChatMessage> Messages { get; private set; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Messages = messages.ToArray();
            return Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "OK")]));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);
            yield return new ChatResponseUpdate(ChatRole.Assistant, response.Messages[0].Contents);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
