using Foundation;
using UIKit;

namespace CometBaristaNotes;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

	public override bool FinishedLaunching(UIApplication application, NSDictionary launchOptions)
	{
		var result = base.FinishedLaunching(application, launchOptions);

		// Set window background immediately so the status bar / Dynamic Island
		// area shows tan instead of black. Must happen AFTER base creates the window.
		var bgColor = new UIColor(0.824f, 0.737f, 0.647f, 1.0f); // #D2BCA5

		// Use modern scene-based API to find the window
		UIWindow window = null;
		foreach (var scene in application.ConnectedScenes)
		{
			if (scene is UIWindowScene ws)
			{
				window = ws.KeyWindow;
				if (window == null && ws.Windows.Length > 0)
					window = ws.Windows[0];
				break;
			}
		}

		if (window != null)
		{
			window.BackgroundColor = bgColor;
			if (window.RootViewController?.View != null)
				window.RootViewController.View.BackgroundColor = bgColor;
		}

		return result;
	}
}
