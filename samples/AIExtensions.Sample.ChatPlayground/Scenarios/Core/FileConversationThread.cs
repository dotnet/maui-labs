using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>
/// A conversation thread saved in a JSON file: the raw updates of every committed turn, written with the
/// Microsoft.Extensions.AI JSON contracts. Each commit rewrites the file atomically, so a crash never leaves half a turn.
/// </summary>
/// <remarks>
/// The engine writes a turn only when it commits, back to back, so a turn that is still running, failed or was
/// discarded never reaches the file. The provider keeps no conversation (<see cref="IsStateful"/> is
/// <see langword="false"/>): the engine sends the restored history with the next request.
/// </remarks>
public sealed class FileConversationThread : IConversationThread
{
    private const string FormatName = "chat-playground-thread";
    private const int CurrentVersion = 1;

    private readonly List<List<JsonObject>> _turns = [];
    private List<JsonObject>? _pending;

    /// <summary>Opens the thread saved at <paramref name="path"/>, or a new one when there is no file yet.</summary>
    /// <param name="path">The file the thread is saved in.</param>
    public FileConversationThread(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        FilePath = path;
        ThreadId = Guid.NewGuid().ToString("N");
        try
        {
            if (File.Exists(path))
                Load(File.ReadAllText(path, Encoding.UTF8));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or
            InvalidDataException or NotSupportedException)
        {
            // A damaged file starts an empty conversation; the next commit replaces it.
            _turns.Clear();
            LoadError = $"Could not read the saved conversation: {exception.Message}";
        }
    }

    /// <summary>Gets the file the thread is saved in.</summary>
    public string FilePath { get; }

    /// <summary>Gets why the saved conversation could not be read, or <see langword="null"/> when it was read.</summary>
    public string? LoadError { get; private set; }

    /// <summary>Gets the number of committed turns.</summary>
    public int TurnCount => _turns.Count;

    /// <summary>Gets when the file was last written, or <see langword="null"/> when there is no file.</summary>
    public DateTimeOffset? SavedAt => File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath) : null;

    /// <summary>Gets the size of the file in bytes, or 0 when there is no file.</summary>
    public long FileSize => File.Exists(FilePath) ? new FileInfo(FilePath).Length : 0;

    /// <summary>Raised after a commit or a clear was saved.</summary>
    public event EventHandler? Saved;

    /// <inheritdoc />
    public string ThreadId { get; private set; }

    /// <inheritdoc />
    public bool IsStateful => false;

    /// <inheritdoc />
    public string? ConversationId => null;

    /// <inheritdoc />
    public void AppendUserMessage(ChatMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        _pending =
        [
            ChatRecordingSerializer.Update(new ChatResponseUpdate
            {
                Role = message.Role,
                AuthorName = message.AuthorName,
                CreatedAt = message.CreatedAt,
                MessageId = message.MessageId ?? Guid.NewGuid().ToString("N"),
                Contents = [.. message.Contents],
                AdditionalProperties = message.AdditionalProperties is null
                    ? null
                    : new AdditionalPropertiesDictionary(message.AdditionalProperties),
            }),
        ];
    }

    /// <inheritdoc />
    public void AppendUpdate(ChatResponseUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (_pending is null)
            throw new InvalidOperationException("AppendUserMessage must start a turn before its updates are appended.");

        // Written now, so later changes to the update's objects, such as arguments a card adds, are not saved.
        _pending.Add(ChatRecordingSerializer.Update(update));
    }

    /// <inheritdoc />
    public void CompleteTurn()
    {
        if (_pending is null)
            return;

        _turns.Add(_pending);
        _pending = null;
        try
        {
            Save();
        }
        catch
        {
            _turns.RemoveAt(_turns.Count - 1);
            throw;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<ChatResponseUpdate> GetUpdates() =>
        [.. _turns.SelectMany(static turn => turn).Select(ReadUpdate)];

    /// <inheritdoc />
    public void Clear()
    {
        _turns.Clear();
        _pending = null;
        ThreadId = Guid.NewGuid().ToString("N");
        LoadError = null;
        Save();
    }

    private void Save()
    {
        var turns = new JsonArray();
        foreach (var turn in _turns)
        {
            var updates = new JsonArray();
            foreach (var update in turn)
                updates.Add(update.DeepClone());
            turns.Add(new JsonObject { ["updates"] = updates });
        }

        var document = new JsonObject
        {
            ["format"] = FormatName,
            ["version"] = CurrentVersion,
            ["threadId"] = ThreadId,
            ["turns"] = turns,
        };
        AtomicFile.WriteAllText(FilePath, document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        Saved?.Invoke(this, EventArgs.Empty);
    }

    private void Load(string json)
    {
        if (JsonNode.Parse(json) is not JsonObject document ||
            document["format"]?.GetValue<string>() != FormatName ||
            document["version"]?.GetValue<int>() != CurrentVersion)
        {
            throw new InvalidDataException($"The file is not a {FormatName} v{CurrentVersion} file.");
        }

        if (document["threadId"]?.GetValue<string>() is { Length: > 0 } threadId)
            ThreadId = threadId;
        foreach (var turn in document["turns"]?.AsArray() ?? [])
        {
            var updates = turn?["updates"]?.AsArray()
                ?? throw new InvalidDataException("A saved turn has no updates.");
            var saved = updates.Select(static update => update as JsonObject
                ?? throw new InvalidDataException("A saved update is not an object.")).ToList();

            // Fail now rather than at restore time.
            foreach (var update in saved)
                _ = ReadUpdate(update);
            _turns.Add(saved);
        }
    }

    private static ChatResponseUpdate ReadUpdate(JsonObject node)
    {
        var update = ChatRecordingSerializer.ReadUpdate(node);

        // JSON reads property values back as JsonElement. The engine marks continuation rounds and stopped turns with
        // string values, so text is given back as text.
        if (update.AdditionalProperties is { Count: > 0 } properties)
        {
            foreach (var (key, value) in properties.ToArray())
            {
                if (value is JsonElement { ValueKind: JsonValueKind.String } text)
                    properties[key] = text.GetString();
            }
        }

        return update;
    }
}
