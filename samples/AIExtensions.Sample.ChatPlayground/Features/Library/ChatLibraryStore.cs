using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AIExtensions.Sample.ChatPlayground.Features.Recording;
using AIExtensions.Sample.ChatPlayground.Features.Storage;
using Microsoft.Extensions.AI;

namespace AIExtensions.Sample.ChatPlayground.Features.Library;

/// <summary>Metadata for one saved conversation, independent of the recording format.</summary>
public sealed record SavedChat(
    string Id,
    string Title,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    int InteractionCount);

/// <summary>Stores each recording separately and tracks which one is active.</summary>
internal sealed class ChatLibraryStore
{
    private const int CatalogVersion = 1;
    private readonly string _chatsDirectory;
    private readonly string _catalogPath;
    private ChatCatalog _catalog;

    public ChatLibraryStore(string dataDirectory)
    {
        _chatsDirectory = Path.Combine(dataDirectory, "chat-playground", "chats");
        _catalogPath = Path.Combine(dataDirectory, "chat-playground", "catalog.json");
        Directory.CreateDirectory(_chatsDirectory);

        if (File.Exists(_catalogPath))
        {
            _catalog = JsonSerializer.Deserialize<ChatCatalog>(File.ReadAllText(_catalogPath))
                ?? throw new InvalidDataException("The chat library catalog is empty.");
            ValidateCatalog(_catalog);
            Current = Read(_catalog.ActiveId);
            return;
        }

        var recording = new ChatRecording();
        var id = Guid.NewGuid().ToString("N");
        var path = RecordingPath(id);
        AtomicFile.WriteAllText(path, ChatRecordingSerializer.Serialize(recording));

        _catalog = new ChatCatalog
        {
            Version = CatalogVersion,
            ActiveId = id,
            Chats = [CreateSummary(id, recording, DateTimeOffset.UtcNow)],
        };
        try
        {
            WriteCatalog(_catalog);
        }
        catch
        {
            File.Delete(path);
            throw;
        }
        Current = recording;
    }

    public ChatRecording Current { get; private set; }
    public string ActiveId => _catalog.ActiveId;
    public string ActivePath => RecordingPath(ActiveId);

    public IReadOnlyList<SavedChat> List(ChatRecording active)
    {
        var index = _catalog.Chats.FindIndex(chat => chat.Id == ActiveId);
        _catalog.Chats[index] = RefreshSummary(_catalog.Chats[index], active);
        return _catalog.Chats
            .OrderByDescending(chat => chat.UpdatedAtUtc)
            .ToArray();
    }

    public void Save(ChatRecording recording) =>
        AtomicFile.WriteAllText(ActivePath, ChatRecordingSerializer.Serialize(recording));

    public ChatRecording New(ChatRecording previous)
    {
        if (previous.Interactions.Count == 0)
            return previous;

        var recording = new ChatRecording();
        var id = Guid.NewGuid().ToString("N");
        var path = RecordingPath(id);
        var catalog = WithRefreshedActive(previous);
        catalog.ActiveId = id;
        catalog.Chats.Add(CreateSummary(id, recording, DateTimeOffset.UtcNow));
        AtomicFile.WriteAllText(path, ChatRecordingSerializer.Serialize(recording));
        try
        {
            WriteCatalog(catalog);
        }
        catch
        {
            File.Delete(path);
            throw;
        }

        _catalog = catalog;
        Current = recording;
        return recording;
    }

    public ChatRecording Open(string id, ChatRecording previous)
    {
        var recording = Read(id);
        if (id == ActiveId)
        {
            Current = recording;
            return recording;
        }

        var catalog = WithRefreshedActive(previous);
        catalog.ActiveId = id;
        WriteCatalog(catalog);
        _catalog = catalog;
        Current = recording;
        return recording;
    }

    public ChatRecording Import(ChatRecording recording)
    {
        var id = Guid.NewGuid().ToString("N");
        var path = RecordingPath(id);
        var catalog = WithRefreshedActive(Current);
        catalog.ActiveId = id;
        catalog.Chats.Add(CreateSummary(id, recording, DateTimeOffset.UtcNow));
        AtomicFile.WriteAllText(path, ChatRecordingSerializer.Serialize(recording));
        try
        {
            WriteCatalog(catalog);
        }
        catch
        {
            File.Delete(path);
            throw;
        }

        _catalog = catalog;
        Current = recording;
        return recording;
    }

    public ChatRecording Read(string id)
    {
        if (!_catalog.Chats.Any(chat => chat.Id == id))
            throw new ArgumentException("The selected chat is not in the library.", nameof(id));

        return ChatRecordingSerializer.Deserialize(File.ReadAllText(RecordingPath(id), Encoding.UTF8));
    }

    // The recording is saved on every turn; its catalog summary is refreshed only when needed.
    private ChatCatalog WithRefreshedActive(ChatRecording active) => new()
    {
        Version = CatalogVersion,
        ActiveId = ActiveId,
        Chats = _catalog.Chats
            .Select(chat => chat.Id == ActiveId ? RefreshSummary(chat, active) : chat)
            .ToList(),
    };

    private SavedChat RefreshSummary(SavedChat chat, ChatRecording recording) => chat with
    {
        Title = chat.InteractionCount == 0 && recording.Interactions.Count > 0
            ? TitleFor(recording)
            : chat.Title,
        UpdatedAtUtc = LastSavedAt(chat.Id),
        InteractionCount = recording.Interactions.Count,
    };

    private DateTimeOffset LastSavedAt(string id)
    {
        var path = RecordingPath(id);
        if (!File.Exists(path))
            throw new FileNotFoundException("The active chat recording is missing.", path);
        return new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
    }

    private static SavedChat CreateSummary(string id, ChatRecording recording, DateTimeOffset created) =>
        new(id, TitleFor(recording), created, created, recording.Interactions.Count);

    private static string TitleFor(ChatRecording recording)
    {
        if (recording.Interactions.Count == 0)
            return "New chat";

        var messages = ChatRecordingSerializer.ReadRequestMessages(recording.Interactions[0].Request);
        var user = messages.FirstOrDefault(message => message.Role == ChatRole.User);
        var text = user is null ? string.Empty : string.Join(" ", user.Contents.OfType<TextContent>()
            .Select(content => content.Text));
        text = Regex.Replace(text, @"\s+", " ").Trim();
        if (text.Length == 0)
            return user?.Contents.OfType<DataContent>().Any() == true ? "Image chat" : "Untitled chat";
        return text.Length <= 72 ? text : text[..69] + "...";
    }

    private void ValidateCatalog(ChatCatalog catalog)
    {
        if (catalog.Version != CatalogVersion || catalog.Chats is null ||
            catalog.Chats.Any(chat => chat is null || string.IsNullOrWhiteSpace(chat.Title) ||
                chat.InteractionCount < 0 || !Guid.TryParseExact(chat.Id, "N", out _) ||
                !File.Exists(RecordingPath(chat.Id))) ||
            !catalog.Chats.Any(chat => chat.Id == catalog.ActiveId) ||
            catalog.Chats.Select(chat => chat.Id).Distinct(StringComparer.Ordinal).Count() != catalog.Chats.Count)
        {
            throw new InvalidDataException("The chat library catalog is invalid or references missing recordings.");
        }
    }

    private string RecordingPath(string id) => Path.Combine(_chatsDirectory, id + ".json");

    private void WriteCatalog(ChatCatalog catalog) =>
        AtomicFile.WriteAllText(_catalogPath, JsonSerializer.Serialize(catalog));

    private sealed class ChatCatalog
    {
        public int Version { get; set; }
        public string ActiveId { get; set; } = string.Empty;
        public List<SavedChat> Chats { get; set; } = [];
    }
}
