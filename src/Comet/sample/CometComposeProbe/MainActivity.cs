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
			// and register them per weight so the Compose backend renders the true outlines. Jetchat
			// ships distinct weight files; synthesizing weights from a single base TTF makes the
			// intermediate weights (e.g. Montserrat Medium) render thin/pale, so register each.
			try
			{
				Android.Graphics.Typeface Asset(string name) =>
					Android.Graphics.Typeface.CreateFromAsset(Assets, "fonts/" + name);

				ComposeFontRegistry.Register("Montserrat", 400, Asset("montserrat_regular.ttf"));
				ComposeFontRegistry.Register("Montserrat", 500, Asset("montserrat_medium.ttf"));
				ComposeFontRegistry.Register("Montserrat", 600, Asset("montserrat_semibold.ttf"));
				ComposeFontRegistry.Register("Karla", 400, Asset("karla_regular.ttf"));
				ComposeFontRegistry.Register("Karla", 700, Asset("karla_bold.ttf"));
			}
			catch (Exception ex)
			{
				Android.Util.Log.Warn("CometProbe", "Font load failed: " + ex);
			}

			// Material 3 color. By DEFAULT we seed the scheme from the Comet brand color via the
			// shared material-color-utilities path (JetchatTheme.ApplyDynamicScheme) — the SAME algorithm
			// and SAME seed as iOS — so the app is theme-driven and looks identical cross-platform.
			// Flip UseWallpaperMaterialYou to instead adopt the device's Material You (gold-faithful to
			// Themes.kt's isDynamicColor=true, but diverges from iOS, which can't read the wallpaper).
			const bool UseWallpaperMaterialYou = false;
			AndroidX.Compose.Material3.ColorScheme scheme;
			if (UseWallpaperMaterialYou && Build.VERSION.SdkInt >= BuildVersionCodes.S)
			{
				scheme = AndroidX.Compose.MaterialTheme.DynamicLightColorScheme(this);
				try { ApplySchemeToTheme(scheme); }
				catch (Exception ex) { Android.Util.Log.Warn("CometProbe", "scheme read-back failed, using static theme: " + ex); }
			}
			else
			{
				// Generate the M3 scheme in shared C# (applies it to the C# theme tokens the Comet views
				// read), then build the matching Compose ColorScheme so the real Material controls
				// (Button, ripples) are seeded identically. Brand seed by default; flip SeedFromContent to
				// derive the scheme from the profile photo (content-based Material You — same generator,
				// image seed — identical to iOS's SeedFromContent path).
				const bool SeedFromContent = false;
				MaterialColorUtilities.Schemes.Scheme<uint> s =
					SeedFromContent && PixelsFromDrawable(Resource.Drawable.ali) is { } px
						? CometSamples.Jetchat.JetchatTheme.ApplyDynamicSchemeFromPixels(px)
						: CometSamples.Jetchat.JetchatTheme.ApplyDynamicScheme(CometSamples.Jetchat.JetchatTheme.SeedColor);
				scheme = ComposeSchemeFromSeed(s);
			}

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

		// Decode a drawable to a small AARRGGBB pixel buffer for content-based theming (the
		// material-color-utilities Quantize+Score wants raw pixels). Downscaled for a fast seed extract.
		uint[]? PixelsFromDrawable(int resId, int max = 64)
		{
			using var bmp = Android.Graphics.BitmapFactory.DecodeResource(Resources, resId);
			if (bmp is null) return null;
			int w = System.Math.Min(max, bmp.Width), h = System.Math.Min(max, bmp.Height);
			using var scaled = Android.Graphics.Bitmap.CreateScaledBitmap(bmp, w, h, true);
			var ints = new int[w * h];
			scaled!.GetPixels(ints, 0, w, 0, 0, w, h);   // ARGB ints
			var px = new uint[ints.Length];
			for (int i = 0; i < ints.Length; i++) px[i] = (uint)ints[i];
			return px;
		}

		// Build the Compose MaterialTheme ColorScheme from the seed-generated M3 roles so the real
		// Material controls (Button, ripples) match the Comet views (which read the same roles from the
		// C# theme tokens). Mirrors the role subset the static scheme used.
		static AndroidX.Compose.Material3.ColorScheme ComposeSchemeFromSeed(MaterialColorUtilities.Schemes.Scheme<uint> s)
		{
			static AndroidX.Compose.Color C(uint argb) =>
				AndroidX.Compose.Color.FromArgb(0xFF, (byte)((argb >> 16) & 0xFF), (byte)((argb >> 8) & 0xFF), (byte)(argb & 0xFF));
			return AndroidX.Compose.MaterialTheme.LightColorScheme(
				primary: C(s.Primary), onPrimary: C(s.OnPrimary),
				surface: C(s.Surface), onSurface: C(s.OnSurface),
				surfaceVariant: C(s.SurfaceVariant), onSurfaceVariant: C(s.OnSurfaceVariant),
				background: C(s.Background), onBackground: C(s.OnBackground),
				tertiary: C(s.Tertiary));
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
