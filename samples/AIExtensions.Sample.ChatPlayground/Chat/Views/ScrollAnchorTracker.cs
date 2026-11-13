namespace AIExtensions.Sample.ChatPlayground;

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

/// <summary>
/// Decides from a scrolling view's position whether it follows the end of its content, and when to scroll to keep the
/// end in view while turns are added or stream in.
/// </summary>
/// <remarks>
/// <para>
/// <c>ItemsUpdatingScrollMode.KeepLastItemInView</c> only reacts to collection changes, so it never follows a block
/// that grows while its response streams, and on MAUI 10.0.41 it does nothing on iOS and scrolls to the top on Android
/// (<see href="https://github.com/dotnet/maui/issues/35802">dotnet/maui#35802</see>). This sample-side tracker and
/// <see cref="ChatArea"/>'s platform adapters stand in for upstream's CollectionView end-anchoring proposal
/// (<c>AnchorMode = ScrollAnchorMode.End</c>,
/// <see href="https://github.com/dotnet/maui/pull/40211">dotnet/maui#40211</see>).
/// </para>
/// <para>
/// A platform adapter reports <see cref="ScrollMetrics"/> after the platform has laid out a change (so a content size
/// change and the offset adjustments the platform makes in the same layout pass arrive together) and after the view
/// scrolled, plus whether the user is scrolling it (touching, dragging, or flinging). When <see cref="Update"/>
/// returns an offset, the adapter scrolls there without animating: it is always the end of the content.
/// </para>
/// </remarks>
internal sealed class ScrollAnchorTracker
{
    /// <summary>The default for how close to the end, in device-independent units, still counts as at the end.</summary>
    public const double DefaultThreshold = 8;

    // Smaller differences are rounding to device pixels, not movement.
    private const double Tolerance = 0.5;

    private ScrollMetrics? _last;
    private bool _lastWasUserScroll;
    private double _drift;
    private bool _correctionPending;
    private bool _scrollRequested;

    /// <summary>Initializes a new tracker, engaged.</summary>
    /// <param name="threshold">How close to the end, in device-independent units, still counts as at the end.</param>
    public ScrollAnchorTracker(double threshold = DefaultThreshold)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(threshold);
        Threshold = threshold;
    }

    /// <summary>Gets how close to the end, in device-independent units, still counts as at the end.</summary>
    public double Threshold { get; }

    /// <summary>Gets whether the anchor is engaged: the view follows the end of its content.</summary>
    public bool IsAnchored { get; private set; } = true;

    /// <summary>Starts over for a new platform view: engaged, with no position seen yet.</summary>
    public void Reset()
    {
        _last = null;
        _lastWasUserScroll = false;
        _drift = 0;
        _correctionPending = false;
        _scrollRequested = false;
        IsAnchored = true;
    }

    /// <summary>Observes where the view is, after the platform laid out a change or the view scrolled.</summary>
    /// <param name="metrics">The view's current position.</param>
    /// <param name="isUserScrolling">Whether the user is scrolling the view: touching, dragging, or flinging it.</param>
    /// <param name="sizeChanged">
    /// Whether the platform reported that the content or the viewport changed size since the last update, even if it is
    /// back to the size it had.
    /// </param>
    /// <returns>The end offset to scroll to without animating, or <see langword="null"/> to stay.</returns>
    public double? Update(ScrollMetrics metrics, bool isUserScrolling, bool sizeChanged = false)
    {
        if (!metrics.IsValid)
            return null;

        var last = _last;
        var wasUserScroll = _lastWasUserScroll;
        _last = metrics;
        _lastWasUserScroll = isUserScrolling;
        var atEnd = metrics.DistanceToEnd <= Threshold;

        if (_scrollRequested)
        {
            _scrollRequested = false;
            if (!isUserScrolling)
            {
                IsAnchored = atEnd;
                _drift = 0;
                _correctionPending = false;
                return null;
            }
        }

        if (last is not { } previous)
            return isUserScrolling ? null : Correction(metrics);

        if (isUserScrolling)
        {
            if (IsAnchored)
            {
                // The user's net movement away from the end in this gesture: moving back toward it cancels it out, and
                // neither content growing below the viewport nor springing back from past the end (a bounce) adds to
                // it. Only movement short of the end counts, and with the content resizing in between, short of both
                // ends.
                var end = Math.Min(previous.EndOffset, metrics.EndOffset);
                _drift = Math.Max(0, _drift - (Math.Min(metrics.Offset, end) - Math.Min(previous.Offset, end)));
                if (!atEnd && _drift > Threshold)
                    IsAnchored = false;
            }
            else if (atEnd)
            {
                IsAnchored = true;
                _drift = 0;
            }

            return null;
        }

        _drift = 0;
        var resized = sizeChanged || Differs(metrics.Extent, previous.Extent) || Differs(metrics.Viewport, previous.Viewport);
        if (IsAnchored)
        {
            if (!resized && metrics.Offset - previous.Offset < -Tolerance && !atEnd)
            {
                IsAnchored = false;
                _correctionPending = false;
                return null;
            }
        }
        else if (atEnd)
        {
            IsAnchored = true;
        }

        // Scroll to the end for a size change, or when a user scroll just ended (the end may have moved meanwhile).
        if (!resized && !wasUserScroll && !_correctionPending)
            return null;

        _correctionPending = false;
        return Correction(metrics);
    }

    /// <summary>Engages the anchor for a scroll to the end that the anchor itself starts.</summary>
    public void ScrollToEnd(bool animated)
    {
        IsAnchored = true;
        _drift = 0;
        _scrollRequested = false;
        _lastWasUserScroll = false;
        _correctionPending = !animated;
    }

    /// <summary>
    /// Notes that the app requested a scroll (such as <c>ScrollTo</c>): the next update judges where it landed, engaging
    /// the anchor only at the end, instead of scrolling the view back.
    /// </summary>
    public void RequestScroll() => _scrollRequested = true;

    private double? Correction(ScrollMetrics metrics)
    {
        if (!IsAnchored || metrics.Viewport <= 0)
            return null;

        var target = metrics.EndOffset;
        if (!Differs(target, metrics.Offset))
            return null;

        _last = metrics with { Offset = target };
        return target;
    }

    private static bool Differs(double a, double b) => Math.Abs(a - b) > Tolerance;
}
