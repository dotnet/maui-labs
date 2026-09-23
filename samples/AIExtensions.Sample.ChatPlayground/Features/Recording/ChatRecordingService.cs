using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace AIExtensions.Sample.ChatPlayground.Features.Recording;

/// <summary>Auto-saves and replays the active recording.</summary>
public sealed class ChatRecordingService
{
    private readonly object _gate = new();
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
        AutosavePath = System.IO.Path.Combine(dataDirectory, "chat-playground.autosave.json");
        var legacyPath = System.IO.Path.Combine(exportDirectory, "chat-playground.autosave.json");
        var restorePath = File.Exists(AutosavePath) ? AutosavePath : legacyPath;

        if (File.Exists(restorePath))
        {
            try
            {
                var json = File.ReadAllText(restorePath, Encoding.UTF8);
                _recording = ChatRecordingSerializer.Deserialize(json);
                if (restorePath != AutosavePath)
                    WriteAtomic(AutosavePath, json);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                JsonException or InvalidDataException or FormatException or ArgumentException or NotSupportedException)
            {
                RestoreError = $"Could not restore the last recording: {exception.Message}";
                logger.LogWarning(exception, "Could not restore the last playground recording.");
            }
        }
    }

    public string? RestoreError { get; }
    public string ExportPath { get; }
    public string AutosavePath { get; }

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
            var recording = new ChatRecording();
            WriteAtomic(AutosavePath, ChatRecordingSerializer.Serialize(recording));
            _recording = recording;
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
                WriteAtomic(AutosavePath, ChatRecordingSerializer.Serialize(_recording));
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

    public string Save()
    {
        string json;
        lock (_gate)
            json = ChatRecordingSerializer.Serialize(_recording);

        WriteAtomic(ExportPath, json);
        return ExportPath;
    }

    public async Task LoadFileAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var recording = await ChatRecordingSerializer.DeserializeAsync(stream, cancellationToken);
        lock (_gate)
        {
            WriteAtomic(AutosavePath, ChatRecordingSerializer.Serialize(recording));
            _recording = recording;
            _cursor = 0;
        }
        NotifyChanged();
    }

    private static void WriteAtomic(string path, string json)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var stagingPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(stagingPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(stagingPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(stagingPath))
                File.Delete(stagingPath);
        }
    }

    private void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
