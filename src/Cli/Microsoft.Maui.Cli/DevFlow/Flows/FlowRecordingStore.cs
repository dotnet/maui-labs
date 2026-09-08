using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Microsoft.Maui.Cli.DevFlow.Flows;

/// <summary>
/// Process-wide registry of in-progress <see cref="FlowRecorder"/> sessions, keyed by a
/// cryptographically-random recordingId. Thread-safe; caps active recordings and opportunistically
/// evicts idle ones so a client that starts recordings without ever stopping them cannot exhaust
/// the cap or leak memory. A mutation always requires the exact (unguessable) id.
/// </summary>
public sealed class FlowRecordingStore
{
    /// <summary>Maximum concurrent in-progress recordings.</summary>
    public const int MaxActive = 32;

    /// <summary>A recording untouched for this long is evicted opportunistically.</summary>
    public static readonly TimeSpan IdleTtl = TimeSpan.FromMinutes(30);

    public static FlowRecordingStore Instance { get; } = new();

    private readonly ConcurrentDictionary<string, FlowRecorder> _sessions = new(StringComparer.Ordinal);

    private FlowRecordingStore() { }

    /// <summary>Starts a recording and returns its id, or null if the active-recording cap is hit.</summary>
    public string? Start(string name, string? app, string? platform, string? preconditions)
    {
        EvictIdle();
        if (_sessions.Count >= MaxActive)
            return null;

        var recorder = new FlowRecorder(name, app, platform, preconditions);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var id = NewId();
            if (_sessions.TryAdd(id, recorder))
                return id;
        }
        return null;
    }

    /// <summary>Looks up a recording (marking it recently used) and returns whether it was found.</summary>
    public bool TryGet(string id, out FlowRecorder recorder)
    {
        if (!string.IsNullOrEmpty(id) && _sessions.TryGetValue(id, out recorder!))
        {
            recorder.Touch();
            return true;
        }
        recorder = null!;
        return false;
    }

    /// <summary>Removes and returns the recording, or null if the id is unknown.</summary>
    public FlowRecorder? Remove(string id) =>
        !string.IsNullOrEmpty(id) && _sessions.TryRemove(id, out var recorder) ? recorder : null;

    public IReadOnlyList<(string Id, string Name, int Steps)> List()
    {
        EvictIdle();
        return _sessions.Select(kv => (kv.Key, kv.Value.Name, kv.Value.StepCount)).ToList();
    }

    private void EvictIdle()
    {
        var cutoff = DateTimeOffset.UtcNow - IdleTtl;
        foreach (var kv in _sessions)
        {
            if (kv.Value.LastTouchedUtc < cutoff)
                _sessions.TryRemove(kv.Key, out _);
        }
    }

    private static string NewId() => Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();

    internal void Reset() => _sessions.Clear();
}
