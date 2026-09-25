namespace AIExtensions.Sample.ChatPlayground.Shared.Controls;

/// <summary>Renders the backdrop and anchored menu surface.</summary>
public partial class PopupMenuView : ContentView
{
    private Border? _panel;

    public static readonly BindableProperty PopupBoundsProperty = BindableProperty.Create(
        nameof(PopupBounds), typeof(Rect), typeof(PopupMenuView), new Rect(0, 0, 0, 0));

    public PopupMenuView() => InitializeComponent();

    public event EventHandler? DismissRequested;

    public Rect PopupBounds
    {
        get => (Rect)GetValue(PopupBoundsProperty);
        set => SetValue(PopupBoundsProperty, value);
    }

    public View? MenuContent
    {
        get => Content as View;
        set => Content = value;
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _panel = GetTemplateChild("PART_Panel") as Border
            ?? throw new InvalidOperationException("The popup template must include a PART_Panel Border.");
    }

    public Size MeasureMenu(double width, double height) =>
        Panel.Measure(width, height);

    public double HorizontalPadding =>
        Panel.Padding.Left + Panel.Padding.Right + 2 * Panel.StrokeThickness;

    public void SetMenuPosition(Rect bounds) =>
        PopupBounds = bounds;

    private Border Panel =>
        _panel ?? throw new InvalidOperationException("The popup template has not been applied.");

    private void BackdropTapped(object? sender, TappedEventArgs e) =>
        DismissRequested?.Invoke(this, EventArgs.Empty);
}
