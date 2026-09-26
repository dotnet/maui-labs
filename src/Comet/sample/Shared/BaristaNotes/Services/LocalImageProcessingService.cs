using System;
using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Graphics.Platform;

namespace CometBaristaNotes.Services;

public sealed class LocalImageProcessingService : IImageProcessingService
{
    private const long MaximumImageBytes = 12 * 1024 * 1024;
    private readonly string _rootDirectory;
    private readonly IImageCodec _codec;

    public LocalImageProcessingService(string? rootDirectory = null)
        : this(rootDirectory, new PlatformImageCodec())
    {
    }

    internal LocalImageProcessingService(string? rootDirectory, IImageCodec codec)
    {
        _rootDirectory = Path.GetFullPath(rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BaristaNotes"));
        _codec = codec ?? throw new ArgumentNullException(nameof(codec));
        Directory.CreateDirectory(_rootDirectory);
    }

    public async Task<ImageValidationResult> ValidateImageAsync(Stream imageStream)
    {
        if (!imageStream.CanRead)
            return ImageValidationResult.Invalid("Image stream is not readable");

        try
        {
            if (imageStream.CanSeek && imageStream.Length > MaximumImageBytes)
                return ImageValidationResult.Invalid("Image is too large");
            var buffer = ArrayPool<byte>.Shared.Rent((int)MaximumImageBytes + 1);
            try
            {
                var length = await ReadBoundedAsync(imageStream, buffer);
                if (length < 0)
                    return ImageValidationResult.Invalid("Image is too large");
                if (length == 0)
                    return ImageValidationResult.Invalid("Image is empty");
                if (!_codec.CanDecode(buffer.AsMemory(0, length)))
                    return ImageValidationResult.Invalid("Image format is unsupported or cannot be decoded");
                return ImageValidationResult.Valid();
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        catch
        {
            return ImageValidationResult.Invalid("Image format is unsupported or cannot be decoded");
        }
        finally
        {
            if (imageStream.CanSeek)
                imageStream.Position = 0;
        }
    }

    public async Task<string> SaveImageAsync(Stream imageStream, string filename)
    {
        var path = ResolveManagedPath(filename);
        if (imageStream.CanSeek && imageStream.Length > MaximumImageBytes)
            throw new InvalidDataException("Image is too large");
        var buffer = ArrayPool<byte>.Shared.Rent((int)MaximumImageBytes + 1);
        try
        {
            var length = await ReadBoundedAsync(imageStream, buffer);
            if (length < 0)
                throw new InvalidDataException("Image is too large");
            if (length < 4
                || buffer[0] != 0xFF
                || buffer[1] != 0xD8
                || buffer[length - 2] != 0xFF
                || buffer[length - 1] != 0xD9)
                throw new InvalidDataException("Only JPEG-encoded image data can be persisted");

            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await output.WriteAsync(buffer.AsMemory(0, length));
            return path;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            if (imageStream.CanSeek)
                imageStream.Position = 0;
        }
    }

    public Task<bool> DeleteImageAsync(string filename)
    {
        var path = ResolveManagedPath(filename);
        if (!File.Exists(path))
            return Task.FromResult(false);
        File.Delete(path);
        return Task.FromResult(true);
    }

    public string GetImagePath(string filename) => ResolveManagedPath(filename);

    public bool ImageExists(string filename)
    {
        try
        {
            return File.Exists(ResolveManagedPath(filename));
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public async Task<MemoryStream?> DownsampleAsync(Stream imageStream, int maxDimension, int quality)
    {
        if (maxDimension < 1)
            throw new ArgumentOutOfRangeException(nameof(maxDimension));
        if (quality is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(quality));
        if (imageStream.CanSeek && imageStream.Length > MaximumImageBytes)
            return null;
        var buffer = ArrayPool<byte>.Shared.Rent((int)MaximumImageBytes + 1);
        try
        {
            var length = await ReadBoundedAsync(imageStream, buffer);
            if (length <= 0)
                return null;
            return await _codec.DownsampleToJpegAsync(buffer.AsMemory(0, length), maxDimension, quality);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            if (imageStream.CanSeek)
                imageStream.Position = 0;
        }
    }

    private static async Task<int> ReadBoundedAsync(Stream input, byte[] buffer)
    {
        if (input.CanSeek)
        {
            input.Position = 0;
            if (input.Length > MaximumImageBytes)
                return -1;
        }

        var total = 0;
        var maximumRead = (int)MaximumImageBytes + 1;
        while (total <= MaximumImageBytes)
        {
            var remaining = Math.Min(maximumRead - total, 81920);
            var read = await input.ReadAsync(buffer.AsMemory(total, remaining));
            if (read == 0)
                return total;
            total += read;
            if (total > MaximumImageBytes)
                return -1;
        }
        return -1;
    }

    private string ResolveManagedPath(string filename)
    {
        if (string.IsNullOrWhiteSpace(filename)
            || Path.IsPathRooted(filename)
            || !string.Equals(Path.GetFileName(filename), filename, StringComparison.Ordinal))
            throw new ArgumentException("Image filename must be a managed leaf filename.", nameof(filename));

        return Path.Combine(_rootDirectory, filename);
    }
}

internal interface IImageCodec
{
    bool CanDecode(ReadOnlyMemory<byte> bytes);
    Task<MemoryStream?> DownsampleToJpegAsync(ReadOnlyMemory<byte> bytes, int maxDimension, int quality);
}

internal sealed class PlatformImageCodec : IImageCodec
{
    public bool CanDecode(ReadOnlyMemory<byte> bytes)
    {
        try
        {
#if ANDROID
            if (!MemoryMarshal.TryGetArray(bytes, out var segment))
                return false;
            var options = new Android.Graphics.BitmapFactory.Options { InJustDecodeBounds = true };
            Android.Graphics.BitmapFactory.DecodeByteArray(
                segment.Array!,
                segment.Offset,
                segment.Count,
                options);
            return options.OutWidth > 0 && options.OutHeight > 0;
#elif IOS || MACCATALYST
            using var data = Foundation.NSData.FromArray(bytes.ToArray());
            using var source = ImageIO.CGImageSource.FromData(data);
            return source is not null && source.ImageCount > 0;
#else
            using var stream = new MemoryStream(bytes.ToArray(), writable: false);
            using var image = PlatformImage.FromStream(stream);
            return image is not null && image.Width > 0 && image.Height > 0;
#endif
        }
        catch
        {
            return false;
        }
    }

    public Task<MemoryStream?> DownsampleToJpegAsync(ReadOnlyMemory<byte> bytes, int maxDimension, int quality)
    {
        try
        {
#if ANDROID
            if (!MemoryMarshal.TryGetArray(bytes, out var segment))
                return Task.FromResult<MemoryStream?>(null);
            var bounds = new Android.Graphics.BitmapFactory.Options { InJustDecodeBounds = true };
            Android.Graphics.BitmapFactory.DecodeByteArray(
                segment.Array!,
                segment.Offset,
                segment.Count,
                bounds);
            if (bounds.OutWidth <= 0 || bounds.OutHeight <= 0)
                return Task.FromResult<MemoryStream?>(null);
            var sampleSize = CalculateInSampleSize(bounds.OutWidth, bounds.OutHeight, maxDimension);
            var decodeOptions = new Android.Graphics.BitmapFactory.Options { InSampleSize = sampleSize };
            using var source = Android.Graphics.BitmapFactory.DecodeByteArray(
                segment.Array!,
                segment.Offset,
                segment.Count,
                decodeOptions);
            if (source is null)
                return Task.FromResult<MemoryStream?>(null);
            var scale = Math.Min(1d, Math.Min((double)maxDimension / source.Width, (double)maxDimension / source.Height));
            var width = Math.Max(1, (int)Math.Round(source.Width * scale));
            var height = Math.Max(1, (int)Math.Round(source.Height * scale));
            var scaled = scale < 1d
                ? Android.Graphics.Bitmap.CreateScaledBitmap(source, width, height, true)
                : source;
            try
            {
                var output = new MemoryStream();
                if (!scaled.Compress(Android.Graphics.Bitmap.CompressFormat.Jpeg!, quality, output))
                {
                    output.Dispose();
                    return Task.FromResult<MemoryStream?>(null);
                }
                output.Position = 0;
                return Task.FromResult<MemoryStream?>(output);
            }
            finally
            {
                if (!ReferenceEquals(scaled, source))
                    scaled.Dispose();
            }
#elif IOS || MACCATALYST
            using var data = Foundation.NSData.FromArray(bytes.ToArray());
            using var source = ImageIO.CGImageSource.FromData(data);
            if (source is null || source.ImageCount == 0)
                return Task.FromResult<MemoryStream?>(null);
            var options = new ImageIO.CGImageThumbnailOptions
            {
                CreateThumbnailFromImageAlways = true,
                CreateThumbnailWithTransform = true,
                MaxPixelSize = maxDimension
            };
            using var thumbnail = source.CreateThumbnail(0, options);
            if (thumbnail is null)
                return Task.FromResult<MemoryStream?>(null);
            using var image = UIKit.UIImage.FromImage(thumbnail);
            using var jpeg = image.AsJPEG((nfloat)(quality / 100.0));
            return Task.FromResult<MemoryStream?>(
                jpeg is null ? null : new MemoryStream(jpeg.ToArray(), writable: false));
#else
            using var stream = new MemoryStream(bytes.ToArray(), writable: false);
            using var image = PlatformImage.FromStream(stream);
            if (image is null || image.Width <= 0 || image.Height <= 0)
                return Task.FromResult<MemoryStream?>(null);
            var scaled = image.Width > maxDimension || image.Height > maxDimension
                ? image.Downsize(maxDimension, true)
                : image;
            try
            {
                var output = new MemoryStream();
                scaled.Save(output, ImageFormat.Jpeg, quality / 100f);
                output.Position = 0;
                return Task.FromResult<MemoryStream?>(output);
            }
            finally
            {
                if (!ReferenceEquals(scaled, image))
                    scaled.Dispose();
            }
#endif
        }
        catch
        {
            return Task.FromResult<MemoryStream?>(null);
        }
    }

    internal static int CalculateInSampleSize(int width, int height, int maxDimension)
    {
        if (width <= 0 || height <= 0 || maxDimension <= 0)
            return 1;
        var longestDimension = Math.Max(width, height);
        var sampleSize = 1;
        while (longestDimension / (sampleSize * 2) >= maxDimension)
            sampleSize *= 2;
        return sampleSize;
    }
}
