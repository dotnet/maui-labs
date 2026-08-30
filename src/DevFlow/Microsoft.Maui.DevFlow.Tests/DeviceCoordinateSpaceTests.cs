using Microsoft.Maui.DevFlow.Devices;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// The coordinate chain between a device video frame and a MAUI element's bounds is the highest
/// bug risk in the device layer: an error here produces an overlay that looks plausible and is
/// wrong, rather than anything that throws. These tests pin the round trip in every orientation,
/// density and stream scale.
/// </summary>
public class DeviceCoordinateSpaceTests
{
    private const double Tolerance = 1e-9;

    private static DeviceCoordinateSpace Phone(
        double streamScale = 1,
        int quarterTurns = 0,
        double displayScale = 3) =>
        new(
            screenWidth: 390,
            screenHeight: 844,
            appWindow: new DeviceRect(0, 47, 390, 763),
            appWidth: 390,
            appHeight: 763,
            displayScale: displayScale,
            streamScale: streamScale,
            frameQuarterTurns: quarterTurns);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void FrameToDevice_RoundTrips_AtEveryOrientation(int quarterTurns)
    {
        var space = Phone(quarterTurns: quarterTurns);
        var points = new[]
        {
            new DevicePoint(0, 0),
            new DevicePoint(390, 844),
            new DevicePoint(195, 422),
            new DevicePoint(1, 843),
        };

        foreach (var original in points)
        {
            var roundTripped = space.FrameToDevice(space.DeviceToFrame(original));

            Assert.Equal(original.X, roundTripped.X, Tolerance);
            Assert.Equal(original.Y, roundTripped.Y, Tolerance);
        }
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(0.5)]
    [InlineData(0.25)]
    [InlineData(0.1)]
    public void FrameToDevice_RoundTrips_AtEveryStreamScale(double streamScale)
    {
        var space = Phone(streamScale: streamScale);
        var original = new DevicePoint(123.5, 456.25);

        var roundTripped = space.FrameToDevice(space.DeviceToFrame(original));

        Assert.Equal(original.X, roundTripped.X, Tolerance);
        Assert.Equal(original.Y, roundTripped.Y, Tolerance);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void FrameToDevice_RoundTrips_AtEveryDensity(double displayScale)
    {
        var space = Phone(displayScale: displayScale);
        var original = new DevicePoint(200, 500);

        var roundTripped = space.FrameToDevice(space.DeviceToFrame(original));

        Assert.Equal(original.X, roundTripped.X, Tolerance);
        Assert.Equal(original.Y, roundTripped.Y, Tolerance);
    }

    [Fact]
    public void AppToFrame_RoundTrips_ThroughEverySpace()
    {
        var space = Phone(streamScale: 0.5);
        var original = new AppPoint(120, 300);

        var roundTripped = space.FrameToApp(space.AppToFrame(original));

        Assert.NotNull(roundTripped);
        Assert.Equal(original.X, roundTripped!.Value.X, Tolerance);
        Assert.Equal(original.Y, roundTripped.Value.Y, Tolerance);
    }

    [Fact]
    public void DeviceToApp_SubtractsTheWindowOrigin()
    {
        var space = Phone();

        // The app window starts 47 points down, below the status bar.
        var appPoint = space.DeviceToApp(new DevicePoint(10, 57));

        Assert.NotNull(appPoint);
        Assert.Equal(10, appPoint!.Value.X, Tolerance);
        Assert.Equal(10, appPoint.Value.Y, Tolerance);
    }

    [Fact]
    public void DeviceToApp_ReturnsNull_AboveTheAppWindow()
    {
        var space = Phone();

        // A point in the status bar belongs to the OS, not the app. Returning null here is what
        // makes an interaction fall through to the device layer instead of being mis-sent.
        Assert.Null(space.DeviceToApp(new DevicePoint(10, 20)));
    }

    [Fact]
    public void DeviceToApp_ReturnsNull_BelowTheAppWindow()
    {
        var space = Phone();

        Assert.Null(space.DeviceToApp(new DevicePoint(10, 830)));
    }

    [Fact]
    public void IsInsideAppWindow_ExcludesTheFarEdges()
    {
        var space = Phone();

        Assert.True(space.IsInsideAppWindow(new DevicePoint(0, 47)));
        Assert.False(space.IsInsideAppWindow(new DevicePoint(390, 47)));
        Assert.False(space.IsInsideAppWindow(new DevicePoint(0, 810)));
    }

    [Fact]
    public void FrameDimensions_SwapWhenQuarterTurned()
    {
        var upright = Phone(quarterTurns: 0);
        var turned = Phone(quarterTurns: 1);

        Assert.Equal(390 * 3, upright.FrameWidth, Tolerance);
        Assert.Equal(844 * 3, upright.FrameHeight, Tolerance);
        Assert.Equal(844 * 3, turned.FrameWidth, Tolerance);
        Assert.Equal(390 * 3, turned.FrameHeight, Tolerance);
    }

    [Fact]
    public void DeviceToFrame_MapsTheOriginToTheTopRight_WhenRotatedClockwise()
    {
        var space = Phone(quarterTurns: 1, displayScale: 1);

        // Rotating the screen a quarter turn clockwise sends its top-left corner to the frame's
        // top-right. Pinning a specific corner catches a transposition that a round-trip test
        // alone would not, because an inverse-consistent but mirrored transform still round-trips.
        var frame = space.DeviceToFrame(new DevicePoint(0, 0));

        Assert.Equal(844, frame.X, Tolerance);
        Assert.Equal(0, frame.Y, Tolerance);
    }

    [Fact]
    public void DeviceToFrame_MapsTheOriginToTheBottomLeft_WhenRotatedCounterClockwise()
    {
        var space = Phone(quarterTurns: 3, displayScale: 1);

        var frame = space.DeviceToFrame(new DevicePoint(0, 0));

        Assert.Equal(0, frame.X, Tolerance);
        Assert.Equal(390, frame.Y, Tolerance);
    }

    [Fact]
    public void AppLogicalUnits_ScaleIndependentlyOfWindowPoints()
    {
        // A platform whose logical units differ from display points must not skew the overlay.
        var space = new DeviceCoordinateSpace(
            screenWidth: 400,
            screenHeight: 800,
            appWindow: new DeviceRect(0, 0, 400, 800),
            appWidth: 200,
            appHeight: 400);

        var appPoint = space.DeviceToApp(new DevicePoint(200, 400));

        Assert.NotNull(appPoint);
        Assert.Equal(100, appPoint!.Value.X, Tolerance);
        Assert.Equal(200, appPoint.Value.Y, Tolerance);
    }

    [Fact]
    public void FromDisplay_DerivesPointSize_WhenOnlyPixelsAreReported()
    {
        var display = new DisplayGeometry
        {
            PixelWidth = 1170,
            PixelHeight = 2532,
            Scale = 3,
        };

        var space = DeviceCoordinateSpace.FromDisplay(display);

        Assert.Equal(390, space.ScreenWidth, Tolerance);
        Assert.Equal(844, space.ScreenHeight, Tolerance);
    }

    [Fact]
    public void FromDisplay_AssumesTheAppFillsTheScreen_WhenNoWindowIsKnown()
    {
        var display = new DisplayGeometry
        {
            PixelWidth = 1170,
            PixelHeight = 2532,
            PointWidth = 390,
            PointHeight = 844,
            Scale = 3,
        };

        var space = DeviceCoordinateSpace.FromDisplay(display);

        Assert.Equal(0, space.AppWindow.X, Tolerance);
        Assert.Equal(0, space.AppWindow.Y, Tolerance);
        Assert.Equal(390, space.AppWindow.Width, Tolerance);
        Assert.Equal(844, space.AppWindow.Height, Tolerance);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1.5)]
    public void Constructor_RejectsAnUnusableStreamScale(double streamScale)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DeviceCoordinateSpace(
            390, 844, new DeviceRect(0, 0, 390, 844), 390, 844, streamScale: streamScale));
    }

    [Fact]
    public void Constructor_NormalisesOutOfRangeQuarterTurns()
    {
        Assert.Equal(1, new DeviceCoordinateSpace(
            390, 844, new DeviceRect(0, 0, 390, 844), 390, 844, frameQuarterTurns: 5).FrameQuarterTurns);
        Assert.Equal(3, new DeviceCoordinateSpace(
            390, 844, new DeviceRect(0, 0, 390, 844), 390, 844, frameQuarterTurns: -1).FrameQuarterTurns);
    }
}
