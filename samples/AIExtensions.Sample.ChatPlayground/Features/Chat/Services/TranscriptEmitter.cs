using AIExtensions.Sample.ChatPlayground.Features.Chat.Models;
using Microsoft.Extensions.AI;

namespace AIExtensions.Sample.ChatPlayground.Features.Chat.Services;

/// <summary>Emits visible transcript changes and keeps entry IDs stable across turns.</summary>
internal sealed class TranscriptEmitter
{
    private long _lastEntryId;
    private long? _systemEntryId;

    internal long NextEntryId() => ++_lastEntryId;

    public void ResetVisibleState() => _systemEntryId = null;

    public void UpdateTranscriptInstructions(string? instructions, Action<TranscriptChange> emit)
    {
        if (string.IsNullOrWhiteSpace(instructions))
        {
            if (_systemEntryId is { } oldEntryId)
                emit(new TranscriptChange.EntryRemoved(oldEntryId));

            _systemEntryId = null;
        }
        else if (_systemEntryId is { } currentEntryId)
        {
            emit(new TranscriptChange.EntryTextChanged(currentEntryId, instructions));
        }
        else
        {
            var entryId = NextEntryId();
            emit(new TranscriptChange.EntryAdded(
                entryId,
                TranscriptEntryKind.System,
                "System instructions",
                instructions));
            _systemEntryId = entryId;
        }
    }

    public void EmitUserMessage(ChatMessage message, string displayText, Action<TranscriptChange> emit)
    {
        var image = message.Contents.OfType<DataContent>().FirstOrDefault();
        emit(new TranscriptChange.EntryAdded(
            NextEntryId(),
            TranscriptEntryKind.User,
            "You",
            displayText,
            ImageBytes: image?.Data.ToArray()));
    }

    public void EmitRecordedRequest(
        IReadOnlyList<ChatMessage> newMessages,
        string? instructions,
        Action<TranscriptChange> emit)
    {
        UpdateTranscriptInstructions(instructions, emit);

        // A recording may jump forward: restore missing assistant/tool entries before the next user turn.
        var pending = new List<ChatMessage>();

        void Flush()
        {
            var projector = new ChatResponseProjector(this, emit, streaming: false, structuredJson: false);
            foreach (var item in pending)
                projector.ProjectMessage(item);
            pending.Clear();
        }

        foreach (var message in newMessages)
        {
            if (message.Role.Value == ChatRole.User.Value)
            {
                Flush();

                var text = string.Concat(message.Contents.OfType<TextContent>().Select(content => content.Text));
                if (string.IsNullOrWhiteSpace(text))
                    text = message.Contents.Any(content => content is DataContent)
                        ? "Attached image"
                        : "(Empty message)";
                EmitUserMessage(message, text, emit);
            }
            else if (message.Role.Value == ChatRole.System.Value)
            {
                Flush();

                var systemText = string.Concat(message.Contents.OfType<TextContent>().Select(content => content.Text));
                UpdateTranscriptInstructions(systemText, emit);
            }
            else
            {
                pending.Add(message);
            }
        }

        Flush();
    }
}
