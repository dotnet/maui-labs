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

			// System bars match the Jetchat header/footer bar tint (light bars, dark icons) so the
			// status + navigation areas blend edge-to-edge with the bars.
			var barTint = Android.Graphics.Color.ParseColor("#E7E9F8");
			Window!.SetStatusBarColor(barTint);
			Window.SetNavigationBarColor(barTint);
			const int light = (int)Android.Views.WindowInsetsControllerAppearance.LightStatusBars
				| (int)Android.Views.WindowInsetsControllerAppearance.LightNavigationBars;
			Window.InsetsController?.SetSystemBarsAppearance(light, light);

			ThreadHelper.SetFireOnMainThread(a => RunOnUiThread(a));

			// Load Jetchat's real fonts (Montserrat titles/labels, Karla body) from bundled assets
			// and register them so the Compose backend renders them (degrades to the default font
			// if anything fails). Variable TTFs — weights are derived per use.
			try
			{
				ComposeFontRegistry.Register("Montserrat", Android.Graphics.Typeface.CreateFromAsset(Assets, "fonts/Montserrat.ttf"));
				ComposeFontRegistry.Register("Karla", Android.Graphics.Typeface.CreateFromAsset(Assets, "fonts/Karla.ttf"));
			}
			catch (Exception ex)
			{
				Android.Util.Log.Warn("CometProbe", "Font load failed: " + ex);
			}

			var root = BuildUi();

			var backend = new ComposeBackendRoot(new EmptyServiceProvider())
			{
				UseYogaLayout = true,
				// Apply the Jetchat Material3 light scheme so real Material controls (Button, Icon,
				// ripples) theme correctly — primary = Jetchat blue, not the default purple.
				WrapContent = content =>
				{
					var theme = new AndroidX.Compose.MaterialTheme { ColorScheme = JetchatColorScheme() };
					theme.Add(content);
					return theme;
				},
			};
			var composeView = backend.CreateView(this, root);
			SetContentView(composeView);
		}

		static AndroidX.Compose.Material3.ColorScheme JetchatColorScheme()
		{
			static AndroidX.Compose.Color C(string hex)
			{
				int v = System.Convert.ToInt32(hex, 16);
				return AndroidX.Compose.Color.FromArgb(0xFF, (byte)((v >> 16) & 0xFF), (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF));
			}
			return AndroidX.Compose.MaterialTheme.LightColorScheme(
				primary: C("1546F6"), onPrimary: C("FFFFFF"),
				surface: C("FBFDFD"), onSurface: C("191C1D"),
				surfaceVariant: C("E2E1EC"), onSurfaceVariant: C("45464F"),
				background: C("FBFDFD"), onBackground: C("191C1D"),
				tertiary: C("7A5900"));
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
