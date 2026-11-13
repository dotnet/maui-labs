using Foundation;
using System.Runtime.InteropServices;

namespace DevFlow.Sample;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    [DllImport(ObjCRuntime.Constants.ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    static extern void SendMessage(IntPtr receiver, IntPtr selector, IntPtr argument);

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    public override bool FinishedLaunching(UIKit.UIApplication application, NSDictionary? launchOptions)
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DEVFLOW_TEST_PORT")))
        {
            using var reason = new NSString(
                "DevFlow integration tests require the sample process to remain available.");
            SendMessage(
                NSProcessInfo.ProcessInfo.Handle,
                ObjCRuntime.Selector.GetHandle("disableAutomaticTermination:"),
                reason.Handle);
        }

        return base.FinishedLaunching(application, launchOptions);
    }
}
