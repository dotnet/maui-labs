using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace AIChat.ClientServer.Sample.AgentServer;

internal sealed class ReplayChatClient : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new ChatResponse(
            [new ChatMessage(ChatRole.Assistant, "Replay mode is enabled.")]));

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = [new TextContent("Replay mode is enabled.")],
        };
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType == typeof(IChatClient) ? this : null;

    public void Dispose()
    {
    }
}
