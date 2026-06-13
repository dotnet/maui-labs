using System;
using System.Collections.Generic;
using System.Linq;
using Android.App;
using Android.OS;
using Comet;
using Comet.Platform.Compose;
using Comet.Reactive;

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

		// A virtualized list of 100 rows rendered through Compose's LazyColumn: each row's
		// template (an HStack of two Texts) is materialized into backend nodes only when it
		// scrolls into view. The list is the root because a LazyColumn needs a bounded height.
		View BuildUi() => new ListView<int>(() => Enumerable.Range(1, 100).ToList())
		{
			ViewFor = i => new HStack
			{
				new Text($"#{i}"),
				new Text($"   Comet row via Compose LazyColumn"),
			},
		};

		sealed class EmptyServiceProvider : IServiceProvider
		{
			public object? GetService(Type serviceType) => null;
		}
	}
}
