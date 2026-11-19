using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace Microsoft.Maui.Platforms.Windows.WPF;

/// <summary>Renders font glyphs for WPF image surfaces using the app's font registrations.</summary>
public static class FontImageSourceHelper
{
	const int MaximumDimension = 4096;
	const int MaximumPixels = 4 * 1024 * 1024;

	/// <summary>Renders a system font family. For registered MAUI aliases, use the overload accepting the app's font manager.</summary>
	public static BitmapSource? RenderGlyph(string glyph, string? fontFamily, float size, Graphics.Color? color)
		=> RenderGlyph(glyph, fontFamily, size, color, new WPFFontManager(new WPFFontRegistrar()));

	internal static BitmapSource? RenderGlyph(IFontImageSource source, IMauiContext? context)
		=> RenderGlyph(source.Glyph, source.Font.Family, (float)source.Font.Size, source.Color,
			WPFFontManager.FromContext(context),
			context?.Services.GetService<ILoggerFactory>()?.CreateLogger(typeof(FontImageSourceHelper).FullName!));

	public static BitmapSource? RenderGlyph(string glyph, string? fontFamily, float size, Graphics.Color? color,
		WPFFontManager fontManager, ILogger? logger = null)
	{
		if (string.IsNullOrEmpty(glyph))
			return null;

		if (!float.IsFinite(size) || size <= 0 || size > MaximumDimension)
		{
			Warn($"Cannot render glyph: size {size} must be finite, positive, and at most {MaximumDimension}.", logger);
			return null;
		}

		if (!fontManager.TryGetFontFamily(Font.OfSize(fontFamily, size), out var family))
			return null;
		var typeface = new Typeface(family, FontStyles.Normal, System.Windows.FontWeights.Normal, FontStretches.Normal);
		if (!typeface.TryGetGlyphTypeface(out var native) ||
			glyph.EnumerateRunes().Any(rune => !native.CharacterToGlyphMap.TryGetValue(rune.Value, out var index) || index == 0))
		{
			Warn($"Cannot render glyph: font '{fontFamily}' does not contain the requested characters.", logger);
			return null;
		}

		try
		{
			var wpfColor = color != null
				? System.Windows.Media.Color.FromArgb(
					(byte)(color.Alpha * 255), (byte)(color.Red * 255),
					(byte)(color.Green * 255), (byte)(color.Blue * 255))
				: System.Windows.Media.Colors.Black;
			var text = new FormattedText(glyph, CultureInfo.InvariantCulture,
				System.Windows.FlowDirection.LeftToRight, typeface,
				size, new SolidColorBrush(wpfColor), 1);
			var bounds = new System.Windows.Rect(0, 0, text.WidthIncludingTrailingWhitespace, text.Height);
			if (!TryGetBitmapDimensions(bounds, out var width, out var height))
			{
				Warn($"Cannot render glyph: bounds exceed {MaximumDimension} pixels per side or {MaximumPixels} total pixels.", logger);
				return null;
			}

			var visual = new DrawingVisual();
			using (var drawing = visual.RenderOpen())
			{
				drawing.DrawText(text, new System.Windows.Point(1, 1));
			}
			var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
			bitmap.Render(visual);
			bitmap.Freeze();
			return bitmap;
		}
		catch (Exception ex) when (ex is ArgumentException or OverflowException or InvalidOperationException or System.Runtime.InteropServices.COMException)
		{
			Warn($"Cannot render glyph: {ex.Message}", logger);
			return null;
		}
	}

	static bool TryGetBitmapDimensions(System.Windows.Rect bounds, out int width, out int height)
	{
		width = height = 0;
		if (bounds.IsEmpty || !double.IsFinite(bounds.X) || !double.IsFinite(bounds.Y) ||
			!double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height) ||
			bounds.Width <= 0 || bounds.Height <= 0)
			return false;

		var paddedWidth = Math.Ceiling(bounds.Width) + 2;
		var paddedHeight = Math.Ceiling(bounds.Height) + 2;
		if (paddedWidth > MaximumDimension || paddedHeight > MaximumDimension ||
			paddedWidth * paddedHeight > MaximumPixels)
			return false;

		width = (int)paddedWidth;
		height = (int)paddedHeight;
		return true;
	}

	static void Warn(string message, ILogger? logger)
	{
		if (logger != null)
			logger.LogWarning("{FontImageError}", message);
		else
			System.Diagnostics.Trace.TraceWarning(message);
	}
}
