using Foundation;
using UIKit;

namespace DevFlow.Sample;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

	public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
	{
		// BGTask handlers must be registered before didFinishLaunchingWithOptions returns,
		// so this has to happen ahead of the base call.
		try
		{
			SampleBackgroundTask.Register();
		}
		catch (Exception ex)
		{
			Console.WriteLine($"[DevFlow.Sample] BGTask registration failed: {ex.Message}");
		}

		var result = base.FinishedLaunching(application, launchOptions);

		try
		{
			Console.WriteLine($"[DevFlow.Sample] BGTask schedule: {SampleBackgroundTask.Schedule()}");
		}
		catch (Exception ex)
		{
			Console.WriteLine($"[DevFlow.Sample] BGTask schedule failed: {ex.Message}");
		}

		return result;
	}
}
