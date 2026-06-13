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
		readonly Signal<int> _taps = new(0);

		CometDevAgent? _agent;

		public override bool FinishedLaunching(UIApplication application, NSDictionary launchOptions)
		{
			// Comet's fluent env writes post through ThreadHelper; we're on the main thread.
			ThreadHelper.SetFireOnMainThread(a => a?.Invoke());

			Window = new UIWindow(UIScreen.MainScreen.Bounds);

			// Dev agent on the DevFlow CLI's default port: `maui devflow ui tree/tap` connects
			// straight to localhost:9223 on the iOS sim, so the stock CLI drives this Comet app
			// (it also still answers the simple /tree, /tap, … routes for curl). Start BEFORE
			// materializing so the registry tracks every node as the tree is built.
			_agent = new CometDevAgent(CometDevAgent.DevFlowPort, a => DispatchQueue.MainQueue.DispatchAsync(a));
			_agent.Start();

			// A real Comet view tree, rendered as SwiftUI through the node protocol —
			// no MAUI handlers in the render path.
			var backend = new SwiftUIBackendRoot(new EmptyServiceProvider());
			Window.RootViewController = backend.CreateController(BuildUi());

			Window.MakeKeyAndVisible();
			return true;
		}

		// Clean single-root tree of distinct, addressable controls — good targets for the CLI
		// (`maui devflow ui tap --text Increment`, `ui fill --text … --automation-id name`).
		View BuildUi() => new VStack
		{
			new Text("Comet → SwiftUI").Color(Colors.White),

			new Text(() => $"Count: {_count.Value}").Color(Colors.White),
			new Button("Increment", () => _count.Value++),

			new TextField(_name, "Type a name"),
			new Text(() => $"Hello, {(_name.Value.Length == 0 ? "stranger" : _name.Value)}").Color(Colors.White),

			new Toggle(_fancy),
			new Text(() => _fancy.Value ? "Fancy: ON" : "Fancy: off").Color(Colors.White),

			new Text(() => $"Tapped: {_taps.Value}× (tap me)").Color(Colors.White)
				.OnTap(_ => _taps.Value++),
		}.Background(Color.FromArgb("#6750A4")).Padding(24);

		sealed class EmptyServiceProvider : System.IServiceProvider
		{
			public object? GetService(System.Type serviceType) => null;
		}
	}
}
