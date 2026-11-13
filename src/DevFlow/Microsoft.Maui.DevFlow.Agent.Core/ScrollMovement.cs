namespace Microsoft.Maui.DevFlow.Agent.Core;

/// <summary>
/// How far a delta scroll really moved a view, in the same units and with the same sign as the
/// requested <c>deltaX</c>/<c>deltaY</c>. A view that is already at the end of its range in the
/// requested direction reports a movement of zero.
/// </summary>
public readonly record struct ScrollMovement(double DeltaX, double DeltaY)
{
    /// <summary>Movements smaller than this (in the view's units) count as "did not move".</summary>
    public const double Tolerance = 0.5;

    /// <summary>A scroll that was performed but whose movement the backend cannot measure.</summary>
    public static ScrollMovement Unmeasured { get; } = new(0, 0) { IsMeasured = false };

    /// <summary>False when the backend could not tell how far the view moved.</summary>
    public bool IsMeasured { get; init; } = true;

    /// <summary>True when the view moved on at least one axis.</summary>
    public bool Moved => Math.Abs(DeltaX) >= Tolerance || Math.Abs(DeltaY) >= Tolerance;

    /// <summary>
    /// The offset a native scroll lands on: <paramref name="current"/> plus <paramref name="change"/>,
    /// clamped to the scrollable range [<paramref name="min"/>, <paramref name="max"/>]. A range whose
    /// maximum lies below its minimum (content smaller than the viewport) clamps to the minimum.
    /// </summary>
    public static double ClampOffset(double current, double change, double min, double max)
        => Math.Clamp(current + change, min, Math.Max(min, max));
}
