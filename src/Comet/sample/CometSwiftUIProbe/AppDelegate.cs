using Comet;
using Comet.Platform.SwiftUI;
using Comet.Reactive;
using Foundation;
using Microsoft.Maui.Graphics;
using UIKit;

namespace CometSwiftUIProbe
{
	[Register("AppDelegate")]
	public class AppDelegate : UIApplicationDelegate
	{
		public override UIWindow? Window { get; set; }

		readonly Signal<int> _count = new(0);

		public override bool FinishedLaunching(UIApplication application, NSDictionary launchOptions)
		{
			// Comet's fluent env writes post through ThreadHelper; we're on the main thread.
			ThreadHelper.SetFireOnMainThread(a => a?.Invoke());

			Window = new UIWindow(UIScreen.MainScreen.Bounds);

			// A real Comet view tree, rendered as SwiftUI through the node protocol —
			// no MAUI handlers in the render path.
			var backend = new SwiftUIBackendRoot(new EmptyServiceProvider());
			Window.RootViewController = backend.CreateController(BuildUi());

			Window.MakeKeyAndVisible();

			// Drive the Signal on a timer to prove the reactive loop C# -> SwiftUI:
			// each tick writes the Signal, the scheduler flushes, the bound Text re-emits
			// to its SwiftUI node, and SwiftUI re-renders. (The Button does the same on tap.)
			NSTimer.CreateScheduledTimer(1.0, true, _ => _count.Value++);

			return true;
		}

		// Interactive reactive loop on iOS: tapping the SwiftUI Button writes the Signal,
		// the reactive scheduler flushes, and the bound Text re-emits to its SwiftUI node.
		View BuildUi() => new VStack
		{
			new Text("Comet → SwiftUI"),
			new Text(() => $"Count: {_count.Value}"),
			new Button("Increment", () => _count.Value++),
		}.Background(Color.FromArgb("#6750A4")).Padding(24);

		sealed class EmptyServiceProvider : System.IServiceProvider
		{
			public object? GetService(System.Type serviceType) => null;
		}
	}
}
