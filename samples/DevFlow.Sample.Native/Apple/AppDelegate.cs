using DevFlow.Sample.Native.Apple;
using Foundation;
using Microsoft.Maui.DevFlow.Agent.Native;
using UIKit;

namespace DevFlow.Sample.Native;

[Register(nameof(AppDelegate))]
public class AppDelegate : UIApplicationDelegate
{
    public override UIWindow? Window { get; set; }

    public override bool FinishedLaunching(UIApplication application, NSDictionary launchOptions)
    {
        // Explicit bootstrap — the agent never starts itself.
        DevFlowAgent.Start();

        Window = new UIWindow(UIScreen.MainScreen.Bounds)
        {
            RootViewController = new UINavigationController(new SampleViewController()),
        };
        Window.MakeKeyAndVisible();

        return true;
    }
}
