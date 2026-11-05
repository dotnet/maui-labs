using System.Buffers.Binary;
using System.ComponentModel;
using System.Globalization;

namespace Microsoft.Maui.DevFlow.Driver;

internal static class ScreenshotImage
{
    internal static async Task<byte[]> ResizeSimulatorAsync(
        string file,
        int? maxWidth = null,
        string? scale = null,
        CancellationToken cancellationToken = default,
        Func<string, string[], CancellationToken, Task<string>>? runProcess = null)
    {
        var original = await File.ReadAllBytesAsync(file, cancellationToken).ConfigureAwait(false);
        if (string.Equals(scale, "native", StringComparison.OrdinalIgnoreCase)
            || string.Equals(scale, "full", StringComparison.OrdinalIgnoreCase))
            return original;

        try
        {
            var size = GetPngDimensions(original);
            if (size == (0, 0))
                throw new InvalidDataException("The simulator screenshot has an invalid PNG header.");

            var target = GetSimulatorSize(size.width, size.height, maxWidth);
            if (target == size)
                return original;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            await (runProcess ?? HostProcess.RunAsync)("/usr/bin/sips",
                ["--resampleHeightWidth", target.height.ToString(CultureInfo.InvariantCulture),
                    target.width.ToString(CultureInfo.InvariantCulture), "--setProperty", "format", "png", file],
                timeout.Token).ConfigureAwait(false);

            var resized = await File.ReadAllBytesAsync(file, cancellationToken).ConfigureAwait(false);
            if (GetPngDimensions(resized) != target)
                throw new InvalidDataException("sips did not produce the requested PNG dimensions.");
            return resized;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Console.Error.WriteLine("Warning: Simulator screenshot resizing timed out; returning native resolution.");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException
            or Win32Exception or InvalidOperationException)
        {
            Console.Error.WriteLine($"Warning: Simulator screenshot resizing failed; returning native resolution. {ex.Message}");
        }

        return original;
    }

    internal static (int width, int height) GetSimulatorSize(int width, int height, int? maxWidth)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        var density = width switch
        {
            2048 or 2388 or 2360 => 2,
            > 1000 => 3,
            _ => 2
        };
        var targetWidth = maxWidth ?? width / density;
        if (targetWidth <= 0 || targetWidth >= width)
            return (width, height);

        // Keep the existing float calculation and truncation, including its rounding at odd sizes.
        var targetHeight = (int)(height * ((float)targetWidth / width));
        return targetHeight > 0 ? (targetWidth, targetHeight) : (width, height);
    }

    internal static (int width, int height) GetPngDimensions(byte[] png)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (png.Length < 24 || !png.AsSpan(0, 8).SequenceEqual(signature))
            return (0, 0);

        var width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16));
        var height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20));
        // Keep the Inspector's bounds for untrusted screenshot headers.
        const int MaxDimension = 32768;
        return width <= 0 || height <= 0 || width > MaxDimension || height > MaxDimension
            ? (0, 0) : (width, height);
    }

    internal static void DeleteTemporaryFile(string file)
    {
        try { File.Delete(file); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Warning: Could not remove screenshot temporary file '{file}': {ex.Message}");
        }
    }
}
