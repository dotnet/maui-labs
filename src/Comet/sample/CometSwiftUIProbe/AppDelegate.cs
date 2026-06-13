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

		sealed record Message(string Author, string Body, string AvatarSeed);

		static readonly System.Collections.Generic.List<Message> Conversation = new()
		{
			new("Taylor Brooks", "Morning everyone! Did you catch the new layout engine demo yesterday?", "taylor"),
			new("Ada Lovelace", "I did — the whole thing reflows from one flexbox pass now, shared across iOS and Android. Pretty wild.", "ada"),
			new("John Glenn", "So the same Comet tree lands pixel-identical on both backends? No per-platform tweaking?", "john"),
			new("Ada Lovelace", "That's the idea. Text wraps, avatars size, rows grow — all computed once in C#.", "ada"),
			new("Taylor Brooks", "Ship it. 🚀", "taylor"),
			new("Grace Hopper", "Let's make sure the long messages still wrap cleanly though — this one is intentionally a good deal longer so we can watch it spill onto several lines inside a virtualized row and confirm the row height grows to fit.", "grace"),
			new("John Glenn", "Confirmed on my iPhone — looks great.", "john"),
			new("Ada Lovelace", "And the list is genuinely lazy: rows only materialize as they scroll in.", "ada"),
			new("Taylor Brooks", "Perfect. Same screen, two platforms, one layout pass.", "taylor"),
			new("Grace Hopper", "That's the dream. Good work today, team.", "grace"),
		};

		// A Jetchat-style conversation screen: a fixed top bar over a virtualized, Yoga-laid-out
		// message list — the same Comet tree the Compose backend renders, so both match.
		View BuildUi() => new VStack(spacing: 0f)
		{
			new HStack(spacing: 12f)
			{
				new Text("#composers").Color(Colors.White),
			}.Padding(new Microsoft.Maui.Thickness(16, 56, 16, 16)).Background(Color.FromArgb("#6750A4")), // top inset clears the status bar

			new ListView<Message>(() => Conversation)
			{
				ViewFor = MessageRow,
			}.FillVertical(),
		}.Background(Color.FromArgb("#F2EFF7")); // tonal page behind the cards

		// Each message is a Material card: a rounded, raised white surface (corner radius +
		// elevation), inset from the page by the outer container's padding (the inter-card gap).
		static View MessageRow(Message m) => new VStack(spacing: 0f)
		{
			new HStack(spacing: 12f)
			{
				new Image($"https://picsum.photos/seed/{m.AvatarSeed}/80").Frame(width: 42, height: 42).CornerRadius(21),
				new VStack(spacing: 2f)
				{
					new Text(m.Author).Color(Color.FromArgb("#1C1B1F")),
					new Text(m.Body).Color(Color.FromArgb("#49454F")),
				},
			}.Padding(new Microsoft.Maui.Thickness(12)).Background(Colors.White).CornerRadius(16).Elevation(2),
		}.Padding(new Microsoft.Maui.Thickness(12, 6, 12, 6));

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
