using Microsoft.Maui.DevFlow.Agent.Core;

namespace Microsoft.Maui.DevFlow.Tests;

public class AppKitScreenshotCaptureTests
{
    [Fact]
    public void IsUniformRgba_AllWhiteImage_IsBlank()
    {
        var pixels = Enumerable.Repeat((byte)255, 16 * 16 * 4).ToArray();

        Assert.True(AppKitScreenshotCapture.IsUniformRgba(pixels));
    }

    [Fact]
    public void IsUniformRgba_ToleratesTinyNoise()
    {
        var pixels = Enumerable.Repeat((byte)255, 16 * 16 * 4).ToArray();
        pixels[40] = 254;

        Assert.True(AppKitScreenshotCapture.IsUniformRgba(pixels));
    }

    [Fact]
    public void IsUniformRgba_VisibleContent_IsNotBlank()
    {
        var pixels = Enumerable.Repeat((byte)255, 16 * 16 * 4).ToArray();
        pixels[400] = 200; // one pixel with visibly different red channel

        Assert.False(AppKitScreenshotCapture.IsUniformRgba(pixels));
    }

    [Fact]
    public void IsUniformRgba_TooFewPixels_IsNotTreatedAsBlank()
    {
        Assert.False(AppKitScreenshotCapture.IsUniformRgba(new byte[] { 255, 255, 255, 255 }));
    }
    [Theory]
    [InlineData(14, 0, false)]
    [InlineData(14, 1, false)]
    [InlineData(14, 2, true)]
    [InlineData(15, 0, true)]
    public async Task CaptureAsync_AttachedSheet_UsesOnlyCaptureThatIncludesChildren(
        int major,
        int minor,
        bool supportsChildWindows)
    {
        byte[] parentOnly = [1];
        byte[] composite = [1, 2];
        var screenCaptureKitCalls = 0;
        var compositeCalls = 0;

        var result = await AppKitScreenshotCapture.CaptureAsync(
            hasAttachedSheet: true,
            () =>
            {
                screenCaptureKitCalls++;
                return Task.FromResult<byte[]?>(parentOnly);
            },
            () =>
            {
                compositeCalls++;
                return composite;
            },
            (requiredMajor, requiredMinor) => new Version(major, minor) >= new Version(requiredMajor, requiredMinor));

        Assert.Equal(supportsChildWindows ? parentOnly : composite, result);
        Assert.Equal(supportsChildWindows ? 1 : 0, screenCaptureKitCalls);
        Assert.Equal(supportsChildWindows ? 0 : 1, compositeCalls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task CaptureAsync_NoAttachedSheet_KeepsScreenCaptureKitPath(int minor)
    {
        byte[] parent = [1];
        var result = await AppKitScreenshotCapture.CaptureAsync(
            hasAttachedSheet: false,
            () => Task.FromResult<byte[]?>(parent),
            () => throw new InvalidOperationException("There is no sheet to composite."),
            (requiredMajor, requiredMinor) => new Version(14, minor) >= new Version(requiredMajor, requiredMinor));

        Assert.Same(parent, result);
    }

    [Fact]
    public async Task CaptureAsync_ScreenCaptureKitUnavailable_FallsBackToComposite()
    {
        byte[] composite = [1, 2];
        var result = await AppKitScreenshotCapture.CaptureAsync(
            hasAttachedSheet: true,
            () => Task.FromResult<byte[]?>(null),
            () => composite,
            (_, _) => true);

        Assert.Same(composite, result);
    }
}
