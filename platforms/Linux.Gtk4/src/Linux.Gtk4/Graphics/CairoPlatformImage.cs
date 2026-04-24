using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Graphics;

/// <summary>
/// IImage implementation backed by a Cairo.ImageSurface.
/// Supports loading from streams and rendering via CairoCanvas.DrawImage.
/// </summary>
internal class CairoPlatformImage : global::Microsoft.Maui.Graphics.IImage
{
	public CairoPlatformImage(Cairo.ImageSurface surface)
	{
		Surface = surface ?? throw new ArgumentNullException(nameof(surface));
	}

	internal Cairo.ImageSurface Surface { get; }

	public float Width => Surface.Width;
	public float Height => Surface.Height;

	public global::Microsoft.Maui.Graphics.IImage Downsize(float maxWidthOrHeight, bool disposeOriginal = false)
	{
		return Downsize(maxWidthOrHeight, maxWidthOrHeight, disposeOriginal);
	}

	public global::Microsoft.Maui.Graphics.IImage Downsize(float maxWidth, float maxHeight, bool disposeOriginal = false)
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

	public global::Microsoft.Maui.Graphics.IImage ToPlatformImage()
	{
		return this;
	}

	public void Draw(global::Microsoft.Maui.Graphics.ICanvas canvas, RectF dirtyRect)
	{
		canvas.DrawImage(this, dirtyRect.X, dirtyRect.Y, dirtyRect.Width, dirtyRect.Height);
	}

	public global::Microsoft.Maui.Graphics.IImage Resize(float width, float height, ResizeMode resizeMode = ResizeMode.Fit, bool disposeOriginal = false)
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
		using var pixbuf = Surface.CreatePixbuf();
		pixbuf.SaveToStream(stream, format);
	}

	public Task SaveAsync(Stream stream, ImageFormat format = ImageFormat.Png, float quality = 1)
	{
		Save(stream, format, quality);
		return Task.CompletedTask;
	}

	/// <summary>
	/// Creates a CairoPlatformImage from a stream.
	/// The format value is validated for GTK support, while GdkPixbuf
	/// detects the actual decoder from the stream contents.
	/// </summary>
	public static CairoPlatformImage? FromStream(Stream stream, ImageFormat format = ImageFormat.Png)
	{
		using var pixbuf = PixbufExtensions.LoadFromStream(stream, format);
		if (pixbuf is null)
			return null;

		var surface = new Cairo.ImageSurface(Cairo.Format.Argb32, pixbuf.Width, pixbuf.Height);
		using var cr = new Cairo.Context(surface);
		cr.PaintPixbuf(pixbuf);

		return new CairoPlatformImage(surface);
	}

	private bool _disposed;

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		Surface?.Dispose();
	}
}
