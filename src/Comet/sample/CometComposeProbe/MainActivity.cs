using System;
using System.Collections.Generic;
using System.Linq;
using Android.App;
using Android.OS;
using Comet;
using Comet.Platform.Compose;
using Comet.Reactive;
using Microsoft.Maui.Graphics;

namespace CometComposeProbe
{
	/// <summary>
	/// Builds a Comet view tree and renders it as a single Jetpack Compose composition
	/// — no MAUI handlers anywhere in the render path. The Phase 1 go/no-go proof.
	/// </summary>
	/// <remarks>Extends <c>ComponentActivity</c> because <c>ComposeView</c> requires the
	/// ViewTree lifecycle / saved-state owners that a plain <c>Activity</c> doesn't set.</remarks>
	[Activity(Label = "Comet+Compose", MainLauncher = true)]
	public class MainActivity : AndroidX.Activity.ComponentActivity
	{
		readonly Signal<string> _name = new(string.Empty);
		readonly Signal<bool> _fancy = new(false);
		readonly Signal<double> _volume = new(0.3);

		protected override void OnCreate(Bundle? savedInstanceState)
		{
			base.OnCreate(savedInstanceState);

			// Comet's fluent env writes post through ThreadHelper; route to the UI thread.
			ThreadHelper.SetFireOnMainThread(a => RunOnUiThread(a));

			var root = BuildUi();

			var backend = new ComposeBackendRoot(new EmptyServiceProvider());
			var composeView = backend.CreateView(this, root);
			SetContentView(composeView);
		}

		// A real master–detail app on the Compose backend, combining everything: a
		// virtualized list of tappable styled rows (the master), navigating to a detail
		// screen and back — navigation + LazyColumn + gestures + styling together.
		static readonly string[] Frameworks =
			{ "Jetpack Compose", "SwiftUI", "WinUI 3", "GTK 4", "Qt Quick", "Flutter", "React Native", "AppKit" };

		View BuildUi()
		{
			var nav = new NavigationView();
			nav.Add(ListScreen(nav));
			return nav;
		}

		static View ListScreen(NavigationView nav) =>
			new ListView<string>(() => Frameworks.ToList())
			{
				ViewFor = item => new VStack
				{
					new Text(item),
					new Text("tap for detail").Color(Colors.Gray),
				}
				.Background(Color.FromArgb("#F4EFF4")).Padding(16)
				.OnTap(_ => nav.Navigate(DetailScreen(nav, item))),
			};

		static View DetailScreen(NavigationView nav, string item) => new VStack
		{
			new Text($"📄  {item}").Color(Colors.White),
			new Text("Pushed from the list via Navigate()").Color(Colors.White),
			new Button("←  Back to list", () => nav.Pop()),
		}.Background(Color.FromArgb("#6750A4")).Padding(28);

		sealed class EmptyServiceProvider : IServiceProvider
		{
			public object? GetService(Type serviceType) => null;
		}
	}
}
