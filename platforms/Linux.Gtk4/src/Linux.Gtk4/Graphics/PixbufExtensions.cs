using System.Buffers;
using Cairo;
using GdkPixbuf;
using Gio;
using GLib;
using Microsoft.Maui.Graphics;

namespace Microsoft.Maui.Platforms.Linux.Gtk4.Graphics;

internal static class PixbufExtensions
{
	public static void PaintPixbuf(this Context context, Pixbuf pixbuf, double x = 0, double y = 0, double? width = null, double? height = null)
	{
		ArgumentNullException.ThrowIfNull(context);
		ArgumentNullException.ThrowIfNull(pixbuf);

		var translate = x != 0 || y != 0;
		var scale = width.HasValue && height.HasValue && (width.Value != pixbuf.Width || height.Value != pixbuf.Height);

		var saved = translate || scale;
		if (saved)
			context.Save();

		try
		{
			if (translate)
				context.Translate(x, y);

			if (scale)
				context.Scale(width!.Value / pixbuf.Width, height!.Value / pixbuf.Height);

			Gdk.Functions.CairoSetSourcePixbuf(context, pixbuf, 0, 0);

			using var source = context.GetSource();
			if (source is SurfacePattern pattern)
			{
				pattern.Filter = width > pixbuf.Width || height > pixbuf.Height
					? Filter.Fast
					: Filter.Good;
			}

			context.Paint();
		}
		finally
		{
			if (saved)
				context.Restore();
		}
	}

	public static Pixbuf? CreatePixbuf(this ImageSurface? surface)
	{
		if (surface is null)
			return null;

		Cairo.Internal.Surface.Flush(surface.Handle);

		var surfaceData = surface.GetData();
		var bytesPerPixel = surface.Format == Format.Argb32 ? 4 : 3;
		var pixbufData = new byte[surfaceData.Length / 4 * bytesPerPixel];

		var readIndex = 0;
		var writeIndex = 0;
		var stride = surface.Stride;
		var width = surface.Width;

		if (BitConverter.IsLittleEndian)
		{
			for (var row = 0; row < surface.Height; row++)
			{
				var rowStart = readIndex;
				for (var col = 0; col < width; col++)
				{
					var alpha = bytesPerPixel == 4 ? surfaceData[readIndex + 3] : (byte)255;
					var alphaFactor = alpha > 0 ? 255d / alpha : 0d;

					pixbufData[writeIndex] = (byte)(surfaceData[readIndex + 2] * alphaFactor + 0.5);
					pixbufData[writeIndex + 1] = (byte)(surfaceData[readIndex + 1] * alphaFactor + 0.5);
					pixbufData[writeIndex + 2] = (byte)(surfaceData[readIndex] * alphaFactor + 0.5);

					if (bytesPerPixel == 4)
						pixbufData[writeIndex + 3] = alpha;

					readIndex += 4;
					writeIndex += bytesPerPixel;
				}

				readIndex = rowStart + stride;
			}
		}
		else
		{
			for (var row = 0; row < surface.Height; row++)
			{
				var rowStart = readIndex;
				for (var col = 0; col < width; col++)
				{
					var alpha = bytesPerPixel == 4 ? surfaceData[readIndex] : (byte)255;
					var alphaFactor = alpha > 0 ? 255d / alpha : 0d;

					pixbufData[writeIndex] = (byte)(surfaceData[readIndex + 1] * alphaFactor + 0.5);
					pixbufData[writeIndex + 1] = (byte)(surfaceData[readIndex + 2] * alphaFactor + 0.5);
					pixbufData[writeIndex + 2] = (byte)(surfaceData[readIndex + 3] * alphaFactor + 0.5);

					if (bytesPerPixel == 4)
						pixbufData[writeIndex + 3] = alpha;

					readIndex += 4;
					writeIndex += bytesPerPixel;
				}

				readIndex = rowStart + stride;
			}
		}

		return Pixbuf.NewFromBytes(
			Bytes.New(pixbufData),
			Colorspace.Rgb,
			bytesPerPixel == 4,
			8,
			surface.Width,
			surface.Height,
			surface.Width * bytesPerPixel);
	}

	public static void SaveToStream(this Pixbuf? pixbuf, Stream stream, ImageFormat imageFormat = ImageFormat.Png)
	{
		ArgumentNullException.ThrowIfNull(stream);

		if (pixbuf is null)
			throw new InvalidOperationException("Unable to create an image buffer from the Cairo surface.");

		var format = GetImageExtension(imageFormat);

		using var outputStream = MemoryOutputStream.NewResizable();
		var success = pixbuf.SaveToStreamv(outputStream, format, null, null, null);

		if (!success)
			throw new InvalidOperationException("Failed to save image data to the target stream.");

		using var bytes = outputStream.StealAsBytes();
		stream.Write(bytes.GetRegionSpan<byte>(0, bytes.GetSize()));
	}

	public static Pixbuf? LoadFromStream(Stream stream, ImageFormat imageFormat = ImageFormat.Png)
	{
		ArgumentNullException.ThrowIfNull(stream);
		_ = GetImageExtension(imageFormat);

		using var loader = PixbufLoader.New();
		var buffer = ArrayPool<byte>.Shared.Rent(8192);

		try
		{
			while (true)
			{
				var bytesRead = stream.Read(buffer, 0, buffer.Length);
				if (bytesRead == 0)
					break;

				loader.Write(buffer.AsSpan(0, bytesRead));
			}
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(buffer);
		}

		loader.Close();
		var pixbuf = loader.GetPixbuf();
		return pixbuf?.Copy();
	}

	private static string GetImageExtension(ImageFormat imageFormat) =>
		imageFormat.ToImageExtension()
		?? throw new NotSupportedException($"Image format '{imageFormat}' is not supported on GTK.");

	private static string? ToImageExtension(this ImageFormat imageFormat) =>
		imageFormat switch
		{
			ImageFormat.Bmp => "bmp",
			ImageFormat.Png => "png",
			ImageFormat.Jpeg => "jpeg",
			ImageFormat.Gif => "gif",
			ImageFormat.Tiff => "tiff",
			_ => null,
		};
}
