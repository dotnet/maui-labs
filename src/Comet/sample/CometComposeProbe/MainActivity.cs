using System;
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
		readonly Signal<int> _count = new(0);

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

		// A small Comet tree: a heading, a live reactive counter, and a button.
		// Tapping Increment writes the Signal -> Comet's reactive scheduler flushes ->
		// the bound Text re-emits Text_Value -> the Compose MutableState recomposes.
		View BuildUi() => new VStack
		{
			new Text("Comet rendering via Jetpack Compose"),
			new Text(() => $"Count: {_count.Value}"),
			new Button("Increment", () => _count.Value++),
		};

		sealed class EmptyServiceProvider : IServiceProvider
		{
			public object? GetService(Type serviceType) => null;
		}
	}
}
