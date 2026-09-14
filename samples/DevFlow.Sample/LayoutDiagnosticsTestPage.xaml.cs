namespace DevFlow.Sample;

public partial class LayoutDiagnosticsTestPage : ContentPage
{
    public LayoutDiagnosticsTestPage()
    {
        InitializeComponent();
    }

    private void OnBlockingOverlayTapped(object? sender, TappedEventArgs e)
    {
        StatusLabel.Text = "blocking overlay received tap";
    }
}
