using AppKit;
using CoreGraphics;
using Foundation;

using Microsoft.Maui.Platforms.MacOS.Handlers;

namespace Microsoft.Maui.Platforms.MacOS.Platform;

/// <summary>
/// Renders a FontImageSource glyph string into an NSImage.
/// Used by ImageHandler, ImageButtonHandler, and anywhere else
/// that needs to convert an IFontImageSource to a platform image.
/// </summary>
internal static class FontImageSourceHelper
{
	public static NSImage? CreateImage(IFontImageSource fontImageSource, IMauiContext? mauiContext)
	{
		if (string.IsNullOrEmpty(fontImageSource.Glyph))
			return null;

		var fontManager = mauiContext?.Services.GetService(typeof(IFontManager)) as MacOSFontManager;
		if (fontManager == null)
			return null;

		var font = fontImageSource.Font;
		var size = font.Size > 0 ? font.Size : 30.0;
		var nsFont = fontManager.GetFont(font, size);

		var color = fontImageSource.Color ?? Graphics.Colors.Black;
		var nsColor = NSColor.FromRgba(
			(nfloat)color.Red,
			(nfloat)color.Green,
			(nfloat)color.Blue,
			(nfloat)color.Alpha);

		var attrs = new NSStringAttributes
		{
			Font = nsFont,
			ForegroundColor = nsColor,
		};

		var nsString = new NSAttributedString(fontImageSource.Glyph, attrs);
		var glyphSize = nsString.Size;

		if (glyphSize.Width <= 0 || glyphSize.Height <= 0)
			return null;

		var image = new NSImage(glyphSize);
		image.LockFocus();
		nsString.DrawAtPoint(CGPoint.Empty);
		image.UnlockFocus();
		image.Template = false;

		return image;
	}
}
