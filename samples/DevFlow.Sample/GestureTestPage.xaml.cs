using System.Globalization;

namespace DevFlow.Sample;

/// <summary>
/// Targets for DevFlow's gesture automation. Each handler writes what it received to an
/// AutomationId'd label so a test can assert the gesture genuinely reached the app,
/// rather than only that the HTTP call returned 200.
/// </summary>
public partial class GestureTestPage : ContentPage
{
    // Accumulated across a pinch the way an app would apply it, so a request for
    // scale 2.0 ends up reported as 2.00 regardless of how many steps it arrived in.
    private double _pinchScale = 1;

    // Last Running pan totals; see OnPanUpdated for why Completed cannot be used.
    private double _panX;
    private double _panY;
    private DateTime _longPressStartedAtUtc;

    // Raw-touch state. The whole sequence is summarised rather than only the last event:
    // a synthetic pinch ends on a single pointer once the others lift, so "touches" at the
    // end says nothing about whether the gesture was genuinely multi-touch.
    private PointF[] _rawTouchStart = [];
    private double _rawTouchSpanStart;
    private int _rawTouchMaxTouches;
    private int _rawTouchDragCount;
    private double _rawTouchPeakScale = 1;

    public GestureTestPage()
    {
        InitializeComponent();

        RawTouchCanvas.Drawable = new RawTouchDrawable();

        for (var i = 1; i <= 30; i++)
            NativeScrollContent.Add(new Label { Text = $"Native row {i}", AutomationId = $"NativeRow{i}" });
    }

    private void OnPinchUpdated(object? sender, PinchGestureUpdatedEventArgs e)
    {
        switch (e.Status)
        {
            case GestureStatus.Started:
                _pinchScale = 1;
                break;
            case GestureStatus.Running:
                _pinchScale *= e.Scale;
                PinchBox.Scale = Math.Clamp(_pinchScale, 0.2, 4);
                break;
            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                break;
        }

        PinchStatusLabel.Text = string.Create(CultureInfo.InvariantCulture,
            $"pinch: {e.Status.ToString().ToLowerInvariant()} scale={_pinchScale:0.00}");
    }

    private void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        // MAUI raises Completed with TotalX/TotalY reset to 0, so keep the last Running
        // totals — those are the values an app would actually have applied.
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _panX = _panY = 0;
                break;
            case GestureStatus.Running:
                _panX = e.TotalX;
                _panY = e.TotalY;
                break;
        }

        PanStatusLabel.Text = string.Create(CultureInfo.InvariantCulture,
            $"pan: {e.StatusType.ToString().ToLowerInvariant()} dx={_panX:0} dy={_panY:0}");
    }

    private void OnSwiped(object? sender, SwipedEventArgs e)
        => SwipeStatusLabel.Text = $"swipe: {e.Direction.ToString().ToLowerInvariant()}";

    private void OnSingleTapped(object? sender, TappedEventArgs e)
        => TapStatusLabel.Text = "tap: single";

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
        => TapStatusLabel.Text = "tap: double";

    private void OnLongPressStarted(object? sender, EventArgs e)
        => _longPressStartedAtUtc = DateTime.UtcNow;

    private void OnLongPressEnded(object? sender, EventArgs e)
    {
        var elapsedMs = (DateTime.UtcNow - _longPressStartedAtUtc).TotalMilliseconds;
        LongPressStatusLabel.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"longpress: {elapsedMs:0}ms");
    }

    private void OnRawTouchStarted(object? sender, TouchEventArgs e)
    {
        _rawTouchStart = e.Touches;
        _rawTouchSpanStart = Span(e.Touches);
        _rawTouchMaxTouches = e.Touches.Length;
        _rawTouchDragCount = 0;
        _rawTouchPeakScale = 1;
        Report(e, "started");
    }

    private void OnRawTouchDragged(object? sender, TouchEventArgs e)
    {
        _rawTouchDragCount++;
        Report(e, "dragged");
    }

    private void OnRawTouchEnded(object? sender, TouchEventArgs e) => Report(e, "ended");

    private void Report(TouchEventArgs e, string phase)
    {
        _rawTouchMaxTouches = Math.Max(_rawTouchMaxTouches, e.Touches.Length);

        var dx = e.Touches.Length > 0 && _rawTouchStart.Length > 0
            ? e.Touches[0].X - _rawTouchStart[0].X
            : 0;
        var dy = e.Touches.Length > 0 && _rawTouchStart.Length > 0
            ? e.Touches[0].Y - _rawTouchStart[0].Y
            : 0;

        // Keep the scale furthest from 1, so a pinch in or out both show their extent.
        var scale = _rawTouchSpanStart > 0 && e.Touches.Length > 1
            ? Span(e.Touches) / _rawTouchSpanStart
            : 1;
        if (Math.Abs(scale - 1) > Math.Abs(_rawTouchPeakScale - 1))
            _rawTouchPeakScale = scale;

        RawTouchStatusLabel.Text = string.Create(CultureInfo.InvariantCulture,
            $"rawtouch: {phase} maxtouches={_rawTouchMaxTouches} drags={_rawTouchDragCount} " +
            $"dx={dx:0} dy={dy:0} scale={_rawTouchPeakScale:0.00}");
    }

    private static double Span(PointF[] touches)
        => touches.Length < 2 ? 0 : Math.Sqrt(
            Math.Pow(touches[1].X - touches[0].X, 2) + Math.Pow(touches[1].Y - touches[0].Y, 2));
}

/// <summary>Fills the raw-touch canvas so it is visible and hit-testable.</summary>
file sealed class RawTouchDrawable : IDrawable
{
    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.FillColor = Color.FromArgb("#6B8E4E");
        canvas.FillRoundedRectangle(dirtyRect, 8);
    }
}
