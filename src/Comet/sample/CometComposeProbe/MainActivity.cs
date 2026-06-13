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
		readonly Signal<int> _len = new(0);

		static readonly string[] Lengths =
		{
			"one line",
			"a slightly longer line that may wrap once on a phone width here",
			"three lines worth of text here that should wrap onto roughly three lines on a typical phone width so we can watch the stack below it move",
			"a much longer paragraph designed to wrap onto five or six lines so that the rows beneath it are pushed substantially further down the screen, proving the whole vertical stack expands and contracts as the text length changes rather than the text just growing inside a fixed slot",
		};

		protected override void OnCreate(Bundle? savedInstanceState)
		{
			base.OnCreate(savedInstanceState);
			ActionBar?.Hide(); // full-screen content; no title bar offsetting the Yoga layout

			// Comet's fluent env writes post through ThreadHelper; route to the UI thread.
			ThreadHelper.SetFireOnMainThread(a => RunOnUiThread(a));

			var root = BuildUi();

			var backend = new ComposeBackendRoot(new EmptyServiceProvider()) { UseYogaLayout = true };
			var composeView = backend.CreateView(this, root);
			SetContentView(composeView);
		}

		// Direct VStack root (NavigationView is own-content and stops the layout engine), so the
		// Yoga engine lays this out. Cycling the text length proves the whole stack reflows — the
		// Android counterpart of the iOS test.
		View BuildUi() => new VStack(spacing: 16f)
		{
			new Text("Image test").Color(Colors.White),
			new HStack(spacing: 12f)
			{
				new Image("https://picsum.photos/seed/comet/160").Frame(width: 64, height: 64),
				new VStack(spacing: 2f)
				{
					new Text("Ada Lovelace").Color(Colors.White),
					new Text("first programmer").Color(Colors.White),
				},
			},
			new Image("https://picsum.photos/seed/banner/800/300").Frame(height: 160),
			new Text("below the banner").Color(Colors.White),
		}.Background(Color.FromArgb("#6750A4")).Padding(24);

		sealed class EmptyServiceProvider : IServiceProvider
		{
			public object? GetService(Type serviceType) => null;
		}
	}
}
