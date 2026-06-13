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

		static readonly string[] Frameworks =
			{ "Jetpack Compose", "SwiftUI", "WinUI 3", "GTK 4", "Qt Quick", "Flutter", "React Native", "AppKit", "Avalonia", "Uno Platform" };

		public override bool FinishedLaunching(UIApplication application, NSDictionary launchOptions)
		{
			// Comet's fluent env writes post through ThreadHelper; we're on the main thread.
			ThreadHelper.SetFireOnMainThread(a => a?.Invoke());

			Window = new UIWindow(UIScreen.MainScreen.Bounds);

			var backend = new SwiftUIBackendRoot(new EmptyServiceProvider());
			Window.RootViewController = backend.CreateController(BuildUi());

			Window.MakeKeyAndVisible();
			return true;
		}

		// A Comet ListView rendered as a native SwiftUI List, each row a styled template.
		View BuildUi() =>
			new ListView<string>(() => Frameworks.ToList())
			{
				ViewFor = item => new VStack
				{
					new Text(item),
					new Text("a UI framework").Color(Colors.Gray),
				},
			};

		sealed class EmptyServiceProvider : System.IServiceProvider
		{
			public object? GetService(System.Type serviceType) => null;
		}
	}
}
