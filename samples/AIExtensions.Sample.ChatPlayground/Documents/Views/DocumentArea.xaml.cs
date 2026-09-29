using System.ComponentModel;

namespace AIExtensions.Sample.ChatPlayground;

public partial class DocumentArea : ContentView
{
    private DocumentPlaygroundViewModel? _viewModel;

    public DocumentArea()
    {
        InitializeComponent();
        BindingContextChanged += OnBindingContextChanged;
    }

    private void OnBindingContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= ViewModelPropertyChanged;
            _viewModel.Settings.PropertyChanged -= SettingsPropertyChanged;
        }

        _viewModel = BindingContext as DocumentPlaygroundViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += ViewModelPropertyChanged;
            _viewModel.Settings.PropertyChanged += SettingsPropertyChanged;
        }
        ApplyLayout();
    }

    private void SettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DocumentSettingsViewModel.SelectedDisplayMode) or
            nameof(DocumentSettingsViewModel.SelectedLayoutMode))
        {
            ApplyLayout();
        }
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

    private void ApplyLayout()
    {
        if (_viewModel is null)
            return;

        var showImages = _viewModel.Settings.SelectedDisplayMode is "Images" or "Both";
        var showJson = _viewModel.Settings.SelectedDisplayMode is "JSON" or "Both";
        var horizontal = _viewModel.Settings.SelectedLayoutMode == "Horizontal";

        ImagesPanel.IsVisible = showImages;
        JsonPanel.IsVisible = showJson;
        InspectorGrid.ColumnDefinitions.Clear();
        InspectorGrid.RowDefinitions.Clear();

        if (showImages && showJson && horizontal)
        {
            InspectorGrid.ColumnDefinitions.Add(new(GridLength.Star));
            InspectorGrid.ColumnDefinitions.Add(new(GridLength.Star));
            InspectorGrid.RowDefinitions.Add(new(GridLength.Star));
            Grid.SetColumn(ImagesPanel, 0);
            Grid.SetRow(ImagesPanel, 0);
            Grid.SetColumn(JsonPanel, 1);
            Grid.SetRow(JsonPanel, 0);
            Grid.SetColumnSpan(ImagesPanel, 1);
            Grid.SetColumnSpan(JsonPanel, 1);
        }
        else if (showImages && showJson)
        {
            InspectorGrid.ColumnDefinitions.Add(new(GridLength.Star));
            InspectorGrid.RowDefinitions.Add(new(GridLength.Star));
            InspectorGrid.RowDefinitions.Add(new(GridLength.Star));
            Grid.SetColumn(ImagesPanel, 0);
            Grid.SetRow(ImagesPanel, 0);
            Grid.SetColumn(JsonPanel, 0);
            Grid.SetRow(JsonPanel, 1);
            Grid.SetColumnSpan(ImagesPanel, 1);
            Grid.SetColumnSpan(JsonPanel, 1);
        }
        else
        {
            InspectorGrid.ColumnDefinitions.Add(new(GridLength.Star));
            InspectorGrid.RowDefinitions.Add(new(GridLength.Star));
            var visiblePanel = showImages ? ImagesPanel : JsonPanel;
            Grid.SetColumn(visiblePanel, 0);
            Grid.SetRow(visiblePanel, 0);
            Grid.SetColumnSpan(visiblePanel, 1);
        }
    }

    private void RawJsonClicked(object? sender, EventArgs e)
    {
        if (sender is Button { BindingContext: DocumentResultNode node } &&
            BindingContext is DocumentPlaygroundViewModel viewModel)
        {
            viewModel.ShowNodeJson(node);
        }
    }
}
