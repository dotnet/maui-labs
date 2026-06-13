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

		readonly Signal<int> _count = new(0);
		readonly Signal<string> _name = new(string.Empty);
		readonly Signal<bool> _fancy = new(false);

		CometDevAgent? _agent;

		public override bool FinishedLaunching(UIApplication application, NSDictionary launchOptions)
		{
			// Comet's fluent env writes post through ThreadHelper; we're on the main thread.
			ThreadHelper.SetFireOnMainThread(a => a?.Invoke());

			Window = new UIWindow(UIScreen.MainScreen.Bounds);

			// In-process dev agent (ailoha/DevFlow model): lets the CLI / an AI agent / curl
			// inspect this Comet tree and drive semantic actions (tap/fill/toggle) over HTTP.
			// Reactive writes must run on the UI thread, so we hand it the main queue. Start it
			// BEFORE materializing so the registry tracks every node as the tree is built.
			_agent = new CometDevAgent(9234, a => DispatchQueue.MainQueue.DispatchAsync(a));
			_agent.Start();

			// A real Comet view tree, rendered as SwiftUI through the node protocol —
			// no MAUI handlers in the render path.
			var backend = new SwiftUIBackendRoot(new EmptyServiceProvider());
			Window.RootViewController = backend.CreateController(BuildUi());

			Window.MakeKeyAndVisible();

			return true;
		}

		// No timer: state changes only via user interaction or the dev agent, so a tap/fill/
		// toggle through the agent is a clean, deterministic verification of the event loop.
		View BuildUi() => new VStack
		{
			new Text("Comet → SwiftUI").Color(Colors.White),

			// Counter — exercises the Button click event path (Swift onTap -> C# -> Signal).
			new Text(() => $"Count: {_count.Value}").Color(Colors.White),
			new Button("Increment", () => _count.Value++),

			// Text input — exercises the TextField write-back path (fill -> Signal -> Text).
			new TextField(_name, "Type a name"),
			new Text(() => $"Hello, {(_name.Value.Length == 0 ? "stranger" : _name.Value)}").Color(Colors.White),

			// Toggle — exercises the bool write-back path.
			new Toggle(_fancy),
			new Text(() => _fancy.Value ? "Fancy: ON" : "Fancy: off").Color(Colors.White),
		}.Background(Color.FromArgb("#6750A4")).Padding(24);

		sealed class EmptyServiceProvider : System.IServiceProvider
		{
			public object? GetService(System.Type serviceType) => null;
		}
	}
}
