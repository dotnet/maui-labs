using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Maui.DevFlow.Testing;

namespace Microsoft.Maui.Cli.DevFlow.Broker;

/// <summary>
/// Durable, bounded broker spool for active workflow recordings. It stores a workflow snapshot,
/// never app data, and is deliberately separate from completed user-authored Markdown flows.
/// </summary>
public sealed class FlowRecordingSpoolStore
{
    public const int SchemaVersion = 1;
    public const int MaxFiles = FlowRecordingStore.MaxActive;
    public const int MaxFileBytes = 1_048_576;
    public static readonly TimeSpan Ttl = FlowRecordingStore.IdleTtl;

    private readonly string _root;
    private readonly TimeProvider _clock;
    private readonly Action<string>? _warning;

    public FlowRecordingSpoolStore(string? storageRoot = null, TimeProvider? clock = null, Action<string>? warning = null)
    {
        _root = storageRoot ?? Path.Combine(BrokerPaths.ConfigDir, "recordings");
        _clock = clock ?? TimeProvider.System;
        _warning = warning;
    }

    public string StorageRoot => _root;

    public void Save(
        string agentId,
        string stableAgentId,
        string? sessionId,
        string recordingId,
        FlowRecorder recorder,
        string? durabilityError = null)
    {
        if (!IsSafeRecordingId(recordingId))
            throw new ArgumentException("Invalid recording id.", nameof(recordingId));
        var snapshot = recorder.Snapshot();
        if (snapshot.Steps.Count > FlowRecorder.MaxSteps)
            throw new InvalidOperationException("Recording exceeds the step limit.");
        var spool = new FlowRecordingSpool
        {
            Schema = SchemaVersion,
            AgentId = agentId,
            StableAgentId = stableAgentId,
            SessionId = sessionId,
            RecordingId = recordingId,
            CreatedUtc = recorder.CreatedAtUtc,
            LastTouchedUtc = recorder.LastTouchedUtc,
            Flow = snapshot,
            DurabilityError = string.IsNullOrWhiteSpace(durabilityError)
                ? null
                : durabilityError.Length <= 1_000
                    ? durabilityError
                    : durabilityError[..1_000]
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(spool, JsonOptions);
        if (bytes.Length > MaxFileBytes)
            throw new InvalidOperationException("Recording snapshot exceeds the spool size limit.");

        Directory.CreateDirectory(_root);
        BrokerPaths.RestrictConfigDirectoryPermissions(_root);
        var path = FilePath(recordingId);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            BrokerPaths.RestrictStateFilePermissions(temporary);
            File.Move(temporary, path, overwrite: true);
            BrokerPaths.RestrictStateFilePermissions(path);
            try { Prune(); } catch { }
        }
        finally
        {
            try { File.Delete(temporary); } catch { }
        }
    }

    public IReadOnlyList<FlowRecordingSpool> Restore()
    {
        if (!Directory.Exists(_root))
            return [];

        Prune();
        var result = new List<FlowRecordingSpool>();
        foreach (var file in Directory.EnumerateFiles(_root, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var info = new FileInfo(file);
                if (info.Length == 0 || info.Length > MaxFileBytes)
                    throw new JsonException("Spool file is outside the allowed size.");
                var spool = JsonSerializer.Deserialize<FlowRecordingSpool>(File.ReadAllText(file), JsonOptions);
                if (!IsValid(spool))
                    throw new JsonException("Spool content is invalid.");
                if (!string.Equals(
                    Path.GetFileNameWithoutExtension(file),
                    spool!.RecordingId,
                    StringComparison.Ordinal))
                {
                    throw new JsonException("Spool filename does not match its recording id.");
                }
                if (IsExpired(spool!))
                {
                    _ = Delete(spool!.RecordingId);
                    continue;
                }
                result.Add(spool!);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                Quarantine(file);
                Warn($"Ignored corrupt workflow recording spool '{Path.GetFileName(file)}'.");
            }
        }
        return result.OrderByDescending(static spool => spool.LastTouchedUtc).Take(MaxFiles).ToArray();
    }

    public bool Delete(string recordingId)
    {
        if (!IsSafeRecordingId(recordingId))
            return false;
        var path = FilePath(recordingId);
        if (!File.Exists(path))
            return true;
        var retired = path + ".deleted-" + Guid.NewGuid().ToString("N");
        try
        {
            File.Move(path, retired);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Warn($"Could not retire workflow recording spool '{Path.GetFileName(path)}': {ex.Message}");
            return false;
        }

        try { File.Delete(retired); } catch { }
        return true;
    }

    public void Prune()
    {
        if (!Directory.Exists(_root))
            return;
        var files = Directory.EnumerateFiles(_root, "*.json", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .OrderByDescending(static file => file.LastWriteTimeUtc)
            .ToArray();
        foreach (var file in files.Skip(MaxFiles))
        {
            try { file.Delete(); } catch { }
        }
    }

    private bool IsExpired(FlowRecordingSpool spool) => spool.LastTouchedUtc <= _clock.GetUtcNow() - Ttl;

    private string FilePath(string recordingId) => Path.Combine(_root, recordingId + ".json");

    private static bool IsValid(FlowRecordingSpool? spool)
        => spool is not null &&
           spool.Schema == SchemaVersion &&
           !string.IsNullOrWhiteSpace(spool.AgentId) &&
           IsSafeRecordingId(spool.RecordingId) &&
           spool.Flow is not null &&
           spool.Flow.Steps.Count <= FlowRecorder.MaxSteps &&
           (spool.DurabilityError is null || spool.DurabilityError.Length <= 1_000);

    private static bool IsSafeRecordingId(string? id)
        => id is { Length: 24 } && id.All(static character =>
            (character >= '0' && character <= '9') || (character >= 'a' && character <= 'f'));

    private void Quarantine(string path)
    {
        try
        {
            File.Move(path, path + ".corrupt-" + _clock.GetUtcNow().ToUnixTimeSeconds(), overwrite: true);
        }
        catch { }
    }

    private void Warn(string text)
    {
        try { _warning?.Invoke(text); } catch { }
    }

    internal void ReportWarning(string text) => Warn(text);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

public sealed class FlowRecordingSpool
{
    [JsonPropertyName("schema")] public int Schema { get; set; }
    [JsonPropertyName("agentId")] public string AgentId { get; set; } = "";
    [JsonPropertyName("stableAgentId")] public string StableAgentId { get; set; } = "";
    [JsonPropertyName("sessionId")] public string? SessionId { get; set; }
    [JsonPropertyName("recordingId")] public string RecordingId { get; set; } = "";
    [JsonPropertyName("createdUtc")] public DateTimeOffset CreatedUtc { get; set; }
    [JsonPropertyName("lastTouchedUtc")] public DateTimeOffset LastTouchedUtc { get; set; }
    [JsonPropertyName("flow")] public MauiFlow Flow { get; set; } = new();
    [JsonPropertyName("durabilityError")] public string? DurabilityError { get; set; }
}
