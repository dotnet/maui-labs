#nullable enable
#if ANDROID
using System;
using System.Collections.Generic;
using Android.Graphics;
using AndroidX.Compose;

namespace Comet.Platform.Compose
{
	/// <summary>
	/// App-populated map from a font-family name to loaded <see cref="Typeface"/>s, so the
	/// Compose backend can render custom fonts (and icon fonts) that the vendored facade can't
	/// construct on its own. The app registers either a single base typeface OR — preferred for
	/// fidelity — one typeface per weight (the real per-weight TTFs carry distinct glyph outlines;
	/// synthesizing weights from a single base with <c>Typeface.Create</c> produces thin, pale
	/// intermediate weights). <see cref="Resolve"/> picks the registered weight closest to the
	/// requested one (falling back to synthesizing from the base), and wraps it as a Compose
	/// <see cref="FontFamily"/>, cached per (family, weight).
	/// </summary>
	public static class ComposeFontRegistry
	{
		static readonly Dictionary<string, Typeface> Bases = new(StringComparer.OrdinalIgnoreCase);
		// Per-family weight → typeface, when the app registers real per-weight files.
		static readonly Dictionary<string, SortedDictionary<int, Typeface>> Weights = new(StringComparer.OrdinalIgnoreCase);
		static readonly Dictionary<(string, int), (Typeface Typeface, FontFamily Family)?> Cache = new();

		/// <summary>Registers a single base typeface under a family name (e.g. "Montserrat").
		/// Used when no per-weight files are available; <see cref="Resolve"/> synthesizes weights.</summary>
		public static void Register(string family, Typeface? typeface)
		{
			if (!string.IsNullOrEmpty(family) && typeface is not null)
				Bases[family] = typeface;
		}

		/// <summary>Registers a real per-weight typeface (e.g. Montserrat at 500 from
		/// <c>montserrat_medium.ttf</c>). Preferred over the single-base overload — distinct weight
		/// files render true, unlike synthesized intermediate weights. Also seeds the base from the
		/// first weight registered so single-base lookups still resolve.</summary>
		public static void Register(string family, int weight, Typeface? typeface)
		{
			if (string.IsNullOrEmpty(family) || typeface is null)
				return;

			if (!Weights.TryGetValue(family, out var byWeight))
				Weights[family] = byWeight = new SortedDictionary<int, Typeface>();
			byWeight[weight] = typeface;
			Bases.TryAdd(family, typeface);
		}

		/// <summary>Resolves a registered family at the given weight to a (Typeface, FontFamily)
		/// pair — the typeface drives native measurement, the family the composed Text. Prefers the
		/// registered weight nearest the request; if a family has only a single base, synthesizes the
		/// weight. Returns null when unregistered or if wrapping fails (so text falls back to the
		/// default font).</summary>
		public static (Typeface Typeface, FontFamily Family)? Resolve(string? family, int weight)
		{
			if (string.IsNullOrEmpty(family))
				return null;

			var key = (family, weight);
			if (Cache.TryGetValue(key, out var cached))
				return cached;

			(Typeface, FontFamily)? result;
			try
			{
				var typeface = PickTypeface(family, weight);
				result = typeface is null ? null : ((Typeface, FontFamily)?)(typeface, FontFamily.FromTypeface(typeface));
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

		// Picks the best typeface for a weight: an exact registered weight, else the nearest
		// registered weight, else the synthesized base (Typeface.Create), else null.
		static Typeface? PickTypeface(string family, int weight)
		{
			if (Weights.TryGetValue(family, out var byWeight) && byWeight.Count > 0)
			{
				int want = weight <= 0 ? 400 : weight;
				if (byWeight.TryGetValue(want, out var exact))
					return exact;

				Typeface? best = null;
				int bestDelta = int.MaxValue;
				foreach (var pair in byWeight)
				{
					int delta = Math.Abs(pair.Key - want);
					if (delta < bestDelta) { bestDelta = delta; best = pair.Value; }
				}
				return best;
			}

			if (!Bases.TryGetValue(family, out var baseFace))
				return null;

			return (weight > 0 && OperatingSystem.IsAndroidVersionAtLeast(28))
				? Typeface.Create(baseFace, weight, false)!
				: baseFace;
		}
	}
}
#endif
