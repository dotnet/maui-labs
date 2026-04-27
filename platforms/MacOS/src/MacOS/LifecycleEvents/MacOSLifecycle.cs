using Foundation;

using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.LifecycleEvents;

public static class MacOSLifecycle
{
    public delegate void DidFinishLaunching(NSNotification notification);
    public delegate void DidBecomeActive(NSNotification notification);
    public delegate void DidResignActive(NSNotification notification);
    public delegate void DidHide(NSNotification notification);
    public delegate void DidUnhide(NSNotification notification);
    public delegate void WillTerminate(NSNotification notification);
}
