using System.Collections.Generic;
using System.Linq;
using Comet;
using Comet.Platform.SwiftUI;
using Foundation;
using Microsoft.Maui.Graphics;
using UIKit;

namespace CometSwiftUIProbe
{
	[Register("AppDelegate")]
	public class AppDelegate : UIApplicationDelegate
	{
		public override UIWindow? Window { get; set; }

		NavigationView? _nav;
		int _navTick;

		public override bool FinishedLaunching(UIApplication application, NSDictionary launchOptions)
		{
			// Comet's fluent env writes post through ThreadHelper; we're on the main thread.
			ThreadHelper.SetFireOnMainThread(a => a?.Invoke());

			Window = new UIWindow(UIScreen.MainScreen.Bounds);

			var backend = new SwiftUIBackendRoot(new EmptyServiceProvider());
			Window.RootViewController = backend.CreateController(BuildUi());

			Window.MakeKeyAndVisible();

			// Drive navigation on a timer (no tap tool on the sim): alternately push a Detail
			// screen and pop back, proving Navigate/Pop re-render the SwiftUI nav stack.
			NSTimer.CreateScheduledTimer(2.5, true, _ =>
			{
				if (_navTick++ % 2 == 0)
					_nav!.Navigate(DetailScreen());
				else
					_nav!.Pop();
			});

			return true;
		}

		View BuildUi()
		{
			_nav = new NavigationView();
			_nav.Add(HomeScreen());
			return _nav;
		}

		static View HomeScreen() => new VStack
		{
			new Text("🏠  Home").Color(Colors.White),
			new Text("Auto-navigating every 2.5s…").Color(Colors.White),
		}.Background(Color.FromArgb("#6750A4")).Padding(28);

		static View DetailScreen() => new VStack
		{
			new Text("📄  Detail").Color(Colors.White),
			new Text("Pushed via Navigate(), pops back").Color(Colors.White),
		}.Background(Color.FromArgb("#7D5260")).Padding(28);

		sealed class EmptyServiceProvider : System.IServiceProvider
		{
			public object? GetService(System.Type serviceType) => null;
		}
	}
}
