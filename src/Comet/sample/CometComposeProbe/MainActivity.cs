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

		// Navigation through Compose: a NavigationView whose top screen is composed; the
		// home screen pushes a detail screen onto the stack, which pops back — driven by
		// Comet's Navigate/Pop and recomposed by the backend nav node.
		View BuildUi()
		{
			var nav = new NavigationView();
			nav.Add(HomeScreen(nav));
			return nav;
		}

		static View HomeScreen(NavigationView nav) => new VStack
		{
			new Text("🏠  Home").Color(Colors.White),
			new Text("Screen one, on the navigation stack").Color(Colors.White),
			new Button("Go to Detail  →", () => nav.Navigate(DetailScreen(nav))),
		}.Background(Color.FromArgb("#6750A4")).Padding(28);

		static View DetailScreen(NavigationView nav) => new VStack
		{
			new Text("📄  Detail").Color(Colors.White),
			new Text("Screen two — pushed via Navigate()").Color(Colors.White),
			new Button("←  Back", () => nav.Pop()),
		}.Background(Color.FromArgb("#7D5260")).Padding(28);

		sealed class EmptyServiceProvider : IServiceProvider
		{
			public object? GetService(Type serviceType) => null;
		}
	}
}
