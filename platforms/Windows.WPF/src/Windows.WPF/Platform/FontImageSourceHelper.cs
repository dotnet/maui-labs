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

		if (!fontManager.TryGetFontFamily(Font.OfSize(fontFamily, size), out var family))
			return null;
		var typeface = new Typeface(family, FontStyles.Normal, System.Windows.FontWeights.Normal, FontStretches.Normal);
		if (!typeface.TryGetGlyphTypeface(out var native) ||
			glyph.EnumerateRunes().Any(rune => !native.CharacterToGlyphMap.TryGetValue(rune.Value, out var index) || index == 0))
		{
			Warn($"Cannot render glyph: font '{fontFamily}' does not contain the requested characters.", logger);
			return null;
		}

		var wpfColor = color != null
			? System.Windows.Media.Color.FromArgb(
				(byte)(color.Alpha * 255), (byte)(color.Red * 255),
				(byte)(color.Green * 255), (byte)(color.Blue * 255))
			: System.Windows.Media.Colors.Black;
		var text = new FormattedText(glyph, CultureInfo.InvariantCulture,
			System.Windows.FlowDirection.LeftToRight, typeface,
			float.IsFinite(size) && size > 0 ? size : 24, new SolidColorBrush(wpfColor), 1);
		var geometry = text.BuildGeometry(new System.Windows.Point());
		var bounds = geometry.Bounds;
		if (bounds.IsEmpty)
			return null;

		var visual = new DrawingVisual();
		using (var drawing = visual.RenderOpen())
		{
			drawing.PushTransform(new TranslateTransform(1 - bounds.X, 1 - bounds.Y));
			drawing.DrawGeometry(new SolidColorBrush(wpfColor), null, geometry);
		}
		var bitmap = new RenderTargetBitmap((int)Math.Ceiling(bounds.Width) + 2,
			(int)Math.Ceiling(bounds.Height) + 2, 96, 96, PixelFormats.Pbgra32);
		bitmap.Render(visual);
		bitmap.Freeze();
		return bitmap;
	}

	static void Warn(string message, ILogger? logger)
	{
		if (logger != null)
			logger.LogWarning("{FontImageError}", message);
		else
			System.Diagnostics.Trace.TraceWarning(message);
	}
}
