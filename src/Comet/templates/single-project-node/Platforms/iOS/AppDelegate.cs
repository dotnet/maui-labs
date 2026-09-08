using Comet;
using Comet.Platform.SwiftUI;
using Foundation;
using UIKit;

namespace CometNodeApp1;

[Register("AppDelegate")]
public class AppDelegate : UIApplicationDelegate
{
	public override UIWindow? Window { get; set; }

	public override bool FinishedLaunching(UIApplication application, NSDictionary launchOptions)
	{
		// Comet's fluent env writes marshal through ThreadHelper; we're on the main thread.
		ThreadHelper.SetFireOnMainThread(a => a?.Invoke());

		Window = new UIWindow(UIScreen.MainScreen.Bounds);

		// topInset clears the notch / Dynamic Island (the node backend doesn't apply
		// safe-area insets yet).
		var backend = new SwiftUIBackendRoot(new App.Services()) { UseYogaLayout = true };
		Window.RootViewController = backend.CreateController(App.Build(topInset: 50));

		Window.MakeKeyAndVisible();
		return true;
	}
}
