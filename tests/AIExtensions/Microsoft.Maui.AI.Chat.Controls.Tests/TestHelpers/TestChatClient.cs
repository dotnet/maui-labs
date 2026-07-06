using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;

namespace Microsoft.Maui.AI.Chat.Controls.Tests.TestHelpers;

/// <summary>
/// A controllable IChatClient for tests. Captures sent messages, returns configured responses.
/// Uses streaming (GetStreamingResponseAsync) since AgentContext calls the streaming API.
/// </summary>
internal sealed class TestChatClient : IChatClient
{
    private readonly Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, Task<ChatResponse>> _handler;
    private readonly List<List<ChatMessage>> _sentMessages = [];

    public IReadOnlyList<List<ChatMessage>> SentMessages => _sentMessages;

    /// <summary>Creates a client that returns a fixed assistant response.</summary>
    public TestChatClient(string response = "Hello!")
        : this((_, _, _) => Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, response)])))
    {
    }

    /// <summary>Creates a client that streams multiple text tokens separately.</summary>
    public static TestChatClient MultiToken(params string[] tokens)
    {
        return new TestChatClient((_, _, _) =>
        {
            // Return a response with multiple TextContent items, each will be streamed individually
            var contents = tokens.Select(t => (AIContent)new TextContent(t)).ToList();
            return Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, contents)]));
        });
    }

    /// <summary>Creates a client with a custom handler.</summary>
    public TestChatClient(Func<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken, Task<ChatResponse>> handler)
    {
        _handler = handler;
    }

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default)
    {
        _sentMessages.Add(messages.ToList());
        return _handler(messages, options, ct);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        _sentMessages.Add(messages.ToList());
        var response = await _handler(messages, options, ct);

        foreach (var msg in response.Messages)
        {
            foreach (var content in msg.Contents)
            {
                yield return new ChatResponseUpdate
                {
                    Role = msg.Role,
                    Contents = [content]
                };
            }
        }
    }

    public void Dispose() { }
    public object? GetService(Type serviceType, object? serviceKey = null) => null;
}
