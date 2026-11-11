namespace AIExtensions.Sample.Scrolling;

/// <summary>
/// The platform-agnostic state machine behind <see cref="ScrollAnchorMode.End"/>: decides from a view's scroll
/// position whether it follows the end of its content, and when to scroll to keep the end in view.
/// </summary>
/// <remarks>
/// <para>
/// A platform adapter reports <see cref="ScrollMetrics"/> after the platform has laid out a change (so a content size
/// change and the offset adjustments the platform makes in the same layout pass arrive together) and after the view
/// scrolled, plus whether the user is scrolling it (touching, dragging, or flinging). When <see cref="Update"/>
/// returns an offset, the adapter scrolls there without animating: it is always the end of the content.
/// </para>
/// <para>The rules:</para>
/// <list type="bullet">
/// <item><description>The anchor starts engaged, so a view shows its end when it first appears.</description></item>
/// <item><description>
/// While engaged, it scrolls to the end whenever the content or the viewport changes size: items appended or removed,
/// any item growing or shrinking, a header or footer changing, the viewport resizing. A scroll alone never makes it
/// scroll, so it never fights a scroll in progress.
/// </description></item>
/// <item><description>
/// A user scroll disengages it once the user has moved the viewport more than <see cref="Threshold"/> away from the
/// end. Content growing while the user scrolls doesn't count as the user moving away.
/// </description></item>
/// <item><description>
/// An offset change the user isn't making that comes with a size change is the platform laying out, and never
/// disengages it, even when the sizes end up where they started (the platform reports the change in between). One that
/// comes alone and moves away from the end is a deliberate scroll the platform doesn't report as the user's (the
/// keyboard, accessibility, a mouse wheel on some platforms): it disengages it.
/// </description></item>
/// <item><description>
/// A scroll the app requests (<c>ScrollTo</c>) is judged by where it lands: engaged only if that's the end.
/// </description></item>
/// <item><description>
/// The viewport being at the end, within <see cref="Threshold"/>, engages it again, however it got there.
/// </description></item>
/// </list>
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
    /// back to the size it had. A platform can shrink its content and grow it back in one layout pass (re-estimating
    /// the rows it measured before), moving the offset as it goes: <paramref name="metrics"/> then show the same size and
    /// a new offset, which this says is the layout's doing.
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
                // The app scrolled the view: follow the end again only if that's where it went.
                IsAnchored = atEnd;
                _drift = 0;
                _correctionPending = false;
                return null;
            }

            // An animated request scrolls like a fling: judge it as one.
        }

        if (last is not { } previous)
            return isUserScrolling ? null : Correction(metrics);

        if (isUserScrolling)
        {
            if (IsAnchored)
            {
                // The user's net movement away from the end in this gesture: moving back toward it cancels it out, and
                // content growing below the viewport doesn't add to it.
                _drift = Math.Max(0, _drift - (metrics.Offset - previous.Offset));
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
    /// <param name="animated">
    /// Whether the scroll animates. An animation only moves toward the end, which never disengages the anchor; one that
    /// doesn't animate is checked on the next update, in case the end moved meanwhile.
    /// </param>
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

        // The adapter scrolls there before anything else happens: measure the next change from there, so this scroll
        // isn't mistaken for part of the next one.
        _last = metrics with { Offset = target };
        return target;
    }

    private static bool Differs(double a, double b) => Math.Abs(a - b) > Tolerance;
}
