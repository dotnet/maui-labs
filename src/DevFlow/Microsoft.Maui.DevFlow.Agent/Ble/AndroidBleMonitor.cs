#if ANDROID
using Microsoft.Maui.DevFlow.Agent.Core;

namespace Microsoft.Maui.DevFlow.Agent.Ble;

internal sealed class AndroidBleMonitor : BleMonitor
{
    // Android's BluetoothProfile.GATT value is not exposed as a named ProfileType member.
    private const global::Android.Bluetooth.ProfileType GattProfile = (global::Android.Bluetooth.ProfileType)7;

    private global::Android.Bluetooth.LE.BluetoothLeScanner? _scanner;
    private DevFlowScanCallback? _scanCallback;
    private DevFlowConnectionReceiver? _connectionReceiver;
    private bool _receiverRegistered;

    public AndroidBleMonitor() : base()
    {
        RegisterConnectionReceiver();
        SnapshotConnectedDevices();
    }

    public override bool SupportsScanning => true;

    private void SnapshotConnectedDevices()
    {
        try
        {
            var manager = (global::Android.Bluetooth.BluetoothManager?)
                global::Android.App.Application.Context.GetSystemService(global::Android.Content.Context.BluetoothService);
            if (manager == null) return;

            var devices = manager.GetConnectedDevices(GattProfile);
            if (devices == null) return;

            foreach (var device in devices)
            {
                string? name = null;
                try { name = device.Name; } catch { }
                RecordConnectionStateChanged(device.Address ?? "", name, "connected");
            }
        }
        catch { /* permissions may not be granted */ }
    }

    protected override string? StartPlatformScan()
    {
        var manager = (global::Android.Bluetooth.BluetoothManager?)
            global::Android.App.Application.Context.GetSystemService(global::Android.Content.Context.BluetoothService);
        var adapter = manager?.Adapter;
        if (adapter == null || !adapter.IsEnabled)
            return "Bluetooth is not enabled";

        _scanner = adapter.BluetoothLeScanner;
        if (_scanner == null)
            return "BLE scanner not available";

        _scanCallback = new DevFlowScanCallback(this);
        var settings = new global::Android.Bluetooth.LE.ScanSettings.Builder()
            .SetScanMode(global::Android.Bluetooth.LE.ScanMode.LowLatency)!
            .Build();

        _scanner.StartScan(null, settings, _scanCallback);
        return null;
    }

    protected override void StopPlatformScan()
    {
        if (_scanner != null && _scanCallback != null)
        {
            try { _scanner.StopScan(_scanCallback); }
            catch { /* scanner may already be stopped */ }
        }
        _scanCallback = null;
        _scanner = null;
    }

    protected override void DisposePlatform()
    {
        if (_receiverRegistered && _connectionReceiver != null)
        {
            try
            {
                global::Android.App.Application.Context.UnregisterReceiver(_connectionReceiver);
            }
            catch (global::Java.Lang.IllegalArgumentException)
            {
                // Receiver was already unregistered by Android.
            }
        }

        _receiverRegistered = false;
        _connectionReceiver?.Dispose();
        _connectionReceiver = null;
    }

    private void RegisterConnectionReceiver()
    {
        if (_receiverRegistered) return;
        try
        {
            _connectionReceiver = new DevFlowConnectionReceiver(this);
            var filter = new global::Android.Content.IntentFilter();
            filter.AddAction(global::Android.Bluetooth.BluetoothDevice.ActionAclConnected);
            filter.AddAction(global::Android.Bluetooth.BluetoothDevice.ActionAclDisconnected);
            filter.AddAction(global::Android.Bluetooth.BluetoothDevice.ActionBondStateChanged);

            var context = global::Android.App.Application.Context;
            if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.Tiramisu)
            {
#pragma warning disable CA1416
                context.RegisterReceiver(
                    _connectionReceiver, filter, global::Android.Content.ReceiverFlags.NotExported);
#pragma warning restore CA1416
            }
            else
            {
#pragma warning disable CA1422
                context.RegisterReceiver(_connectionReceiver, filter);
#pragma warning restore CA1422
            }
            _receiverRegistered = true;
        }
        catch { /* permissions may not be granted */ }
    }

    private sealed class DevFlowScanCallback : global::Android.Bluetooth.LE.ScanCallback
    {
        private readonly AndroidBleMonitor _monitor;

        public DevFlowScanCallback(AndroidBleMonitor monitor) => _monitor = monitor;

        public override void OnScanResult(global::Android.Bluetooth.LE.ScanCallbackType callbackType, global::Android.Bluetooth.LE.ScanResult? result)
        {
            if (result?.Device == null) return;

            string? name = null;
            try { name = result.Device.Name; } catch { }

            _monitor.RecordScanResult(
                result.Device.Address ?? "",
                name,
                result.Rssi,
                result.ScanRecord?.GetBytes() is byte[] bytes ? Convert.ToHexString(bytes) : null
            );
        }

        public override void OnBatchScanResults(IList<global::Android.Bluetooth.LE.ScanResult>? results)
        {
            if (results == null) return;
            foreach (var result in results)
                OnScanResult(global::Android.Bluetooth.LE.ScanCallbackType.AllMatches, result);
        }

        public override void OnScanFailed(global::Android.Bluetooth.LE.ScanFailure errorCode)
        {
            _monitor.RecordEvent(new BleEvent
            {
                Type = "scan_failed",
                Data = errorCode.ToString()
            });
        }
    }

    private sealed class DevFlowConnectionReceiver : global::Android.Content.BroadcastReceiver
    {
        private readonly AndroidBleMonitor _monitor;

        public DevFlowConnectionReceiver(AndroidBleMonitor monitor) => _monitor = monitor;

        public override void OnReceive(global::Android.Content.Context? context, global::Android.Content.Intent? intent)
        {
            if (intent == null) return;

#pragma warning disable CA1422
            var device = intent.GetParcelableExtra(global::Android.Bluetooth.BluetoothDevice.ExtraDevice) as global::Android.Bluetooth.BluetoothDevice;
#pragma warning restore CA1422
            if (device == null) return;

            string? name = null;
            try { name = device.Name; } catch { }

            var action = intent.Action;
            if (action == global::Android.Bluetooth.BluetoothDevice.ActionAclConnected)
            {
                _monitor.RecordConnectionStateChanged(device.Address ?? "", name, "connected");
            }
            else if (action == global::Android.Bluetooth.BluetoothDevice.ActionAclDisconnected)
            {
                _monitor.RecordConnectionStateChanged(device.Address ?? "", name, "disconnected");
            }
            else if (action == global::Android.Bluetooth.BluetoothDevice.ActionBondStateChanged)
            {
                var state = (global::Android.Bluetooth.Bond)intent.GetIntExtra(
                    global::Android.Bluetooth.BluetoothDevice.ExtraBondState,
                    (int)global::Android.Bluetooth.Bond.None);
                _monitor.RecordEvent(new BleEvent
                {
                    Type = "bond_state_changed",
                    DeviceId = device.Address ?? "",
                    DeviceName = name,
                    Data = state.ToString()
                });
            }
        }
    }
}
#endif
