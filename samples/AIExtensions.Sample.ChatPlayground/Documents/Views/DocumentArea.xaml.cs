using System.ComponentModel;

namespace AIExtensions.Sample.ChatPlayground;

public partial class DocumentArea : ContentView
{
    private DocumentPlaygroundViewModel? _viewModel;
    private bool? _isVertical;

    public DocumentArea()
    {
        InitializeComponent();
        BindingContextChanged += OnBindingContextChanged;
    }

    private void OnBindingContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= ViewModelPropertyChanged;

        _viewModel = BindingContext as DocumentPlaygroundViewModel;
        if (_viewModel is not null)
            _viewModel.PropertyChanged += ViewModelPropertyChanged;
    }

    private void ViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DocumentPlaygroundViewModel.SelectedNode) ||
            _viewModel?.SelectedNode is not { } node)
        {
            return;
        }

        ResultTree.ScrollTo(node, position: ScrollToPosition.Center, animate: true);
        var preview = _viewModel.PreviewPages.FirstOrDefault(page => page.PageNumber == node.PageNumber);
        if (preview is not null)
            PreviewPagesView.ScrollTo(preview, position: ScrollToPosition.Center, animate: true);
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width > 0)
            ApplyLayout(width < 760);
    }

    private void ApplyLayout(bool vertical)
    {
        if (_isVertical == vertical)
            return;
        _isVertical = vertical;
        InspectorGrid.ColumnDefinitions.Clear();
        InspectorGrid.RowDefinitions.Clear();

        if (vertical)
        {
            InspectorGrid.ColumnDefinitions.Add(new(GridLength.Star));
            InspectorGrid.RowDefinitions.Add(new(GridLength.Star));
            InspectorGrid.RowDefinitions.Add(new(GridLength.Star));
            Grid.SetColumn(ImagesPanel, 0);
            Grid.SetRow(ImagesPanel, 0);
            Grid.SetColumn(ResultsPanel, 0);
            Grid.SetRow(ResultsPanel, 1);
            InspectorGrid.ColumnSpacing = 0;
            InspectorGrid.RowSpacing = 8;
        }
        else
        {
            InspectorGrid.ColumnDefinitions.Add(new(GridLength.Star));
            InspectorGrid.ColumnDefinitions.Add(new(GridLength.Star));
            InspectorGrid.RowDefinitions.Add(new(GridLength.Star));
            Grid.SetColumn(ImagesPanel, 0);
            Grid.SetRow(ImagesPanel, 0);
            Grid.SetColumn(ResultsPanel, 1);
            Grid.SetRow(ResultsPanel, 0);
            InspectorGrid.ColumnSpacing = 8;
            InspectorGrid.RowSpacing = 0;
        }

        Grid.SetColumnSpan(DocumentLoading, vertical ? 1 : 2);
        Grid.SetRowSpan(DocumentLoading, vertical ? 2 : 1);
    }
}
