using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Maui.Media;
using Microsoft.Maui.Platforms.Windows.WPF.Essentials;

namespace Essentials.Tests;

public class ScreenshotTests
{
    [Fact]
    public async Task Result_ReportsDimensionsAndReturnsIndependentPngStreams()
    {
        var result = CreateResult();

        await using var first = await result.OpenReadAsync();
        await using var second = await result.OpenReadAsync();

        Assert.Equal(3, result.Width);
        Assert.Equal(2, result.Height);
        Assert.NotSame(first, second);
        Assert.True(first.Length > 0);
        Assert.Equal(first.Length, second.Length);
    }

    [Fact]
    public async Task Result_EncodesJpegAndValidatesQuality()
    {
        var result = CreateResult();

        await using var jpeg = await result.OpenReadAsync(ScreenshotFormat.Jpeg, 75);
        Assert.Equal(0xFF, jpeg.ReadByte());
        Assert.Equal(0xD8, jpeg.ReadByte());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => result.OpenReadAsync(ScreenshotFormat.Jpeg, 0));
        await Assert.ThrowsAsync<NotSupportedException>(
            () => result.OpenReadAsync((ScreenshotFormat)int.MaxValue));
    }

    [Fact]
    public async Task CopyToAsync_DoesNotShareReadPosition()
    {
        var result = CreateResult();
        await using var first = new MemoryStream();
        await using var second = new MemoryStream();

        await result.CopyToAsync(first);
        await result.CopyToAsync(second);

        Assert.Equal(first.ToArray(), second.ToArray());
    }

    static WPFScreenshotResult CreateResult()
    {
        var pixels = new byte[3 * 2 * 4];
        var bitmap = BitmapSource.Create(3, 2, 96, 96, PixelFormats.Bgra32, null, pixels, 3 * 4);
        bitmap.Freeze();
        return new WPFScreenshotResult(bitmap);
    }
}
