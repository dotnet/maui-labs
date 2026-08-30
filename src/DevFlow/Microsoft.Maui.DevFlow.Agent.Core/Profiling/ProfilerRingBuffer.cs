namespace Microsoft.Maui.DevFlow.Agent.Core.Profiling;

public sealed class ProfilerRingBufferReadResult<T> where T : class
{
    public List<T> Items { get; init; } = new();
    public long NextCursor { get; init; }
    public long OldestCursor { get; init; }
    public long LatestCursor { get; init; }
    public long LostCount { get; init; }
    public int AvailableCount { get; init; }
}

public class ProfilerRingBuffer<T> where T : class
{
    private readonly (long Sequence, T Value)[] _buffer;
    private long _latestSequence;
    private int _count;
    private readonly object _gate = new();

    public ProfilerRingBuffer(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be > 0");
        _buffer = new (long Sequence, T Value)[capacity];
    }

    public int Capacity => _buffer.Length;

    public long Add(T value)
    {
        lock (_gate)
        {
            var next = ++_latestSequence;
            var index = (int)((next - 1) % _buffer.Length);
            _buffer[index] = (next, value);
            if (_count < _buffer.Length)
                _count++;
            return next;
        }
    }

    public ProfilerRingBufferReadResult<T> ReadAfter(long afterSequence, int limit)
    {
        lock (_gate)
        {
            var requestedCursor = Math.Max(0, afterSequence);
            var latestCursor = _latestSequence;
            var oldestCursor = _count == 0 ? 0 : latestCursor - _count + 1;
            var lostCount = _count == 0
                ? 0
                : Math.Max(0, oldestCursor - requestedCursor - 1);
            var firstAvailableCursor = _count == 0
                ? 0
                : Math.Max(requestedCursor + 1, oldestCursor);
            var availableCount = _count == 0 || firstAvailableCursor > latestCursor
                ? 0
                : (int)(latestCursor - firstAvailableCursor + 1);
            var take = Math.Min(Math.Max(0, limit), availableCount);
            var results = new List<T>(take);

            for (var sequence = firstAvailableCursor; sequence < firstAvailableCursor + take; sequence++)
            {
                var index = (int)((sequence - 1) % _buffer.Length);
                results.Add(_buffer[index].Value);
            }

            return new ProfilerRingBufferReadResult<T>
            {
                Items = results,
                NextCursor = take == 0 ? requestedCursor : firstAvailableCursor + take - 1,
                OldestCursor = oldestCursor,
                LatestCursor = latestCursor,
                LostCount = lostCount,
                AvailableCount = availableCount
            };
        }
    }

    public ProfilerRingBufferReadResult<T> ReadLatest(int limit)
    {
        lock (_gate)
        {
            var latestCursor = _latestSequence;
            var oldestCursor = _count == 0 ? 0 : latestCursor - _count + 1;
            var take = Math.Min(Math.Max(0, limit), _count);
            var firstCursor = take == 0 ? 0 : latestCursor - take + 1;
            var results = new List<T>(take);
            for (var sequence = firstCursor; sequence <= latestCursor && sequence > 0; sequence++)
            {
                var index = (int)((sequence - 1) % _buffer.Length);
                results.Add(_buffer[index].Value);
            }

            return new ProfilerRingBufferReadResult<T>
            {
                Items = results,
                NextCursor = take == 0 ? 0 : latestCursor,
                OldestCursor = oldestCursor,
                LatestCursor = latestCursor,
                LostCount = _count == 0 ? 0 : Math.Max(0, oldestCursor - 1),
                AvailableCount = _count
            };
        }
    }

    /// <summary>
    /// Compatibility overload. The returned cursor is the last item actually returned, so callers
    /// can continue paging without skipping unread entries.
    /// </summary>
    public List<T> ReadAfter(long afterSequence, int limit, out long nextCursor)
    {
        var result = ReadAfter(afterSequence, limit);
        nextCursor = result.NextCursor;
        return result.Items;
    }

    public void Clear()
    {
        lock (_gate)
        {
            _latestSequence = 0;
            _count = 0;
            Array.Clear(_buffer);
        }
    }
}
