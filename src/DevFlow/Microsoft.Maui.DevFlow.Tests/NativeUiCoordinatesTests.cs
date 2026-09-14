using Microsoft.Maui.DevFlow.Agent.Native;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// Covers <see cref="NativeUi.FlipAppKitWindowBaseToTopLeft"/>: the pure math behind
/// <c>NativeUi.Describe</c> (macOS) that turns AppKit's bottom-left-origin window base
/// coordinates into the top-left-origin, window-relative coordinates the native agent's
/// <c>ui.hit-test</c> capability advertises as <c>window-logical-coordinates</c>.
/// </summary>
/// <remarks>
/// This flip was previously done against the screen's frame instead of the window's, so it only
/// diverged from window-logical coordinates once a window moved away from the screen origin —
/// exactly the scenario a live, on-screen NSWindow test would not exercise unless it was
/// deliberately repositioned. These cases assert the algebra directly, including a window placed
/// well away from (0, 0).
/// </remarks>
public class NativeUiCoordinatesTests
{
    [Fact]
    public void FlipAppKitWindowBaseToTopLeft_ViewAtWindowOrigin_ReturnsWindowOrigin()
    {
        var (x, y) = NativeUi.FlipAppKitWindowBaseToTopLeft(
            windowBaseX: 0,
            windowBaseY: 0,
            height: 40,
            windowHeight: 600);

        // A view pinned to the window's bottom-left corner (AppKit's origin) is 40pt tall, so its
        // top-left, window-logical Y sits 40pt above the window's bottom edge.
        Assert.Equal(0, x);
        Assert.Equal(560, y);
    }

    [Fact]
    public void FlipAppKitWindowBaseToTopLeft_ViewAtWindowTopLeft_ReturnsZero()
    {
        // A view flush with the window's *visual* top-left corner sits at
        // (windowHeight - height) in AppKit's bottom-left-origin base coordinates.
        var (x, y) = NativeUi.FlipAppKitWindowBaseToTopLeft(
            windowBaseX: 0,
            windowBaseY: 560,
            height: 40,
            windowHeight: 600);

        Assert.Equal(0, x);
        Assert.Equal(0, y);
    }

    [Theory]
    [InlineData(0, 0, 900)]
    [InlineData(1200, 850, 900)] // window moved far right and near the bottom of a taller screen
    [InlineData(-400, -50, 900)] // window moved to negative screen coordinates (secondary monitor)
    public void FlipAppKitWindowBaseToTopLeft_IsIndependentOfWindowScreenPosition(
        double windowScreenX, double windowScreenY, double windowHeight)
    {
        // The flip only ever takes the view's window-base rect and the window's own frame height —
        // never the window's (or screen's) position on screen — so moving the window around the
        // screen must not change the result for the same view-in-window geometry. This is exactly
        // the regression the reviewer flagged: computing against window.Frame.Y (screen-relative)
        // instead of the window's own height silently breaks once a window isn't at the screen
        // origin.
        _ = (windowScreenX, windowScreenY); // included for readability of the InlineData cases only

        var (x, y) = NativeUi.FlipAppKitWindowBaseToTopLeft(
            windowBaseX: 10,
            windowBaseY: 20,
            height: 30,
            windowHeight: windowHeight);

        Assert.Equal(10, x);
        Assert.Equal(windowHeight - 50, y);
    }

    [Fact]
    public void FlipAppKitWindowBaseToTopLeft_ViewSpanningFullWindow_ReturnsWindowOrigin()
    {
        var (x, y) = NativeUi.FlipAppKitWindowBaseToTopLeft(
            windowBaseX: 0,
            windowBaseY: 0,
            height: 480,
            windowHeight: 480);

        Assert.Equal(0, x);
        Assert.Equal(0, y);
    }
}
