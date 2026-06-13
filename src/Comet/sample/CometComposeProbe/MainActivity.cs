using System;
using System.Collections.Generic;
using System.Linq;
using Android.App;
using Android.OS;
using Comet;
using Comet.Platform.Compose;
using Comet.Reactive;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;

namespace CometComposeProbe
{
	/// <summary>
	/// Reproduces a Jetchat-style conversation screen (a C# port of Google's Compose
	/// "Jetchat" sample) on the Comet node backend: a fixed top bar over a virtualized,
	/// Yoga-laid-out message list. Every row is laid out by the shared C# Yoga engine
	/// (avatar + author + wrapping body), so it renders identically to the iOS/SwiftUI
	/// backend — no MAUI handlers in the path.
	/// </summary>
	[Activity(Label = "Comet+Compose", MainLauncher = true)]
	public class MainActivity : AndroidX.Activity.ComponentActivity
	{
		protected override void OnCreate(Bundle? savedInstanceState)
		{
			base.OnCreate(savedInstanceState);
			ActionBar?.Hide();

			ThreadHelper.SetFireOnMainThread(a => RunOnUiThread(a));

			var root = BuildUi();

			var backend = new ComposeBackendRoot(new EmptyServiceProvider()) { UseYogaLayout = true };
			var composeView = backend.CreateView(this, root);
			SetContentView(composeView);
		}

		// The faithful Jetchat conversation screen (shared tree, identical on iOS). A ~24dp top
		// inset clears the status bar.
		View BuildUi() => CometSamples.Jetchat.JetchatConversation.Build(topInset: 24);

		sealed class EmptyServiceProvider : IServiceProvider
		{
			public object? GetService(Type serviceType) => null;
		}
	}
}
