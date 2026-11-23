#nullable enable
using System;

namespace Comet.Backend
{
	/// <summary>
	/// Sanitizes fixed Compose dimensions using the packed <c>Constraints</c> limits.
	/// Compose stores four dimensions in one 64-bit value: the larger axis receives
	/// at most 18 bits and the other axis receives the remaining 13, 15, or 16 bits.
	/// </summary>
	/// <remarks>
	/// Thresholds mirror AndroidX Compose's Constraints.kt:
	/// https://android.googlesource.com/platform/frameworks/support/+/androidx-main/compose/ui/ui-unit/src/commonMain/kotlin/androidx/compose/ui/unit/Constraints.kt
	/// </remarks>
	internal static class ComposeConstraintSanitizer
	{
		internal readonly record struct FrameSize(
			float? Width,
			float? Height,
			bool WasSanitized);

		const int Max13BitValue = 8_190;
		const int Max15BitValue = 32_766;
		const int Max16BitValue = 65_534;
		const int Max18BitValue = 262_142;

		public static FrameSize Sanitize(float widthDp, float heightDp, float density)
		{
			if (!float.IsFinite(density) || density <= 0)
				density = 1;

			var width = ToPixels(widthDp, density);
			var height = ToPixels(heightDp, density);
			bool sanitized = width.WasSanitized || height.WasSanitized;

			if (width.Pixels is { } w && height.Pixels is { } h &&
				BitsNeeded(w) + BitsNeeded(h) > 31)
			{
				// Match Compose's fitPrioritizing* semantics: preserve the axis that
				// needs more bits and cap the other to the exact remaining packed range.
				if (w >= h)
				{
					int maxHeight = MaxAllowedForSize(w);
					if (h > maxHeight)
					{
						height = height with { Pixels = maxHeight, WasSanitized = true };
						sanitized = true;
					}
				}
				else
				{
					int maxWidth = MaxAllowedForSize(h);
					if (w > maxWidth)
					{
						width = width with { Pixels = maxWidth, WasSanitized = true };
						sanitized = true;
					}
				}
			}

			return new FrameSize(
				ToDp(width, widthDp, density),
				ToDp(height, heightDp, density),
				sanitized);
		}

		readonly record struct PixelDimension(int? Pixels, bool WasSanitized);

		static PixelDimension ToPixels(float dp, float density)
		{
			if (!float.IsFinite(dp) || dp < 0)
				return new PixelDimension(null, true);

			double pixels = (double)dp * density;
			if (!double.IsFinite(pixels))
				return new PixelDimension(null, true);

			// Density.roundToPx uses nearest-integer rounding. Clamp only after that
			// conversion so every representable large Dp value remains unchanged.
			double rounded = Math.Floor(pixels + 0.5d);
			if (rounded > Max18BitValue)
				return new PixelDimension(Max18BitValue, true);

			return new PixelDimension((int)rounded, false);
		}

		static float? ToDp(PixelDimension value, float originalDp, float density)
			=> value.Pixels is null
				? null
				: value.WasSanitized
					? value.Pixels.Value / density
					: originalDp;

		static int BitsNeeded(int size)
			=> size <= Max13BitValue ? 13
				: size <= Max15BitValue ? 15
				: size <= Max16BitValue ? 16
				: size <= Max18BitValue ? 18
				: 255;

		static int MaxAllowedForSize(int size)
			=> size <= Max13BitValue ? Max18BitValue
				: size <= Max15BitValue ? Max16BitValue
				: size <= Max16BitValue ? Max15BitValue
				: Max13BitValue;
	}
}
