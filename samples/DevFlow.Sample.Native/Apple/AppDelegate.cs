using Foundation;
using Microsoft.Maui.DevFlow.Agent.Native;
using UIKit;

namespace DevFlow.Sample.Native;

[Register(nameof(AppDelegate))]
public class AppDelegate : UIApplicationDelegate
{
    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
    {
        // Explicit bootstrap — the agent never starts itself.
        DevFlowAgent.Start(SampleAgentOptions.Create());

        return true;
    }

    public override UISceneConfiguration GetConfiguration(
        UIApplication application,
        UISceneSession connectingSceneSession,
        UISceneConnectionOptions options)
        => new("Default Configuration", connectingSceneSession.Role);
}
