using System.Buffers.Binary;

namespace Microsoft.Maui.AI.Chat.Controls;

/// <summary>
/// An image that takes its final size before it has loaded when its pixel size is known. Images load
/// asynchronously, and a message list on iOS does not measure a row again when one finishes loading after the row
/// was bound (scrolling back to it), so an image measured before it loaded would stay collapsed.
/// </summary>
internal sealed class ChatImage : Image
{
    private readonly bool _themed = ChatElements.EnsureTheme();
    private Size? _pixelSize;

    /// <summary>Gets or sets the image's size in pixels, laid out as device-independent units; null when unknown.</summary>
    public Size? PixelSize
    {
        get => _pixelSize;
        set
        {
            if (_pixelSize == value)
                return;

            _pixelSize = value;
            InvalidateMeasure();
        }
    }

    protected override Size MeasureOverride(double widthConstraint, double heightConstraint)
    {
        if (_pixelSize is not { Width: > 0, Height: > 0 } pixels)
            return base.MeasureOverride(widthConstraint, heightConstraint);

        // Scaled down (never up) to fit the maximum size and the space available, keeping the aspect ratio.
        var margin = Margin;
        var maxWidth = Math.Min(MaximumWidthRequest, widthConstraint - margin.HorizontalThickness);
        var maxHeight = Math.Min(MaximumHeightRequest, heightConstraint - margin.VerticalThickness);
        var scale = Math.Max(0, Math.Min(1, Math.Min(maxWidth / pixels.Width, maxHeight / pixels.Height)));
        return new Size(
            pixels.Width * scale + margin.HorizontalThickness,
            pixels.Height * scale + margin.VerticalThickness);
    }
}

/// <summary>Reads an image's pixel size from the header of PNG, JPEG, GIF, BMP and WebP data.</summary>
internal static class ImageHeader
{
    /// <summary>Returns the size the image is shown at (a JPEG's EXIF rotation applied), or null when unknown.</summary>
    public static Size? ReadSize(ReadOnlySpan<byte> data)
    {
        var size = ReadPng(data) ?? ReadGif(data) ?? ReadBmp(data) ?? ReadWebP(data) ?? ReadJpeg(data);
        return size is { Width: > 0, Height: > 0 } ? size : null;
    }

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static Size? ReadPng(ReadOnlySpan<byte> data) =>
        data.Length >= 24 && data[..8].SequenceEqual(PngSignature) && data[12..16].SequenceEqual("IHDR"u8)
            ? new Size(BinaryPrimitives.ReadUInt32BigEndian(data[16..]), BinaryPrimitives.ReadUInt32BigEndian(data[20..]))
            : null;

    private static Size? ReadGif(ReadOnlySpan<byte> data) =>
        data.Length >= 10 && (data[..6].SequenceEqual("GIF87a"u8) || data[..6].SequenceEqual("GIF89a"u8))
            ? new Size(BinaryPrimitives.ReadUInt16LittleEndian(data[6..]), BinaryPrimitives.ReadUInt16LittleEndian(data[8..]))
            : null;

    private static Size? ReadBmp(ReadOnlySpan<byte> data) =>
        data.Length >= 26 && data[..2].SequenceEqual("BM"u8)
            ? new Size(
                Math.Abs((long)BinaryPrimitives.ReadInt32LittleEndian(data[18..])),
                Math.Abs((long)BinaryPrimitives.ReadInt32LittleEndian(data[22..])))
            : null;

    private static Size? ReadWebP(ReadOnlySpan<byte> data)
    {
        if (data.Length < 30 || !data[..4].SequenceEqual("RIFF"u8) || !data[8..12].SequenceEqual("WEBP"u8))
            return null;

        var chunk = data[12..16];
        if (chunk.SequenceEqual("VP8 "u8) && data[23] == 0x9D && data[24] == 0x01 && data[25] == 0x2A)
        {
            return new Size(
                BinaryPrimitives.ReadUInt16LittleEndian(data[26..]) & 0x3FFF,
                BinaryPrimitives.ReadUInt16LittleEndian(data[28..]) & 0x3FFF);
        }

        if (chunk.SequenceEqual("VP8L"u8) && data[20] == 0x2F)
        {
            var bits = BinaryPrimitives.ReadUInt32LittleEndian(data[21..]);
            return new Size((bits & 0x3FFF) + 1, ((bits >> 14) & 0x3FFF) + 1);
        }

        if (chunk.SequenceEqual("VP8X"u8))
            return new Size(ReadUInt24LittleEndian(data[24..]) + 1, ReadUInt24LittleEndian(data[27..]) + 1);

        return null;
    }

    private static Size? ReadJpeg(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8)
            return null;

        var orientation = 1;
        var index = 2;
        while (index + 4 <= data.Length)
        {
            if (data[index] != 0xFF)
                return null;

            var marker = data[index + 1];
            if (marker == 0xFF)
            {
                index++;
                continue;
            }

            if (marker is 0x01 or (>= 0xD0 and <= 0xD7))
            {
                index += 2;
                continue;
            }

            if (marker is 0xD9 or 0xDA)
                return null;

            var length = BinaryPrimitives.ReadUInt16BigEndian(data[(index + 2)..]);
            if (length < 2 || index + 2 + length > data.Length)
                return null;

            var segment = data.Slice(index + 4, length - 2);
            if (marker == 0xE1)
                orientation = ReadExifOrientation(segment) ?? orientation;

            // Start of frame (baseline, progressive, lossless...); C4, C8 and CC share the range but are not frames.
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC && segment.Length >= 5)
            {
                var height = BinaryPrimitives.ReadUInt16BigEndian(segment[1..]);
                var width = BinaryPrimitives.ReadUInt16BigEndian(segment[3..]);

                // Orientations 5 to 8 turn the image a quarter, so it is shown with width and height swapped.
                return orientation is >= 5 and <= 8 ? new Size(height, width) : new Size(width, height);
            }

            index += 2 + length;
        }

        return null;
    }

    private static int? ReadExifOrientation(ReadOnlySpan<byte> segment)
    {
        if (segment.Length < 14 || !segment[..6].SequenceEqual("Exif\0\0"u8))
            return null;

        var tiff = segment[6..];
        bool littleEndian;
        if (tiff[..2].SequenceEqual("II"u8))
            littleEndian = true;
        else if (tiff[..2].SequenceEqual("MM"u8))
            littleEndian = false;
        else
            return null;

        var offset = ReadUInt32(tiff[4..], littleEndian);
        if (offset > (uint)tiff.Length - 2)
            return null;

        var count = ReadUInt16(tiff[(int)offset..], littleEndian);
        for (var entry = (int)offset + 2; count > 0 && entry + 12 <= tiff.Length; count--, entry += 12)
        {
            if (ReadUInt16(tiff[entry..], littleEndian) == 0x0112)
                return ReadUInt16(tiff[(entry + 8)..], littleEndian);
        }

        return null;
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, bool littleEndian) =>
        littleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(data) : BinaryPrimitives.ReadUInt16BigEndian(data);

    private static uint ReadUInt32(ReadOnlySpan<byte> data, bool littleEndian) =>
        littleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(data) : BinaryPrimitives.ReadUInt32BigEndian(data);

    private static int ReadUInt24LittleEndian(ReadOnlySpan<byte> data) => data[0] | (data[1] << 8) | (data[2] << 16);
}
