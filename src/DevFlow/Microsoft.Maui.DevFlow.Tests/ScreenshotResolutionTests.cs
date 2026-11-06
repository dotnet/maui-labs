using Microsoft.Maui.DevFlow.Agent.Core;

namespace Microsoft.Maui.DevFlow.Tests;

public class ScreenshotResolutionTests
{
    private static readonly byte[] SamplePng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAZAAAAEsCAIAAABi1XKVAAABdElEQVR42u3BMQEAAADCoPVPbQwfoAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAD+Bn+3AAHo8fTrAAAAAElFTkSuQmCC");

    [Fact]
    public void ProcessScreenshotPng_NoHints_PreservesLegacyDensityScaling()
    {
        var result = TestAgent.Process(
            SamplePng,
            nativeScale: 2,
            autoScale: true,
            targetScale: null,
            maxPixelWidth: null,
            maxPixelHeight: null,
            maxPixels: null);

        Assert.Equal(200, result.PixelWidth);
        Assert.Equal(150, result.PixelHeight);
        Assert.Equal(200, result.LogicalWidth);
        Assert.Equal(150, result.LogicalHeight);
        Assert.Equal(1, result.CaptureScale);
        Assert.Equal(2, result.NativeScale);
        Assert.False(result.Clamped);
    }

    [Fact]
    public void ProcessScreenshotPng_TargetScale_ReturnsRequestedPixelsWithoutChangingLogicalSize()
    {
        var result = TestAgent.Process(
            SamplePng,
            nativeScale: 2,
            autoScale: true,
            targetScale: 1.5,
            maxPixelWidth: null,
            maxPixelHeight: null,
            maxPixels: null);

        Assert.Equal(300, result.PixelWidth);
        Assert.Equal(225, result.PixelHeight);
        Assert.Equal(200, result.LogicalWidth);
        Assert.Equal(150, result.LogicalHeight);
        Assert.Equal(1.5, result.CaptureScale);
        Assert.False(result.Clamped);
    }

    [Fact]
    public void ProcessScreenshotPng_PixelBudget_BoundsNegotiatedOutput()
    {
        var result = TestAgent.Process(
            SamplePng,
            nativeScale: 2,
            autoScale: true,
            targetScale: 2,
            maxPixelWidth: 350,
            maxPixelHeight: 260,
            maxPixels: 60_000);

        Assert.True(result.PixelWidth * result.PixelHeight <= 60_000);
        Assert.True(result.PixelWidth <= 350);
        Assert.True(result.PixelHeight <= 260);
        Assert.True(result.Clamped);
    }

    [Theory]
    [InlineData(1000, 1)]
    [InlineData(1, 1000)]
    public void ProcessScreenshotPng_ThinImage_EnforcesExactPixelBudget(int width, int height)
    {
        var result = TestAgent.Process(
            CreatePng(width, height),
            nativeScale: 1,
            autoScale: true,
            targetScale: 1,
            maxPixelWidth: null,
            maxPixelHeight: null,
            maxPixels: 100);

        Assert.True(result.PixelWidth * result.PixelHeight <= 100);
        Assert.True(result.Clamped);
    }

    [Fact]
    public void ProcessScreenshotPng_CapsWithoutTargetScale_PreserveAutoScaling()
    {
        var result = TestAgent.Process(
            SamplePng,
            nativeScale: 2,
            autoScale: true,
            targetScale: null,
            maxPixelWidth: 1000,
            maxPixelHeight: 1000,
            maxPixels: 1_000_000);

        Assert.Equal(200, result.PixelWidth);
        Assert.Equal(150, result.PixelHeight);
        Assert.Equal(1, result.CaptureScale);
    }

    [Fact]
    public void ProcessScreenshotPng_ExplicitLegacyWidth_PreservesWidthOverride()
    {
        var result = TestAgent.Process(
            SamplePng,
            nativeScale: 2,
            autoScale: true,
            targetScale: null,
            maxPixelWidth: 1000,
            maxPixelHeight: 1000,
            maxPixels: 1_000_000,
            maxWidth: 300,
            scaleModeExplicit: false);

        Assert.Equal(300, result.PixelWidth);
        Assert.Equal(225, result.PixelHeight);
        Assert.Equal(1.5, result.CaptureScale);
    }

    [Fact]
    public void ProcessScreenshotPng_CompatibilityWidthWithExplicitAuto_PreservesAutoScaling()
    {
        var result = TestAgent.Process(
            SamplePng,
            nativeScale: 2,
            autoScale: true,
            targetScale: null,
            maxPixelWidth: 300,
            maxPixelHeight: 1000,
            maxPixels: 1_000_000,
            maxWidth: 300,
            scaleModeExplicit: true);

        Assert.Equal(200, result.PixelWidth);
        Assert.Equal(150, result.PixelHeight);
        Assert.Equal(1, result.CaptureScale);
    }

    [Fact]
    public void ProcessScreenshotPng_InvalidPngWithHints_ReturnsStructuredFailure()
    {
        var response = TestAgent.ProcessResponse(
            [0x01, 0x02, 0x03],
            nativeScale: 2,
            autoScale: true,
            targetScale: 1,
            maxPixelWidth: 200,
            maxPixelHeight: 200,
            maxPixels: 40_000);

        Assert.Equal(500, response.StatusCode);
        Assert.Contains("screenshot-resolution-failed", response.Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("targetScale", "NaN")]
    [InlineData("targetScale", "0")]
    [InlineData("maxPixelWidth", "-1")]
    [InlineData("maxPixelHeight", "abc")]
    [InlineData("maxPixels", "0")]
    public void ParseHints_InvalidValue_ReturnsBadRequest(string name, string value)
    {
        var result = TestAgent.Parse(name, value);

        Assert.False(result.Success);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal("invalid-screenshot-hint", result.Reason);
    }

    [Fact]
    public void ParseHints_OverLimitValues_AreClamped()
    {
        var result = TestAgent.Parse(
            ("targetScale", "8"),
            ("maxPixelWidth", "50000"),
            ("maxPixelHeight", "40000"),
            ("maxPixels", "99999999"));

        Assert.True(result.Success);
        Assert.Equal(4, result.TargetScale);
        Assert.Equal(16_384, result.MaxPixelWidth);
        Assert.Equal(16_384, result.MaxPixelHeight);
        Assert.Equal(16_777_216, result.MaxPixels);
        Assert.True(result.Clamped);
    }

    private static byte[] CreatePng(int width, int height)
    {
        using var bitmap = new SkiaSharp.SKBitmap(width, height);
        bitmap.Erase(SkiaSharp.SKColors.Transparent);
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    private sealed class TestAgent : DevFlowAgentService
    {
        public static ProcessResult Process(
            byte[] data,
            double nativeScale,
            bool autoScale,
            double? targetScale,
            int? maxPixelWidth,
            int? maxPixelHeight,
            long? maxPixels,
            int? maxWidth = null,
            bool scaleModeExplicit = false)
        {
            var hints = CreateHints(targetScale, maxPixelWidth, maxPixelHeight, maxPixels);
            var result = ProcessScreenshotPng(
                data,
                maxWidth,
                nativeScale,
                autoScale,
                scaleModeExplicit,
                hints);
            return new ProcessResult(
                result.PixelWidth!.Value,
                result.PixelHeight!.Value,
                result.LogicalWidth!.Value,
                result.LogicalHeight!.Value,
                result.CaptureScale!.Value,
                result.NativeScale!.Value,
                result.ResolutionClamped ?? false);
        }

        public static HttpResponse ProcessResponse(
            byte[] data,
            double nativeScale,
            bool autoScale,
            double? targetScale,
            int? maxPixelWidth,
            int? maxPixelHeight,
            long? maxPixels)
            => BuildScreenshotResponse(ProcessScreenshotPng(
                data,
                maxWidth: null,
                nativeScale,
                autoScale,
                scaleModeExplicit: true,
                CreateHints(targetScale, maxPixelWidth, maxPixelHeight, maxPixels)));

        public static ParseResult Parse(string name, string value)
            => Parse([(name, value)]);

        public static ParseResult Parse(params (string Name, string Value)[] values)
        {
            var request = new HttpRequest
            {
                QueryParams = values.ToDictionary(pair => pair.Name, pair => pair.Value)
            };
            var success = TryParseScreenshotResolutionHints(request, out var hints, out var error);
            return new ParseResult(
                success,
                error?.StatusCode,
                error?.Body?.Contains("invalid-screenshot-hint", StringComparison.Ordinal) == true
                    ? "invalid-screenshot-hint"
                    : null,
                hints?.TargetScale,
                hints?.MaxPixelWidth,
                hints?.MaxPixelHeight,
                hints?.MaxPixels,
                hints?.WasClamped ?? false);
        }

        private static ScreenshotResolutionHints? CreateHints(
            double? targetScale,
            int? maxPixelWidth,
            int? maxPixelHeight,
            long? maxPixels)
            => targetScale.HasValue
                || maxPixelWidth.HasValue
                || maxPixelHeight.HasValue
                || maxPixels.HasValue
                    ? new ScreenshotResolutionHints(
                        targetScale,
                        maxPixelWidth,
                        maxPixelHeight,
                        maxPixels,
                        WasClamped: false)
                    : null;
    }

    private sealed record ProcessResult(
        int PixelWidth,
        int PixelHeight,
        double LogicalWidth,
        double LogicalHeight,
        double CaptureScale,
        double NativeScale,
        bool Clamped);

    private sealed record ParseResult(
        bool Success,
        int? StatusCode,
        string? Reason,
        double? TargetScale,
        int? MaxPixelWidth,
        int? MaxPixelHeight,
        long? MaxPixels,
        bool Clamped);
}
