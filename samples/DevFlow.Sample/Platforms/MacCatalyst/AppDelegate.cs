using Foundation;

namespace DevFlow.Sample;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    public override bool FinishedLaunching(UIKit.UIApplication application, NSDictionary? launchOptions)
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DEVFLOW_TEST_PORT")))
        {
            NSProcessInfo.ProcessInfo.DisableAutomaticTermination(
                "DevFlow integration tests require the sample process to remain available.");
        }

        return base.FinishedLaunching(application, launchOptions);
    }
}
