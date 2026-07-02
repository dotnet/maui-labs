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
#if DEBUG
		/// <summary>The logical root, exposed so the hot-reload demo receiver can log reload state.</summary>
		internal static View? RootView;
#endif

		protected override void OnCreate(Bundle? savedInstanceState)
		{
			base.OnCreate(savedInstanceState);
			ActionBar?.Hide();

			// P7 IME handling lives in ComposeBackendRoot (decor-level insets listener):
			// no SoftInput mode needed — Android 15's forced edge-to-edge makes
			// AdjustResize a no-op anyway, and the insets path works on every version.

			ThreadHelper.SetFireOnMainThread(a => RunOnUiThread(a));

#if DEBUG
			// Must be on BEFORE any View is constructed: view registration and the
			// active-view list (what TriggerReload targets) are gated on IsEnabled.
			Microsoft.Maui.HotReload.MauiHotReloadHelper.IsEnabled = true;
#endif

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

				// Google's Material Icons font as the cross-platform icon set (same glyphs as iOS).
				ComposeFontRegistry.Register(CometSamples.Jetchat.JetchatIcons.Font, 400, Asset("material_icons.ttf"));
				CometSamples.Jetchat.JetchatIcons.Register();
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
			// Follow the system light/dark setting (the gold's isSystemInDarkTheme) so the generated
			// scheme is the light OR dark M3 mapping. Read at startup (a live toggle would re-create
			// the activity, which rebuilds anyway).
			bool dark = (Resources!.Configuration!.UiMode & Android.Content.Res.UiMode.NightMask)
				== Android.Content.Res.UiMode.NightYes;

			const bool UseWallpaperMaterialYou = false;
			AndroidX.Compose.Material3.ColorScheme scheme;
			if (UseWallpaperMaterialYou && Build.VERSION.SdkInt >= BuildVersionCodes.S)
			{
				scheme = dark ? AndroidX.Compose.MaterialTheme.DynamicDarkColorScheme(this)
					: AndroidX.Compose.MaterialTheme.DynamicLightColorScheme(this);
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
						? CometSamples.Jetchat.JetchatTheme.ApplyDynamicSchemeFromPixels(px, dark)
						: CometSamples.Jetchat.JetchatTheme.ApplyDynamicScheme(CometSamples.Jetchat.JetchatTheme.SeedColor, dark);
				scheme = ComposeSchemeFromSeed(s, dark);
			}

			// Status bar: Surface (matches the header bar).
			// Nav bar: SurfaceTinted (matches the footer/UserInput bar so the background is seamless).
			var surf = CometSamples.Jetchat.JetchatTheme.Surface;
			var statusTint = Android.Graphics.Color.Argb(255, (int)(surf.Red * 255), (int)(surf.Green * 255), (int)(surf.Blue * 255));
			var footerSurf = CometSamples.Jetchat.JetchatTheme.SurfaceTinted;
			var navTint = Android.Graphics.Color.Argb(255, (int)(footerSurf.Red * 255), (int)(footerSurf.Green * 255), (int)(footerSurf.Blue * 255));
			Window!.SetStatusBarColor(statusTint);
			Window.SetNavigationBarColor(navTint);
			// Light bars (dark icons) on a light surface; in dark mode clear the flags for light icons.
			const int lightBars = (int)Android.Views.WindowInsetsControllerAppearance.LightStatusBars
				| (int)Android.Views.WindowInsetsControllerAppearance.LightNavigationBars;
			Window.InsetsController?.SetSystemBarsAppearance(dark ? 0 : lightBars, lightBars);

			var root = BuildUi();
#if DEBUG
			RootView = root;
#endif

			// Link taps in chat bubbles open the system browser (the gold's uriHandler.openUri).
			CometSamples.Jetchat.JetchatConversation.OpenUrl = url =>
			{
				try { StartActivity(new Android.Content.Intent(Android.Content.Intent.ActionView, Android.Net.Uri.Parse(url))); }
				catch (Exception ex) { Android.Util.Log.Warn("CometProbe", "open url failed: " + ex.Message); }
			};

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
#if DEBUG
			DevFlowHelper.Start(this, composeView);
#endif
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
		static AndroidX.Compose.Material3.ColorScheme ComposeSchemeFromSeed(MaterialColorUtilities.Schemes.Scheme<uint> s, bool dark)
		{
			static AndroidX.Compose.Color C(uint argb) =>
				AndroidX.Compose.Color.FromArgb(0xFF, (byte)((argb >> 16) & 0xFF), (byte)((argb >> 8) & 0xFF), (byte)(argb & 0xFF));
			return dark
				? AndroidX.Compose.MaterialTheme.DarkColorScheme(
					primary: C(s.Primary), onPrimary: C(s.OnPrimary),
					surface: C(s.Surface), onSurface: C(s.OnSurface),
					surfaceVariant: C(s.SurfaceVariant), onSurfaceVariant: C(s.OnSurfaceVariant),
					background: C(s.Background), onBackground: C(s.OnBackground),
					tertiary: C(s.Tertiary))
				: AndroidX.Compose.MaterialTheme.LightColorScheme(
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
#if DEBUG
		// A [Body] root is what hot reload targets (see HotReloadDemo.cs).
		View BuildUi() => new JetchatRoot();
#else
		View BuildUi() => CometSamples.Jetchat.JetchatConversation.Build(topInset: 24);
#endif

		sealed class EmptyServiceProvider : IServiceProvider
		{
			public object? GetService(Type serviceType) => null;
		}
	}
}
