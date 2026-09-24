namespace Microsoft.Maui.DevFlow.Agent.Core;

/// <summary>
/// Where injected touches go on a view, in the view's own device-independent coordinates. Every
/// point stays strictly inside the bounds: a finger that starts off the view hits whatever is
/// beside it, and the far edge of a rect counts as outside. Shared by the UIKit synthetic-touch
/// tier and Android's MotionEvent injection, and free of both so it can be tested off-device.
/// </summary>
internal static class SyntheticTouchGeometry
{
    /// <summary>Distance kept from every edge.</summary>
    internal const double EdgeInset = 2;

    internal readonly record struct Pinch(double CenterX, double CenterY, double StartRadius, double EndRadius);

    internal readonly record struct Rotation(double CenterX, double CenterY, double Radius);

    internal readonly record struct Drag(double StartX, double StartY, double DeltaX, double DeltaY);

    /// <summary>
    /// Two fingers on a horizontal line through the origin. The origin is moved inwards when a
    /// full-radius pinch there would put a finger off the view. Null when the view is too small
    /// to hold a touch or the scale is not positive.
    /// </summary>
    internal static Pinch? ForPinch(double width, double height, double originX, double originY, double scale)
    {
        if (!HasRoom(width, height) || !(scale > 0) || !double.IsFinite(scale)
            || !double.IsFinite(originX) || !double.IsFinite(originY))
            return null;

        var radius = Math.Min(Math.Min(width, height) * 0.4, width / 2 - EdgeInset);
        var centerX = Math.Clamp(width * originX, EdgeInset + radius, width - EdgeInset - radius);
        var centerY = Math.Clamp(height * originY, EdgeInset, height - EdgeInset);

        // Zooming in starts with the fingers together and zooming out with them apart, so the
        // fingers never travel beyond the radius the view can hold.
        var (start, end) = scale >= 1 ? (radius / scale, radius) : (radius, radius * scale);
        return new Pinch(centerX, centerY, start, end);
    }

    /// <summary>
    /// Two fingers opposite each other on a circle around the origin, free to turn to any angle.
    /// The origin is moved inwards on both axes until the whole circle fits. Null when the view is
    /// too small to hold a touch.
    /// </summary>
    internal static Rotation? ForRotation(double width, double height, double originX, double originY)
    {
        if (!HasRoom(width, height) || !double.IsFinite(originX) || !double.IsFinite(originY))
            return null;

        var radius = Math.Min(Math.Min(width, height) * 0.4, Math.Min(width, height) / 2 - EdgeInset);
        var centerX = Math.Clamp(width * originX, EdgeInset + radius, width - EdgeInset - radius);
        var centerY = Math.Clamp(height * originY, EdgeInset + radius, height - EdgeInset - radius);
        return new Rotation(centerX, centerY, radius);
    }

    /// <summary>
    /// A single-finger drag centred on the view and offset against its travel, so it starts
    /// and ends on the view. A drag longer than the view is shortened along its own direction
    /// rather than started off the view; <see cref="Drag.DeltaX"/> and <see cref="Drag.DeltaY"/>
    /// are what is actually delivered.
    /// </summary>
    internal static Drag? ForDrag(double width, double height, double deltaX, double deltaY)
    {
        if (!HasRoom(width, height) || !double.IsFinite(deltaX) || !double.IsFinite(deltaY))
            return null;

        var maxX = width - EdgeInset * 2;
        var maxY = height - EdgeInset * 2;
        var factor = 1.0;
        if (Math.Abs(deltaX) > maxX) factor = Math.Min(factor, maxX / Math.Abs(deltaX));
        if (Math.Abs(deltaY) > maxY) factor = Math.Min(factor, maxY / Math.Abs(deltaY));

        var dx = deltaX * factor;
        var dy = deltaY * factor;
        return new Drag(width / 2 - dx / 2, height / 2 - dy / 2, dx, dy);
    }

    private static bool HasRoom(double width, double height)
        => width > EdgeInset * 2 && height > EdgeInset * 2;
}
