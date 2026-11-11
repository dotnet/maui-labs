namespace Microsoft.Maui.AI.Chat.Controls.Tests;

public class ChatImageTests
{
    [Fact]
    public void ReadSize_ReadsPngGifAndBmpHeaders()
    {
        Assert.Equal(new Size(400, 300), ImageHeader.ReadSize(Png(400, 300)));
        Assert.Equal(new Size(400, 300), ImageHeader.ReadSize([.. "GIF89a"u8, .. Le16(400), .. Le16(300)]));

        // A negative BMP height is a top-down bitmap of the same size.
        byte[] bmp = [.. "BM"u8, .. new byte[16], .. Le32(400), .. Le32(-300)];
        Assert.Equal(new Size(400, 300), ImageHeader.ReadSize(bmp));
    }

    [Fact]
    public void ReadSize_ReadsEveryKindOfWebP()
    {
        byte[] lossy = [.. WebP("VP8 "), 0, 0, 0, 0x9D, 0x01, 0x2A, .. Le16(400), .. Le16(300)];
        Assert.Equal(new Size(400, 300), ImageHeader.ReadSize(lossy));

        var bits = (uint)(400 - 1) | ((uint)(300 - 1) << 14);
        byte[] lossless = [.. WebP("VP8L"), 0x2F, .. BitConverter.GetBytes(bits), 0, 0, 0, 0, 0];
        Assert.Equal(new Size(400, 300), ImageHeader.ReadSize(lossless));

        byte[] extended = [.. WebP("VP8X"), 0, 0, 0, 0, .. Le24(400 - 1), .. Le24(300 - 1)];
        Assert.Equal(new Size(400, 300), ImageHeader.ReadSize(extended));
    }

    [Fact]
    public void ReadSize_ReadsTheJpegFrameAndAppliesTheExifRotation()
    {
        Assert.Equal(new Size(400, 300), ImageHeader.ReadSize(Jpeg(400, 300)));

        // Phones store portrait photos as landscape pixels rotated a quarter by EXIF; they are shown upright.
        Assert.Equal(new Size(300, 400), ImageHeader.ReadSize(Jpeg(400, 300, orientation: 6, littleEndian: false)));
        Assert.Equal(new Size(300, 400), ImageHeader.ReadSize(Jpeg(400, 300, orientation: 8, littleEndian: true)));
        Assert.Equal(new Size(400, 300), ImageHeader.ReadSize(Jpeg(400, 300, orientation: 3, littleEndian: true)));
    }

    [Fact]
    public void ReadSize_ReturnsNullForUnknownOrTruncatedData()
    {
        Assert.Null(ImageHeader.ReadSize([]));
        Assert.Null(ImageHeader.ReadSize(Blocks.Png().Data.Span));
        Assert.Null(ImageHeader.ReadSize("GIF89a"u8));
        Assert.Null(ImageHeader.ReadSize(Jpeg(400, 300).AsSpan(0, 12)));
        Assert.Null(ImageHeader.ReadSize(Png(0, 300)));
        Assert.Null(ImageHeader.ReadSize("plain text, not an image"u8));
    }

    [Theory]
    [InlineData(400, 300, 1000, 320, 240)]
    [InlineData(300, 400, 1000, 180, 240)]
    [InlineData(40, 30, 1000, 40, 30)]
    [InlineData(400, 300, 100, 100, 75)]
    public void AnImageOfKnownSize_IsMeasuredBeforeItLoads(double width, double height, double available, double expectedWidth, double expectedHeight)
    {
        var image = new ChatImage { PixelSize = new Size(width, height), MaximumWidthRequest = 320, MaximumHeightRequest = 240 };

        var size = ((IView)image).Measure(available, double.PositiveInfinity);

        Assert.Equal(new Size(expectedWidth, expectedHeight), size);
    }

    [Fact]
    public void ImageData_IsSizedFromItsHeader()
    {
        var data = new DataContent(Png(400, 300), "image/png") { Name = "photo.png" };

        var image = Assert.IsType<ChatImage>(ChatVisuals.CreateMediaView(data, alignEnd: true));

        Assert.Equal(new Size(400, 300), image.PixelSize);
        Assert.Equal(LayoutOptions.End, image.HorizontalOptions);

        // A partial image replaced by the final one keeps the view and takes the new size.
        Assert.Same(image, ChatVisuals.UpdateMediaView(image, new DataContent(Png(300, 400), "image/png")));
        Assert.Equal(new Size(300, 400), image.PixelSize);
    }

    private static byte[] Png(int width, int height) =>
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, .. "IHDR"u8, .. Be32(width), .. Be32(height), 8, 6, 0, 0, 0];

    private static byte[] WebP(string chunk) =>
        [.. "RIFF"u8, 0, 0, 0, 0, .. "WEBP"u8, .. System.Text.Encoding.ASCII.GetBytes(chunk), 0, 0, 0, 0];

    private static byte[] Jpeg(int width, int height, int orientation = 0, bool littleEndian = false)
    {
        var bytes = new List<byte> { 0xFF, 0xD8 };
        if (orientation > 0)
        {
            byte[] tiff = littleEndian
                ? [.. "II"u8, 0x2A, 0, 8, 0, 0, 0, 1, 0, 0x12, 0x01, 3, 0, 1, 0, 0, 0, (byte)orientation, 0, 0, 0, 0, 0, 0, 0]
                : [.. "MM"u8, 0, 0x2A, 0, 0, 0, 8, 0, 1, 0x01, 0x12, 0, 3, 0, 0, 0, 1, 0, (byte)orientation, 0, 0, 0, 0, 0, 0];
            byte[] exif = [.. "Exif\0\0"u8, .. tiff];
            bytes.AddRange([0xFF, 0xE1, .. Be16(exif.Length + 2), .. exif]);
        }

        bytes.AddRange([0xFF, 0xC0, .. Be16(17), 8, .. Be16(height), .. Be16(width), 3, 1, 0x22, 0, 2, 0x11, 1, 3, 0x11, 1]);
        bytes.AddRange([0xFF, 0xDA, 0, 2]);
        return [.. bytes];
    }

    private static byte[] Be16(int value) => [(byte)(value >> 8), (byte)value];

    private static byte[] Be32(int value) => [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

    private static byte[] Le16(int value) => [(byte)value, (byte)(value >> 8)];

    private static byte[] Le24(int value) => [(byte)value, (byte)(value >> 8), (byte)(value >> 16)];

    private static byte[] Le32(int value) => BitConverter.GetBytes(value);
}
