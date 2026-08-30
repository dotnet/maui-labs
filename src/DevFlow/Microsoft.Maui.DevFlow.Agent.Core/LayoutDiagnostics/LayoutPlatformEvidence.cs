namespace Microsoft.Maui.DevFlow.Agent.Core.LayoutDiagnostics;

public static class LayoutPlatformEvidence
{
    public static LayoutRegionInfo Region(LayoutRect bounds, string precision)
        => new()
        {
            Bounds = bounds,
            Area = Math.Max(0, bounds.Width) * Math.Max(0, bounds.Height),
            Precision = precision,
            Points = bounds.Width <= 0 || bounds.Height <= 0
                ? []
                :
                [
                    new LayoutPointInfo { X = bounds.X, Y = bounds.Y },
                    new LayoutPointInfo { X = bounds.Right, Y = bounds.Y },
                    new LayoutPointInfo { X = bounds.Right, Y = bounds.Bottom },
                    new LayoutPointInfo { X = bounds.X, Y = bounds.Bottom },
                ],
        };

    public static LayoutRect? Intersect(LayoutRect left, LayoutRect right)
    {
        var x = Math.Max(left.X, right.X);
        var y = Math.Max(left.Y, right.Y);
        var rightEdge = Math.Min(left.Right, right.Right);
        var bottomEdge = Math.Min(left.Bottom, right.Bottom);
        return rightEdge > x && bottomEdge > y
            ? new LayoutRect { X = x, Y = y, Width = rightEdge - x, Height = bottomEdge - y }
            : null;
    }
}
