#if MACOS
using CoreBluetooth;
using Foundation;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace Microsoft.Maui.DevFlow.Agent.Ble;

internal sealed class MacOsBleMonitor : BleMonitor
{
    private CBCentralManager? _centralManager;
    private DevFlowCentralDelegate? _delegate;
    private bool _snapshotTaken;

    public MacOsBleMonitor() : base()
    {
        _delegate = new DevFlowCentralDelegate(this);
        _centralManager = new CBCentralManager(_delegate, null);
    }

    public override bool SupportsScanning => true;

    protected override string? StartPlatformScan()
    {
        if (_centralManager == null)
        {
            _delegate = new DevFlowCentralDelegate(this);
            _centralManager = new CBCentralManager(_delegate, null);
        }

        if (_centralManager.State == CBManagerState.PoweredOn)
        {
            _centralManager.ScanForPeripherals(
                (CBUUID[]?)null,
                new PeripheralScanningOptions { AllowDuplicatesKey = true }
            );
        }

        return null;
    }

    protected override void StopPlatformScan()
    {
        if (_centralManager != null)
        {
            try { _centralManager.StopScan(); }
            catch { }
        }
    }

    protected override void DisposePlatform()
    {
        _centralManager?.Dispose();
        _centralManager = null;
        _delegate?.Dispose();
        _delegate = null;
    }

    internal void SnapshotConnectedDevices(CBCentralManager central)
    {
        if (_snapshotTaken) return;
        _snapshotTaken = true;

        try
        {
            var commonServices = new[]
            {
                CBUUID.FromString("180A"), // Device Information
                CBUUID.FromString("180F"), // Battery Service
                CBUUID.FromString("1800"), // Generic Access
                CBUUID.FromString("1801"), // Generic Attribute
                CBUUID.FromString("180D"), // Heart Rate
                CBUUID.FromString("1812"), // HID
            };

            var peripherals = central.RetrieveConnectedPeripherals(commonServices);
            if (peripherals == null) return;

            var seen = new HashSet<string>();
            foreach (var peripheral in peripherals)
            {
                var id = peripheral.Identifier.ToString();
                if (seen.Add(id))
                    RecordConnectionStateChanged(id, peripheral.Name, "connected");
            }
        }
        catch { }
    }

    private sealed class DevFlowCentralDelegate : CBCentralManagerDelegate
    {
        private readonly MacOsBleMonitor _monitor;

        public DevFlowCentralDelegate(MacOsBleMonitor monitor) => _monitor = monitor;

        public override void UpdatedState(CBCentralManager central)
        {
            if (central.State == CBManagerState.PoweredOn)
            {
                _monitor.SnapshotConnectedDevices(central);

                if (_monitor.IsScanning)
                {
                    central.ScanForPeripherals(
                        (CBUUID[]?)null,
                        new PeripheralScanningOptions { AllowDuplicatesKey = true }
                    );
                }
            }
            else
            {
                _monitor.RecordEvent(new BleEvent
                {
                    Type = "adapter_state_changed",
                    Data = central.State.ToString()
                });
            }
        }

        public override void DiscoveredPeripheral(CBCentralManager central, CBPeripheral peripheral, NSDictionary advertisementData, NSNumber rssi)
        {
            _monitor.RecordScanResult(
                peripheral.Identifier.ToString(),
                peripheral.Name,
                rssi.Int32Value,
                FormatAdvertisementData(advertisementData)
            );
        }

        public override void ConnectedPeripheral(CBCentralManager central, CBPeripheral peripheral)
        {
            _monitor.RecordConnectionStateChanged(
                peripheral.Identifier.ToString(),
                peripheral.Name,
                "connected"
            );
        }

        public override void DisconnectedPeripheral(CBCentralManager central, CBPeripheral peripheral, NSError? error)
        {
            _monitor.RecordConnectionStateChanged(
                peripheral.Identifier.ToString(),
                peripheral.Name,
                "disconnected"
            );
        }

        public override void FailedToConnectPeripheral(CBCentralManager central, CBPeripheral peripheral, NSError? error)
        {
            _monitor.RecordEvent(new BleEvent
            {
                Type = "connection_failed",
                DeviceId = peripheral.Identifier.ToString(),
                DeviceName = peripheral.Name,
                Data = error?.LocalizedDescription
            });
        }

        private static string? FormatAdvertisementData(NSDictionary? data)
        {
            if (data == null || data.Count == 0) return null;
            var parts = new List<string>();
            foreach (var key in data.Keys)
            {
                var value = data[key];
                if (value is NSData nsData)
                    parts.Add($"{key}={Convert.ToHexString(nsData.ToArray())}");
                else
                    parts.Add($"{key}={value}");
            }
            return string.Join(";", parts);
        }
    }
}
#endif
