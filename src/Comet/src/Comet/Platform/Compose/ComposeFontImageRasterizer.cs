#nullable enable
#if ANDROID
using System;
using System.Collections.Generic;
using Comet.Backend;
using Android.Graphics;
using AndroidPaint = global::Android.Graphics.Paint;
using Size = Microsoft.Maui.Graphics.Size;

namespace Comet.Platform.Compose
{
	readonly struct ComposeFontImageSpec : IEquatable<ComposeFontImageSpec>
	{
		public ComposeFontImageSpec(
			string glyph,
			string family,
			float size,
			int weight,
			bool italic,
			bool autoScaling,
			uint argb)
		{
			Glyph = glyph;
			Family = family;
			Size = size;
			Weight = weight;
			Italic = italic;
			AutoScaling = autoScaling;
			Argb = argb;
		}

		public string Glyph { get; }
		public string Family { get; }
		public float Size { get; }
		public int Weight { get; }
		public bool Italic { get; }
		public bool AutoScaling { get; }
		public uint Argb { get; }

		public bool Equals(ComposeFontImageSpec other) =>
			Glyph == other.Glyph &&
			Family == other.Family &&
			Size.Equals(other.Size) &&
			Weight == other.Weight &&
			Italic == other.Italic &&
			AutoScaling == other.AutoScaling &&
			Argb == other.Argb;

		public override bool Equals(object? obj) =>
			obj is ComposeFontImageSpec other && Equals(other);

		public override int GetHashCode() =>
			HashCode.Combine(Glyph, Family, Size, Weight, Italic, AutoScaling, Argb);
	}

	static class ComposeFontImageRasterizer
	{
		static readonly object WarningGate = new();
		static readonly HashSet<string> WarnedFamilies = new(StringComparer.OrdinalIgnoreCase);

		public static Size Measure(in ComposeFontImageSpec spec)
		{
			using var paint = CreatePaint(spec);
			var metrics = MeasurePixels(paint, spec.Glyph);
			var scale = CurrentScale();
			return metrics.Width <= 0 || metrics.Height <= 0
				? Size.Zero
				: new Size(
					scale.PixelsToLogical(metrics.Width),
					scale.PixelsToLogical(metrics.Height));
		}

		public static Bitmap? Render(in ComposeFontImageSpec spec)
		{
			using var paint = CreatePaint(spec);
			var metrics = MeasurePixels(paint, spec.Glyph);
			if (metrics.Width <= 0 || metrics.Height <= 0)
				return null;

			var bitmap = Bitmap.CreateBitmap(metrics.Width, metrics.Height, Bitmap.Config.Argb8888!)!;
			using var canvas = new global::Android.Graphics.Canvas(bitmap);
			canvas.DrawText(spec.Glyph, 0, metrics.Baseline, paint);
			return bitmap;
		}

		static AndroidPaint CreatePaint(in ComposeFontImageSpec spec)
		{
			var paint = new AndroidPaint
			{
				AntiAlias = true,
				TextAlign = AndroidPaint.Align.Left,
				TextSize = spec.Size * CurrentScale().RasterDensity(spec.AutoScaling),
				Color = global::Android.Graphics.Color.Argb(
					(byte)(spec.Argb >> 24),
					(byte)(spec.Argb >> 16),
					(byte)(spec.Argb >> 8),
					(byte)spec.Argb),
			};

			if (ComposeFontRegistry.Resolve(spec.Family, spec.Weight, spec.Italic)?.Typeface is { } registered)
				paint.SetTypeface(registered);
			else
			{
				if (!string.IsNullOrWhiteSpace(spec.Family))
					WarnUnsupportedFamily(spec.Family);
				if (OperatingSystem.IsAndroidVersionAtLeast(28))
					paint.SetTypeface(Typeface.Create(
						string.IsNullOrWhiteSpace(spec.Family) ? Typeface.Default : Typeface.Create(spec.Family, TypefaceStyle.Normal),
						spec.Weight > 0 ? spec.Weight : 400,
						spec.Italic));
				else
				{
					var bold = spec.Weight >= 700;
					var style = (bold, spec.Italic) switch
					{
						(true, true) => TypefaceStyle.BoldItalic,
						(true, false) => TypefaceStyle.Bold,
						(false, true) => TypefaceStyle.Italic,
						_ => TypefaceStyle.Normal,
					};
					paint.SetTypeface(Typeface.Create(spec.Family, style));
				}
			}

			return paint;
		}

		static (int Width, int Height, float Baseline) MeasurePixels(AndroidPaint paint, string glyph)
		{
			if (string.IsNullOrEmpty(glyph))
				return default;

			var width = (int)(paint.MeasureText(glyph) + .5f);
			var baseline = (int)(-paint.Ascent() + .5f);
			var height = (int)(baseline + paint.Descent() + .5f);
			return (width, height, baseline);
		}

		static FontImageScaleMetrics CurrentScale()
		{
			var metrics = global::Android.App.Application.Context?.Resources?.DisplayMetrics;
			var deviceDensity = metrics is { Density: > 0 }
				? metrics.Density
				: Math.Max(ComposeNode.Density, 1f);
			return new FontImageScaleMetrics(
				deviceDensity,
				metrics?.ScaledDensity ?? deviceDensity);
		}

		static void WarnUnsupportedFamily(string family)
		{
			lock (WarningGate)
			{
				if (!WarnedFamilies.Add(family))
					return;
			}
			global::Android.Util.Log.Warn(
				"CometFontImage",
				$"Font family '{family}' is not registered; falling back to Android font matching.");
		}
	}
}
#endif
