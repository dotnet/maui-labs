using System.Text.Json.Serialization;

namespace Microsoft.Maui.DevFlow.Devices;

/// <summary>A point in decoded video-frame or device-screenshot pixels.</summary>
public readonly record struct FramePoint(double X, double Y);

/// <summary>A point in device-independent points, relative to the device screen origin.</summary>
public readonly record struct DevicePoint(double X, double Y);

/// <summary>A point in the app window's logical units — the space MAUI element bounds use.</summary>
public readonly record struct AppPoint(double X, double Y);

/// <summary>A rectangle in device-independent points.</summary>
public readonly record struct DeviceRect(
    [property: JsonPropertyName("x")] double X,
    [property: JsonPropertyName("y")] double Y,
    [property: JsonPropertyName("width")] double Width,
    [property: JsonPropertyName("height")] double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;

    public bool Contains(DevicePoint point) =>
        point.X >= X && point.X < Right && point.Y >= Y && point.Y < Bottom;
}

/// <summary>
/// Converts between the coordinate spaces that sit between a device video frame and a MAUI
/// element's bounds.
/// <para>
/// The chain is: <c>frame pixels → device pixels → device points → app logical units</c>.
/// Every space is a place to be silently and plausibly wrong, so the whole chain lives here
/// rather than being recomputed by individual callers.
/// </para>
/// <para>
/// This type deliberately stops at app logical units. Subtracting the visual tree's root offset
/// is the existing renderer's job and is not duplicated here.
/// </para>
/// </summary>
public sealed class DeviceCoordinateSpace
{
    /// <summary>
    /// Fraction of the device's pixel dimensions actually encoded into the frame. A client
    /// renders into a panel that is usually far smaller than a 3x framebuffer and asks the
    /// encoder to scale down, so frame pixels are not device pixels.
    /// </summary>
    public double StreamScale { get; }

    /// <summary>Device pixels per device point.</summary>
    public double DisplayScale { get; }

    /// <summary>The device screen size in points.</summary>
    public double ScreenWidth { get; }

    /// <summary>The device screen size in points.</summary>
    public double ScreenHeight { get; }

    /// <summary>
    /// The app window's rectangle within the device screen, in points. On a phone this is
    /// usually the screen inset by the status and navigation bars.
    /// </summary>
    public DeviceRect AppWindow { get; }

    /// <summary>
    /// The app window size in the app's own logical units. Normally equal to the window's point
    /// size, but carried separately so a platform whose logical units differ from display points
    /// does not silently skew the overlay.
    /// </summary>
    public double AppWidth { get; }

    /// <summary>The app window size in the app's own logical units.</summary>
    public double AppHeight { get; }

    /// <summary>
    /// Clockwise quarter-turns that were applied to the device screen to produce the frame.
    /// <para>
    /// Zero — the default — means the frame already arrives in the device's current orientation,
    /// which is what both simulator screenshots and the emulator video stream do today. It is
    /// modelled explicitly anyway so a backend that delivers frames in the display's natural
    /// orientation can be corrected without reshaping this API.
    /// </para>
    /// </summary>
    public int FrameQuarterTurns { get; }

    public DeviceCoordinateSpace(
        double screenWidth,
        double screenHeight,
        DeviceRect appWindow,
        double appWidth,
        double appHeight,
        double displayScale = 1,
        double streamScale = 1,
        int frameQuarterTurns = 0)
    {
        if (screenWidth <= 0) throw new ArgumentOutOfRangeException(nameof(screenWidth));
        if (screenHeight <= 0) throw new ArgumentOutOfRangeException(nameof(screenHeight));
        if (appWidth <= 0) throw new ArgumentOutOfRangeException(nameof(appWidth));
        if (appHeight <= 0) throw new ArgumentOutOfRangeException(nameof(appHeight));
        if (displayScale <= 0) throw new ArgumentOutOfRangeException(nameof(displayScale));
        if (streamScale <= 0 || streamScale > 1) throw new ArgumentOutOfRangeException(nameof(streamScale));

        ScreenWidth = screenWidth;
        ScreenHeight = screenHeight;
        AppWindow = appWindow;
        AppWidth = appWidth;
        AppHeight = appHeight;
        DisplayScale = displayScale;
        StreamScale = streamScale;
        FrameQuarterTurns = ((frameQuarterTurns % 4) + 4) % 4;
    }

    /// <summary>
    /// Builds a coordinate space from a device's reported geometry, assuming the app window
    /// fills the screen. Callers that know the real window rectangle should pass it instead:
    /// the window origin is the one value the device layer cannot infer.
    /// </summary>
    public static DeviceCoordinateSpace FromDisplay(
        DisplayGeometry display,
        DeviceRect? appWindow = null,
        double? appWidth = null,
        double? appHeight = null,
        double streamScale = 1,
        int frameQuarterTurns = 0)
    {
        ArgumentNullException.ThrowIfNull(display);

        // Prefer the reported point size; fall back to deriving it so a backend that only fills
        // in pixels still produces a usable space rather than throwing.
        var scale = display.Scale > 0 ? display.Scale : 1;
        var pointWidth = display.PointWidth > 0 ? display.PointWidth : display.PixelWidth / scale;
        var pointHeight = display.PointHeight > 0 ? display.PointHeight : display.PixelHeight / scale;

        var window = appWindow ?? new DeviceRect(0, 0, pointWidth, pointHeight);
        return new DeviceCoordinateSpace(
            pointWidth,
            pointHeight,
            window,
            appWidth ?? window.Width,
            appHeight ?? window.Height,
            scale,
            streamScale,
            frameQuarterTurns);
    }

    /// <summary>The frame's width in pixels, after stream scaling and rotation.</summary>
    public double FrameWidth => IsQuarterTurned ? ScreenHeight * DisplayScale * StreamScale
                                               : ScreenWidth * DisplayScale * StreamScale;

    /// <summary>The frame's height in pixels, after stream scaling and rotation.</summary>
    public double FrameHeight => IsQuarterTurned ? ScreenWidth * DisplayScale * StreamScale
                                                 : ScreenHeight * DisplayScale * StreamScale;

    private bool IsQuarterTurned => FrameQuarterTurns is 1 or 3;

    /// <summary>Converts a point in frame pixels to device points.</summary>
    public DevicePoint FrameToDevice(FramePoint point)
    {
        // Undo stream scaling and display density first, so rotation happens in a single,
        // well-defined space rather than being tangled with scaling.
        var x = point.X / StreamScale / DisplayScale;
        var y = point.Y / StreamScale / DisplayScale;

        // Frame dimensions in device points, before un-rotating.
        var frameWidthPoints = IsQuarterTurned ? ScreenHeight : ScreenWidth;

        return FrameQuarterTurns switch
        {
            0 => new DevicePoint(x, y),
            1 => new DevicePoint(y, frameWidthPoints - x),
            2 => new DevicePoint(ScreenWidth - x, ScreenHeight - y),
            _ => new DevicePoint(ScreenWidth - y, x),
        };
    }

    /// <summary>Converts a point in device points to frame pixels.</summary>
    public FramePoint DeviceToFrame(DevicePoint point)
    {
        var (x, y) = FrameQuarterTurns switch
        {
            0 => (point.X, point.Y),
            1 => (ScreenHeight - point.Y, point.X),
            2 => (ScreenWidth - point.X, ScreenHeight - point.Y),
            _ => (point.Y, ScreenWidth - point.X),
        };

        return new FramePoint(x * DisplayScale * StreamScale, y * DisplayScale * StreamScale);
    }

    /// <summary>True when a device point falls inside the app window.</summary>
    public bool IsInsideAppWindow(DevicePoint point) => AppWindow.Contains(point);

    /// <summary>
    /// Converts a device point to the app's logical space. Returns <c>null</c> when the point
    /// lies outside the app window — which is precisely the signal that an interaction must fall
    /// through to the device layer instead of being sent to the app agent.
    /// </summary>
    public AppPoint? DeviceToApp(DevicePoint point)
    {
        if (!IsInsideAppWindow(point))
            return null;

        return new AppPoint(
            (point.X - AppWindow.X) * (AppWidth / AppWindow.Width),
            (point.Y - AppWindow.Y) * (AppHeight / AppWindow.Height));
    }

    /// <summary>Converts an app logical point back to device points.</summary>
    public DevicePoint AppToDevice(AppPoint point) =>
        new(AppWindow.X + point.X * (AppWindow.Width / AppWidth),
            AppWindow.Y + point.Y * (AppWindow.Height / AppHeight));

    /// <summary>
    /// Resolves a frame point to the app's logical space, or <c>null</c> when it falls outside
    /// the app window. This is the entry point for a click on the live surface.
    /// </summary>
    public AppPoint? FrameToApp(FramePoint point) => DeviceToApp(FrameToDevice(point));

    /// <summary>Projects an app logical point onto the frame.</summary>
    public FramePoint AppToFrame(AppPoint point) => DeviceToFrame(AppToDevice(point));
}
