using System.Collections.Concurrent;
using System.Reactive;
using System.Reactive.Linq;
using Shiny.BluetoothLE;

namespace ShinyBle.DevFlowPlugin;

/// <summary>
/// Wraps Shiny's <see cref="IBleManager"/> so everything the app does through it is recorded by
/// <see cref="BleActivityMonitor"/>. Peripherals come back wrapped too, so reads, writes, notifications and
/// connection changes are seen without the app changing any code.
/// </summary>
/// <remarks>
/// This sample can only use Shiny's public API, so it decorates the manager. A plugin that ships inside
/// Shiny would use an internal hook instead and keep Shiny's optional peripheral features
/// (MTU requests, pairing, L2CAP), which a wrapper hides from <c>is</c> checks.
/// </remarks>
public sealed class MonitoredBleManager(IBleManager inner, BleActivityMonitor monitor) : IBleManager
{
    readonly ConcurrentDictionary<string, MonitoredPeripheral> _peripherals = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The Shiny manager being monitored.</summary>
    public IBleManager Inner => inner;

    public AccessState CurrentAccess => inner.CurrentAccess;

    public bool IsScanning => inner.IsScanning;

    public IObservable<AccessState> RequestAccess() => inner.RequestAccess();

    public IPeripheral? GetKnownPeripheral(string peripheralUuid)
        => inner.GetKnownPeripheral(peripheralUuid) is { } peripheral ? Wrap(peripheral) : null;

    public IEnumerable<IPeripheral> GetConnectedPeripherals()
        => inner.GetConnectedPeripherals().Select(Wrap).ToArray();

    public IObservable<ScanResult> Scan(ScanConfig? scanConfig = null) => Observable.Create<ScanResult>(observer =>
    {
        monitor.ScanStarted(scanConfig);
        var subscription = inner
            .Scan(scanConfig)
            .Select(result => result with { Peripheral = Wrap(result.Peripheral) })
            .Subscribe(
                result =>
                {
                    monitor.Scanned(result);
                    observer.OnNext(result);
                },
                ex =>
                {
                    monitor.ScanFailed(ex);
                    observer.OnError(ex);
                },
                observer.OnCompleted);

        return () =>
        {
            subscription.Dispose();
            monitor.ScanStopped();
        };
    });

    public void StopScan()
    {
        inner.StopScan();
        monitor.ScanStopped();
    }

    internal IPeripheral Wrap(IPeripheral peripheral)
        => peripheral as MonitoredPeripheral
            ?? _peripherals.GetOrAdd(peripheral.Uuid, _ => new MonitoredPeripheral(peripheral, monitor));
}

/// <summary>A peripheral whose operations are recorded by <see cref="BleActivityMonitor"/>.</summary>
public sealed class MonitoredPeripheral : IPeripheral
{
    readonly IPeripheral _inner;
    readonly BleActivityMonitor _monitor;

    internal MonitoredPeripheral(IPeripheral inner, BleActivityMonitor monitor)
    {
        _inner = inner;
        _monitor = monitor;
        monitor.Track(this);

        // Kept for the app's lifetime, like the peripheral itself. Status subscriptions are passive.
        inner.WhenStatusChanged().Subscribe(state => monitor.StatusChanged(this, state), _ => { });
        inner.WhenConnectionFailed().Subscribe(ex => monitor.ConnectionFailed(this, ex), _ => { });
    }

    /// <summary>The Shiny peripheral being monitored.</summary>
    public IPeripheral Inner => _inner;

    public string Uuid => _inner.Uuid;

    public string? Name => _inner.Name;

    public int Mtu => _inner.Mtu;

    public ConnectionState Status => _inner.Status;

    public void Connect(ConnectionConfig? config)
    {
        _monitor.ConnectRequested(this, config);
        _inner.Connect(config);
    }

    public void CancelConnection()
    {
        _monitor.DisconnectRequested(this);
        _inner.CancelConnection();
    }

    public IObservable<ConnectionState> WhenStatusChanged() => _inner.WhenStatusChanged();

    public IObservable<BleException> WhenConnectionFailed() => _inner.WhenConnectionFailed();

    public IObservable<Unit> WhenServicesChanged() => _inner.WhenServicesChanged();

    public IObservable<int> ReadRssi() => _inner.ReadRssi();

    public IObservable<BleServiceInfo> GetService(string serviceUuid) => _inner.GetService(serviceUuid);

    public IObservable<IReadOnlyList<BleServiceInfo>> GetServices() => _inner.GetServices();

    public IObservable<BleCharacteristicInfo> GetCharacteristic(string serviceUuid, string characteristicUuid)
        => _inner.GetCharacteristic(serviceUuid, characteristicUuid);

    public IObservable<IReadOnlyList<BleCharacteristicInfo>> GetCharacteristics(string serviceUuid)
        => _inner.GetCharacteristics(serviceUuid);

    public IObservable<BleCharacteristicResult> NotifyCharacteristic(string serviceUuid, string characteristicUuid, bool useIndicationsIfAvailable = true)
        => Record("notify", characteristicUuid, _inner.NotifyCharacteristic(serviceUuid, characteristicUuid, useIndicationsIfAvailable));

    public IObservable<BleCharacteristicInfo> WhenCharacteristicSubscriptionChanged(string serviceUuid, string characteristicUuid)
        => _inner.WhenCharacteristicSubscriptionChanged(serviceUuid, characteristicUuid);

    public IObservable<BleCharacteristicResult> ReadCharacteristic(string serviceUuid, string characteristicUuid)
        => Record("read", characteristicUuid, _inner.ReadCharacteristic(serviceUuid, characteristicUuid));

    public IObservable<BleCharacteristicResult> WriteCharacteristic(string serviceUuid, string characteristicUuid, byte[] data, bool withResponse = true)
        => Record("write", characteristicUuid, _inner.WriteCharacteristic(serviceUuid, characteristicUuid, data, withResponse));

    public IObservable<BleDescriptorInfo> GetDescriptor(string serviceUuid, string characteristicUuid, string descriptorUuid)
        => _inner.GetDescriptor(serviceUuid, characteristicUuid, descriptorUuid);

    public IObservable<IReadOnlyList<BleDescriptorInfo>> GetDescriptors(string serviceUuid, string characteristicUuid)
        => _inner.GetDescriptors(serviceUuid, characteristicUuid);

    public IObservable<BleDescriptorResult> ReadDescriptor(string serviceUuid, string characteristicUuid, string descriptorUuid)
        => _inner.ReadDescriptor(serviceUuid, characteristicUuid, descriptorUuid);

    public IObservable<BleDescriptorResult> WriteDescriptor(string serviceUuid, string characteristicUuid, string descriptorUuid, byte[] data)
        => _inner.WriteDescriptor(serviceUuid, characteristicUuid, descriptorUuid, data);

    IObservable<BleCharacteristicResult> Record(string operation, string characteristicUuid, IObservable<BleCharacteristicResult> source)
        => source.Do(
            result => _monitor.Characteristic(this, result),
            ex => _monitor.OperationFailed(this, operation, characteristicUuid, ex));
}
