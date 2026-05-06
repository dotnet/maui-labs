using CoreFoundation;
using Foundation;
using Microsoft.Maui.Dispatching;

using Microsoft.Maui.Platforms.MacOS.Handlers;

namespace Microsoft.Maui.Platforms.MacOS.Platform;

public class MacOSDispatcher : IDispatcher
{
    public static IDispatcher? GetForCurrentThread()
    {
        if (NSThread.IsMain)
            return new MacOSDispatcher();
        return null;
    }

    public bool IsDispatchRequired => !NSThread.IsMain;

    public IDispatcherTimer CreateTimer()
    {
        return new MacOSDispatcherTimer();
    }

    public bool Dispatch(Action action)
    {
        DispatchQueue.MainQueue.DispatchAsync(action);
        return true;
    }

    public bool DispatchDelayed(TimeSpan delay, Action action)
    {
        DispatchQueue.MainQueue.DispatchAfter(
            new DispatchTime(DispatchTime.Now, delay),
            action);
        return true;
    }
}

public class MacOSDispatcherProvider : IDispatcherProvider
{
    public IDispatcher? GetForCurrentThread() => MacOSDispatcher.GetForCurrentThread();
}
