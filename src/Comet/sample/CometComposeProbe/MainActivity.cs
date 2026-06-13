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
		readonly Signal<string> _name = new(string.Empty);
		readonly Signal<bool> _fancy = new(false);

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

		// A form exercising two-way input through Compose: a TextField bound to a Signal
		// (typing updates the greeting live) and a Switch bound to a Signal (flipping
		// updates a label) — both round-tripping user input back into Comet's reactive state.
		View BuildUi() => new VStack
		{
			new Text("Comet → Jetpack Compose"),

			new TextField(_name, "Enter your name"),
			new Text(() => string.IsNullOrEmpty(_name.Value)
				? "Hello, stranger"
				: $"Hello, {_name.Value}!"),

			new HStack
			{
				new Text("Fancy mode"),
				new Toggle(_fancy),
			},
			new Text(() => _fancy.Value ? "✨ Fancy is ON ✨" : "plain mode"),
		};

		sealed class EmptyServiceProvider : IServiceProvider
		{
			public object? GetService(Type serviceType) => null;
		}
	}
}
