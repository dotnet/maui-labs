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
		readonly Signal<bool> _long = new(false);

		NavigationView? _nav;
		CometDevAgent? _agent;

		public override bool FinishedLaunching(UIApplication application, NSDictionary launchOptions)
		{
			// Comet's fluent env writes post through ThreadHelper; we're on the main thread.
			ThreadHelper.SetFireOnMainThread(a => a?.Invoke());

			Window = new UIWindow(UIScreen.MainScreen.Bounds);

			// Dev agent on the DevFlow CLI's default port: `maui devflow ui tree/tap` connects
			// straight to localhost:9223 on the iOS sim, so the stock CLI drives this Comet app.
			// Start BEFORE materializing so the registry tracks every node as the tree is built.
			_agent = new CometDevAgent(CometDevAgent.DevFlowPort, a => DispatchQueue.MainQueue.DispatchAsync(a));
			_agent.Start();

			// A real Comet view tree, rendered as SwiftUI through the node protocol —
			// no MAUI handlers in the render path.
			var backend = new SwiftUIBackendRoot(new EmptyServiceProvider()) { UseYogaLayout = true };
			Window.RootViewController = backend.CreateController(BuildUi());

			Window.MakeKeyAndVisible();
			return true;
		}

		// Direct VStack root (no NavigationView, which is own-content and stops the layout
		// engine) so the Yoga engine lays this out under UseYogaLayout. The 24pt Padding should
		// inset from the screen edges; tapping Toggle grows the middle text's height, and the
		// rows below must reflow downward (proving relayout-on-reactive-change).
		View BuildUi() => new VStack
		{
			new Text("Reflow + padding").Color(Colors.White),
			new Button("Toggle", () => _long.Value = !_long.Value).AutomationId("toggleBtn"),
			new Text(() => _long.Value
				? "This is a long paragraph that wraps onto several lines, so its height grows when toggled and the rows beneath it are pushed down by Yoga."
				: "short").Color(Colors.White),
			new Text("── below ──").Color(Colors.White),
			new Text("row 2").Color(Colors.White),
		}.Background(Color.FromArgb("#6750A4")).Padding(24);

		// Interactive controls with AutomationIds (clean `--automationId` selector targets) plus
		// a Navigate button, so the dev tree pruning across push/pop is observable.
		View HomeScreen() => new VStack
		{
			new Text("Comet → SwiftUI").Color(Colors.White),

			new Text(() => $"Count: {_count.Value}").Color(Colors.White).AutomationId("countLabel"),
			new Button("Increment", () => _count.Value++).AutomationId("incrementButton"),

			new TextField(_name, "Type a name").AutomationId("nameField"),
			new Text(() => $"Hello, {(_name.Value.Length == 0 ? "stranger" : _name.Value)}").Color(Colors.White),

			new Toggle(_fancy).AutomationId("fancyToggle"),
			new Text(() => _fancy.Value ? "Fancy: ON" : "Fancy: off").Color(Colors.White),

			new Text(() => $"Tapped: {_taps.Value}× (tap me)").Color(Colors.White)
				.OnTap(_ => _taps.Value++).AutomationId("tapTarget"),

			new Button("Go to Page 2 →", () => _nav!.Navigate(SecondScreen())).AutomationId("gotoPage2"),
		}.Background(Color.FromArgb("#6750A4")).Padding(24);

		View SecondScreen() => new VStack
		{
			new Text("📄  Page 2").Color(Colors.White),
			new Text("Pushed via Navigate()").Color(Colors.White),
			new Button("← Back", () => _nav!.Pop()).AutomationId("backButton"),
		}.Background(Color.FromArgb("#7D5260")).Padding(28);

		sealed class EmptyServiceProvider : System.IServiceProvider
		{
			public object? GetService(System.Type serviceType) => null;
		}
	}
}
