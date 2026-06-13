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

		readonly Signal<string> _name = new(string.Empty);
		readonly Signal<bool> _fancy = new(false);
		readonly Signal<double> _vol = new(0.2);

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

			// Drive the bound Signals on a timer to prove the controlled-component read
			// direction (Signal -> SwiftUI control): the Toggle's knob flips and the Slider
			// moves on their own, and the dependent Texts update — all via setBool/setDouble
			// -> @Published -> SwiftUI re-render.
			NSTimer.CreateScheduledTimer(2.0, true, _ =>
			{
				_fancy.Value = !_fancy.Value;
				_vol.Value = (_vol.Value + 0.25) % 1.01;
			});

			return true;
		}

		// A form of two-way controls bound to Signals, rendered as SwiftUI. The dependent
		// Texts reflect each control's bound value (read direction); user edits write back
		// (same cross-platform C# path as Android).
		View BuildUi() => new VStack
		{
			new Text("Comet → SwiftUI"),
			new TextField(_name, "Enter your name"),
			new Text(() => string.IsNullOrEmpty(_name.Value) ? "Hello, stranger" : $"Hello, {_name.Value}!"),
			new HStack
			{
				new Text("Fancy"),
				new Toggle(_fancy),
			},
			new Text(() => _fancy.Value ? "Fancy is ON" : "plain"),
			new Slider(_vol),
			new Text(() => $"Volume: {(int)(_vol.Value * 100)}%"),
		}.Background(Color.FromArgb("#6750A4")).Padding(24);

		sealed class EmptyServiceProvider : System.IServiceProvider
		{
			public object? GetService(System.Type serviceType) => null;
		}
	}
}
