using System.Buffers.Binary;
using System.ComponentModel;
using System.Globalization;
using System.IO.Compression;

namespace Microsoft.Maui.DevFlow.Driver;

internal static class ScreenshotImage
{
    private static readonly uint[] PngCrcTable = CreatePngCrcTable();

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

    internal static bool IsCompletePng(byte[] png)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (png.Length < 45 || !png.AsSpan(0, 8).SequenceEqual(signature))
            return false;

        var offset = 8;
        var sawHeader = false;
        var sawPalette = false;
        var sawTransparency = false;
        var sawImageData = false;
        var imageDataEnded = false;
        var width = 0;
        var height = 0;
        byte bitDepth = 0;
        byte colorType = 0;
        byte interlaceMethod = 0;
        var paletteEntries = 0;
        using var compressedImageData = new MemoryStream();
        while (offset <= png.Length - 12)
        {
            var chunkLength = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset, 4));
            var chunkEnd = (long)offset + 12 + chunkLength;
            if (chunkEnd > png.Length)
                return false;

            var chunkType = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset + 4, 4));
            var chunkDataLength = checked((int)chunkLength);
            var storedCrc = BinaryPrimitives.ReadUInt32BigEndian(
                png.AsSpan(offset + 8 + chunkDataLength, 4));
            if (ComputePngCrc(png.AsSpan(offset + 4, 4 + chunkDataLength)) != storedCrc)
                return false;

            if (sawImageData && chunkType != 0x49444154)
                imageDataEnded = true;

            switch (chunkType)
            {
                case 0x49484452: // IHDR
                    if (sawHeader || offset != 8 || chunkLength != 13)
                    {
                        return false;
                    }
                    (width, height) = GetPngDimensions(png);
                    bitDepth = png[offset + 16];
                    colorType = png[offset + 17];
                    var compressionMethod = png[offset + 18];
                    var filterMethod = png[offset + 19];
                    interlaceMethod = png[offset + 20];
                    if (width <= 0 || height <= 0 ||
                        !IsValidPngBitDepth(colorType, bitDepth) ||
                        compressionMethod != 0 ||
                        filterMethod != 0 ||
                        interlaceMethod > 1)
                    {
                        return false;
                    }
                    sawHeader = true;
                    break;
                case 0x504C5445: // PLTE
                    if (!sawHeader || sawPalette || sawImageData ||
                        colorType is 0 or 4 ||
                        chunkLength == 0 || chunkLength % 3 != 0 ||
                        chunkLength > 768)
                    {
                        return false;
                    }
                    paletteEntries = chunkDataLength / 3;
                    if (colorType == 3 && paletteEntries > 1 << bitDepth)
                        return false;
                    sawPalette = true;
                    break;
                case 0x74524E53: // tRNS
                    if (!sawHeader || sawTransparency || sawImageData ||
                        colorType switch
                        {
                            0 => chunkLength != 2,
                            2 => chunkLength != 6,
                            3 => !sawPalette || chunkLength == 0 || chunkLength > paletteEntries,
                            _ => true
                        })
                    {
                        return false;
                    }
                    sawTransparency = true;
                    break;
                case 0x49444154: // IDAT
                    if (!sawHeader || imageDataEnded || (colorType == 3 && !sawPalette))
                        return false;
                    compressedImageData.Write(png, offset + 8, chunkDataLength);
                    sawImageData = true;
                    break;
                case 0x49454E44: // IEND
                    return sawHeader &&
                        sawImageData &&
                        chunkLength == 0 &&
                        chunkEnd == png.Length &&
                        HasValidPngImageData(
                            compressedImageData,
                            width,
                            height,
                            bitDepth,
                            colorType,
                            interlaceMethod);
                default:
                    // Unknown critical chunks cannot be decoded safely. Ancillary chunks are
                    // identified by the lowercase bit on the first type byte.
                    if ((png[offset + 4] & 0x20) == 0)
                        return false;
                    break;
            }

            offset = checked((int)chunkEnd);
        }

        return false;
    }

    private static bool IsValidPngBitDepth(byte colorType, byte bitDepth)
        => colorType switch
        {
            0 => bitDepth is 1 or 2 or 4 or 8 or 16,
            2 => bitDepth is 8 or 16,
            3 => bitDepth is 1 or 2 or 4 or 8,
            4 => bitDepth is 8 or 16,
            6 => bitDepth is 8 or 16,
            _ => false
        };

    private static bool HasValidPngImageData(
        MemoryStream compressedImageData,
        int width,
        int height,
        byte bitDepth,
        byte colorType,
        byte interlaceMethod)
    {
        var channels = colorType switch
        {
            0 or 3 => 1,
            2 => 3,
            4 => 2,
            6 => 4,
            _ => 0
        };
        if (channels == 0)
            return false;

        try
        {
            var bitsPerPixel = checked(channels * bitDepth);
            long decodedBytes;
            if (interlaceMethod == 0)
            {
                decodedBytes = GetPngPassByteCount(width, height, bitsPerPixel);
            }
            else
            {
                ReadOnlySpan<int> startX = [0, 4, 0, 2, 0, 1, 0];
                ReadOnlySpan<int> startY = [0, 0, 4, 0, 2, 0, 1];
                ReadOnlySpan<int> stepX = [8, 8, 4, 4, 2, 2, 1];
                ReadOnlySpan<int> stepY = [8, 8, 8, 4, 4, 2, 2];
                decodedBytes = 0;
                for (var pass = 0; pass < 7; pass++)
                {
                    var passWidth = GetAdam7Dimension(width, startX[pass], stepX[pass]);
                    var passHeight = GetAdam7Dimension(height, startY[pass], stepY[pass]);
                    decodedBytes = checked(
                        decodedBytes + GetPngPassByteCount(passWidth, passHeight, bitsPerPixel));
                }
            }
            const long MaxDecodedPngBytes = 512L * 1024 * 1024;
            if (decodedBytes <= 0 || decodedBytes > MaxDecodedPngBytes)
                return false;

            compressedImageData.Position = 0;
            using var zlib = new ZLibStream(
                compressedImageData,
                CompressionMode.Decompress,
                leaveOpen: true);
            if (interlaceMethod == 0)
            {
                if (!ValidatePngPass(zlib, width, height, bitsPerPixel))
                    return false;
            }
            else
            {
                ReadOnlySpan<int> startX = [0, 4, 0, 2, 0, 1, 0];
                ReadOnlySpan<int> startY = [0, 0, 4, 0, 2, 0, 1];
                ReadOnlySpan<int> stepX = [8, 8, 4, 4, 2, 2, 1];
                ReadOnlySpan<int> stepY = [8, 8, 8, 4, 4, 2, 2];
                for (var pass = 0; pass < 7; pass++)
                {
                    var passWidth = GetAdam7Dimension(width, startX[pass], stepX[pass]);
                    var passHeight = GetAdam7Dimension(height, startY[pass], stepY[pass]);
                    if (passWidth > 0 && passHeight > 0 &&
                        !ValidatePngPass(zlib, passWidth, passHeight, bitsPerPixel))
                    {
                        return false;
                    }
                }
            }

            return zlib.ReadByte() == -1;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or OverflowException)
        {
            return false;
        }
    }

    private static bool ValidatePngPass(
        Stream decompressed,
        int width,
        int height,
        int bitsPerPixel)
    {
        var rowBytes = checked((width * (long)bitsPerPixel + 7) / 8);
        if (rowBytes > int.MaxValue - 1)
            return false;

        var row = new byte[checked((int)rowBytes + 1)];
        for (var y = 0; y < height; y++)
        {
            decompressed.ReadExactly(row);
            if (row[0] > 4)
                return false;
        }

        return true;
    }

    private static long GetPngPassByteCount(int width, int height, int bitsPerPixel)
    {
        if (width <= 0 || height <= 0)
            return 0;
        var rowBytes = checked((width * (long)bitsPerPixel + 7) / 8);
        return checked(height * (rowBytes + 1));
    }

    private static int GetAdam7Dimension(int size, int start, int step)
        => size <= start ? 0 : (size - start + step - 1) / step;

    private static uint ComputePngCrc(ReadOnlySpan<byte> data)
    {
        var crc = uint.MaxValue;
        foreach (var value in data)
            crc = PngCrcTable[(crc ^ value) & 0xff] ^ (crc >> 8);
        return ~crc;
    }

    private static uint[] CreatePngCrcTable()
    {
        var table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            var value = index;
            for (var bit = 0; bit < 8; bit++)
                value = (value & 1) != 0 ? 0xedb88320u ^ (value >> 1) : value >> 1;
            table[index] = value;
        }
        return table;
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
