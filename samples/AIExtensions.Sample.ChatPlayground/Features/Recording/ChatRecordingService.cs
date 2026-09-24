using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace AIExtensions.Sample.ChatPlayground.Features.Recording;

/// <summary>Auto-saves and replays the active recording.</summary>
public sealed class ChatRecordingService
{
    private readonly object _gate = new();
    private readonly ChatLibraryStore? _library;
    private ChatRecording _recording = new();
    private int _cursor;

    public event EventHandler? Changed;

    public ChatRecordingService(
        ILogger<ChatRecordingService> logger,
        string dataDirectory,
        string exportDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(exportDirectory);
        ExportPath = System.IO.Path.Combine(exportDirectory, "chat-playground.json");
        try
        {
            _library = new ChatLibraryStore(logger, dataDirectory, exportDirectory);
            _recording = _library.Current;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            System.Text.Json.JsonException or InvalidDataException or FormatException or
            ArgumentException or NotSupportedException)
        {
            RestoreError = $"Could not restore the chat library: {exception.Message}";
            logger.LogWarning(exception, "Could not restore the playground chat library.");
        }
    }

    public string? RestoreError { get; }
    public string ExportPath { get; }
    public string AutosavePath => Library.ActivePath;
    public string ActiveChatId => Library.ActiveId;

    private ChatLibraryStore Library => _library
        ?? throw new InvalidOperationException(RestoreError ?? "The chat library is unavailable.");

    public IReadOnlyList<SavedChat> ListChats()
    {
        lock (_gate)
            return Library.List(_recording);
    }

    public ChatRecording ReadChat(string id)
    {
        lock (_gate)
            return Library.Read(id);
    }

    public void OpenChat(string id)
    {
        lock (_gate)
        {
            _recording = Library.Open(id, _recording);
            _cursor = 0;
        }
        NotifyChanged();
    }

    public int InteractionCount
    {
        get { lock (_gate) return _recording.Interactions.Count; }
    }

    public int ReplayPosition
    {
        get { lock (_gate) return _cursor; }
    }

    public bool HasReplayRemaining
    {
        get { lock (_gate) return _cursor < _recording.Interactions.Count; }
    }

    public void NewRecording()
    {
        lock (_gate)
        {
            _recording = Library.New(_recording);
            _cursor = 0;
        }
        NotifyChanged();
    }

    public void RestartReplay()
    {
        lock (_gate)
            _cursor = 0;
        NotifyChanged();
    }

    public void AddResponse(JsonObject request, Microsoft.Extensions.AI.ChatResponse response)
    {
        AddInteraction(new RecordedInteraction
        {
            IsStreaming = false,
            Request = request,
            Response = ChatRecordingSerializer.Response(response),
        });
    }

    public RecordedInteraction BeginStreaming(JsonObject request) => new()
    {
        IsStreaming = true,
        Request = request,
    };

    public void AddUpdate(RecordedInteraction interaction, JsonObject update)
    {
        lock (_gate)
            interaction.Updates.Add(update);
    }

    public void CompleteStreaming(RecordedInteraction interaction) => AddInteraction(interaction);

    private void AddInteraction(RecordedInteraction interaction)
    {
        lock (_gate)
        {
            interaction.Sequence = _recording.Interactions.Count;
            _recording.Interactions.Add(interaction);
            try
            {
                Library.Save(_recording);
            }
            catch
            {
                // A failed auto-save must not leave an interaction visible only in memory.
                _recording.Interactions.RemoveAt(_recording.Interactions.Count - 1);
                throw;
            }
        }
        NotifyChanged();
    }

    public RecordedInteraction PeekNext()
    {
        lock (_gate)
        {
            if (_cursor >= _recording.Interactions.Count)
                throw new InvalidOperationException("Replay reached the end of the recording. Rewind to play again.");
            return _recording.Interactions[_cursor];
        }
    }

    public RecordedInteraction PeekNext(bool isStreaming, JsonObject request)
    {
        RecordedInteraction interaction;
        lock (_gate)
        {
            if (_cursor >= _recording.Interactions.Count)
                throw new InvalidOperationException("Replay reached the end of the recording. Record more interactions or restart replay.");

            interaction = _recording.Interactions[_cursor];
            if (interaction.IsStreaming != isStreaming)
                throw new ChatRecordingMismatchException(
                    _cursor,
                    "$.isStreaming",
                    JsonValue.Create(interaction.IsStreaming),
                    JsonValue.Create(isStreaming));

            ChatRecordingSerializer.AssertEqual(_cursor, interaction.Request, request);
        }
        return interaction;
    }

    public void CompleteReplay(RecordedInteraction interaction)
    {
        lock (_gate)
        {
            // Only the interaction just consumed may advance the replay cursor.
            if (_cursor >= _recording.Interactions.Count ||
                !ReferenceEquals(_recording.Interactions[_cursor], interaction))
            {
                throw new InvalidOperationException("Replay state changed before the interaction completed.");
            }
            _cursor++;
        }
        NotifyChanged();
    }

    public string Export()
    {
        string json;
        lock (_gate)
            json = ChatRecordingSerializer.Serialize(_recording);

        ChatLibraryStore.WriteAtomic(ExportPath, json);
        return ExportPath;
    }

    public async Task LoadFileAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var recording = await ChatRecordingSerializer.DeserializeAsync(stream, cancellationToken);
        lock (_gate)
        {
            _recording = Library.Import(recording);
            _cursor = 0;
        }
        NotifyChanged();
    }

    private void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
