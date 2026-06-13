using System.Collections.Generic;
using System.Linq;
using Comet;
using Comet.DevTools;
using Comet.Platform.SwiftUI;
using Comet.Reactive;
using CoreFoundation;
using Foundation;
using Microsoft.Maui.Graphics;
using UIKit;

namespace CometSwiftUIProbe
{
	[Register("AppDelegate")]
	public class AppDelegate : UIApplicationDelegate
	{
		public override UIWindow? Window { get; set; }

		static readonly string[] Planets =
			{ "Mercury", "Venus", "Earth", "Mars", "Jupiter", "Saturn", "Uranus", "Neptune" };

		NavigationView? _nav;
		CometDevAgent? _agent;

		public override bool FinishedLaunching(UIApplication application, NSDictionary launchOptions)
		{
			// Comet's fluent env writes post through ThreadHelper; we're on the main thread.
			ThreadHelper.SetFireOnMainThread(a => a?.Invoke());

			Window = new UIWindow(UIScreen.MainScreen.Bounds);

			// In-process dev agent (ailoha/DevFlow model): inspect this Comet tree and drive
			// semantic actions over HTTP. Start BEFORE materializing so it tracks every node.
			_agent = new CometDevAgent(9234, a => DispatchQueue.MainQueue.DispatchAsync(a));
			_agent.Start();

			// A real Comet view tree, rendered as SwiftUI through the node protocol —
			// no MAUI handlers in the render path.
			var backend = new SwiftUIBackendRoot(new EmptyServiceProvider());
			Window.RootViewController = backend.CreateController(BuildUi());

			Window.MakeKeyAndVisible();
			return true;
		}

		// Master-detail capstone: a virtualized List of rows; tapping a row (an arbitrary-view
		// tap gesture, not a Button) navigates to a Detail screen. Detail carries a reactive
		// counter (Button click loop) and a Back button (Pop). Exercises list + tap gesture +
		// navigation + reactivity together — the iOS counterpart of the Android capstone.
		View BuildUi()
		{
			_nav = new NavigationView();
			_nav.Add(MasterScreen());
			return _nav;
		}

		View MasterScreen() =>
			new ListView<string>(() => (IReadOnlyList<string>)Planets.ToList())
			{
				ViewFor = planet => new Text($"🪐  {planet}")
					.Color(Colors.White)
					.OnTap(_ => _nav!.Navigate(DetailScreen(planet))),
			};

		View DetailScreen(string planet)
		{
			var count = new Signal<int>(0);
			return new VStack
			{
				new Text($"📄  {planet}").Color(Colors.White),
				new Text(() => $"Taps: {count.Value}").Color(Colors.White),
				new Button("Increment", () => count.Value++),
				new Button("← Back", () => _nav!.Pop()),
			}.Background(Color.FromArgb("#7D5260")).Padding(28);
		}

		sealed class EmptyServiceProvider : System.IServiceProvider
		{
			public object? GetService(System.Type serviceType) => null;
		}
	}
}
