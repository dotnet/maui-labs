using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Replays without a provider reference, so neither model can be contacted.</summary>
public sealed class ReplayChatClient(IChatRecordingSession recording) : IChatClient
{
    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var interaction = recording.PeekNext();
        var response = interaction.IsStreaming
            ? await ReadUpdates(interaction, cancellationToken).ToChatResponseAsync(cancellationToken)
            : ChatRecordingSerializer.ReadResponse(interaction.Response ?? throw new InvalidDataException("The non-streaming recording has no response."));
        cancellationToken.ThrowIfCancellationRequested();
        recording.CompleteReplay(interaction);
        return response;
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var interaction = recording.PeekNext();
        var completed = false;
        try
        {
            await foreach (var update in ReadUpdates(interaction, cancellationToken))
                yield return update;
            completed = true;
        }
        finally
        {
            // Leave the cursor in place when enumeration stops early so this turn can be retried.
            if (completed)
                recording.CompleteReplay(interaction);
        }
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> ReadUpdates(
        RecordedInteraction interaction, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        IEnumerable<ChatResponseUpdate> updates = interaction.IsStreaming
            ? interaction.Updates.Select(ChatRecordingSerializer.ReadUpdate)
            : ChatRecordingSerializer.ReadResponse(
                interaction.Response ?? throw new InvalidDataException("The non-streaming recording has no response."))
                .ToChatResponseUpdates();

        foreach (var update in updates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return update;
            await Task.Yield();
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && (serviceType == typeof(ReplayChatClient) || serviceType == typeof(IChatClient))
            ? this
            : null;

    public void Dispose()
    {
    }
}
