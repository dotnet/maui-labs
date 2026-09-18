using Comet;
using Comet.Backend;
using Comet.DevTools;
using Comet.Platform.SwiftUI;
using Comet.Reactive;
using CometSamples.BaristaNotes.Components;
using CoreFoundation;
using Foundation;
using Microsoft.Maui.Graphics;
using CometBaristaNotes.Services.Voice;
using CometSwiftUIProbe.BaristaNotes;
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
		readonly Signal<int> _len = new(0);

		static readonly string[] Lengths =
		{
			"one line",
			"a slightly longer line that may wrap once on a phone width here",
			"three lines worth of text here that should wrap onto roughly three lines on a typical phone width so we can watch the stack below it move",
			"a much longer paragraph designed to wrap onto five or six lines so that the rows beneath it are pushed substantially further down the screen, proving the whole vertical stack expands and contracts as the text length changes rather than the text just growing inside a fixed slot",
		};

		NavigationView? _nav;
		CometDevAgent? _agent;
		BaristaNotesPhotoService? _baristaPhotos;

		public override bool FinishedLaunching(UIApplication application, NSDictionary launchOptions)
		{
			ThreadHelper.SetFireOnMainThread(a =>
			{
				if (NSThread.IsMain)
					a();
				else
					DispatchQueue.MainQueue.DispatchAsync(a);
			});

			Window = new UIWindow(UIScreen.MainScreen.Bounds);

			if (Screen == "baristanotes")
				BaristaVoiceIntegration.ConfigurePlatform(new IosSpeechRecognitionAdapter());

			if (Screen == "baristanotes")
			{
				_baristaPhotos = new BaristaNotesPhotoService(() => Window);
				ProfilePhotoMedia.CaptureFromCamera = _baristaPhotos.CapturePhotoAsync;
				ProfilePhotoMedia.PickFromLibrary = _baristaPhotos.PickPhotoAsync;
				CometBaristaNotes.Services.BaristaPlatformServices.Configure(
					_baristaPhotos,
					CometBaristaNotes.Services.AzureOpenAiVisionAnalyzer.FromEnvironment());
			}

			// Dev agent on the DevFlow CLI's default port: `maui devflow ui tree/tap` connects
			// straight to localhost:9223 on the iOS sim, so the stock CLI drives this Comet app.
			// Start BEFORE materializing so the registry tracks every node as the tree is built.
			_agent = new CometDevAgent(CometDevAgent.DevFlowPort, a => DispatchQueue.MainQueue.DispatchAsync(a));
			_agent.Start();

			// Use Google's Material Icons font (bundled, registered via Info.plist UIAppFonts) as the
			// cross-platform icon set, so Icon("mic") etc. draw the SAME Material glyph as Android.
			CometSamples.Jetchat.JetchatIcons.Register();

			if (Screen == "baristanotes")
			{
				FontFamilyRegistry.Register("Manrope", "Manrope-Regular", Microsoft.Maui.FontWeight.Regular);
				FontFamilyRegistry.Register("ManropeSemibold", "Manrope-SemiBold", Microsoft.Maui.FontWeight.Semibold);
				FontFamilyRegistry.Register("MaterialIcons", "MaterialSymbolsOutlined-Regular", Microsoft.Maui.FontWeight.Regular);
				FontFamilyRegistry.Register("coffee-icons", "coffeeicons", Microsoft.Maui.FontWeight.Regular);
			}

			// Generate the Material 3 scheme from the Comet theme using Google's material-color-utilities
			// (the SAME algorithm as Android's Material You) so iOS renders a real tonal scheme — identical
			// to Android when both seed from the same input. Must run BEFORE BuildUi() (the tree reads the
			// theme tokens at build time). Brand seed by default; flip SeedFromContent to derive the scheme
			// from the user's profile photo (content-based Material You — same generator, image seed).
			// Follow the system light/dark setting (the gold's isSystemInDarkTheme) so the generated
			// scheme is the light OR dark M3 mapping. Read at startup; a live toggle would need the
			// structural re-render (re-apply the scheme + rebuild the tree).
			bool dark = UIScreen.MainScreen.TraitCollection.UserInterfaceStyle == UIUserInterfaceStyle.Dark;
			const bool SeedFromContent = false;
			if (SeedFromContent && PixelsFromBundle("ali") is { } pixels)
				CometSamples.Jetchat.JetchatTheme.ApplyDynamicSchemeFromPixels(pixels, dark);
			else
				CometSamples.Jetchat.JetchatTheme.ApplyDynamicScheme(CometSamples.Jetchat.JetchatTheme.SeedColor, dark);

			// A real Comet view tree, rendered as SwiftUI through the node protocol —
			// no MAUI handlers in the render path.
			var backend = new SwiftUIBackendRoot(new EmptyServiceProvider()) { UseYogaLayout = true };
			Window.RootViewController = backend.CreateController(BuildUi());

			Window.MakeKeyAndVisible();
			DispatchQueue.MainQueue.DispatchAsync(() =>
			{
				if (Window is not { } window)
					return;

				var safeArea = window.SafeAreaInsets;
				CometWindowMetrics.Shared.UpdateSafeArea(new Microsoft.Maui.Thickness(
					safeArea.Left,
					safeArea.Top,
					safeArea.Right,
					safeArea.Bottom));
				ReactiveScheduler.FlushSync();
			});
			return true;
		}

		public override void DidEnterBackground(UIApplication application)
		{
			ObserveLifecycleTask(
				BaristaVoiceIntegration.OnAppBackgroundedAsync(),
				"Voice background deactivation");
			base.DidEnterBackground(application);
		}

		public override void WillTerminate(UIApplication application)
		{
			ObserveLifecycleTask(
				BaristaVoiceIntegration.ShutdownAsync(),
				"Voice shutdown");
			_baristaPhotos?.Dispose();
			_baristaPhotos = null;
			ProfilePhotoMedia.CaptureFromCamera = null;
			ProfilePhotoMedia.PickFromLibrary = null;
			base.WillTerminate(application);
		}

		static void ObserveLifecycleTask(System.Threading.Tasks.Task task, string operation)
		{
			_ = task.ContinueWith(
				failed => System.Diagnostics.Debug.WriteLine(
					$"[CometSwiftUIProbe] {operation} failed: {failed.Exception?.GetBaseException()}"),
				System.Threading.CancellationToken.None,
				System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted,
				System.Threading.Tasks.TaskScheduler.Default);
		}

		// The faithful Jetchat conversation screen (shared tree, identical on Android). iPhone
		// safe-area insets: status bar / Dynamic Island ~50dp top, home indicator ~28dp bottom.
		// The screen comes from SIMCTL_CHILD_COMET_SCREEN (the smoke scripts — the iOS twin
		// of the Android probe's intent extra) or, for the standalone per-sample installs
		// (-p:CometSample=…), from the bundle id (com.comet.sample.<name>).
		static string Screen =>
			System.Environment.GetEnvironmentVariable("COMET_SCREEN")
			?? (NSBundle.MainBundle.BundleIdentifier is { } bid && bid.StartsWith("com.comet.sample.")
				? bid.Substring("com.comet.sample.".Length)
				: "jetchat");

		View BuildUi() => Screen switch
		{
			"reply" => new CometSamples.Reply.ReplyProbeRoot(),
			"jetnews" => new CometSamples.JetNews.JetNewsRoot(),
			"jetsnack" => new CometSamples.Jetsnack.JetsnackRoot(topInset: 59),
			"baristanotes" => new CometSamples.BaristaNotes.BaristaNotesApp(),
			_ => CometSamples.Jetchat.JetchatConversation.Build(topInset: 50, bottomInset: 28),
		};

		// Decode a bundled image to a small AARRGGBB pixel buffer for content-based theming (the
		// material-color-utilities Quantize+Score wants raw pixels). Downscaled for a fast seed extract.
		static uint[]? PixelsFromBundle(string name, int max = 64)
		{
			using var img = UIImage.FromBundle(name);
			if (img?.CGImage is not CoreGraphics.CGImage cg) return null;
			int w = System.Math.Min(max, (int)cg.Width), h = System.Math.Min(max, (int)cg.Height);
			if (w <= 0 || h <= 0) return null;
			var bytes = new byte[w * h * 4];
			using (var cs = CoreGraphics.CGColorSpace.CreateDeviceRGB())
			using (var ctx = new CoreGraphics.CGBitmapContext(bytes, w, h, 8, w * 4, cs, CoreGraphics.CGImageAlphaInfo.PremultipliedLast))
				ctx.DrawImage(new CoreGraphics.CGRect(0, 0, w, h), cg);
			var px = new uint[w * h];
			for (int i = 0; i < px.Length; i++)
				px[i] = ((uint)bytes[i * 4 + 3] << 24) | ((uint)bytes[i * 4] << 16) | ((uint)bytes[i * 4 + 1] << 8) | bytes[i * 4 + 2];
			return px;
		}

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
