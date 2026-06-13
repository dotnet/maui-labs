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
		readonly Signal<int> _a = new(0);
		readonly Signal<int> _b = new(0);

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

		// A richer tree exercising nested layout (Row inside Column), two independent
		// reactive counters, and a DERIVED value (Sum) that tracks BOTH signals — proving
		// multi-dependency reactive tracking flows through the Compose bridge.
		View BuildUi() => new VStack
		{
			new Text("Comet → Jetpack Compose"),

			new Text(() => $"A = {_a.Value}"),
			new HStack
			{
				new Button("A +", () => _a.Value++),
				new Button("A −", () => _a.Value--),
			},

			new Text(() => $"B = {_b.Value}"),
			new HStack
			{
				new Button("B +", () => _b.Value++),
				new Button("B −", () => _b.Value--),
			},

			new Text(() => $"Sum = {_a.Value + _b.Value}"),
		};

		sealed class EmptyServiceProvider : IServiceProvider
		{
			public object? GetService(Type serviceType) => null;
		}
	}
}
