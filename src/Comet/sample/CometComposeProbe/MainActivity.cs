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

		// Styling through Compose modifiers: background colors, padding, and VStack/HStack
		// spacing — all flowing from Comet's fluent API through the backend's modifier chain.
		View BuildUi() => new VStack
		{
			new VStack
			{
				new Text("Comet → Jetpack Compose").Color(Colors.White),
				new Text("Styled with Compose modifiers").Color(Colors.White),
			}.Background(Color.FromArgb("#6750A4")).Padding(24),

			new VStack
			{
				new Text("Card: background + padding"),
				new Text("Row spacing comes from the VStack"),
			}.Background(Color.FromArgb("#ECE6F0")).Padding(20),

			new HStack
			{
				new Text("A row"),
				new Text("with spacing + tint"),
			}.Background(Color.FromArgb("#E8DEF8")).Padding(16),
		}.Background(Color.FromArgb("#FFFBFE")).Padding(12);

		sealed class EmptyServiceProvider : IServiceProvider
		{
			public object? GetService(Type serviceType) => null;
		}
	}
}
