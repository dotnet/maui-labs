#nullable enable
#if ANDROID
using System;
using System.Collections.Generic;
using Android.Graphics;
using AndroidX.Compose;

namespace Comet.Platform.Compose
{
	/// <summary>
	/// App-populated map from a font-family name to a loaded <see cref="Typeface"/>, so the
	/// Compose backend can render custom fonts (and icon fonts) that the vendored facade can't
	/// construct on its own. The app registers a base typeface (e.g. a variable font loaded with
	/// <c>Typeface.CreateFromAsset</c>); <see cref="Resolve"/> derives a weighted typeface and
	/// wraps it as a Compose <see cref="FontFamily"/>, cached per (family, weight).
	/// </summary>
	public static class ComposeFontRegistry
	{
		static readonly Dictionary<string, Typeface> Bases = new(StringComparer.OrdinalIgnoreCase);
		static readonly Dictionary<(string, int), (Typeface Typeface, FontFamily Family)?> Cache = new();

		/// <summary>Registers a base typeface under a family name (e.g. "Montserrat").</summary>
		public static void Register(string family, Typeface? typeface)
		{
			if (!string.IsNullOrEmpty(family) && typeface is not null)
				Bases[family] = typeface;
		}

		/// <summary>Resolves a registered family at the given weight to a (Typeface, FontFamily)
		/// pair — the typeface drives native measurement, the family the composed Text. Returns
		/// null when unregistered or if wrapping fails (so text falls back to the default font).</summary>
		public static (Typeface Typeface, FontFamily Family)? Resolve(string? family, int weight)
		{
			if (string.IsNullOrEmpty(family) || !Bases.TryGetValue(family, out var baseFace))
				return null;

			var key = (family, weight);
			if (Cache.TryGetValue(key, out var cached))
				return cached;

			(Typeface, FontFamily)? result;
			try
			{
				var typeface = (weight > 0 && OperatingSystem.IsAndroidVersionAtLeast(28))
					? Typeface.Create(baseFace, weight, false)!
					: baseFace;
				result = (typeface, FontFamily.FromTypeface(typeface));
			}
			catch (Exception ex)
			{
				// The custom-font JNI path failed — degrade to the default font, never crash.
				global::Android.Util.Log.Warn("CometFont", $"resolve {family}@{weight} failed: {ex.Message}");
				result = null;
			}

			Cache[key] = result;
			return result;
		}
	}
}
#endif
