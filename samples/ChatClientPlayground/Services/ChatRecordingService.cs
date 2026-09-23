using System.Text;
using System.Text.Json.Nodes;
using ChatClientPlayground.Models;

namespace ChatClientPlayground.Services;

/// <summary>Owns the in-memory tape, its replay cursor, and stable app-local persistence.</summary>
public sealed class ChatRecordingService
{
    private readonly object _gate = new();
    private ChatRecording _recording = new();
    private int _cursor;

    public event EventHandler? Changed;

    public RecordingMode Mode { get; private set; }
    public string Path { get; } = System.IO.Path.Combine(FileSystem.AppDataDirectory, "chat-playground.recording.json");

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

    public void SetMode(RecordingMode mode)
    {
        lock (_gate)
            Mode = mode;
        NotifyChanged();
    }

    public void NewRecording()
    {
        lock (_gate)
        {
            _recording = new ChatRecording();
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
        lock (_gate)
        {
            _recording.Interactions.Add(new RecordedInteraction
            {
                Sequence = _recording.Interactions.Count,
                IsStreaming = false,
                Request = request,
                Response = ChatRecordingSerializer.Response(response),
            });
        }
        NotifyChanged();
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

    public void CompleteStreaming(RecordedInteraction interaction)
    {
        lock (_gate)
        {
            interaction.Sequence = _recording.Interactions.Count;
            _recording.Interactions.Add(interaction);
        }
        NotifyChanged();
    }

    public RecordedInteraction PeekNext(bool isStreaming, JsonObject request)
    {
        RecordedInteraction interaction;
        lock (_gate)
        {
            if (_cursor >= _recording.Interactions.Count)
                throw new InvalidOperationException("Replay reached the end of the recording. Load or record more interactions, or restart replay.");

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
            if (_cursor >= _recording.Interactions.Count ||
                !ReferenceEquals(_recording.Interactions[_cursor], interaction))
            {
                throw new InvalidOperationException("Replay state changed before the interaction completed.");
            }
            _cursor++;
        }
        NotifyChanged();
    }

    public void Save()
    {
        string json;
        lock (_gate)
            json = ChatRecordingSerializer.Serialize(_recording);

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var stagingPath = Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(stagingPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(stagingPath, Path, overwrite: true);
        }
        finally
        {
            if (File.Exists(stagingPath))
                File.Delete(stagingPath);
        }
    }

    public void Load()
    {
        if (!File.Exists(Path))
            throw new FileNotFoundException("No saved playground recording exists yet.", Path);

        var recording = ChatRecordingSerializer.Deserialize(File.ReadAllText(Path, Encoding.UTF8));
        lock (_gate)
        {
            _recording = recording;
            _cursor = 0;
        }
        NotifyChanged();
    }

    private void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
