using System.Runtime.InteropServices;
using Microsoft.Maui.Graphics;
using IImage = Microsoft.Maui.Graphics.IImage;

namespace Platform.Maui.Linux.Gtk4.Graphics;

/// <summary>
/// IImage implementation backed by a Cairo.ImageSurface.
/// Supports loading from streams and rendering via CairoCanvas.DrawImage.
/// </summary>
internal class CairoPlatformImage : IImage
{
	public CairoPlatformImage(Cairo.ImageSurface surface)
	{
		Surface = surface ?? throw new ArgumentNullException(nameof(surface));
	}

	internal Cairo.ImageSurface Surface { get; }

	public float Width => cairo_image_surface_get_width(Surface.Handle.DangerousGetHandle());
	public float Height => cairo_image_surface_get_height(Surface.Handle.DangerousGetHandle());

	public IImage Downsize(float maxWidthOrHeight, bool disposeOriginal = false)
	{
		return Downsize(maxWidthOrHeight, maxWidthOrHeight, disposeOriginal);
	}

	public IImage Downsize(float maxWidth, float maxHeight, bool disposeOriginal = false)
	{
		var ratioX = maxWidth / Width;
		var ratioY = maxHeight / Height;
		var ratio = Math.Min(ratioX, ratioY);
		if (ratio >= 1)
			return this;

		int newWidth = (int)(Width * ratio);
		int newHeight = (int)(Height * ratio);
		var newSurface = new Cairo.ImageSurface(Cairo.Format.Argb32, newWidth, newHeight);
		var cr = new Cairo.Context(newSurface);

		cr.Scale(ratio, ratio);
		cr.SetSourceSurface(Surface, 0, 0);
		cr.Paint();
		cr.Dispose();

		if (disposeOriginal)
			Dispose();

		return new CairoPlatformImage(newSurface);
	}

	public IImage ToPlatformImage()
	{
		return this;
	}

	public void Draw(ICanvas canvas, RectF dirtyRect)
	{
		canvas.DrawImage(this, dirtyRect.X, dirtyRect.Y, dirtyRect.Width, dirtyRect.Height);
	}

	public IImage Resize(float width, float height, ResizeMode resizeMode = ResizeMode.Fit, bool disposeOriginal = false)
	{
		int newWidth = (int)width;
		int newHeight = (int)height;
		var newSurface = new Cairo.ImageSurface(Cairo.Format.Argb32, newWidth, newHeight);
		var cr = new Cairo.Context(newSurface);

		double scaleX = width / Width;
		double scaleY = height / Height;

		switch (resizeMode)
		{
			case ResizeMode.Fit:
				double fitScale = Math.Min(scaleX, scaleY);
				double offsetX = (width - Width * fitScale) / 2;
				double offsetY = (height - Height * fitScale) / 2;
				cr.Translate(offsetX, offsetY);
				cr.Scale(fitScale, fitScale);
				break;
			case ResizeMode.Bleed:
				double bleedScale = Math.Max(scaleX, scaleY);
				double bleedOffsetX = (width - Width * bleedScale) / 2;
				double bleedOffsetY = (height - Height * bleedScale) / 2;
				cr.Translate(bleedOffsetX, bleedOffsetY);
				cr.Scale(bleedScale, bleedScale);
				break;
			default:
				cr.Scale(scaleX, scaleY);
				break;
		}

		cr.SetSourceSurface(Surface, 0, 0);
		cr.Paint();
		cr.Dispose();

		if (disposeOriginal)
			Dispose();

		return new CairoPlatformImage(newSurface);
	}

	public void Save(Stream stream, ImageFormat format = ImageFormat.Png, float quality = 1)
	{
		var tmpPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"maui_img_{Guid.NewGuid():N}.png");
		try
		{
			cairo_surface_write_to_png(Surface.Handle.DangerousGetHandle(), tmpPath);
			using var fs = File.OpenRead(tmpPath);
			fs.CopyTo(stream);
		}
		finally
		{
			try { File.Delete(tmpPath); } catch { }
		}
	}

	public Task SaveAsync(Stream stream, ImageFormat format = ImageFormat.Png, float quality = 1)
	{
		Save(stream, format, quality);
		return Task.CompletedTask;
	}

	/// <summary>
	/// Creates a CairoPlatformImage from a PNG stream.
	/// </summary>
	public static CairoPlatformImage? FromStream(Stream stream)
	{
		var tmpPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"maui_img_{Guid.NewGuid():N}.png");
		try
		{
			using (var fs = File.Create(tmpPath))
				stream.CopyTo(fs);

			var surfaceHandle = cairo_image_surface_create_from_png(tmpPath);
			if (surfaceHandle == nint.Zero)
				return null;

			// Wrap the unmanaged surface handle. We use an ImageSurface that manages its own lifetime.
			var surface = new Cairo.ImageSurface(Cairo.Format.Argb32,
				cairo_image_surface_get_width(surfaceHandle),
				cairo_image_surface_get_height(surfaceHandle));

			// Copy the PNG data onto our managed surface
			var cr = new Cairo.Context(surface);
			cairo_set_source_surface(cr.Handle.DangerousGetHandle(), surfaceHandle, 0, 0);
			Cairo.Internal.Context.Paint(cr.Handle);
			cr.Dispose();
			cairo_surface_destroy(surfaceHandle);

			return new CairoPlatformImage(surface);
		}
		finally
		{
			try { File.Delete(tmpPath); } catch { }
		}
	}

	public void Dispose()
	{
		Surface?.Dispose();
	}

	[DllImport("libcairo.so.2")]
	private static extern int cairo_surface_write_to_png(nint surface, [MarshalAs(UnmanagedType.LPUTF8Str)] string filename);

	[DllImport("libcairo.so.2")]
	private static extern nint cairo_image_surface_create_from_png([MarshalAs(UnmanagedType.LPUTF8Str)] string filename);

	[DllImport("libcairo.so.2")]
	private static extern int cairo_image_surface_get_width(nint surface);

	[DllImport("libcairo.so.2")]
	private static extern int cairo_image_surface_get_height(nint surface);

	[DllImport("libcairo.so.2")]
	private static extern void cairo_set_source_surface(nint cr, nint surface, double x, double y);

	[DllImport("libcairo.so.2")]
	private static extern void cairo_surface_destroy(nint surface);
}
