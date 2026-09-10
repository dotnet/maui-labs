namespace DevFlow.Sample;

public partial class LayoutDiagnosticsTestPage : ContentPage
{
    private bool _showProblems = true;

    public LayoutDiagnosticsTestPage()
    {
        InitializeComponent();
    }

    private void OnToggleBaselineFixtures(object? sender, EventArgs e)
    {
        _showProblems = !_showProblems;
        ConflictingLimitsBox.MinimumWidthRequest = _showProblems ? 160 : 40;
        ConstrainedDesiredProbe.ShowProblem = _showProblems;
        BaselineOverflowChild.WidthRequest = _showProblems ? 180 : 80;
        BaselineStateLabel.Text = _showProblems ? "Problem examples" : "Valid layout";
        ToggleBaselineButton.Text = _showProblems ? "Use valid layout" : "Restore problem examples";
    }

    private void OnBlockingOverlayTapped(object? sender, TappedEventArgs e)
    {
        StatusLabel.Text = "blocking overlay received tap";
    }
}
