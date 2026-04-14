using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WApplication = System.Windows.Application;
using WWindow = System.Windows.Window;

namespace Microsoft.Maui.Platforms.Windows.WPF
{
	/// <summary>
	/// Renders font glyphs to BitmapSource for use in Image, Button.ImageSource, ToolbarItems.
	/// </summary>
	public static class FontImageSourceHelper
	{
		public static BitmapSource? RenderGlyph(string glyph, string fontFamily, float size, Microsoft.Maui.Graphics.Color? color)
		{
			if (string.IsNullOrEmpty(glyph))
				return null;

			try
			{
				var wpfColor = color != null
					? System.Windows.Media.Color.FromArgb(
						(byte)(color.Alpha * 255), (byte)(color.Red * 255),
						(byte)(color.Green * 255), (byte)(color.Blue * 255))
					: System.Windows.Media.Colors.Black;

				var typeface = !string.IsNullOrEmpty(fontFamily)
					? new Typeface(new FontFamily(fontFamily), FontStyles.Normal, System.Windows.FontWeights.Normal, FontStretches.Normal)
					: new Typeface("Segoe UI");

				var formattedText = new FormattedText(
					glyph,
					CultureInfo.InvariantCulture,
					System.Windows.FlowDirection.LeftToRight,
					typeface,
					size > 0 ? size : 24,
					new System.Windows.Media.SolidColorBrush(wpfColor),
					VisualTreeHelper.GetDpi(WApplication.Current?.MainWindow ?? new WWindow()).PixelsPerDip);

				int width = (int)Math.Ceiling(formattedText.Width) + 2;
				int height = (int)Math.Ceiling(formattedText.Height) + 2;

				if (width <= 0 || height <= 0)
					return null;

				var drawingVisual = new DrawingVisual();
				using (var dc = drawingVisual.RenderOpen())
				{
					dc.DrawText(formattedText, new System.Windows.Point(1, 1));
				}

				var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
				bitmap.Render(drawingVisual);
				bitmap.Freeze();

				return bitmap;
			}
			catch
			{
				return null;
			}
		}
	}
}
