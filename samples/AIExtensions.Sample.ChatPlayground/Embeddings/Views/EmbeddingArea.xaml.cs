namespace AIExtensions.Sample.ChatPlayground;

public partial class EmbeddingArea : ContentView
{
    private bool? _stacked;

    public EmbeddingArea() => InitializeComponent();

    private void DocumentTapped(object? sender, TappedEventArgs e)
    {
        if (sender is BindableObject { BindingContext: DocumentSearchHit hit } &&
            BindingContext is EmbeddingPlaygroundViewModel viewModel)
            viewModel.SelectedResult = hit;
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (ResultsLayout is null || width <= 0)
            return;
        var stacked = width < 700;
        if (_stacked == stacked)
            return;
        _stacked = stacked;
        ResultsLayout.RowDefinitions.Clear();
        ResultsLayout.ColumnDefinitions.Clear();
        ResultsLayout.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        ResultsLayout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        if (stacked)
            ResultsLayout.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        else
            ResultsLayout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        Grid.SetRow(ChunksPanel, stacked ? 1 : 0);
        Grid.SetColumn(ChunksPanel, stacked ? 0 : 1);
    }
}
