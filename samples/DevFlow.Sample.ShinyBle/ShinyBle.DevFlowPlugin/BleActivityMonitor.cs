using System.Collections.Concurrent;
using Microsoft.Maui.DevFlow.Extensibility;
using Shiny.BluetoothLE;

namespace ShinyBle.DevFlowPlugin;

/// <summary>
/// Records what the app does with Bluetooth: scans, connections and characteristic traffic. The
/// <see cref="MonitoredBleManager"/> decorator feeds it; <see cref="ShinyBleDevFlowExtension"/> serves it.
/// </summary>
public sealed class BleActivityMonitor
{
    // Scanning reports every advertisement, often several per second per device. A device is reported
    // when first seen, then again only after this long or when its signal moves noticeably.
    static readonly TimeSpan ScanRepeatInterval = TimeSpan.FromSeconds(5);
    const int ScanRssiDelta = 10;
    const int MaxValueBytes = 64;

    readonly ConcurrentDictionary<string, KnownPeripheral> _peripherals = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The live feed served as the <c>events</c> stream.</summary>
    public DevFlowEventStream<BleEvent> Events { get; } = new(2000);

    /// <summary>Peripherals seen so far, connected ones first.</summary>
    public IReadOnlyList<BlePeripheralInfo> GetPeripherals(bool connectedOnly = false, string? name = null)
        => _peripherals.Values
            .Select(p => p.ToInfo())
            .Where(p => !connectedOnly || p.Status == nameof(ConnectionState.Connected))
            .Where(p => string.IsNullOrWhiteSpace(name) || (p.Name?.Contains(name, StringComparison.OrdinalIgnoreCase) ?? false))
            .OrderByDescending(p => p.Status == nameof(ConnectionState.Connected))
            .ThenByDescending(p => p.LastSeen)
            .ToList();

    public int KnownPeripheralCount => _peripherals.Count;

    /// <summary>Forgets every peripheral and empties the event buffer.</summary>
    public void Clear()
    {
        _peripherals.Clear();
        Events.Clear();
    }

    internal void ScanStarted(ScanConfig? config)
        => Publish("scanStart", null, null, detail: config?.ServiceUuids is { Length: > 0 } uuids
            ? $"services {string.Join(",", uuids)}"
            : "all devices");

    internal void ScanStopped() => Publish("scanStop", null, null);

    internal void ScanFailed(Exception ex) => Publish("scanError", null, null, detail: ex.Message);

    internal void Scanned(ScanResult result)
    {
        var known = Track(result.Peripheral);
        if (!known.Sighted(result, ScanRepeatInterval, ScanRssiDelta))
            return;

        var ad = result.AdvertisementData;
        var detail = ad?.ServiceUuids is { Length: > 0 } uuids
            ? $"rssi {result.Rssi}, services {string.Join(",", uuids)}"
            : $"rssi {result.Rssi}";
        Publish("scan", result.Peripheral, ad?.LocalName, rssi: result.Rssi, detail: detail);
    }

    internal void ConnectRequested(IPeripheral peripheral, ConnectionConfig? config)
    {
        Track(peripheral);
        Publish("connect", peripheral, null, detail: config?.AutoConnect == true ? "auto-connect" : null);
    }

    internal void DisconnectRequested(IPeripheral peripheral)
    {
        Track(peripheral);
        Publish("disconnect", peripheral, null);
    }

    internal void ConnectionFailed(IPeripheral peripheral, Exception ex)
    {
        Track(peripheral);
        Publish("connectFailed", peripheral, null, detail: ex.Message);
    }

    internal void StatusChanged(IPeripheral peripheral, ConnectionState state)
    {
        if (Track(peripheral).StatusChanged(state))
            Publish("status", peripheral, null, detail: state.ToString());
    }

    internal void Characteristic(IPeripheral peripheral, BleCharacteristicResult result)
    {
        Track(peripheral).Count(result.Event);
        var kind = result.Event switch
        {
            BleCharacteristicEvent.Read => "read",
            BleCharacteristicEvent.Write => "write",
            BleCharacteristicEvent.WriteWithoutResponse => "writeNoResponse",
            BleCharacteristicEvent.Notification => "notify",
            _ => result.Event.ToString()
        };

        Publish(
            kind,
            peripheral,
            null,
            service: result.Characteristic.Service.Uuid,
            characteristic: result.Characteristic.Uuid,
            value: ToHex(result.Data),
            detail: result.Data is null ? null : $"{result.Data.Length} bytes");
    }

    internal void OperationFailed(IPeripheral peripheral, string operation, string? characteristic, Exception ex)
    {
        Track(peripheral);
        Publish("error", peripheral, null, characteristic: characteristic, detail: $"{operation}: {ex.Message}");
    }

    internal KnownPeripheral Track(IPeripheral peripheral)
        => _peripherals.GetOrAdd(peripheral.Uuid, _ => new KnownPeripheral(peripheral));

    void Publish(
        string kind,
        IPeripheral? peripheral,
        string? fallbackName,
        string? service = null,
        string? characteristic = null,
        string? value = null,
        int? rssi = null,
        string? detail = null)
        => Events.Publish(new BleEvent(
            kind,
            peripheral?.Uuid,
            peripheral?.Name ?? fallbackName,
            service,
            characteristic,
            value,
            rssi,
            detail));

    internal static string? ToHex(byte[]? data)
    {
        if (data is null)
            return null;

        return data.Length <= MaxValueBytes
            ? Convert.ToHexString(data)
            : Convert.ToHexString(data, 0, MaxValueBytes) + "…";
    }

    internal sealed class KnownPeripheral(IPeripheral peripheral)
    {
        readonly object _gate = new();
        DateTimeOffset _lastSeen = DateTimeOffset.UtcNow;
        DateTimeOffset _lastReported = DateTimeOffset.MinValue;
        int? _rssi;
        int? _reportedRssi;
        IAdvertisementData? _advertisement;
        ConnectionState? _lastState;
        int _reads;
        int _writes;
        int _notifications;

        public IPeripheral Peripheral { get; } = peripheral;

        public bool Sighted(ScanResult result, TimeSpan repeat, int rssiDelta)
        {
            lock (_gate)
            {
                var now = DateTimeOffset.UtcNow;
                _lastSeen = now;
                _rssi = result.Rssi;
                _advertisement = result.AdvertisementData;

                var report = _reportedRssi is null
                    || now - _lastReported >= repeat
                    || Math.Abs(result.Rssi - _reportedRssi.Value) >= rssiDelta;
                if (report)
                {
                    _lastReported = now;
                    _reportedRssi = result.Rssi;
                }
                return report;
            }
        }

        public bool StatusChanged(ConnectionState state)
        {
            lock (_gate)
            {
                // The first value a status subscription delivers is the current state, not a change.
                var changed = _lastState is not null && _lastState != state;
                _lastState = state;
                _lastSeen = DateTimeOffset.UtcNow;
                return changed;
            }
        }

        public void Count(BleCharacteristicEvent kind)
        {
            lock (_gate)
            {
                _lastSeen = DateTimeOffset.UtcNow;
                switch (kind)
                {
                    case BleCharacteristicEvent.Read:
                        _reads++;
                        break;
                    case BleCharacteristicEvent.Write:
                    case BleCharacteristicEvent.WriteWithoutResponse:
                        _writes++;
                        break;
                    case BleCharacteristicEvent.Notification:
                        _notifications++;
                        break;
                }
            }
        }

        public BlePeripheralInfo ToInfo()
        {
            lock (_gate)
            {
                var ad = _advertisement;
                return new BlePeripheralInfo(
                    Peripheral.Uuid,
                    Peripheral.Name ?? ad?.LocalName,
                    Peripheral.Status.ToString(),
                    Peripheral.Mtu,
                    _rssi,
                    _lastSeen,
                    ad?.IsConnectable,
                    ad?.ServiceUuids,
                    ad?.ManufacturerData?.CompanyId,
                    ad?.TxPower,
                    _reads,
                    _writes,
                    _notifications);
            }
        }
    }
}

/// <summary>One entry in the <c>events</c> stream.</summary>
/// <param name="Kind">
/// <c>scanStart</c>, <c>scanStop</c>, <c>scan</c>, <c>connect</c>, <c>disconnect</c>, <c>status</c>,
/// <c>connectFailed</c>, <c>read</c>, <c>write</c>, <c>writeNoResponse</c>, <c>notify</c> or <c>error</c>.
/// </param>
/// <param name="Peripheral">The peripheral's UUID.</param>
/// <param name="Name">The peripheral's name, when known.</param>
/// <param name="Service">Service UUID for characteristic events.</param>
/// <param name="Characteristic">Characteristic UUID for characteristic events.</param>
/// <param name="Value">The value in hex; long values are cut at 64 bytes.</param>
/// <param name="Rssi">Signal strength for scan events.</param>
/// <param name="Detail">Readable summary, e.g. the new connection state or the value length.</param>
public sealed record BleEvent(
    string Kind,
    string? Peripheral,
    string? Name,
    string? Service,
    string? Characteristic,
    string? Value,
    int? Rssi,
    string? Detail);

/// <summary>One entry in the result of the <c>peripherals</c> tool.</summary>
public sealed record BlePeripheralInfo(
    string Uuid,
    string? Name,
    string Status,
    int Mtu,
    int? Rssi,
    DateTimeOffset LastSeen,
    bool? IsConnectable,
    string[]? ServiceUuids,
    ushort? ManufacturerCompanyId,
    int? TxPower,
    int Reads,
    int Writes,
    int Notifications);
