namespace DevFlow.Sample;

/// <summary>
/// Targets for DevFlow's drag from a point. The slider and the card are driven only by MAUI's
/// pan and tap recognizers, so they move only for a touch that went through the platform's
/// gesture pipeline; every status label counts what that pipeline delivered.
/// </summary>
public partial class DragTestPage : ContentPage
{
    private const double ThumbSize = 24;

    private double _sliderValue;
    private double _dragStartCenter;
    private bool _sliderDragging;
    private int _sliderPans;
    private int _sliderTaps;
    private int _sliderCommits;

    private int _drops;

    public DragTestPage()
    {
        InitializeComponent();
        SliderTrack.SizeChanged += (_, _) => PlaceThumb();
    }

    private double Travel => Math.Max(0, SliderTrack.Width - ThumbSize);

    private double CenterFor(double value) => ThumbSize / 2 + Travel * value / 100;

    private void SetSliderFromCenter(double center)
    {
        _sliderValue = Travel > 0 ? Math.Round(Math.Clamp((center - ThumbSize / 2) / Travel, 0, 1) * 100) : 0;
        PlaceThumb();
    }

    private void PlaceThumb()
    {
        AbsoluteLayout.SetLayoutBounds(SliderThumb, new Rect(CenterFor(_sliderValue) - ThumbSize / 2, 8, ThumbSize, ThumbSize));
        SliderStatusLabel.Text =
            $"slider: value={_sliderValue:0} pans={_sliderPans} taps={_sliderTaps} commits={_sliderCommits}";
    }

    private void OnSliderPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _sliderDragging = true;
                _sliderPans++;
                _dragStartCenter = CenterFor(_sliderValue);
                break;
            case GestureStatus.Running when _sliderDragging:
                SetSliderFromCenter(_dragStartCenter + e.TotalX);
                break;
            case GestureStatus.Completed or GestureStatus.Canceled when _sliderDragging:
                _sliderDragging = false;
                _sliderCommits++;
                break;
        }

        PlaceThumb();
    }

    private void OnSliderTapped(object? sender, TappedEventArgs e)
    {
        _sliderTaps++;
        if (e.GetPosition(SliderTrack) is { } point)
            SetSliderFromCenter(point.X);
        PlaceThumb();
    }

    private void OnCardPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Running:
                DragCard.TranslationX = e.TotalX;
                DragCard.TranslationY = e.TotalY;
                break;
            case GestureStatus.Completed:
                var card = DragCard.Bounds.Offset(DragCard.TranslationX, DragCard.TranslationY);
                var zone = DropZone.Bounds;
                var over = zone.Contains(card.Center) ? "DropZone" : "outside";
                _drops++;
                DropStatusLabel.Text = $"drop: {over} drops={_drops}";
                DragCard.TranslationX = 0;
                DragCard.TranslationY = 0;
                break;
            case GestureStatus.Canceled:
                DropStatusLabel.Text = $"drop: canceled drops={_drops}";
                DragCard.TranslationX = 0;
                DragCard.TranslationY = 0;
                break;
        }
    }

    private void OnResetClicked(object? sender, EventArgs e)
    {
        _sliderValue = 0;
        _sliderPans = _sliderTaps = _sliderCommits = 0;
        _drops = 0;
        DropStatusLabel.Text = "drop: none drops=0";
        PlaceThumb();
    }
}
