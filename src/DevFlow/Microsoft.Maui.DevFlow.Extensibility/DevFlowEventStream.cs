using System.Text.Json;

namespace Microsoft.Maui.DevFlow.Extensibility;

/// <summary>One event in a <see cref="DevFlowEventStream"/>.</summary>
/// <param name="Sequence">Position in the stream, starting at 1. Never reused while the app runs.</param>
/// <param name="Timestamp">When the event was published.</param>
/// <param name="Data">The event payload, already serialized.</param>
public sealed record DevFlowEvent(long Sequence, DateTimeOffset Timestamp, JsonElement Data);

/// <summary>A page of events read from a <see cref="DevFlowEventStream"/>.</summary>
/// <param name="Events">Events after the requested sequence, oldest first.</param>
/// <param name="NextSequence">Pass this as <c>since</c> on the next read.</param>
/// <param name="Dropped">Events after the requested sequence that had already left the buffer.</param>
public sealed record DevFlowEventPage(IReadOnlyList<DevFlowEvent> Events, long NextSequence, long Dropped);

/// <summary>
/// A bounded, thread-safe buffer of events a plugin publishes for the CLI, MCP clients and the
/// inspector. When full, the oldest event is dropped; readers learn about the gap through
/// <see cref="DevFlowEventPage.Dropped"/>. Publishing never blocks.
/// </summary>
public class DevFlowEventStream
{
    readonly object gate = new();
    readonly DevFlowEvent?[] buffer;
    readonly List<Action<DevFlowEvent>> observers = [];
    Action<DevFlowEvent>[] observerSnapshot = [];
    int start;
    int count;
    long lastSequence;

    /// <param name="capacity">How many events to keep. Older events are dropped.</param>
    public DevFlowEventStream(int capacity = 1000)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        buffer = new DevFlowEvent?[capacity];
    }

    /// <summary>How many events the buffer keeps.</summary>
    public int Capacity => buffer.Length;

    /// <summary>Sequence of the newest event, or 0 when nothing has been published.</summary>
    public long LastSequence
    {
        get { lock (gate) return lastSequence; }
    }

    /// <summary>Publishes an event. The payload is serialized with <see cref="DevFlowJson.Options"/>.</summary>
    public void Publish(object? data) => PublishElement(DevFlowJson.SerializeToElement(data));

    /// <summary>Publishes an already serialized payload.</summary>
    public void PublishElement(JsonElement data)
    {
        DevFlowEvent evt;
        Action<DevFlowEvent>[] snapshot;
        lock (gate)
        {
            evt = new DevFlowEvent(++lastSequence, DateTimeOffset.UtcNow, data);
            var index = (start + count) % buffer.Length;
            buffer[index] = evt;
            if (count == buffer.Length)
                start = (start + 1) % buffer.Length;
            else
                count++;
            snapshot = observerSnapshot;
        }

        foreach (var observer in snapshot)
        {
            try { observer(evt); }
            catch { /* an observer must not break the publisher */ }
        }
    }

    /// <summary>
    /// Reads events after <paramref name="since"/>. A <paramref name="since"/> newer than the stream
    /// (the app restarted) reads from the start.
    /// </summary>
    public DevFlowEventPage Read(long since = 0, int limit = 100)
    {
        if (limit < 1)
            limit = 1;

        lock (gate)
        {
            if (since > lastSequence || since < 0)
                since = 0;

            var oldest = count == 0 ? lastSequence + 1 : buffer[start]!.Sequence;
            var dropped = Math.Max(0, oldest - 1 - since);
            var events = new List<DevFlowEvent>(Math.Min(limit, count));
            for (var i = 0; i < count && events.Count < limit; i++)
            {
                var evt = buffer[(start + i) % buffer.Length]!;
                if (evt.Sequence > since)
                    events.Add(evt);
            }

            var next = events.Count > 0 ? events[^1].Sequence : Math.Max(since, oldest - 1);
            return new DevFlowEventPage(events, next, dropped);
        }
    }

    /// <summary>
    /// Calls <paramref name="observer"/> for every event published from now on. Observers run on the
    /// publishing thread, so they must be quick. Dispose the result to stop.
    /// </summary>
    public IDisposable Subscribe(Action<DevFlowEvent> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        lock (gate)
        {
            observers.Add(observer);
            observerSnapshot = [.. observers];
        }
        return new Subscription(this, observer);
    }

    /// <summary>Removes all buffered events. Sequences keep counting up.</summary>
    public void Clear()
    {
        lock (gate)
        {
            Array.Clear(buffer);
            start = 0;
            count = 0;
        }
    }

    void Unsubscribe(Action<DevFlowEvent> observer)
    {
        lock (gate)
        {
            observers.Remove(observer);
            observerSnapshot = [.. observers];
        }
    }

    sealed class Subscription(DevFlowEventStream stream, Action<DevFlowEvent> observer) : IDisposable
    {
        int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
                stream.Unsubscribe(observer);
        }
    }
}

/// <summary>A <see cref="DevFlowEventStream"/> with a typed <see cref="Publish(T)"/>.</summary>
public sealed class DevFlowEventStream<T>(int capacity = 1000) : DevFlowEventStream(capacity)
{
    /// <summary>Publishes an event.</summary>
    public void Publish(T data) => base.Publish(data);
}
