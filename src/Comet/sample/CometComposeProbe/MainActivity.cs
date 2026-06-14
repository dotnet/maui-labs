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

			// Build the active Material 3 ColorScheme: Material You (dynamicLightColorScheme) on
			// Android 12+, else Jetchat's static light scheme — exactly Themes.kt's
			// JetchatTheme(isDynamicColor = true). Mirror its roles into the C# theme tokens BEFORE
			// building the tree so the views' explicit .Color()s are the dynamic ones too.
			var scheme = BuildColorScheme();
			try { ApplySchemeToTheme(scheme); }
			catch (Exception ex) { Android.Util.Log.Warn("CometProbe", "scheme read-back failed, using static theme: " + ex); }

			// System bars match the (now dynamic) surface so the status/nav areas blend edge-to-edge.
			var surf = CometSamples.Jetchat.JetchatTheme.Surface;
			var barTint = Android.Graphics.Color.Argb(255, (int)(surf.Red * 255), (int)(surf.Green * 255), (int)(surf.Blue * 255));
			Window!.SetStatusBarColor(barTint);
			Window.SetNavigationBarColor(barTint);
			const int light = (int)Android.Views.WindowInsetsControllerAppearance.LightStatusBars
				| (int)Android.Views.WindowInsetsControllerAppearance.LightNavigationBars;
			Window.InsetsController?.SetSystemBarsAppearance(light, light);

			var root = BuildUi();

			var backend = new ComposeBackendRoot(new EmptyServiceProvider())
			{
				UseYogaLayout = true,
				// Same scheme drives the real Material controls (Button, ripples) so they match.
				WrapContent = content =>
				{
					var theme = new AndroidX.Compose.MaterialTheme { ColorScheme = scheme };
					theme.Add(content);
					return theme;
				},
			};
			var composeView = backend.CreateView(this, root);
			SetContentView(composeView);
		}

		AndroidX.Compose.Material3.ColorScheme BuildColorScheme()
		{
			if (Build.VERSION.SdkInt >= BuildVersionCodes.S)
				return AndroidX.Compose.MaterialTheme.DynamicLightColorScheme(this);
			return JetchatStaticColorScheme();
		}

		static AndroidX.Compose.Material3.ColorScheme JetchatStaticColorScheme()
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

		// Read the scheme's role colors back into the C# theme tokens (facade Color exposes A/R/G/B).
		static void ApplySchemeToTheme(AndroidX.Compose.Material3.ColorScheme s)
		{
			static Microsoft.Maui.Graphics.Color M(AndroidX.Compose.Color c) =>
				new Microsoft.Maui.Graphics.Color(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f);

			CometSamples.Jetchat.JetchatTheme.ApplyScheme(
				primary: M(s.Primary), onPrimary: M(s.OnPrimary), primaryContainer: M(s.PrimaryContainer),
				secondary: M(s.Secondary), surface: M(s.Surface), onSurface: M(s.OnSurface),
				surfaceVariant: M(s.SurfaceVariant), onSurfaceVariant: M(s.OnSurfaceVariant),
				background: M(s.Background), tertiary: M(s.Tertiary), tertiaryContainer: M(s.TertiaryContainer),
				onTertiaryContainer: M(s.OnTertiaryContainer), outline: M(s.Outline));
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
