using Microsoft.Maui.DevFlow.Agent.Core;

namespace Microsoft.Maui.DevFlow.Tests;

/// <summary>
/// Injected touches must start on the view the caller named — UIKit refuses them otherwise, and on
/// Android a pointer off the view would be a gesture the view never sees — so the geometry has to
/// keep each point inside the bounds whatever was requested.
/// </summary>
public class SyntheticTouchGeometryTests
{
    private const double Width = 300;
    private const double Height = 140;
    private const double Inset = SyntheticTouchGeometry.EdgeInset;

    private static void AssertOnView(double x, double y)
    {
        Assert.InRange(x, Inset, Width - Inset);
        Assert.InRange(y, Inset, Height - Inset);
    }

    [Fact]
    public void Drag_ThatFits_IsDeliveredUnchanged()
    {
        var drag = SyntheticTouchGeometry.ForDrag(Width, Height, 80, 0);

        Assert.NotNull(drag);
        Assert.Equal(80, drag.Value.DeltaX);
        Assert.Equal(0, drag.Value.DeltaY);
        Assert.Equal(110, drag.Value.StartX);
        Assert.Equal(70, drag.Value.StartY);
    }

    [Theory]
    [InlineData(5000, 0)]
    [InlineData(-5000, 0)]
    [InlineData(0, 5000)]
    [InlineData(0, -5000)]
    [InlineData(1000, -1000)]
    public void Drag_LongerThanTheView_StartsAndEndsOnIt(double deltaX, double deltaY)
    {
        var drag = SyntheticTouchGeometry.ForDrag(Width, Height, deltaX, deltaY);

        Assert.NotNull(drag);
        var d = drag.Value;
        AssertOnView(d.StartX, d.StartY);
        AssertOnView(d.StartX + d.DeltaX, d.StartY + d.DeltaY);
    }

    [Fact]
    public void Drag_LongerThanTheView_IsShortenedAlongItsOwnDirection()
    {
        var drag = SyntheticTouchGeometry.ForDrag(Width, Height, 600, 600);

        Assert.NotNull(drag);
        // Height is the tighter bound; clamping each axis on its own would turn a 45° drag shallow.
        Assert.Equal(Height - Inset * 2, drag.Value.DeltaY, 6);
        Assert.Equal(drag.Value.DeltaY, drag.Value.DeltaX, 6);
    }

    [Theory]
    [InlineData(0, Height, 10, 0)]
    [InlineData(Width, Inset * 2, 10, 0)]
    [InlineData(Width, Height, double.NaN, 0)]
    [InlineData(Width, Height, 0, double.PositiveInfinity)]
    public void Drag_IsRefusedWhenItCannotBeDelivered(double width, double height, double deltaX, double deltaY)
        => Assert.Null(SyntheticTouchGeometry.ForDrag(width, height, deltaX, deltaY));

    [Theory]
    [InlineData(0.5, 0.5, 0.5)]
    [InlineData(0.5, 0.5, 2)]
    [InlineData(0, 0, 0.5)]
    [InlineData(1, 1, 0.5)]
    [InlineData(0, 1, 2)]
    [InlineData(1, 0, 2)]
    [InlineData(-3, 7, 4)]
    public void Pinch_FromAnyOrigin_KeepsBothFingersOnTheView(double originX, double originY, double scale)
    {
        var pinch = SyntheticTouchGeometry.ForPinch(Width, Height, originX, originY, scale);

        Assert.NotNull(pinch);
        var p = pinch.Value;
        foreach (var radius in new[] { p.StartRadius, p.EndRadius })
        {
            AssertOnView(p.CenterX - radius, p.CenterY);
            AssertOnView(p.CenterX + radius, p.CenterY);
        }
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(2)]
    public void Pinch_FingerSpanChangesByTheRequestedScale(double scale)
    {
        var pinch = SyntheticTouchGeometry.ForPinch(Width, Height, 0.5, 0.5, scale);

        Assert.NotNull(pinch);
        Assert.Equal(scale, pinch.Value.EndRadius / pinch.Value.StartRadius, 6);
    }

    [Theory]
    [InlineData(Width, Height, 0)]
    [InlineData(Width, Height, -1)]
    [InlineData(Width, Height, double.NaN)]
    [InlineData(Inset * 2, Height, 2)]
    public void Pinch_IsRefusedWhenItCannotBeDelivered(double width, double height, double scale)
        => Assert.Null(SyntheticTouchGeometry.ForPinch(width, height, 0.5, 0.5, scale));

    [Theory]
    [InlineData(0.5, 0.5)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(0, 1)]
    [InlineData(-3, 7)]
    public void Rotation_FromAnyOrigin_KeepsBothFingersOnTheViewAtEveryAngle(double originX, double originY)
    {
        var rotation = SyntheticTouchGeometry.ForRotation(Width, Height, originX, originY);

        Assert.NotNull(rotation);
        var r = rotation.Value;
        for (var degrees = 0; degrees < 360; degrees += 15)
        {
            var radians = degrees * Math.PI / 180;
            var dx = Math.Cos(radians) * r.Radius;
            var dy = Math.Sin(radians) * r.Radius;
            AssertOnView(r.CenterX - dx, r.CenterY - dy);
            AssertOnView(r.CenterX + dx, r.CenterY + dy);
        }
    }

    [Theory]
    [InlineData(Width, Inset * 2, 0.5)]
    [InlineData(0, Height, 0.5)]
    [InlineData(Width, Height, double.NaN)]
    public void Rotation_IsRefusedWhenItCannotBeDelivered(double width, double height, double originX)
        => Assert.Null(SyntheticTouchGeometry.ForRotation(width, height, originX, 0.5));
}
