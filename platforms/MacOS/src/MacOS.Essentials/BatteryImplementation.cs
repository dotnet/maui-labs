using System.Runtime.InteropServices;
using CoreFoundation;
using Foundation;
using ObjCRuntime;
using Microsoft.Maui.Devices;

namespace Microsoft.Maui.Platforms.MacOS.Essentials;

class BatteryImplementation : IBattery
{
    const string IOKitLibrary = "/System/Library/Frameworks/IOKit.framework/IOKit";
    const string kIOPMACPowerKey = "AC Power";
    const string kIOPMUPSPowerKey = "UPS Power";
    const string kIOPMBatteryPowerKey = "Battery Power";
    const string kIOPSCurrentCapacityKey = "Current Capacity";
    const string kIOPSMaxCapacityKey = "Max Capacity";
    const string kIOPSTypeKey = "Type";
    const string kIOPSInternalBatteryType = "InternalBattery";
    const string kIOPSIsPresentKey = "Is Present";
    const string kIOPSIsChargingKey = "Is Charging";
    const string kIOPSIsChargedKey = "Is Charged";
    const string kIOPSIsFinishingChargeKey = "Is Finishing Charge";

    IntPtr _powerSourceNotificationRef;
    bool _listening;
    IOPowerSourceCallback? _callbackDelegate; // prevent GC collection
    event EventHandler<BatteryInfoChangedEventArgs>? _batteryInfoChanged;
    event EventHandler<EnergySaverStatusChangedEventArgs>? _energySaverStatusChanged;

    double _currentLevel;
    BatteryPowerSource _currentSource;
    BatteryState _currentState;

    public double ChargeLevel => GetInternalBatteryChargeLevel();

    public BatteryState State => GetInternalBatteryState();

    public BatteryPowerSource PowerSource => GetProvidingPowerSource();

    public EnergySaverStatus EnergySaverStatus => EnergySaverStatus.Off;

    public event EventHandler<BatteryInfoChangedEventArgs> BatteryInfoChanged
    {
        add
        {
            if (_batteryInfoChanged is null)
            {
                _currentLevel = ChargeLevel;
                _currentSource = PowerSource;
                _currentState = State;
                StartBatteryListeners();
            }
            _batteryInfoChanged += value;
        }
        remove
        {
            _batteryInfoChanged -= value;
            if (_batteryInfoChanged is null)
                StopBatteryListeners();
        }
    }

    public event EventHandler<EnergySaverStatusChangedEventArgs> EnergySaverStatusChanged
    {
        add => _energySaverStatusChanged += value;
        remove => _energySaverStatusChanged -= value;
    }

    void StartBatteryListeners()
    {
        if (_listening) return;
        _callbackDelegate = PowerSourceCallback;
        var powerSourceNotificationRef = IOPSNotificationCreateRunLoopSource(_callbackDelegate, IntPtr.Zero);
        if (powerSourceNotificationRef != IntPtr.Zero)
        {
            try
            {
                CFRunLoopAddSource(CFRunLoopGetMain(), powerSourceNotificationRef, GetDefaultRunLoopMode());
                _powerSourceNotificationRef = powerSourceNotificationRef;
                _listening = true;
            }
            catch
            {
                CFRelease(powerSourceNotificationRef);
                _callbackDelegate = null;
                throw;
            }
        }
    }

    void StopBatteryListeners()
    {
        if (!_listening || _powerSourceNotificationRef == IntPtr.Zero) return;
        CFRunLoopRemoveSource(CFRunLoopGetMain(), _powerSourceNotificationRef, GetDefaultRunLoopMode());
        CFRelease(_powerSourceNotificationRef);
        _powerSourceNotificationRef = IntPtr.Zero;
        _callbackDelegate = null;
        _listening = false;
    }

    void PowerSourceCallback(IntPtr context)
    {
        var level = ChargeLevel;
        var state = State;
        var source = PowerSource;

        if (_currentLevel != level || _currentState != state || _currentSource != source)
        {
            _currentLevel = level;
            _currentState = state;
            _currentSource = source;
            var args = new BatteryInfoChangedEventArgs(level, state, source);
            DispatchQueue.MainQueue.DispatchAsync(() => _batteryInfoChanged?.Invoke(null, args));
        }
    }

    static bool TryGet<T>(NSDictionary dic, string key, out T? value) where T : NSObject
    {
        if (dic.TryGetValue((NSString)key, out var obj) && obj is T val)
        {
            value = val;
            return true;
        }
        value = default;
        return false;
    }

    BatteryState GetInternalBatteryState()
    {
        var infoHandle = IntPtr.Zero;
        var sourcesRef = IntPtr.Zero;
        try
        {
            var hasBattery = false;
            var fullyCharged = true;

            infoHandle = IOPSCopyPowerSourcesInfo();
            sourcesRef = IOPSCopyPowerSourcesList(infoHandle);
            var sources = NSArray.ArrayFromHandle<NSObject>(sourcesRef);
            foreach (var source in sources)
            {
                var dicRef = IOPSGetPowerSourceDescription(infoHandle, source.Handle);
                var dic = Runtime.GetNSObject<NSDictionary>(dicRef, false);
                if (dic is null) continue;

                if (TryGet<NSString>(dic, kIOPSTypeKey, out var type) && type == kIOPSInternalBatteryType &&
                    TryGet<NSNumber>(dic, kIOPSIsPresentKey, out var present) && present?.BoolValue == true)
                {
                    hasBattery = true;

                    if (TryGet<NSNumber>(dic, kIOPSIsChargingKey, out var charging) && charging?.BoolValue == true)
                        return BatteryState.Charging;

                    if ((!TryGet<NSNumber>(dic, kIOPSIsChargedKey, out var charged) || charged?.BoolValue != true) ||
                        (!TryGet<NSNumber>(dic, kIOPSIsFinishingChargeKey, out var finishing) && finishing?.BoolValue != true))
                        fullyCharged = false;
                }
            }

            if (!hasBattery)
                return BatteryState.NotPresent;

            if (fullyCharged)
                return BatteryState.Full;

            var typeHandle = IOPSGetProvidingPowerSourceType(infoHandle);
#pragma warning disable CS0618
            if (NSString.FromHandle(typeHandle) == kIOPMBatteryPowerKey)
#pragma warning restore CS0618
                return BatteryState.Discharging;

            return BatteryState.NotCharging;
        }
        finally
        {
            if (infoHandle != IntPtr.Zero) CFRelease(infoHandle);
            if (sourcesRef != IntPtr.Zero) CFRelease(sourcesRef);
        }
    }

    double GetInternalBatteryChargeLevel()
    {
        var infoHandle = IntPtr.Zero;
        var sourcesRef = IntPtr.Zero;
        try
        {
            var totalCurrent = 0.0;
            var totalMax = 0.0;

            infoHandle = IOPSCopyPowerSourcesInfo();
            sourcesRef = IOPSCopyPowerSourcesList(infoHandle);
            var sources = NSArray.ArrayFromHandle<NSObject>(sourcesRef);
            foreach (var source in sources)
            {
                var dicRef = IOPSGetPowerSourceDescription(infoHandle, source.Handle);
                var dic = Runtime.GetNSObject<NSDictionary>(dicRef, false);
                if (dic is null) continue;

                if (TryGet<NSString>(dic, kIOPSTypeKey, out var type) && type == kIOPSInternalBatteryType &&
                    TryGet<NSNumber>(dic, kIOPSIsPresentKey, out var present) && present?.BoolValue == true &&
                    TryGet<NSNumber>(dic, kIOPSCurrentCapacityKey, out var current) && current?.Int32Value > 0 &&
                    TryGet<NSNumber>(dic, kIOPSMaxCapacityKey, out var max) && max?.Int32Value > 0)
                {
                    totalCurrent += current.Int32Value;
                    totalMax += max.Int32Value;
                }
            }

            if (totalMax <= 0) return 1.0;
            return totalCurrent / totalMax;
        }
        finally
        {
            if (infoHandle != IntPtr.Zero) CFRelease(infoHandle);
            if (sourcesRef != IntPtr.Zero) CFRelease(sourcesRef);
        }
    }

    BatteryPowerSource GetProvidingPowerSource()
    {
        var infoHandle = IntPtr.Zero;
        try
        {
            infoHandle = IOPSCopyPowerSourcesInfo();
            var typeHandle = IOPSGetProvidingPowerSourceType(infoHandle);
#pragma warning disable CS0618
            return NSString.FromHandle(typeHandle) switch
#pragma warning restore CS0618
            {
                kIOPMBatteryPowerKey => BatteryPowerSource.Battery,
                kIOPMACPowerKey or kIOPMUPSPowerKey => BatteryPowerSource.AC,
                _ => BatteryPowerSource.Unknown
            };
        }
        finally
        {
            if (infoHandle != IntPtr.Zero) CFRelease(infoHandle);
        }
    }

    // IOKit P/Invoke
    [DllImport(IOKitLibrary)]
    static extern IntPtr IOPSCopyPowerSourcesInfo();

    [DllImport(IOKitLibrary)]
    static extern IntPtr IOPSGetProvidingPowerSourceType(IntPtr snapshot);

    [DllImport(IOKitLibrary)]
    static extern IntPtr IOPSCopyPowerSourcesList(IntPtr blob);

    [DllImport(IOKitLibrary)]
    static extern IntPtr IOPSGetPowerSourceDescription(IntPtr blob, IntPtr ps);

    [DllImport(IOKitLibrary)]
    static extern IntPtr IOPSNotificationCreateRunLoopSource(IOPowerSourceCallback callback, IntPtr context);

    [DllImport(Constants.CoreFoundationLibrary)]
    static extern void CFRelease(IntPtr obj);

    [DllImport(Constants.CoreFoundationLibrary)]
    static extern IntPtr CFRunLoopGetMain();

    [DllImport(Constants.CoreFoundationLibrary)]
    static extern void CFRunLoopAddSource(IntPtr rl, IntPtr source, IntPtr mode);

    [DllImport(Constants.CoreFoundationLibrary)]
    static extern void CFRunLoopRemoveSource(IntPtr rl, IntPtr source, IntPtr mode);

    [DllImport(Constants.CoreFoundationLibrary)]
    static extern IntPtr CFRunLoopMode_kCFRunLoopDefaultMode();

    static IntPtr GetDefaultRunLoopMode() => CFRunLoop.ModeDefault.Handle;

    delegate void IOPowerSourceCallback(IntPtr context);
}
