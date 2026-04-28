using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.Maui.DevFlow.Agent.Core;

/// <summary>
/// Platform-agnostic BLE event monitor with ring buffer and WebSocket subscriber support.
/// Platform-specific agents override StartScanning/StopScanning and push events via RecordEvent.
/// Apps and BLE libraries can also push events directly via the static <see cref="Instance"/> singleton.
/// </summary>
public class BleMonitor : IDisposable
{
    /// <summary>
    /// Global singleton so that app code / BLE libraries can push events without a reference to the agent.
    /// </summary>
    public static BleMonitor? Instance { get; internal set; }

    private readonly ConcurrentQueue<BleEvent> _events = new();
    private readonly List<BleSubscriber> _subscribers = new();
    private readonly object _gate = new();
    private readonly int _maxEvents;
    private int _eventCount;
    private bool _scanning;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public BleMonitor(int maxEvents = 2000)
    {
        _maxEvents = Math.Max(0, maxEvents);
    }

    public bool IsScanning
    {
        get { lock (_gate) return _scanning; }
    }

    public virtual bool SupportsScanning => false;

    public virtual IReadOnlyList<string> SupportedFeatures
        => SupportsScanning
            ? ["status", "events", "scan", "stream"]
            : ["status", "events", "stream"];

    public object GetStatus()
    {
        lock (_gate)
        {
            return new
            {
                scanning = _scanning,
                eventCount = _eventCount,
                subscribers = _subscribers.Count,
                supportsScanning = SupportsScanning
            };
        }
    }

    public List<BleEvent> GetEvents(int limit = 100, string? type = null)
    {
        if (limit <= 0)
            return [];

        var all = _events.ToArray();
        IEnumerable<BleEvent> filtered = all;
        if (!string.IsNullOrEmpty(type))
            filtered = filtered.Where(e => e.Type.Equals(type, StringComparison.OrdinalIgnoreCase));
        return filtered.TakeLast(limit).ToList();
    }

    public void ClearEvents()
    {
        while (_events.TryDequeue(out _)) { }
        Interlocked.Exchange(ref _eventCount, 0);
    }

    /// <summary>
    /// Record a BLE event. Called by platform-specific code or app BLE libraries.
    /// </summary>
    public void RecordEvent(BleEvent evt)
    {
        if (_disposed)
            return;

        evt.Timestamp ??= DateTimeOffset.UtcNow.ToString("O");

        _events.Enqueue(evt);
        Interlocked.Increment(ref _eventCount);

        // Trim ring buffer
        while (_events.Count > _maxEvents)
        {
            if (!_events.TryDequeue(out _))
                break;

            Interlocked.Decrement(ref _eventCount);
        }

        // Broadcast to WebSocket subscribers
        var json = JsonSerializer.Serialize(new
        {
            type = "ble_event",
            timestamp = evt.Timestamp,
            @event = evt
        }, JsonOpts);

        List<BleSubscriber> snapshot;
        lock (_gate) { snapshot = new List<BleSubscriber>(_subscribers); }
        foreach (var subscriber in snapshot)
        {
            if (subscriber.Type != null && !evt.Type.Equals(subscriber.Type, StringComparison.OrdinalIgnoreCase))
                continue;

            subscriber.Queue.Enqueue(json);
        }
    }

    // Convenience methods for common event types

    public void RecordScanResult(string deviceId, string? deviceName, int? rssi, string? advertisementData = null)
        => RecordEvent(new BleEvent
        {
            Type = "scan_result",
            DeviceId = deviceId,
            DeviceName = deviceName,
            Rssi = rssi,
            Data = advertisementData
        });

    public void RecordConnectionStateChanged(string deviceId, string? deviceName, string state)
        => RecordEvent(new BleEvent
        {
            Type = state switch
            {
                "connected" => "connected",
                "disconnected" => "disconnected",
                _ => "connection_state_changed"
            },
            DeviceId = deviceId,
            DeviceName = deviceName,
            Data = state
        });

    public void RecordCharacteristicRead(string deviceId, string? deviceName, string serviceUuid, string characteristicUuid, string? valueHex)
        => RecordEvent(new BleEvent
        {
            Type = "characteristic_read",
            DeviceId = deviceId,
            DeviceName = deviceName,
            ServiceUuid = serviceUuid,
            CharacteristicUuid = characteristicUuid,
            Data = valueHex
        });

    public void RecordCharacteristicWrite(string deviceId, string? deviceName, string serviceUuid, string characteristicUuid, string? valueHex, bool withResponse = true)
        => RecordEvent(new BleEvent
        {
            Type = withResponse ? "characteristic_write" : "characteristic_write_no_response",
            DeviceId = deviceId,
            DeviceName = deviceName,
            ServiceUuid = serviceUuid,
            CharacteristicUuid = characteristicUuid,
            Data = valueHex
        });

    public void RecordNotification(string deviceId, string? deviceName, string serviceUuid, string characteristicUuid, string? valueHex)
        => RecordEvent(new BleEvent
        {
            Type = "notification",
            DeviceId = deviceId,
            DeviceName = deviceName,
            ServiceUuid = serviceUuid,
            CharacteristicUuid = characteristicUuid,
            Data = valueHex
        });

    public void RecordServiceDiscovered(string deviceId, string? deviceName, string serviceUuid)
        => RecordEvent(new BleEvent
        {
            Type = "service_discovered",
            DeviceId = deviceId,
            DeviceName = deviceName,
            ServiceUuid = serviceUuid
        });

    public void RecordDescriptorWrite(string deviceId, string? deviceName, string serviceUuid, string characteristicUuid, string descriptorUuid, string? valueHex)
        => RecordEvent(new BleEvent
        {
            Type = "descriptor_write",
            DeviceId = deviceId,
            DeviceName = deviceName,
            ServiceUuid = serviceUuid,
            CharacteristicUuid = characteristicUuid,
            DescriptorUuid = descriptorUuid,
            Data = valueHex
        });

    public void RecordMtuChanged(string deviceId, string? deviceName, int mtu)
        => RecordEvent(new BleEvent
        {
            Type = "mtu_changed",
            DeviceId = deviceId,
            DeviceName = deviceName,
            Data = mtu.ToString()
        });

    // Scanning lifecycle — platform agents override StartPlatformScan/StopPlatformScan

    public string? StartScanning()
    {
        TryStartScanning(out var error);
        return error;
    }

    public bool TryStartScanning(out string? error)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                error = "BLE monitor disposed";
                return false;
            }

            if (_scanning)
            {
                error = null;
                return false;
            }

            error = StartPlatformScan();
            if (error != null)
                return false;

            _scanning = true;
            return true;
        }
    }

    public string? StopScanning()
    {
        lock (_gate)
        {
            if (!_scanning) return null;
            StopPlatformScan();
            _scanning = false;
            return null;
        }
    }

    /// <summary>Override in platform subclass to start native BLE scan.</summary>
    protected virtual string? StartPlatformScan() => "BLE scanning not supported on this platform";

    /// <summary>Override in platform subclass to stop native BLE scan.</summary>
    protected virtual void StopPlatformScan() { }

    // WebSocket subscriber management

    public ConcurrentQueue<string> Subscribe(string? type = null)
    {
        var queue = new ConcurrentQueue<string>();
        lock (_gate) { _subscribers.Add(new BleSubscriber(queue, type)); }
        return queue;
    }

    public void Unsubscribe(ConcurrentQueue<string> queue)
    {
        lock (_gate) { _subscribers.RemoveAll(subscriber => ReferenceEquals(subscriber.Queue, queue)); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopScanning();
        DisposePlatform();
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Allows platform-specific subclasses to release native resources that are not
    /// tied strictly to an active scan, such as watchers, receivers, or callbacks.
    /// </summary>
    protected virtual void DisposePlatform()
    {
    }

    private sealed class BleSubscriber(ConcurrentQueue<string> queue, string? type)
    {
        public ConcurrentQueue<string> Queue { get; } = queue;
        public string? Type { get; } = string.IsNullOrWhiteSpace(type) ? null : type;
    }
}

public class BleEvent
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }

    [JsonPropertyName("deviceId")]
    public string? DeviceId { get; set; }

    [JsonPropertyName("deviceName")]
    public string? DeviceName { get; set; }

    [JsonPropertyName("rssi")]
    public int? Rssi { get; set; }

    [JsonPropertyName("serviceUuid")]
    public string? ServiceUuid { get; set; }

    [JsonPropertyName("characteristicUuid")]
    public string? CharacteristicUuid { get; set; }

    [JsonPropertyName("descriptorUuid")]
    public string? DescriptorUuid { get; set; }

    [JsonPropertyName("data")]
    public string? Data { get; set; }
}
