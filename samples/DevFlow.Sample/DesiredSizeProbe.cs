namespace DevFlow.Sample;

// Deliberately models a custom control whose measurement disagrees with its allocated slot.
public sealed class DesiredSizeProbe : ContentView
{
    private bool _showProblem = true;

    public bool ShowProblem
    {
        get => _showProblem;
        set
        {
            if (_showProblem == value)
                return;
            _showProblem = value;
            WidthRequest = value ? -1 : 100;
            InvalidateMeasure();
        }
    }

    protected override Size MeasureOverride(double widthConstraint, double heightConstraint)
    {
        var measured = base.MeasureOverride(widthConstraint, heightConstraint);
        return _showProblem ? new Size(Math.Max(180, measured.Width), measured.Height) : measured;
    }
}
