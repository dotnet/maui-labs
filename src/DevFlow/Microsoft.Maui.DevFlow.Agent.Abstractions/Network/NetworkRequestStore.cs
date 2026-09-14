using System.Collections.Concurrent;

namespace Microsoft.Maui.DevFlow.Agent.Core.Network;

/// <summary>
/// Thread-safe ring buffer that stores captured network requests.
/// Evicts oldest entries when capacity is reached.
/// </summary>
public class NetworkRequestStore
{
    private readonly ConcurrentQueue<NetworkRequestEntry> _entries = new();
    private int _count;
    private readonly int _maxEntries;

    public event Action<NetworkRequestEntry>? OnRequestCaptured;

    public NetworkRequestStore(int maxEntries = 500)
    {
        _maxEntries = maxEntries;
    }

    public void Add(NetworkRequestEntry entry)
    {
        _entries.Enqueue(entry);
        var count = Interlocked.Increment(ref _count);

        // Evict oldest if over capacity
        while (count > _maxEntries && _entries.TryDequeue(out _))
        {
            count = Interlocked.Decrement(ref _count);
        }

        OnRequestCaptured?.Invoke(entry);
    }

    public IReadOnlyList<NetworkRequestEntry> GetRecent(int count = 100, string? host = null, string? method = null, int? status = null)
    {
        IEnumerable<NetworkRequestEntry> query = _entries.Reverse();

        if (!string.IsNullOrEmpty(host))
            query = query.Where(e => e.Host != null && e.Host.Contains(host, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(method))
            query = query.Where(e => e.Method.Equals(method, StringComparison.OrdinalIgnoreCase));
        if (status.HasValue)
            query = query.Where(e => e.StatusCode == status.Value);

        return query.Take(count).ToList();
    }

    public NetworkRequestEntry? GetById(string id)
    {
        return _entries.FirstOrDefault(e => e.Id == id);
    }

    public void Clear()
    {
        while (_entries.TryDequeue(out _)) { }
        Interlocked.Exchange(ref _count, 0);
    }

    public int Count => _count;
}
