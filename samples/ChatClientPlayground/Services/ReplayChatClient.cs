using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace ChatClientPlayground.Services;

/// <summary>Replays without a provider reference, so neither model can be contacted.</summary>
public sealed class ReplayChatClient(ChatRecordingService recording) : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var interaction = recording.PeekNext(false, ChatRecordingSerializer.Request(messages, options));
        var response = ChatRecordingSerializer.ReadResponse(
            interaction.Response ?? throw new InvalidDataException("The non-streaming recording has no response."));
        recording.CompleteReplay(interaction);
        return Task.FromResult(response);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var interaction = recording.PeekNext(true, ChatRecordingSerializer.Request(messages, options));
        var completed = false;
        try
        {
            foreach (var update in interaction.Updates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return ChatRecordingSerializer.ReadUpdate(update);
                await Task.Yield();
            }
            completed = true;
        }
        finally
        {
            if (completed)
                recording.CompleteReplay(interaction);
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType == typeof(IChatClient) && serviceKey is null ? this : null;

    public void Dispose()
    {
    }
}
