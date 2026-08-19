using Foundation;
using UIKit;

namespace Microsoft.Maui.Testing;

public abstract class MauiTestAppDelegate : UIApplicationDelegate
{
    private MauiTestApp? _application;

    protected abstract MauiTestApp CreateMauiTestApp();

    public override bool FinishedLaunching(
        UIApplication application,
        NSDictionary? launchOptions)
    {
        _application ??= CreateMauiTestApp();
        MauiTestAppleHost.Run(_application);
        return true;
    }
}
