using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat.Tests;

internal sealed class SequenceChatClient(params ChatResponseUpdate[][] responses) : IChatClient
{
    private readonly Queue<ChatResponseUpdate[]> _responses = new(responses);

    public List<List<ChatMessage>> ReceivedMessages { get; } = [];

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        GetStreamingResponseAsync(messages, options, cancellationToken).ToChatResponseAsync(cancellationToken);

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ReceivedMessages.Add([.. messages]);

        if (!_responses.TryDequeue(out var response))
        {
            yield break;
        }

        foreach (var update in response)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return update;
            await Task.Yield();
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
