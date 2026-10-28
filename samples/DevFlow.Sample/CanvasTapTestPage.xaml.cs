using SkiaSharp;
using SkiaSharp.Views.Maui;
using Syncfusion.Maui.Toolkit.Charts;

namespace DevFlow.Sample;

/// <summary>
/// Targets for DevFlow's tap-at-a-point. Nothing tappable here is a view: the SkiaSharp canvas
/// and the Syncfusion chart hit-test what they drew from the touch location, so each reports
/// which drawn item it resolved — a test can check the tap landed where it was aimed, not only
/// that the HTTP call returned 200.
/// </summary>
public partial class CanvasTapTestPage : ContentPage
{
    private static readonly SalesPoint[] Sales = [new("A", 3), new("B", 7), new("C", 5)];

    private int _skiaTaps;
    private int _chartSelections;
    private int _overlayTouches;
    private int _listenerOverlayTouches;

    public CanvasTapTestPage()
    {
        InitializeComponent();
        SalesSeries.ItemsSource = Sales;
#if ANDROID
        ListenerOverlay.HandlerChanged += (_, _) =>
            (ListenerOverlay.Handler?.PlatformView as Android.Views.View)?.SetOnTouchListener(
                new KeepingTouchListener(() =>
                {
                    _listenerOverlayTouches++;
                    ShowListenerOverlayStatus();
                }));
#endif
    }

    private void OnSkiaPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var info = e.Info;
        canvas.Clear();

        using var left = new SKPaint { Color = new SKColor(0xE5, 0x39, 0x35) };
        using var right = new SKPaint { Color = new SKColor(0x1E, 0x88, 0xE5) };
        canvas.DrawRect(0, 0, info.Width / 2f, info.Height, left);
        canvas.DrawRect(info.Width / 2f, 0, info.Width / 2f, info.Height, right);
    }

    private void OnSkiaTouch(object? sender, SKTouchEventArgs e)
    {
        // Claiming the touch is what makes the canvas receive the rest of it.
        e.Handled = true;
        if (e.ActionType != SKTouchAction.Released)
            return;

        // Touch locations are in canvas pixels; the halves are split on the pixel width too.
        var pixelWidth = SkiaCanvas.CanvasSize.Width;
        var scale = SkiaCanvas.Width > 0 ? pixelWidth / SkiaCanvas.Width : 1;
        var half = e.Location.X < pixelWidth / 2 ? "left" : "right";
        _skiaTaps++;
        SkiaStatusLabel.Text =
            $"skia: {half} at ({e.Location.X / scale:0}, {e.Location.Y / scale:0}) taps={_skiaTaps}";
    }

    private void OnOverlayPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(new SKColor(0x1C, 0x1B, 0x1F, 0x99));
    }

    private void OnOverlayTouch(object? sender, SKTouchEventArgs e)
    {
        e.Handled = true;
        if (e.ActionType == SKTouchAction.Released)
            _overlayTouches++;
        ShowOverlayStatus();
    }

    private void OnOverlayToggleClicked(object? sender, EventArgs e)
    {
        SkiaOverlay.IsVisible = !SkiaOverlay.IsVisible;
        _overlayTouches = 0;
        ShowOverlayStatus();
    }

    private void ShowOverlayStatus() =>
        SkiaOverlayStatusLabel.Text =
            $"overlay: {(SkiaOverlay.IsVisible ? "shown" : "hidden")} touches={_overlayTouches}";

    private void OnListenerOverlayToggleClicked(object? sender, EventArgs e)
    {
        ListenerOverlay.IsVisible = !ListenerOverlay.IsVisible;
        _listenerOverlayTouches = 0;
        ShowListenerOverlayStatus();
    }

    private void ShowListenerOverlayStatus() =>
        ListenerOverlayStatusLabel.Text =
            $"label overlay: {(ListenerOverlay.IsVisible ? "shown" : "hidden")} touches={_listenerOverlayTouches}";

    private void OnChartSelectionChanged(object? sender, ChartSelectionChangedEventArgs e)
    {
        _chartSelections++;
        var selected = e.NewIndexes.Count > 0 ? Sales[e.NewIndexes[0]].Name : "none";
        ChartStatusLabel.Text = $"chart: selected {selected} changes={_chartSelections}";
    }

    private sealed record SalesPoint(string Name, double Value);

#if ANDROID
    /// <summary>Keeps every touch it is offered and counts each one that ends.</summary>
    private sealed class KeepingTouchListener(Action released) : Java.Lang.Object, Android.Views.View.IOnTouchListener
    {
        public bool OnTouch(Android.Views.View? v, Android.Views.MotionEvent? e)
        {
            if (e?.ActionMasked == Android.Views.MotionEventActions.Up)
                released();
            return true;
        }
    }
#endif
}
