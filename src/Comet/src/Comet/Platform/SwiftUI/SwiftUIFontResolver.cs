#nullable enable
#if IOS
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Comet.Backend;
using CoreGraphics;
using Foundation;
using Microsoft.Maui.Graphics;
using UIKit;

namespace Comet.Platform.SwiftUI
{
	readonly struct SwiftUIFontImageSpec : IEquatable<SwiftUIFontImageSpec>
	{
		public SwiftUIFontImageSpec(
			string glyph,
			string family,
			double size,
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
		public double Size { get; }
		public int Weight { get; }
		public bool Italic { get; }
		public bool AutoScaling { get; }
		public uint Argb { get; }

		public bool Equals(SwiftUIFontImageSpec other) =>
			Glyph == other.Glyph &&
			Family == other.Family &&
			Size.Equals(other.Size) &&
			Weight == other.Weight &&
			Italic == other.Italic &&
			AutoScaling == other.AutoScaling &&
			Argb == other.Argb;

		public override bool Equals(object? obj) =>
			obj is SwiftUIFontImageSpec other && Equals(other);

		public override int GetHashCode() =>
			HashCode.Combine(Glyph, Family, Size, Weight, Italic, AutoScaling, Argb);
	}

	static class SwiftUIFontResolver
	{
		static readonly object WarningGate = new();
		static readonly HashSet<string> WarnedFamilies = new(StringComparer.OrdinalIgnoreCase);

		public static UIFont Resolve(
			string? family,
			double size,
			int weight,
			bool italic,
			bool warnWhenMissing = false)
		{
			var pointSize = (NFloat)(size > 0 ? size : 17);
			var nativeWeight = NativeWeight(weight);
			UIFont? font = null;

			if (!string.IsNullOrWhiteSpace(family))
			{
				font = UIFont.FromName(WeightedFontName(family, weight), pointSize);
				if (font is null && UIFont.FromName(family, pointSize) is { } baseFont)
				{
					var traits = new UIFontTraits
					{
						Weight = (float)nativeWeight.GetWeight(),
					};
					var attributes = new UIFontAttributes
					{
						Traits = traits,
					};
					using var descriptor = baseFont.FontDescriptor.CreateWithAttributes(attributes);
					var weightedFont = UIFont.FromDescriptor(descriptor, pointSize);
					if (weightedFont is not null)
					{
						baseFont.Dispose();
						font = weightedFont;
					}
					else
						font = baseFont;
				}
				if (font is null && warnWhenMissing)
					WarnUnsupportedFamily(family);
			}

			font ??= UIFont.SystemFontOfSize(pointSize, nativeWeight);

			if (italic)
			{
				var descriptor = font.FontDescriptor.CreateWithTraits(
					font.FontDescriptor.SymbolicTraits | UIFontDescriptorSymbolicTraits.Italic);
				if (descriptor is not null)
				{
					using (descriptor)
					{
						var italicFont = UIFont.FromDescriptor(descriptor, pointSize);
						if (italicFont is not null)
						{
							font.Dispose();
							font = italicFont;
						}
					}
				}
			}

			return font;
		}

		static string WeightedFontName(string family, int weight)
		{
			var suffix = weight switch
			{
				>= 700 => "Bold",
				>= 600 => "SemiBold",
				>= 500 => "Medium",
				_ => "Regular",
			};
			return $"{family}-{suffix}";
		}

		static UIFontWeight NativeWeight(int weight) =>
			NativeFontWeightMapping.Classify(weight) switch
			{
				NativeFontWeightClass.Thin => UIFontWeight.Thin,
				NativeFontWeightClass.UltraLight => UIFontWeight.UltraLight,
				NativeFontWeightClass.Light => UIFontWeight.Light,
				NativeFontWeightClass.Medium => UIFontWeight.Medium,
				NativeFontWeightClass.Semibold => UIFontWeight.Semibold,
				NativeFontWeightClass.Bold => UIFontWeight.Bold,
				NativeFontWeightClass.Heavy => UIFontWeight.Heavy,
				NativeFontWeightClass.Black => UIFontWeight.Black,
				_ => UIFontWeight.Regular,
			};

		static void WarnUnsupportedFamily(string family)
		{
			lock (WarningGate)
			{
				if (!WarnedFamilies.Add(family))
					return;
			}
			Logger.Warn(
				$"Font family '{family}' is unavailable to the SwiftUI backend; using the system font.");
		}
	}

	static class SwiftUIFontImageRasterizer
	{
		public static NSData? Render(
			in SwiftUIFontImageSpec spec,
			out Size measuredSize)
		{
			measuredSize = Size.Zero;
			if (string.IsNullOrEmpty(spec.Glyph) || spec.Size <= 0)
				return null;

			using var baseFont = SwiftUIFontResolver.Resolve(
				spec.Family, spec.Size, spec.Weight, spec.Italic, warnWhenMissing: true);
			using var font = spec.AutoScaling
				? UIFontMetrics.DefaultMetrics.GetScaledFont(baseFont)
				: null;
			var resolvedFont = font ?? baseFont;
			using var platformColor = ToUIColor(spec.Argb);
			using var attributed = new NSAttributedString(spec.Glyph, resolvedFont, platformColor);
			var nativeGlyph = (NSString)spec.Glyph;
			var imageSize = nativeGlyph.GetSizeUsingAttributes(
				attributed.GetUIKitAttributes(0, out _)!);
			if (imageSize.Width <= 0 || imageSize.Height <= 0)
				return null;

			measuredSize = new Size((double)imageSize.Width, (double)imageSize.Height);
			using var format = new UIGraphicsImageRendererFormat
			{
				Opaque = false,
				Scale = UIScreen.MainScreen.Scale,
			};
			using var renderer = new UIGraphicsImageRenderer(imageSize, format);
			using var image = renderer.CreateImage(_ =>
			{
				using var context = new NSStringDrawingContext();
				var bounds = attributed.GetBoundingRect(imageSize, 0, context);
				attributed.DrawString(new CGRect(
					imageSize.Width / 2 - bounds.Size.Width / 2,
					imageSize.Height / 2 - bounds.Size.Height / 2,
					imageSize.Width,
					imageSize.Height));
			});
			return image.AsPNG();
		}

		static UIColor ToUIColor(uint argb) =>
			UIColor.FromRGBA(
				(NFloat)((argb >> 16 & 0xff) / 255d),
				(NFloat)((argb >> 8 & 0xff) / 255d),
				(NFloat)((argb & 0xff) / 255d),
				(NFloat)((argb >> 24) / 255d));
	}
}
#endif
