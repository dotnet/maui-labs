using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Microsoft.Extensions.AI;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Connects live and replayed IChatClient turns to a MAUI-independent transcript.</summary>
public sealed class ChatConversation
{
    private readonly ChatTurnExecutor _turns = new();
    private readonly TranscriptEmitter _transcript = new();

    public bool HistoryContainsImage => _turns.HistoryContainsImage;

    /// <summary>Clears protocol history and the tracked visible-entry state.</summary>
    public void Clear()
    {
        _turns.Clear();
        _transcript.ResetVisibleState();
    }

    /// <summary>Sends a user message and streams its visible changes without waiting for UI delivery.</summary>
    public IAsyncEnumerable<TranscriptChange> SendTurnAsync(
        IChatClient client,
        ChatMessage message,
        string displayText,
        ChatOptions? options,
        bool streaming,
        bool structuredJson,
        CancellationToken cancellationToken = default) =>
        ReadTurnChangesAsync(client, options, streaming, structuredJson, emit =>
        {
            _transcript.UpdateTranscriptInstructions(options?.Instructions, emit);
            _turns.AppendUserMessage(message);
            _transcript.EmitUserMessage(message, displayText, emit);
        }, cancellationToken);

    /// <summary>Replays a recorded request through the same channel and turn execution path.</summary>
    public IAsyncEnumerable<TranscriptChange> ReplayTurnAsync(
        IChatClient client,
        JsonObject request,
        ChatOptions? options,
        bool streaming,
        bool structuredJson,
        CancellationToken cancellationToken = default) =>
        ReadTurnChangesAsync(client, options, streaming, structuredJson, emit =>
        {
            var newMessages = _turns.RestoreRecordedRequest(request, out var replacedHistory);
            if (replacedHistory)
            {
                _transcript.ResetVisibleState();
                emit(new TranscriptChange.Cleared());
            }
            _transcript.EmitRecordedRequest(
                newMessages, ChatRecordingSerializer.ReadInstructions(request), emit);
        }, cancellationToken);

    private async IAsyncEnumerable<TranscriptChange> ReadTurnChangesAsync(
        IChatClient client,
        ChatOptions? options,
        bool streaming,
        bool structuredJson,
        Action<Action<TranscriptChange>> prepareTurn,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // One unbounded channel per turn keeps provider streaming independent of UI rendering.
        var channel = Channel.CreateUnbounded<TranscriptChange>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
        });
        using var producerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var producer = ProduceAsync();

        async Task ProduceAsync()
        {
            try
            {
                void Enqueue(TranscriptChange change)
                {
                    if (!channel.Writer.TryWrite(change))
                        throw new InvalidOperationException("The transcript channel closed during a chat turn.");
                }

                prepareTurn(Enqueue);
                await ExecuteTurnAsync(client, options, streaming, structuredJson, Enqueue, producerCancellation.Token);
                channel.Writer.TryComplete();
            }
            catch (Exception exception)
            {
                channel.Writer.TryComplete(exception);
            }
        }

        try
        {
            await foreach (var change in channel.Reader.ReadAllAsync())
                yield return change;
        }
        finally
        {
            // A reader that stops early must not leave a producer mutating the next turn's history.
            if (!producer.IsCompleted)
                producerCancellation.Cancel();
            await producer;
        }
    }

    private async Task ExecuteTurnAsync(
        IChatClient client,
        ChatOptions? options,
        bool streaming,
        bool structuredJson,
        Action<TranscriptChange> emit,
        CancellationToken cancellationToken)
    {
        var projector = new ChatResponseProjector(_transcript, emit, streaming, structuredJson);
        try
        {
            await _turns.ExecuteTurnAsync(client, options, streaming,
                response =>
                {
                    foreach (var message in response.Messages)
                        projector.ProjectMessage(message);
                },
                projector.ProjectContent,
                cancellationToken);
            projector.Complete();
        }
        catch
        {
            if (streaming)
                projector.Abort();
            throw;
        }
    }
}
