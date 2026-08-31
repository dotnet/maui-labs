using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Maui.Cli.DevFlow.Broker;

/// <summary>
/// A small, local-only resume point. It intentionally contains only a Shell route and connection
/// metadata; application state and view-model values are never serialized here.
/// </summary>
public sealed class RouteCheckpoint
{
    public const int SchemaVersion = 1;

    [JsonPropertyName("schema")] public int Schema { get; set; } = SchemaVersion;
    [JsonPropertyName("agentId")] public string AgentId { get; set; } = "";
    [JsonPropertyName("sessionId")] public string? SessionId { get; set; }
    [JsonPropertyName("route")] public string Route { get; set; } = "";
    [JsonPropertyName("appName")] public string? AppName { get; set; }
    [JsonPropertyName("platform")] public string? Platform { get; set; }
    [JsonPropertyName("project")] public string? Project { get; set; }
    [JsonPropertyName("savedUtc")] public DateTimeOffset SavedUtc { get; set; }
    [JsonPropertyName("lastRestore")] public RouteRestoreResult? LastRestore { get; set; }
}

public sealed class RouteRestoreResult
{
    [JsonPropertyName("attemptedUtc")] public DateTimeOffset AttemptedUtc { get; set; }
    [JsonPropertyName("success")] public bool Success { get; set; }
    [JsonPropertyName("kind")] public string Kind { get; set; } = "unknown";
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("observedRoute")] public string? ObservedRoute { get; set; }
}

public sealed class RouteCheckpointStatus
{
    [JsonPropertyName("ok")] public bool Ok { get; set; } = true;
    [JsonPropertyName("hasCheckpoint")] public bool HasCheckpoint { get; set; }
    [JsonPropertyName("connected")] public bool Connected { get; set; }
    [JsonPropertyName("stale")] public bool Stale { get; set; }
    [JsonPropertyName("warning")] public string? Warning { get; set; }
    [JsonPropertyName("checkpoint")] public RouteCheckpoint? Checkpoint { get; set; }
}

/// <summary>
/// Versioned, bounded and atomically persisted local checkpoint storage. The storage root and
/// clock are injectable so tests never write to a user's profile or depend on wall clock time.
/// </summary>
public sealed class RouteCheckpointStore
{
    public const int MaxEntries = 64;
    public const int MaxRouteLength = 2048;
    public static readonly TimeSpan Ttl = TimeSpan.FromDays(14);

    private readonly string _root;
    private readonly string _stateFile;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private string? _loadWarning;

    public RouteCheckpointStore(string? storageRoot = null, TimeProvider? clock = null)
    {
        _root = storageRoot ?? BrokerPaths.ConfigDir;
        _stateFile = Path.Combine(_root, "route-checkpoints.v1.json");
        _clock = clock ?? TimeProvider.System;
    }

    public string StorageRoot => _root;

    public RouteCheckpointStatus Get(string agentId, string? sessionId = null, bool connected = false)
    {
        lock (_gate)
        {
            var document = ReadAndPruneLocked();
            var checkpoint = Find(document, agentId, sessionId);
            return new RouteCheckpointStatus
            {
                HasCheckpoint = checkpoint is not null,
                Connected = connected,
                Stale = checkpoint is not null && IsExpired(checkpoint),
                Warning = _loadWarning,
                Checkpoint = checkpoint
            };
        }
    }

    public RouteCheckpoint Save(
        string agentId,
        string? sessionId,
        string route,
        string? appName,
        string? platform,
        string? project)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        if (!TryNormalizeRoute(route, out var normalized, out var error))
            throw new ArgumentException(error, nameof(route));

        lock (_gate)
        {
            var document = ReadAndPruneLocked();
            document.Checkpoints.RemoveAll(c => SameKey(c, agentId, sessionId));
            var checkpoint = new RouteCheckpoint
            {
                AgentId = agentId,
                SessionId = Clean(sessionId, 256),
                Route = normalized!,
                AppName = Clean(appName, 256),
                Platform = Clean(platform, 128),
                Project = Clean(project, 4096),
                SavedUtc = _clock.GetUtcNow()
            };
            document.Checkpoints.Add(checkpoint);
            TrimToCap(document);
            WriteLocked(document);
            return checkpoint;
        }
    }

    public bool Clear(string agentId, string? sessionId = null)
    {
        lock (_gate)
        {
            var document = ReadAndPruneLocked();
            var removed = document.Checkpoints.RemoveAll(c => SameKey(c, agentId, sessionId)) > 0;
            if (removed)
                WriteLocked(document);
            return removed;
        }
    }

    public void RecordRestoreResult(string agentId, string? sessionId, RouteRestoreResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        lock (_gate)
        {
            var document = ReadAndPruneLocked();
            var checkpoint = Find(document, agentId, sessionId);
            if (checkpoint is null)
                return;
            checkpoint.LastRestore = result;
            WriteLocked(document);
        }
    }

    public static bool TryNormalizeRoute(string? route, out string? normalized, out string? error)
    {
        normalized = null;
        error = null;
        if (string.IsNullOrWhiteSpace(route))
        {
            error = "The app did not report a Shell route.";
            return false;
        }
        var candidate = route.Trim();
        if (candidate.Length > MaxRouteLength)
        {
            error = $"The route exceeds the {MaxRouteLength}-character checkpoint limit.";
            return false;
        }
        if (candidate.Any(char.IsControl))
        {
            error = "The route contains control characters and cannot be checkpointed.";
            return false;
        }
        normalized = candidate;
        return true;
    }

    private CheckpointDocument ReadAndPruneLocked()
    {
        var document = ReadLocked();
        var expired = document.Checkpoints.RemoveAll(IsExpired) > 0;
        TrimToCap(document);
        if (expired)
            WriteLocked(document);
        return document;
    }

    private CheckpointDocument ReadLocked()
    {
        _loadWarning = null;
        if (!File.Exists(_stateFile))
            return new CheckpointDocument();
        try
        {
            var json = File.ReadAllText(_stateFile);
            var document = JsonSerializer.Deserialize<CheckpointDocument>(json, JsonOptions);
            if (document is null || document.Schema != RouteCheckpoint.SchemaVersion)
                throw new JsonException("Unsupported checkpoint schema.");
            document.Checkpoints ??= [];
            document.Checkpoints.RemoveAll(c =>
                string.IsNullOrWhiteSpace(c.AgentId) ||
                !TryNormalizeRoute(c.Route, out _, out _));
            return document;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _loadWarning = "Saved resume checkpoints could not be read; starting with a clean checkpoint store.";
            TryQuarantineCorruptFile();
            return new CheckpointDocument();
        }
    }

    private void WriteLocked(CheckpointDocument document)
    {
        Directory.CreateDirectory(_root);
        BrokerPaths.RestrictConfigDirectoryPermissions(_root);
        var temporary = _stateFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(document, JsonOptions));
            BrokerPaths.RestrictStateFilePermissions(temporary);
            File.Move(temporary, _stateFile, overwrite: true);
            BrokerPaths.RestrictStateFilePermissions(_stateFile);
        }
        finally
        {
            try { File.Delete(temporary); } catch { }
        }
    }

    private void TryQuarantineCorruptFile()
    {
        try
        {
            var quarantine = _stateFile + ".corrupt-" + _clock.GetUtcNow().ToUnixTimeSeconds();
            File.Move(_stateFile, quarantine, overwrite: true);
            BrokerPaths.RestrictStateFilePermissions(quarantine);
        }
        catch { }
    }

    private bool IsExpired(RouteCheckpoint checkpoint)
        => checkpoint.SavedUtc <= _clock.GetUtcNow() - Ttl;

    private static RouteCheckpoint? Find(CheckpointDocument document, string agentId, string? sessionId)
        => document.Checkpoints
            .Select(static (checkpoint, index) => (checkpoint, index))
            .Where(entry => SameKey(entry.checkpoint, agentId, sessionId))
            .OrderByDescending(static entry => entry.checkpoint.SavedUtc)
            .ThenByDescending(static entry => entry.index)
            .Select(static entry => entry.checkpoint)
            .FirstOrDefault();

    private static bool SameKey(RouteCheckpoint checkpoint, string agentId, string? sessionId)
        => string.Equals(checkpoint.AgentId, agentId, StringComparison.Ordinal) &&
           (string.IsNullOrWhiteSpace(sessionId) ||
            string.Equals(checkpoint.SessionId, sessionId, StringComparison.Ordinal));

    private static void TrimToCap(CheckpointDocument document)
    {
        if (document.Checkpoints.Count <= MaxEntries)
            return;
        // SavedUtc ties are the normal case, not the exception: DateTimeOffset.UtcNow only advances
        // about every 15ms on Windows, so a burst of saves shares one timestamp. LINQ sorts are
        // stable, so ordering on SavedUtc alone keeps the earliest members of a tie group and
        // evicts the newest checkpoint. Save always appends, so insertion order breaks the tie.
        // The survivors are written back in insertion order so that index keeps meaning insertion
        // order on the next trim; selecting newest-first and persisting that would invert it.
        document.Checkpoints = document.Checkpoints
            .Select(static (checkpoint, index) => (checkpoint, index))
            .OrderByDescending(static entry => entry.checkpoint.SavedUtc)
            .ThenByDescending(static entry => entry.index)
            .Take(MaxEntries)
            .OrderBy(static entry => entry.index)
            .Select(static entry => entry.checkpoint)
            .ToList();
    }

    private static string? Clean(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = new string(value.Trim().Where(static character => !char.IsControl(character)).ToArray());
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private sealed class CheckpointDocument
    {
        [JsonPropertyName("schema")] public int Schema { get; set; } = RouteCheckpoint.SchemaVersion;
        [JsonPropertyName("checkpoints")] public List<RouteCheckpoint> Checkpoints { get; set; } = [];
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };
}
