using System.Runtime.InteropServices;
using Microsoft.Maui.Graphics;
using Platform.Maui.Linux.Gtk4.Graphics;
using IImage = Microsoft.Maui.Graphics.IImage;

namespace Platform.Maui.Linux.Gtk4.Graphics;

/// <summary>
/// IStringSizeService implementation using Pango text layout.
/// Measures text without requiring an active drawing context.
/// </summary>
internal class CairoStringSizeService : IStringSizeService
{
	public SizeF GetStringSize(string value, IFont font, float fontSize)
	{
		if (string.IsNullOrEmpty(value))
			return SizeF.Zero;

		// Create a temporary Cairo surface + context for Pango measurement
		var surface = new Cairo.ImageSurface(Cairo.Format.Argb32, 1, 1);
		var cr = new Cairo.Context(surface);

		var layout = PangoCairo.Functions.CreateLayout(cr);
		var fontDesc = Pango.FontDescription.New();
		fontDesc.SetFamily(font?.Name ?? "Sans");
		fontDesc.SetAbsoluteSize(fontSize * Pango.Constants.SCALE);
		fontDesc.SetWeight((font?.Weight ?? 400) >= 600 ? Pango.Weight.Bold : Pango.Weight.Normal);
		fontDesc.SetStyle(font?.StyleType switch
		{
			FontStyleType.Italic => Pango.Style.Italic,
			FontStyleType.Oblique => Pango.Style.Oblique,
			_ => Pango.Style.Normal,
		});
		layout.SetFontDescription(fontDesc);
		layout.SetText(value, -1);

		layout.GetPixelSize(out int textW, out int textH);

		cr.Dispose();
		surface.Dispose();

		return new SizeF(textW, textH);
	}

	public SizeF GetStringSize(string value, IFont font, float fontSize,
		HorizontalAlignment horizontalAlignment, VerticalAlignment verticalAlignment)
	{
		return GetStringSize(value, font, fontSize);
	}
}

/// <summary>
/// IBitmapExportService implementation using Cairo ImageSurface.
/// Creates export contexts that render MAUI.Graphics drawing commands to bitmaps.
/// </summary>
internal class CairoBitmapExportService : IBitmapExportService
{
	public BitmapExportContext CreateContext(int width, int height, float displayScale = 1)
	{
		return new CairoBitmapExportContext(width, height, displayScale);
	}
}

/// <summary>
/// BitmapExportContext backed by a Cairo ImageSurface.
/// Provides an ICanvas for drawing and produces an IImage result.
/// </summary>
internal class CairoBitmapExportContext : BitmapExportContext
{
	private readonly Cairo.ImageSurface _surface;
	private readonly Cairo.Context _cr;
	private readonly CairoCanvas _canvas;

	public CairoBitmapExportContext(int width, int height, float displayScale)
		: base(width, height, displayScale)
	{
		int scaledWidth = (int)(width * displayScale);
		int scaledHeight = (int)(height * displayScale);
		_surface = new Cairo.ImageSurface(Cairo.Format.Argb32, scaledWidth, scaledHeight);
		_cr = new Cairo.Context(_surface);

		if (displayScale != 1)
			_cr.Scale(displayScale, displayScale);

		_canvas = new CairoCanvas(_cr);
	}

	public override ICanvas Canvas => _canvas;

	public override IImage Image
	{
		get
		{
			Cairo.Internal.Surface.Flush(_surface.Handle);
			return new CairoPlatformImage(_surface);
		}
	}

	public override void WriteToStream(Stream stream)
	{
		Cairo.Internal.Surface.Flush(_surface.Handle);

		var tmpPath = Path.Combine(Path.GetTempPath(), $"maui_export_{Guid.NewGuid():N}.png");
		try
		{
			cairo_surface_write_to_png(_surface.Handle.DangerousGetHandle(), tmpPath);
			using var fs = File.OpenRead(tmpPath);
			fs.CopyTo(stream);
		}
		finally
		{
			try { File.Delete(tmpPath); } catch { }
		}
	}

	public override void Dispose()
	{
		_cr?.Dispose();
		// Don't dispose _surface here — it may still be referenced via Image
	}

	[DllImport("libcairo.so.2")]
	private static extern int cairo_surface_write_to_png(nint surface,
		[MarshalAs(UnmanagedType.LPUTF8Str)] string filename);
}

/// <summary>
/// IImageLoadingService implementation using Cairo PNG loading.
/// </summary>
internal class CairoImageLoadingService : IImageLoadingService
{
	public IImage FromStream(Stream stream, ImageFormat format = ImageFormat.Png)
	{
		ArgumentNullException.ThrowIfNull(stream);

		var image = CairoPlatformImage.FromStream(stream);
		if (image == null)
			throw new ArgumentException("Could not decode image from stream.");

		return image;
	}
}
