using Foundation;
using Microsoft.Maui.Dispatching;

using Microsoft.Maui.Platforms.MacOS.Handlers;

namespace Microsoft.Maui.Platforms.MacOS.Platform;

public class MacOSDispatcherTimer : IDispatcherTimer
{
    NSTimer? _timer;
    TimeSpan _interval = TimeSpan.FromMilliseconds(16);

    public TimeSpan Interval
    {
        get => _interval;
        set => _interval = value;
    }

    public bool IsRepeating { get; set; } = true;

    public bool IsRunning { get; private set; }

    public event EventHandler? Tick;

    public void Start()
    {
        if (IsRunning)
            return;

        IsRunning = true;
        _timer = NSTimer.CreateRepeatingScheduledTimer(
            _interval,
            t =>
            {
                Tick?.Invoke(this, EventArgs.Empty);
                if (!IsRepeating)
                    Stop();
            });
    }

    public void Stop()
    {
        if (!IsRunning)
            return;

        IsRunning = false;
        _timer?.Invalidate();
        _timer = null;
    }
}
