using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace AIExtensions.Sample.ChatPlayground.Features.Recording;

/// <summary>Records a selected real provider without changing its streaming behavior.</summary>
public sealed class RecordingChatClient(IChatClient inner, IChatRecordingSession recording) : DelegatingChatClient(inner)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var snapshot = messages.ToList();
        var request = ChatRecordingSerializer.Request(snapshot, options);
        var response = await base.GetResponseAsync(snapshot, options, cancellationToken).ConfigureAwait(false);
        recording.AddResponse(request, response);
        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var snapshot = messages.ToList();
        var interaction = recording.BeginStreaming(ChatRecordingSerializer.Request(snapshot, options));
        var completed = false;
        try
        {
            await foreach (var update in base.GetStreamingResponseAsync(snapshot, options, cancellationToken)
                .WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                recording.AddUpdate(interaction, ChatRecordingSerializer.Update(update));
                yield return update;
            }
            completed = true;
        }
        finally
        {
            // Partial streams cannot be saved as completed, replayable interactions.
            if (completed)
                recording.CompleteStreaming(interaction);
        }
    }
}
