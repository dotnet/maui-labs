using System.Buffers.Binary;
using System.ComponentModel;
using Microsoft.Maui.DevFlow.Driver;
using SkiaSharp;
using Xunit;
using Xunit.Sdk;

namespace Microsoft.Maui.Cli.UnitTests;

[Collection("CLI")]
public sealed class ScreenshotImageTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("devflow screenshot tests ").FullName;
    private readonly TextWriter _previousError = Console.Error;
    private readonly StringWriter _errors = new();

    public ScreenshotImageTests() => Console.SetError(_errors);

    [Theory]
    [InlineData(1290, 2796, 430, 932)]
    [InlineData(1320, 2868, 440, 956)]
    [InlineData(1206, 2622, 402, 874)]
    [InlineData(1170, 2532, 390, 844)]
    [InlineData(1125, 2436, 375, 812)]
    [InlineData(1242, 2688, 414, 896)]
    [InlineData(828, 1792, 414, 896)]
    [InlineData(750, 1334, 375, 667)]
    [InlineData(2048, 2732, 1024, 1366)]
    [InlineData(2388, 1668, 1194, 834)]
    [InlineData(2360, 1640, 1180, 820)]
    [InlineData(1080, 2400, 360, 800)]
    [InlineData(800, 1200, 400, 600)]
    public void GetSimulatorSize_Default_PreservesExistingDensity(
        int width, int height, int expectedWidth, int expectedHeight)
        => Assert.Equal((expectedWidth, expectedHeight), ScreenshotImage.GetSimulatorSize(width, height, null));

    [Theory]
    [InlineData(300, 300, 652)]
    [InlineData(1206, 1206, 2622)]
    [InlineData(2000, 1206, 2622)]
    [InlineData(0, 1206, 2622)]
    [InlineData(-1, 1206, 2622)]
    public void GetSimulatorSize_MaxWidth_PreservesAspectRatioAndNoUpscaling(
        int maxWidth, int expectedWidth, int expectedHeight)
        => Assert.Equal((expectedWidth, expectedHeight), ScreenshotImage.GetSimulatorSize(1206, 2622, maxWidth));

    [Fact]
    public void GetSimulatorSize_SubpixelHeight_PreservesOriginal()
        => Assert.Equal((100, 1), ScreenshotImage.GetSimulatorSize(100, 1, 1));

    [Theory]
    [InlineData("native")]
    [InlineData("NaTiVe")]
    [InlineData("full")]
    [InlineData("FULL")]
    public async Task Resize_NativeScale_ReturnsUnmodifiedPngWithoutStartingProcess(string scale)
    {
        var png = CreatePng(90, 60);
        var file = WriteImage(png);

        var result = await ScreenshotImage.ResizeSimulatorAsync(file, 30, scale, runProcess: NeverRun);

        Assert.Equal(png, result);
        Assert.Equal(png, await File.ReadAllBytesAsync(file));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(90)]
    [InlineData(180)]
    public async Task Resize_NoDownscaling_ReturnsOriginalWithoutStartingProcess(int maxWidth)
    {
        var png = CreatePng(90, 60);
        var result = await ScreenshotImage.ResizeSimulatorAsync(WriteImage(png), maxWidth, runProcess: NeverRun);

        Assert.Equal(png, result);
    }

    [Fact]
    public async Task Resize_InvalidImageAndNativeScale_PreserveFallback()
    {
        byte[] invalid = [1, 2, 3];
        var file = WriteImage(invalid);

        Assert.Equal(invalid, await ScreenshotImage.ResizeSimulatorAsync(file, 100, runProcess: NeverRun));
        Assert.Equal(invalid, await ScreenshotImage.ResizeSimulatorAsync(file, scale: "native", runProcess: NeverRun));
        Assert.Contains("invalid PNG header", _errors.ToString());
    }

    [Fact]
    public async Task Resize_UsesExactDimensionsAndOneSafelyPassedFile()
    {
        var file = WriteImage(CreatePng(90, 60));
        var calls = 0;
        var result = await ScreenshotImage.ResizeSimulatorAsync(file, 30, runProcess: async (executable, arguments, token) =>
        {
            calls++;
            Assert.Equal("/usr/bin/sips", executable);
            Assert.Equal(["--resampleHeightWidth", "20", "30", "--setProperty", "format", "png", file], arguments);
            await File.WriteAllBytesAsync(file, CreatePng(30, 20), token);
            return string.Empty;
        });

        Assert.Equal(1, calls);
        Assert.Equal((30, 20), ScreenshotImage.GetPngDimensions(result));
    }

    [Theory]
    [InlineData("exit")]
    [InlineData("missing")]
    [InlineData("malformed")]
    [InlineData("wrong-size")]
    public async Task Resize_ReplacementFailure_PreservesOriginalAndReportsWarning(string failure)
    {
        var original = CreatePng(90, 60);
        var file = WriteImage(original);
        var result = await ScreenshotImage.ResizeSimulatorAsync(file, 30, runProcess: async (_, _, token) =>
        {
            if (failure == "exit")
                throw new InvalidOperationException("sips failed (exit 1)");
            if (failure == "missing")
                throw new Win32Exception("sips unavailable");
            await File.WriteAllBytesAsync(file, failure == "malformed" ? [1, 2, 3] : CreatePng(20, 20), token);
            return string.Empty;
        });

        Assert.Equal(original, result);
        Assert.Contains("returning native resolution", _errors.ToString());
    }

    [Fact]
    public async Task Resize_CallerCancellation_Propagates()
    {
        var file = WriteImage(CreatePng(90, 60));
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ScreenshotImage.ResizeSimulatorAsync(file, 30, cancellationToken: cancellation.Token,
                runProcess: (_, _, _) =>
                {
                    cancellation.Cancel();
                    return Task.FromCanceled<string>(cancellation.Token);
                }));
    }

    [Fact]
    public async Task Resize_ProcessTimeout_PreservesOriginalAndReportsWarning()
    {
        var original = CreatePng(90, 60);
        var file = WriteImage(original);
        var result = await ScreenshotImage.ResizeSimulatorAsync(file, 30,
            runProcess: (_, _, _) => throw new OperationCanceledException());

        Assert.Equal(original, result);
        Assert.Contains("resizing timed out", _errors.ToString());
    }

    [Theory]
    [InlineData(1206, 2622, null, 402, 874)]
    [InlineData(750, 1334, null, 375, 667)]
    [InlineData(2048, 2732, null, 1024, 1366)]
    [InlineData(1242, 2688, 333, 333, 720)]
    public async Task Resize_MacOS_ProducesDecodablePngAtExistingDimensions(
        int width, int height, int? maxWidth, int expectedWidth, int expectedHeight)
    {
        if (!OperatingSystem.IsMacOS())
            return; // xUnit v2 has no runtime skip.

        var result = await ScreenshotImage.ResizeSimulatorAsync(WriteImage(CreatePng(width, height)), maxWidth);

        using var image = SKBitmap.Decode(result);
        Assert.NotNull(image);
        Assert.Equal(expectedWidth, image.Width);
        Assert.Equal(expectedHeight, image.Height);
        Assert.Equal(SKColors.Red, image.GetPixel(expectedWidth / 2, expectedHeight / 2));
    }

    [Fact]
    public async Task Resize_MacOS_PreservesTransparency()
    {
        if (!OperatingSystem.IsMacOS())
            return; // xUnit v2 has no runtime skip.

        var color = new SKColor(255, 0, 0, 128);
        var result = await ScreenshotImage.ResizeSimulatorAsync(WriteImage(CreatePng(90, 60, color)), 30);

        using var image = SKBitmap.Decode(result);
        Assert.NotNull(image);
        Assert.Equal(color, image.GetPixel(15, 10));
    }

    [Fact]
    public async Task Resize_MacOS_ConcurrentCapturesStayIndependent()
    {
        if (!OperatingSystem.IsMacOS())
            return; // xUnit v2 has no runtime skip.

        var files = Enumerable.Range(0, 4).Select(_ => WriteImage(CreatePng(90, 60))).ToArray();
        var results = await Task.WhenAll(files.Select(file => ScreenshotImage.ResizeSimulatorAsync(file, 30)));

        Assert.All(results, png => Assert.Equal((30, 20), ScreenshotImage.GetPngDimensions(png)));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, 1)]
    [InlineData(1, -1)]
    [InlineData(32769, 1)]
    [InlineData(1, 32769)]
    public void GetPngDimensions_InvalidDimensions_PreservesInspectorLimits(int width, int height)
    {
        var png = CreatePng(2, 2);
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(16), width);
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(20), height);

        Assert.Equal((0, 0), ScreenshotImage.GetPngDimensions(png));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(31, 17)]
    [InlineData(400, 300)]
    public void IsCompletePng_ValidImage_ReturnsTrue(int width, int height)
    {
        Assert.True(ScreenshotImage.IsCompletePng(CreatePng(width, height)));
    }

    [Fact]
    public void IsCompletePng_TruncatedAfterHeader_ReturnsFalse()
    {
        var png = CreatePng(2, 2);

        Assert.False(ScreenshotImage.IsCompletePng(png[..24]));
    }

    [Fact]
    public void IsCompletePng_TruncatedZlibTrailerWithValidChunkCrc_ReturnsFalse()
    {
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAABklEQVR4nGNgQAfMKBUWAAAAAElFTkSuQmCC");

        Assert.False(ScreenshotImage.IsCompletePng(png));
    }

    [Fact]
    public void IsCompletePng_UnfinishedDeflateWithValidAdler_ReturnsFalse()
    {
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAC0lEQVR4nGJgQAcAABIAAWCK7kMAAAAASUVORK5CYII=");

        Assert.False(ScreenshotImage.IsCompletePng(png));
    }

    [Fact]
    public void IsCompletePng_NonZeroTerminalDeflatePadding_ReturnsTrue()
    {
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP4z8DwHxAFAAH/Nie4jwAAAABJRU5ErkJggg==");

        Assert.True(ScreenshotImage.IsCompletePng(png));
    }

    [Fact]
    public void IsCompletePng_CorruptChunkCrc_ReturnsFalse()
    {
        var png = CreatePng(2, 2);
        png[29] ^= 0xff;

        Assert.False(ScreenshotImage.IsCompletePng(png));
    }

    [Fact]
    public void IsCompletePng_InvalidBitDepth_ReturnsFalse()
    {
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABBwIAAAASJ8QPAAAABElEQVQAAQIDQN6+CAAAAABJRU5ErkJggg==");

        Assert.False(ScreenshotImage.IsCompletePng(png));
    }

    private string WriteImage(byte[] png)
    {
        var file = Path.Combine(_directory, $"{Guid.NewGuid():N}.png");
        File.WriteAllBytes(file, png);
        return file;
    }

    private static byte[] CreatePng(int width, int height, SKColor? color = null)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        bitmap.Erase(color ?? SKColors.Red);
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }

    private static Task<string> NeverRun(string executable, string[] arguments, CancellationToken token)
        => throw new XunitException("No image-processing subprocess should be needed.");

    public void Dispose()
    {
        Console.SetError(_previousError);
        _errors.Dispose();
        Directory.Delete(_directory, recursive: true);
    }
}

[Collection("CLI")]
public sealed class WindowsPngEncoderTests
{
    [Fact]
    public void Encode_Windows_PreservesTopDownBgraAndForcesOpaqueAlpha()
    {
        if (!OperatingSystem.IsWindows())
            return; // xUnit v2 has no runtime skip.

        byte[] pixels = [
            0, 0, 255, 0,     0, 255, 0, 127,
            255, 0, 0, 255,   255, 255, 255, 1
        ];
        var png = WindowsPngEncoder.Encode(pixels, 2, 2);

        using var image = SKBitmap.Decode(png);
        Assert.NotNull(image);
        Assert.Equal(2, image.Width);
        Assert.Equal(2, image.Height);
        Assert.Equal(SKColors.Red, image.GetPixel(0, 0));
        Assert.Equal(SKColors.Lime, image.GetPixel(1, 0));
        Assert.Equal(SKColors.Blue, image.GetPixel(0, 1));
        Assert.Equal(SKColors.White, image.GetPixel(1, 1));
    }

    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(1, 0, 0)]
    [InlineData(-1, 1, 0)]
    [InlineData(1, 1, 3)]
    [InlineData(2, 2, 12)]
    public void Encode_InvalidBuffer_RejectsBeforeNativeAllocation(int width, int height, int length)
        => Assert.ThrowsAny<ArgumentException>(() => WindowsPngEncoder.Encode(new byte[length], width, height));

    [Fact]
    public void Encode_Overflow_RejectsBeforeNativeAllocation()
        => Assert.Throws<OverflowException>(() => WindowsPngEncoder.Encode([], int.MaxValue, 2));

    [Fact]
    public async Task Encode_Windows_ConcurrentCallsReleaseTheirTemporaryFiles()
    {
        if (!OperatingSystem.IsWindows())
            return; // xUnit v2 has no runtime skip.

        var pattern = $"devflow-gdi-{Environment.ProcessId}-*.png";
        var before = Directory.GetFiles(Path.GetTempPath(), pattern).ToHashSet();
        var images = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            Task.Run(() => WindowsPngEncoder.Encode([0, 0, 255, 0], 1, 1))));

        foreach (var png in images)
        {
            using var image = SKBitmap.Decode(png);
            Assert.NotNull(image);
            Assert.Equal(SKColors.Red, image.GetPixel(0, 0));
        }
        Assert.Empty(Directory.GetFiles(Path.GetTempPath(), pattern).Except(before));
    }
}
