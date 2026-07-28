using System.Runtime.Versioning;
using Microsoft.Graphics.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Microsoft.Maui.Essentials.AI;

/// <summary>
/// Converts between encoded image bytes (PNG, JPEG, ...) and the <see cref="ImageBuffer"/> type
/// used by the Windows AI imaging APIs.
/// </summary>
[SupportedOSPlatform("windows10.0.26100.0")]
internal static class PhiSilicaImageBuffers
{
	/// <summary>The media type used when no other type is requested.</summary>
	public const string DefaultMediaType = "image/png";

	/// <summary>
	/// Decodes encoded image bytes into an <see cref="ImageBuffer"/>.
	/// </summary>
	/// <param name="bytes">The encoded image bytes.</param>
	/// <returns>An <see cref="ImageBuffer"/> in BGRA8 premultiplied format.</returns>
	public static async Task<ImageBuffer> DecodeAsync(ReadOnlyMemory<byte> bytes)
	{
		using var stream = await ToRandomAccessStreamAsync(bytes);

		var decoder = await BitmapDecoder.CreateAsync(stream);

		// The imaging APIs expect a consistent pixel layout, so normalize on decode.
		var bitmap = await decoder.GetSoftwareBitmapAsync(
			BitmapPixelFormat.Bgra8,
			BitmapAlphaMode.Premultiplied);

		using (bitmap)
		{
			return ImageBuffer.CreateForSoftwareBitmap(bitmap);
		}
	}

	/// <summary>
	/// Encodes an <see cref="ImageBuffer"/> into image bytes of the requested media type.
	/// </summary>
	/// <param name="buffer">The image to encode.</param>
	/// <param name="mediaType">The media type to encode as, for example <c>image/png</c>.</param>
	/// <returns>The encoded image bytes.</returns>
	/// <exception cref="NotSupportedException">Thrown when <paramref name="mediaType"/> has no encoder.</exception>
	public static async Task<byte[]> EncodeAsync(ImageBuffer buffer, string mediaType)
	{
		var encoderId = GetEncoderId(mediaType);

		using var bitmap = ToEncodableBitmap(buffer);
		using var stream = new InMemoryRandomAccessStream();

		var encoder = await BitmapEncoder.CreateAsync(encoderId, stream);
		encoder.SetSoftwareBitmap(bitmap);
		await encoder.FlushAsync();

		stream.Seek(0);

		var bytes = new byte[stream.Size];
		using var reader = new DataReader(stream);
		await reader.LoadAsync((uint)stream.Size);
		reader.ReadBytes(bytes);

		return bytes;
	}

	private static SoftwareBitmap ToEncodableBitmap(ImageBuffer buffer)
	{
		var bitmap = buffer.CopyToSoftwareBitmap();

		// BitmapEncoder only accepts BGRA8 with straight or premultiplied alpha.
		if (bitmap.BitmapPixelFormat == BitmapPixelFormat.Bgra8 &&
			bitmap.BitmapAlphaMode != BitmapAlphaMode.Straight)
		{
			return bitmap;
		}

		using (bitmap)
		{
			return SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
		}
	}

	private static async Task<InMemoryRandomAccessStream> ToRandomAccessStreamAsync(ReadOnlyMemory<byte> bytes)
	{
		var stream = new InMemoryRandomAccessStream();
		try
		{
			using (var writer = new DataWriter(stream))
			{
				writer.WriteBytes(bytes.ToArray());
				await writer.StoreAsync();
				await writer.FlushAsync();

				// Detach so disposing the writer does not close the stream we return.
				writer.DetachStream();
			}

			stream.Seek(0);
			return stream;
		}
		catch
		{
			stream.Dispose();
			throw;
		}
	}

	private static Guid GetEncoderId(string mediaType) => mediaType switch
	{
		"image/png" => BitmapEncoder.PngEncoderId,
		"image/jpeg" or "image/jpg" => BitmapEncoder.JpegEncoderId,
		"image/bmp" => BitmapEncoder.BmpEncoderId,
		"image/tiff" => BitmapEncoder.TiffEncoderId,
		_ => throw new NotSupportedException($"No image encoder is available for media type '{mediaType}'.")
	};
}
