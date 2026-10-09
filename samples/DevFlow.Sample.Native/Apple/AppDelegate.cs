using DevFlow.Sample.Native.Apple;
using Foundation;
using Microsoft.Maui.DevFlow.Agent.Native;
using UIKit;

namespace DevFlow.Sample.Native;

[Register(nameof(AppDelegate))]
public class AppDelegate : UIApplicationDelegate
{
    public override UIWindow? Window { get; set; }

    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
    {
        // Explicit bootstrap — the agent never starts itself.
        DevFlowAgent.Start(SampleAgentOptions.Create());

        // This sample deliberately stays on the classic, scene-less UIApplicationDelegate
        // lifecycle: it exists to prove the agent works in the plainest possible .NET app, and
        // the agent's root discovery is expected to cope with an app that never adopts UIScene.
        // The frame-based UIWindow constructor is the one that goes with that lifecycle; its
        // replacement takes a UIWindowScene, which by definition does not exist here.
#pragma warning disable CA1422
        Window = new UIWindow(UIScreen.MainScreen.Bounds)
        {
            RootViewController = new UINavigationController(new SampleViewController()),
        };
#pragma warning restore CA1422
        Window.MakeKeyAndVisible();

        return true;
    }
}
