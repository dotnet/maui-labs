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
		readonly Signal<int> _count = new(0);

		protected override void OnCreate(Bundle? savedInstanceState)
		{
			base.OnCreate(savedInstanceState);

			// Comet's fluent env writes post through ThreadHelper; route to the UI thread.
			ThreadHelper.SetFireOnMainThread(a => RunOnUiThread(a));

			var root = BuildUi();

			var backend = new ComposeBackendRoot(new EmptyServiceProvider()) { UseYogaLayout = true };
			var composeView = backend.CreateView(this, root);
			SetContentView(composeView);
		}

		// Direct VStack root (no NavigationView, which is own-content and stops the layout
		// engine) so the Yoga engine lays this out — the Android counterpart of the iOS test.
		View BuildUi() => new VStack
		{
			new Text("Yoga layout").Color(Colors.White),
			new Text("A").Color(Colors.White),
			new Text("BB").Color(Colors.White),
			new Text("CCC").Color(Colors.White),
			new Button("Increment", () => _count.Value++),
			new Text(() => $"Count: {_count.Value}").Color(Colors.White),
		}.Background(Color.FromArgb("#6750A4")).Padding(24);

		sealed class EmptyServiceProvider : IServiceProvider
		{
			public object? GetService(Type serviceType) => null;
		}
	}
}
