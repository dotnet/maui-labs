using Microsoft.Maui.Platforms.MacOS.Platform;

namespace Microsoft.Maui.LifecycleEvents;

public static class MacOSLifecycleBuilderExtensions
{
    public static IMacOSLifecycleBuilder DidFinishLaunching(this IMacOSLifecycleBuilder lifecycle, MacOSLifecycle.DidFinishLaunching del) { lifecycle.AddEvent(nameof(MacOSLifecycle.DidFinishLaunching), del); return lifecycle; }
    public static IMacOSLifecycleBuilder DidBecomeActive(this IMacOSLifecycleBuilder lifecycle, MacOSLifecycle.DidBecomeActive del) { lifecycle.AddEvent(nameof(MacOSLifecycle.DidBecomeActive), del); return lifecycle; }
    public static IMacOSLifecycleBuilder DidResignActive(this IMacOSLifecycleBuilder lifecycle, MacOSLifecycle.DidResignActive del) { lifecycle.AddEvent(nameof(MacOSLifecycle.DidResignActive), del); return lifecycle; }
    public static IMacOSLifecycleBuilder DidHide(this IMacOSLifecycleBuilder lifecycle, MacOSLifecycle.DidHide del) { lifecycle.AddEvent(nameof(MacOSLifecycle.DidHide), del); return lifecycle; }
    public static IMacOSLifecycleBuilder DidUnhide(this IMacOSLifecycleBuilder lifecycle, MacOSLifecycle.DidUnhide del) { lifecycle.AddEvent(nameof(MacOSLifecycle.DidUnhide), del); return lifecycle; }
    public static IMacOSLifecycleBuilder WillTerminate(this IMacOSLifecycleBuilder lifecycle, MacOSLifecycle.WillTerminate del) { lifecycle.AddEvent(nameof(MacOSLifecycle.WillTerminate), del); return lifecycle; }
}
