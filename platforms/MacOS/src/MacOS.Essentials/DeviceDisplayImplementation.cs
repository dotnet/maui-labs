using System.Runtime.InteropServices;
using AppKit;
using CoreFoundation;
using CoreGraphics;
using Foundation;
using ObjCRuntime;
using Microsoft.Maui.Devices;

namespace Microsoft.Maui.Platforms.MacOS.Essentials;

class DeviceDisplayImplementation : IDeviceDisplay
{
    NSObject? _observer;
    event EventHandler<DisplayInfoChangedEventArgs>? _mainDisplayInfoChanged;
    DisplayInfo _currentMetrics;

    public bool KeepScreenOn
    {
        get => _keepScreenOnAssertionId != 0;
        set
        {
            if (value == KeepScreenOn) return;
            if (value)
                PreventIdleSleep();
            else
                AllowIdleSleep();
        }
    }

    public DisplayInfo MainDisplayInfo => GetMainDisplayInfo();

    public event EventHandler<DisplayInfoChangedEventArgs> MainDisplayInfoChanged
    {
        add
        {
            if (_mainDisplayInfoChanged is null)
            {
                _currentMetrics = MainDisplayInfo;
                StartListeners();
            }
            _mainDisplayInfoChanged += value;
        }
        remove
        {
            _mainDisplayInfoChanged -= value;
            if (_mainDisplayInfoChanged is null)
                StopListeners();
        }
    }

    DisplayInfo GetMainDisplayInfo()
    {
        var screen = NSScreen.MainScreen;
        if (screen is null)
            return new DisplayInfo(0, 0, 1, DisplayOrientation.Landscape, DisplayRotation.Rotation0, 0);

        var frame = screen.Frame;
        var scale = screen.BackingScaleFactor;
        var refreshRate = (float)GetRefreshRate();

        return new DisplayInfo(
            width: frame.Width * scale,
            height: frame.Height * scale,
            density: scale,
            orientation: frame.Width >= frame.Height ? DisplayOrientation.Landscape : DisplayOrientation.Portrait,
            rotation: DisplayRotation.Rotation0,
            rate: refreshRate);
    }

    void StartListeners()
    {
        _observer = NSNotificationCenter.DefaultCenter.AddObserver(
            NSApplication.DidChangeScreenParametersNotification,
            OnDisplayInfoChanged);
    }

    void StopListeners()
    {
        _observer?.Dispose();
        _observer = null;
    }

    void OnDisplayInfoChanged(NSNotification notification)
    {
        var info = MainDisplayInfo;
        if (!_currentMetrics.Equals(info))
        {
            _currentMetrics = info;
            _mainDisplayInfoChanged?.Invoke(null, new DisplayInfoChangedEventArgs(info));
        }
    }

    // Keep screen on via IOKit assertions
    uint _keepScreenOnAssertionId;

    void PreventIdleSleep()
    {
        var result = IOPMAssertionCreateWithName(
            CFString.CreateNative("PreventUserIdleDisplaySleep"),
            255, // kIOPMAssertionLevelOn
            CFString.CreateNative("MAUI KeepScreenOn"),
            out var id);
        if (result == 0)
            _keepScreenOnAssertionId = id;
    }

    void AllowIdleSleep()
    {
        if (_keepScreenOnAssertionId != 0)
        {
            IOPMAssertionRelease(_keepScreenOnAssertionId);
            _keepScreenOnAssertionId = 0;
        }
    }

    static double GetRefreshRate()
    {
        var displayId = CGMainDisplayID();
        var mode = CGDisplayCopyDisplayMode(displayId);
        if (mode == IntPtr.Zero) return 0;
        var rate = CGDisplayModeGetRefreshRate(mode);
        CGDisplayModeRelease(mode);
        return rate;
    }

    const string IOKitLibrary = "/System/Library/Frameworks/IOKit.framework/IOKit";

    [DllImport(IOKitLibrary)]
    static extern uint IOPMAssertionCreateWithName(IntPtr type, uint level, IntPtr name, out uint id);

    [DllImport(IOKitLibrary)]
    static extern uint IOPMAssertionRelease(uint id);

    [DllImport(Constants.CoreGraphicsLibrary)]
    static extern uint CGMainDisplayID();

    [DllImport(Constants.CoreGraphicsLibrary)]
    static extern IntPtr CGDisplayCopyDisplayMode(uint display);

    [DllImport(Constants.CoreGraphicsLibrary)]
    static extern void CGDisplayModeRelease(IntPtr mode);

    [DllImport(Constants.CoreGraphicsLibrary)]
    static extern double CGDisplayModeGetRefreshRate(IntPtr mode);
}
