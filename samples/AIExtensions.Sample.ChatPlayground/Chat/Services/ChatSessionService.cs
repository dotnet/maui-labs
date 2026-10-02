using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Autosaves one current chat and supplies its record/replay session.</summary>
public sealed class ChatSessionService : IChatRecordingSession
{
    private readonly object _gate = new();
    private ChatRecording _recording = new();
    private int _cursor;

    public ChatSessionService(ILogger<ChatSessionService> logger, string dataDirectory, string exportDirectory)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(exportDirectory);
        AutosavePath = Path.Combine(dataDirectory, "chat-playground", "current.json");
        ExportPath = Path.Combine(exportDirectory, "chat-playground.json");
        try
        {
            if (File.Exists(AutosavePath))
                _recording = ChatRecordingSerializer.Deserialize(File.ReadAllText(AutosavePath, Encoding.UTF8));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            JsonException or InvalidDataException or FormatException or ArgumentException or NotSupportedException)
        {
            RestoreError = $"Could not restore the current chat: {exception.Message} " +
                "Import a recording or start a new chat to replace the damaged file.";
            logger.LogWarning(exception, "Could not restore the playground's current chat.");
        }
    }

    public event EventHandler? Changed;

    public string? RestoreError { get; private set; }
    public string AutosavePath { get; }
    public string ExportPath { get; }

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
            if (_recording.Interactions.Count > 0 || RestoreError is not null)
                AtomicFile.WriteAllText(AutosavePath, ChatRecordingSerializer.Serialize(new ChatRecording()));
            _recording = new ChatRecording();
            _cursor = 0;
            RestoreError = null;
        }
        NotifyChanged();
    }

    public void RestartReplay()
    {
        lock (_gate)
            _cursor = 0;
        NotifyChanged();
    }

    public void AddResponse(JsonObject request, ChatResponse response) =>
        AddInteraction(new RecordedInteraction
        {
            IsStreaming = false,
            Request = request,
            Response = ChatRecordingSerializer.Response(response),
        });

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
            if (RestoreError is not null)
                throw new InvalidOperationException(RestoreError);
            interaction.Sequence = _recording.Interactions.Count;
            _recording.Interactions.Add(interaction);
            try
            {
                AtomicFile.WriteAllText(AutosavePath, ChatRecordingSerializer.Serialize(_recording));
            }
            catch
            {
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

    public void CompleteReplay(RecordedInteraction interaction)
    {
        lock (_gate)
        {
            if (_cursor >= _recording.Interactions.Count ||
                !ReferenceEquals(_recording.Interactions[_cursor], interaction))
                throw new InvalidOperationException("Replay state changed before the interaction completed.");
            _cursor++;
        }
        NotifyChanged();
    }

    public string Export()
    {
        string json;
        lock (_gate)
        {
            if (RestoreError is not null)
                throw new InvalidOperationException(RestoreError);
            json = ChatRecordingSerializer.Serialize(_recording);
        }

        AtomicFile.WriteAllText(ExportPath, json);
        return ExportPath;
    }

    public async Task LoadFileAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var recording = await ChatRecordingSerializer.DeserializeAsync(stream, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            AtomicFile.WriteAllText(AutosavePath, ChatRecordingSerializer.Serialize(recording));
            _recording = recording;
            _cursor = 0;
            RestoreError = null;
        }
        NotifyChanged();
    }

    private void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
