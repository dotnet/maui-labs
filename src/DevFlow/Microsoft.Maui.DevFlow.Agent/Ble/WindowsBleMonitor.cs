#if WINDOWS
using Microsoft.Maui.DevFlow.Agent.Core;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Enumeration;

namespace Microsoft.Maui.DevFlow.Agent.Ble;

internal sealed class WindowsBleMonitor : BleMonitor
{
    private BluetoothLEAdvertisementWatcher? _watcher;
    private DeviceWatcher? _deviceWatcher;
    private readonly Dictionary<string, string> _deviceAddresses = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _deviceGate = new();

    public WindowsBleMonitor() : base()
    {
        StartConnectionWatcher();
        SnapshotConnectedDevices();
    }

    public override bool SupportsScanning => true;

    protected override string? StartPlatformScan()
    {
        _watcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Active
        };
        _watcher.Received += OnAdvertisementReceived;
        _watcher.Stopped += OnWatcherStopped;
        _watcher.Start();
        return null;
    }

    protected override void StopPlatformScan()
    {
        if (_watcher != null)
        {
            _watcher.Received -= OnAdvertisementReceived;
            _watcher.Stopped -= OnWatcherStopped;
            try { _watcher.Stop(); }
            catch { /* may already be stopped */ }
            _watcher = null;
        }
    }

    protected override void DisposePlatform()
    {
        if (_deviceWatcher != null)
        {
            try { _deviceWatcher.Stop(); }
            catch { /* may already be stopped */ }

            _deviceWatcher.Added -= OnDeviceAdded;
            _deviceWatcher.Removed -= OnDeviceRemoved;
            _deviceWatcher = null;
        }

        lock (_deviceGate)
        {
            _deviceAddresses.Clear();
        }
    }

    private void OnAdvertisementReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        var serviceUuids = args.Advertisement.ServiceUuids;
        var dataString = serviceUuids.Count > 0
            ? string.Join(",", serviceUuids)
            : null;

        RecordScanResult(
            args.BluetoothAddress.ToString("X12"),
            args.Advertisement.LocalName,
            args.RawSignalStrengthInDBm,
            dataString
        );
    }

    private void OnWatcherStopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs args)
    {
        if (args.Error != BluetoothError.Success)
        {
            RecordEvent(new BleEvent
            {
                Type = "scan_failed",
                Data = args.Error.ToString()
            });
        }
    }

    private void StartConnectionWatcher()
    {
        try
        {
            // Watch for BLE device connection/disconnection
            var selector = BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected);
            _deviceWatcher = DeviceInformation.CreateWatcher(selector);
            _deviceWatcher.Added += OnDeviceAdded;
            _deviceWatcher.Removed += OnDeviceRemoved;
            _deviceWatcher.Start();
        }
        catch { /* Bluetooth may not be available */ }
    }

    private async void OnDeviceAdded(DeviceWatcher sender, DeviceInformation info)
    {
        try
        {
            using var device = await BluetoothLEDevice.FromIdAsync(info.Id);
            if (device != null)
            {
                var deviceId = device.BluetoothAddress.ToString("X12");
                lock (_deviceGate)
                {
                    _deviceAddresses[info.Id] = deviceId;
                }

                RecordConnectionStateChanged(
                    deviceId,
                    device.Name,
                    "connected"
                );
            }
        }
        catch { }
    }

    private void OnDeviceRemoved(DeviceWatcher sender, DeviceInformationUpdate info)
    {
        string deviceId;
        lock (_deviceGate)
        {
            if (_deviceAddresses.Remove(info.Id, out var mappedDeviceId))
                deviceId = mappedDeviceId;
            else
                deviceId = info.Id;
        }

        RecordConnectionStateChanged(deviceId, null, "disconnected");
    }

    private async void SnapshotConnectedDevices()
    {
        try
        {
            var selector = BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected);
            var devices = await DeviceInformation.FindAllAsync(selector);
            foreach (var deviceInfo in devices)
            {
                try
                {
                    using var device = await BluetoothLEDevice.FromIdAsync(deviceInfo.Id);
                    if (device != null)
                    {
                        var deviceId = device.BluetoothAddress.ToString("X12");
                        lock (_deviceGate)
                        {
                            _deviceAddresses[deviceInfo.Id] = deviceId;
                        }

                        RecordConnectionStateChanged(
                            deviceId,
                            device.Name,
                            "connected"
                        );
                    }
                }
                catch { }
            }
        }
        catch { /* Bluetooth may not be available */ }
    }
}
#endif
