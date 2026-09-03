namespace Microsoft.Maui.DevFlow.Agent.Native;

/// <summary>
/// Framework-neutral coordinate-space math shared by the native <c>NativeUi</c> backends.
/// Kept in its own file — with no AppKit/UIKit/Android dependency — so the conversion itself,
/// the part most likely to regress silently when a window sits away from the screen origin,
/// can be unit tested without a live window.
/// </summary>
internal static partial class NativeUi
{
    /// <summary>
    /// Converts a rectangle already expressed in AppKit's window base coordinate system
    /// (bottom-left origin, relative to the containing <c>NSWindow</c>'s own frame — the result
    /// of <c>NSView.ConvertRectToView(bounds, null)</c>) into DevFlow's window-logical
    /// coordinates: top-left origin, still relative to that same window.
    /// </summary>
    /// <remarks>
    /// The <c>ui.hit-test</c> capability advertises <c>window-logical-coordinates</c>, and
    /// <see cref="NativeDevFlowAgentService"/> compares hit-test x/y directly against
    /// <c>ElementInfo.Bounds</c> — so every backend's reported bounds must be relative to the
    /// containing window's own top-left corner, not the screen. Flipping against
    /// <paramref name="windowHeight"/> (the window's own frame height) rather than the screen's
    /// height is what keeps the result correct once the window has moved away from the screen
    /// origin.
    /// </remarks>
    public static (double X, double Y) FlipAppKitWindowBaseToTopLeft(
        double windowBaseX,
        double windowBaseY,
        double height,
        double windowHeight)
        => (windowBaseX, windowHeight - (windowBaseY + height));
}
