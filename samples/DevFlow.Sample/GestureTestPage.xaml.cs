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

    public GestureTestPage()
    {
        InitializeComponent();

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
}
