namespace Microsoft.Maui.AI.Chat.Controls.Tests.TestHelpers;

/// <summary>A model that answers each request with the next scripted response.</summary>
internal sealed class ScriptedChatClient : IChatClient
{
    private readonly Queue<Func<CancellationToken, IAsyncEnumerable<ChatResponseUpdate>>> _responses = new();

    internal void Enqueue(Func<CancellationToken, IAsyncEnumerable<ChatResponseUpdate>> response)
        => _responses.Enqueue(response);

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => _responses.Dequeue()(cancellationToken);

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null)
        => serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }
}
