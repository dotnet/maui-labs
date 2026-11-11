namespace AIExtensions.Sample.Scrolling;

/// <summary>Where a scrolling view is along its scrolling axis, in device-independent units.</summary>
/// <param name="Offset">How far the content is scrolled: 0 shows its start.</param>
/// <param name="Extent">The length of the scrollable content, including any content insets.</param>
/// <param name="Viewport">The length of the visible area.</param>
internal readonly record struct ScrollMetrics(double Offset, double Extent, double Viewport)
{
    /// <summary>Gets how far the end of the content is past the end of the viewport: zero or less while it is in view.</summary>
    public double DistanceToEnd => Extent - Offset - Viewport;

    /// <summary>Gets the offset that shows the end of the content.</summary>
    public double EndOffset => Math.Max(0, Extent - Viewport);

    /// <summary>Gets whether every value is a finite number and the lengths aren't negative.</summary>
    public bool IsValid =>
        double.IsFinite(Offset) && double.IsFinite(Extent) && double.IsFinite(Viewport) && Extent >= 0 && Viewport >= 0;
}
