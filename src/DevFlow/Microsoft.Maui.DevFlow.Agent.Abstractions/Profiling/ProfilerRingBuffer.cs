namespace Microsoft.Maui.DevFlow.Agent.Core.Profiling;

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

    public List<T> ReadAfter(long afterSequence, int limit, out long latestSequence)
    {
        if (limit <= 0)
        {
            latestSequence = _latestSequence;
            return new List<T>();
        }

        lock (_gate)
        {
            latestSequence = _latestSequence;
            if (_count == 0 || afterSequence >= _latestSequence)
                return new List<T>();

            var oldestSequence = _latestSequence - _count + 1;
            var firstSequence = Math.Max(afterSequence + 1, oldestSequence);
            var remaining = _latestSequence - firstSequence + 1;
            var take = (int)Math.Min(limit, remaining);
            var results = new List<T>(take);

            for (var sequence = firstSequence; sequence < firstSequence + take; sequence++)
            {
                var index = (int)((sequence - 1) % _buffer.Length);
                results.Add(_buffer[index].Value);
            }

            return results;
        }
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
