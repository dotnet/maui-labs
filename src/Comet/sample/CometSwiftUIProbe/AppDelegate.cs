using Comet.SwiftUI.Interop;
using Foundation;
using UIKit;

namespace CometSwiftUIProbe
{
	[Register("AppDelegate")]
	public class AppDelegate : UIApplicationDelegate
	{
		public override UIWindow? Window { get; set; }

		public override bool FinishedLaunching(UIApplication application, NSDictionary launchOptions)
		{
			Window = new UIWindow(UIScreen.MainScreen.Bounds);

			// The SwiftUI view, hosted via the Swift @objc shim, driven from C#.
			Window.RootViewController = CometSwiftUIHost.MakeHostController("Hello from C# → SwiftUI");

			Window.MakeKeyAndVisible();
			return true;
		}
	}
}
