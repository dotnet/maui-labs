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
            if (!compressedImageData.TryGetBuffer(out var compressedBuffer) ||
                compressedImageData.Length < 6)
            {
                return false;
            }

            var zlibData = compressedBuffer.Array!.AsSpan(
                compressedBuffer.Offset,
                checked((int)compressedImageData.Length));
            var compressionMethodAndInfo = zlibData[0];
            var flags = zlibData[1];
            if ((compressionMethodAndInfo & 0x0f) != 8 ||
                (compressionMethodAndInfo >> 4) > 7 ||
                ((compressionMethodAndInfo << 8) | flags) % 31 != 0 ||
                (flags & 0x20) != 0)
            {
                return false;
            }
            var expectedAdler32 = BinaryPrimitives.ReadUInt32BigEndian(zlibData[^4..]);
            uint adlerA = 1;
            uint adlerB = 0;

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
            if (!HasCompleteDeflateStream(zlibData[2..^4], decodedBytes))
                return false;

            compressedImageData.Position = 0;
            using var zlib = new ZLibStream(
                compressedImageData,
                CompressionMode.Decompress,
                leaveOpen: true);
            if (interlaceMethod == 0)
            {
                if (!ValidatePngPass(
                        zlib,
                        width,
                        height,
                        bitsPerPixel,
                        ref adlerA,
                        ref adlerB))
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
                        !ValidatePngPass(
                            zlib,
                            passWidth,
                            passHeight,
                            bitsPerPixel,
                            ref adlerA,
                            ref adlerB))
                    {
                        return false;
                    }
                }
            }

            var actualAdler32 = (adlerB << 16) | adlerA;
            return zlib.ReadByte() == -1 && actualAdler32 == expectedAdler32;
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
        int bitsPerPixel,
        ref uint adlerA,
        ref uint adlerB)
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
            UpdateAdler32(row, ref adlerA, ref adlerB);
        }

        return true;
    }

    private static void UpdateAdler32(
        ReadOnlySpan<byte> data,
        ref uint adlerA,
        ref uint adlerB)
    {
        const uint Modulus = 65521;
        foreach (var value in data)
        {
            adlerA = (adlerA + value) % Modulus;
            adlerB = (adlerB + adlerA) % Modulus;
        }
    }

    private static bool HasCompleteDeflateStream(
        ReadOnlySpan<byte> compressed,
        long expectedOutputBytes)
    {
        var reader = new DeflateBitReader(compressed);
        long outputBytes = 0;
        while (true)
        {
            if (!reader.TryReadBits(1, out var finalBlock) ||
                !reader.TryReadBits(2, out var blockType))
            {
                return false;
            }

            DeflateHuffman? literalLengthCodes;
            DeflateHuffman? distanceCodes;
            switch (blockType)
            {
                case 0:
                    if (!reader.AlignToByte() ||
                        !reader.TryReadBits(16, out var storedLength) ||
                        !reader.TryReadBits(16, out var storedLengthComplement) ||
                        (storedLength ^ 0xffff) != storedLengthComplement ||
                        !reader.TrySkipAlignedBytes(storedLength))
                    {
                        return false;
                    }
                    outputBytes = checked(outputBytes + storedLength);
                    break;
                case 1:
                    CreateFixedDeflateCodes(out literalLengthCodes, out distanceCodes);
                    if (!TryDecodeCompressedDeflateBlock(
                            ref reader,
                            literalLengthCodes,
                            distanceCodes,
                            ref outputBytes,
                            expectedOutputBytes))
                    {
                        return false;
                    }
                    break;
                case 2:
                    if (!TryReadDynamicDeflateCodes(
                            ref reader,
                            out literalLengthCodes,
                            out distanceCodes) ||
                        !TryDecodeCompressedDeflateBlock(
                            ref reader,
                            literalLengthCodes!,
                            distanceCodes,
                            ref outputBytes,
                            expectedOutputBytes))
                    {
                        return false;
                    }
                    break;
                default:
                    return false;
            }

            if (outputBytes > expectedOutputBytes)
                return false;
            if (finalBlock == 0)
                continue;
            return outputBytes == expectedOutputBytes && reader.HasOnlyZeroPadding();
        }
    }

    private static void CreateFixedDeflateCodes(
        out DeflateHuffman literalLengthCodes,
        out DeflateHuffman distanceCodes)
    {
        Span<byte> literalLengths = stackalloc byte[288];
        literalLengths[..144].Fill(8);
        literalLengths[144..256].Fill(9);
        literalLengths[256..280].Fill(7);
        literalLengths[280..].Fill(8);
        DeflateHuffman.TryCreate(literalLengths, out literalLengthCodes!);

        Span<byte> distanceLengths = stackalloc byte[32];
        distanceLengths.Fill(5);
        DeflateHuffman.TryCreate(distanceLengths, out distanceCodes!);
    }

    private static bool TryReadDynamicDeflateCodes(
        ref DeflateBitReader reader,
        out DeflateHuffman? literalLengthCodes,
        out DeflateHuffman? distanceCodes)
    {
        literalLengthCodes = null;
        distanceCodes = null;
        if (!reader.TryReadBits(5, out var literalCountOffset) ||
            !reader.TryReadBits(5, out var distanceCountOffset) ||
            !reader.TryReadBits(4, out var codeLengthCountOffset))
        {
            return false;
        }

        var literalCount = literalCountOffset + 257;
        var distanceCount = distanceCountOffset + 1;
        var codeLengthCount = codeLengthCountOffset + 4;
        ReadOnlySpan<int> codeLengthOrder =
            [16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15];
        Span<byte> codeLengthLengths = stackalloc byte[19];
        for (var index = 0; index < codeLengthCount; index++)
        {
            if (!reader.TryReadBits(3, out var length))
                return false;
            codeLengthLengths[codeLengthOrder[index]] = (byte)length;
        }
        if (!DeflateHuffman.TryCreate(codeLengthLengths, out var codeLengthCodes))
            return false;

        var lengths = new byte[literalCount + distanceCount];
        var position = 0;
        while (position < lengths.Length)
        {
            if (!codeLengthCodes!.TryDecode(ref reader, out var symbol))
                return false;

            if (symbol <= 15)
            {
                lengths[position++] = (byte)symbol;
                continue;
            }

            int repeat;
            byte repeatedLength;
            switch (symbol)
            {
                case 16:
                    if (position == 0 ||
                        !reader.TryReadBits(2, out var repeatPreviousOffset))
                    {
                        return false;
                    }
                    repeat = repeatPreviousOffset + 3;
                    repeatedLength = lengths[position - 1];
                    break;
                case 17:
                    if (!reader.TryReadBits(3, out var repeatZeroOffset))
                        return false;
                    repeat = repeatZeroOffset + 3;
                    repeatedLength = 0;
                    break;
                case 18:
                    if (!reader.TryReadBits(7, out var repeatLongZeroOffset))
                        return false;
                    repeat = repeatLongZeroOffset + 11;
                    repeatedLength = 0;
                    break;
                default:
                    return false;
            }

            if (position + repeat > lengths.Length)
                return false;
            lengths.AsSpan(position, repeat).Fill(repeatedLength);
            position += repeat;
        }

        if (lengths[256] == 0 ||
            !DeflateHuffman.TryCreate(
                lengths.AsSpan(0, literalCount),
                out literalLengthCodes))
        {
            return false;
        }

        var distanceLengths = lengths.AsSpan(literalCount, distanceCount);
        return IsAllZero(distanceLengths) ||
            DeflateHuffman.TryCreate(distanceLengths, out distanceCodes);
    }

    private static bool TryDecodeCompressedDeflateBlock(
        ref DeflateBitReader reader,
        DeflateHuffman literalLengthCodes,
        DeflateHuffman? distanceCodes,
        ref long outputBytes,
        long expectedOutputBytes)
    {
        ReadOnlySpan<int> lengthBases =
            [3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31, 35, 43,
                51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258];
        ReadOnlySpan<byte> lengthExtraBits =
            [0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3,
                3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0];
        ReadOnlySpan<int> distanceBases =
            [1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193,
                257, 385, 513, 769, 1025, 1537, 2049, 3073, 4097, 6145,
                8193, 12289, 16385, 24577];
        ReadOnlySpan<byte> distanceExtraBits =
            [0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7,
                8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13];

        while (true)
        {
            if (!literalLengthCodes.TryDecode(ref reader, out var symbol))
                return false;
            if (symbol < 256)
            {
                outputBytes++;
                if (outputBytes > expectedOutputBytes)
                    return false;
                continue;
            }
            if (symbol == 256)
                return true;
            if (symbol is < 257 or > 285 || distanceCodes is null)
                return false;

            var lengthIndex = symbol - 257;
            if (!reader.TryReadBits(lengthExtraBits[lengthIndex], out var lengthExtra) ||
                !distanceCodes.TryDecode(ref reader, out var distanceSymbol) ||
                distanceSymbol >= distanceBases.Length ||
                !reader.TryReadBits(distanceExtraBits[distanceSymbol], out var distanceExtra))
            {
                return false;
            }

            var length = lengthBases[lengthIndex] + lengthExtra;
            var distance = distanceBases[distanceSymbol] + distanceExtra;
            if (distance > outputBytes)
                return false;
            outputBytes = checked(outputBytes + length);
            if (outputBytes > expectedOutputBytes)
                return false;
        }
    }

    private static bool IsAllZero(ReadOnlySpan<byte> values)
    {
        foreach (var value in values)
        {
            if (value != 0)
                return false;
        }
        return true;
    }

    private ref struct DeflateBitReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _bitPosition;

        public DeflateBitReader(ReadOnlySpan<byte> data) => _data = data;

        public bool TryReadBits(int count, out int value)
        {
            value = 0;
            if (count < 0 || count > 16 || _bitPosition > _data.Length * 8 - count)
                return false;
            for (var bit = 0; bit < count; bit++)
            {
                value |= ((_data[_bitPosition >> 3] >> (_bitPosition & 7)) & 1) << bit;
                _bitPosition++;
            }
            return true;
        }

        public bool AlignToByte()
        {
            _bitPosition = (_bitPosition + 7) & ~7;
            return _bitPosition <= _data.Length * 8;
        }

        public bool TrySkipAlignedBytes(int count)
        {
            if ((_bitPosition & 7) != 0 ||
                count < 0 ||
                _bitPosition > _data.Length * 8 - count * 8)
            {
                return false;
            }
            _bitPosition += count * 8;
            return true;
        }

        public bool HasOnlyZeroPadding()
        {
            var remainingBits = _data.Length * 8 - _bitPosition;
            if (remainingBits is < 0 or >= 8)
                return false;
            while (_bitPosition < _data.Length * 8)
            {
                if (((_data[_bitPosition >> 3] >> (_bitPosition & 7)) & 1) != 0)
                    return false;
                _bitPosition++;
            }
            return true;
        }
    }

    private sealed class DeflateHuffman
    {
        private readonly Dictionary<int, int> _symbols;
        private readonly int _maximumBits;

        private DeflateHuffman(Dictionary<int, int> symbols, int maximumBits)
        {
            _symbols = symbols;
            _maximumBits = maximumBits;
        }

        public static bool TryCreate(
            ReadOnlySpan<byte> lengths,
            out DeflateHuffman? huffman)
        {
            huffman = null;
            Span<int> counts = stackalloc int[16];
            var symbolCount = 0;
            var maximumBits = 0;
            foreach (var length in lengths)
            {
                if (length > 15)
                    return false;
                if (length == 0)
                    continue;
                counts[length]++;
                symbolCount++;
                maximumBits = Math.Max(maximumBits, length);
            }
            if (symbolCount == 0)
                return false;

            var remaining = 1;
            for (var bits = 1; bits <= 15; bits++)
            {
                remaining = (remaining << 1) - counts[bits];
                if (remaining < 0)
                    return false;
            }

            Span<int> nextCodes = stackalloc int[16];
            var code = 0;
            for (var bits = 1; bits <= 15; bits++)
            {
                code = (code + counts[bits - 1]) << 1;
                nextCodes[bits] = code;
            }

            var symbols = new Dictionary<int, int>(symbolCount);
            for (var symbol = 0; symbol < lengths.Length; symbol++)
            {
                var length = lengths[symbol];
                if (length == 0)
                    continue;
                var reversedCode = ReverseBits(nextCodes[length]++, length);
                symbols[(length << 16) | reversedCode] = symbol;
            }

            huffman = new DeflateHuffman(symbols, maximumBits);
            return true;
        }

        public bool TryDecode(ref DeflateBitReader reader, out int symbol)
        {
            var code = 0;
            for (var length = 1; length <= _maximumBits; length++)
            {
                if (!reader.TryReadBits(1, out var bit))
                {
                    symbol = 0;
                    return false;
                }
                code |= bit << (length - 1);
                if (_symbols.TryGetValue((length << 16) | code, out symbol))
                    return true;
            }

            symbol = 0;
            return false;
        }

        private static int ReverseBits(int value, int bitCount)
        {
            var reversed = 0;
            for (var bit = 0; bit < bitCount; bit++)
            {
                reversed = (reversed << 1) | (value & 1);
                value >>= 1;
            }
            return reversed;
        }
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
