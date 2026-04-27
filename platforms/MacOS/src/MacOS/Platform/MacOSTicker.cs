using Foundation;
using Microsoft.Maui.Animations;

using Microsoft.Maui.Platforms.MacOS.Handlers;

namespace Microsoft.Maui.Platforms.MacOS.Platform;

/// <summary>
/// macOS-specific animation ticker that uses NSTimer on the main run loop.
/// The default System.Timers.Timer-based Ticker fires on threadpool threads,
/// which is unsafe for AppKit — NSView properties must be updated on the main thread.
/// </summary>
public class MacOSTicker : Ticker
{
    NSTimer? _timer;

    public override bool IsRunning => _timer != null;

    public override void Start()
    {
        if (_timer != null)
            return;

        var interval = 1.0 / MaxFps;
        _timer = NSTimer.CreateRepeatingScheduledTimer(interval, _ => Fire?.Invoke());
        NSRunLoop.Main.AddTimer(_timer, NSRunLoopMode.Common);
    }

    public override void Stop()
    {
        if (_timer == null)
            return;

        _timer.Invalidate();
        _timer.Dispose();
        _timer = null;
    }
}
