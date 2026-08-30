using System.Text.Json;

namespace Microsoft.Maui.DevFlow.Agent.Core;

internal sealed class UiEventSubscriptionQueue
{
    private readonly Queue<string> _items;
    private readonly int _capacity;
    private readonly object _gate = new();
    private long _droppedSinceLastNotification;
    private long _droppedCount;

    public UiEventSubscriptionQueue(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be > 0");

        _capacity = capacity;
        _items = new Queue<string>(capacity);
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _items.Count;
            }
        }
    }

    public long DroppedCount
    {
        get
        {
            lock (_gate)
            {
                return _droppedCount;
            }
        }
    }

    public void Enqueue(string payload)
    {
        lock (_gate)
        {
            if (_items.Count == _capacity)
            {
                _items.Dequeue();
                _droppedSinceLastNotification++;
                _droppedCount++;
            }

            _items.Enqueue(payload);
        }
    }

    public bool TryDequeue(out string message)
    {
        lock (_gate)
        {
            if (_droppedSinceLastNotification > 0)
            {
                message = JsonSerializer.Serialize(new
                {
                    type = "loss",
                    timestamp = DateTimeOffset.UtcNow.ToString("O"),
                    data = new
                    {
                        stream = "ui-events",
                        reason = "queue-overflow",
                        droppedCount = _droppedSinceLastNotification,
                        totalDroppedCount = _droppedCount
                    }
                });
                _droppedSinceLastNotification = 0;
                return true;
            }

            if (_items.TryDequeue(out var payload))
            {
                message = payload;
                return true;
            }

            message = string.Empty;
            return false;
        }
    }
}
