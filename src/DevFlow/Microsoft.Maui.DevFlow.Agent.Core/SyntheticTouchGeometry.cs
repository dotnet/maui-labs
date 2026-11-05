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

    internal readonly record struct Tap(double X, double Y);

    /// <summary>
    /// A drag from a point on the element to an end point. <see cref="ToX"/>/<see cref="ToY"/> are
    /// relative to the drop target when there is one and to the element otherwise; both are null
    /// when the drag ends at the centre of the drop target.
    /// </summary>
    internal readonly record struct PointDrag(double FromX, double FromY, double? ToX, double? ToY);

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

    /// <summary>
    /// A tap exactly where it was asked for. Unlike the gestures, a tap is not nudged inwards —
    /// content on a canvas is hit-tested from this point, so moving it would tap something else.
    /// Null when the point is not on the view: the near edges count as on it, the far edges not.
    /// </summary>
    internal static Tap? ForTap(double width, double height, double x, double y)
    {
        if (!(width > 0) || !(height > 0) || !double.IsFinite(x) || !double.IsFinite(y))
            return null;

        return x >= 0 && y >= 0 && x < width && y < height ? new Tap(x, y) : null;
    }

    /// <summary>
    /// Works out where a drag from a point ends: at <paramref name="toX"/>/<paramref name="toY"/>, at
    /// the drop target (its centre unless a point on it is given), or a vector away from the start.
    /// Returns the reason when the request is incomplete or names more than one end. The start
    /// point is not nudged or checked against the element here; the caller does that against the
    /// element's live bounds, and the end may lie off the element — dragging past a slider's end or
    /// onto another view is what a drag is for.
    /// </summary>
    internal static string? TryPlanPointDrag(
        double? x, double? y, double? toX, double? toY, bool hasDropTarget,
        double? deltaX, double? deltaY, out PointDrag plan)
    {
        plan = default;
        if (x is not { } fromX || y is not { } fromY)
            return "A drag from a point needs both x and y";
        if (toX.HasValue != toY.HasValue)
            return "toX and toY must be given together";
        if (!double.IsFinite(fromX) || !double.IsFinite(fromY)
            || toX is { } tx && !double.IsFinite(tx) || toY is { } ty && !double.IsFinite(ty)
            || deltaX is { } dx0 && !double.IsFinite(dx0) || deltaY is { } dy0 && !double.IsFinite(dy0))
            return "Drag coordinates must be finite numbers";

        var hasDelta = deltaX.HasValue || deltaY.HasValue;
        if (hasDelta && (toX.HasValue || hasDropTarget))
            return "Give the end of a drag once: as toX/toY, a drop target, or a deltaX/deltaY vector";

        if (hasDropTarget || toX.HasValue)
        {
            plan = new PointDrag(fromX, fromY, toX, toY);
            return null;
        }

        var dx = deltaX ?? 0;
        var dy = deltaY ?? 0;
        if (dx == 0 && dy == 0)
            return "A drag from a point needs an end: toX/toY, a drop target, or a non-zero deltaX/deltaY";

        plan = new PointDrag(fromX, fromY, fromX + dx, fromY + dy);
        return null;
    }

    private static bool HasRoom(double width, double height)
        => width > EdgeInset * 2 && height > EdgeInset * 2;
}
