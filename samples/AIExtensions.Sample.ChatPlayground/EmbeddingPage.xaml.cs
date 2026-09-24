using AIExtensions.Sample.ChatPlayground.ViewModels;

namespace AIExtensions.Sample.ChatPlayground;

public partial class EmbeddingPage : ContentPage
{
    private bool _isCompact;

    public EmbeddingPage(EmbeddingPlaygroundViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        SizeChanged += PageSizeChanged;
    }

    private void PageSizeChanged(object? sender, EventArgs e)
    {
        var compact = Width > 0 && Width < 760;
        if (compact)
            SettingsPanel.WidthRequest = Width < 480 ? Math.Max(0, Width - 24) : 300;
        if (compact == _isCompact)
            return;

        _isCompact = compact;
        OpenSettingsButton.IsVisible = compact;
        CloseSettingsButton.IsVisible = compact;
        SettingsBackdrop.IsVisible = false;
        if (compact)
        {
            RootGrid.ColumnDefinitions[0].Width = 0;
            SettingsPanel.IsVisible = false;
            SettingsPanel.HorizontalOptions = LayoutOptions.Start;
            Grid.SetColumnSpan(SettingsPanel, 2);
            Grid.SetColumn(ResultsPanel, 0);
            Grid.SetColumnSpan(ResultsPanel, 2);
        }
        else
        {
            RootGrid.ColumnDefinitions[0].Width = 300;
            SettingsPanel.IsVisible = true;
            SettingsPanel.ClearValue(WidthRequestProperty);
            SettingsPanel.HorizontalOptions = LayoutOptions.Fill;
            Grid.SetColumnSpan(SettingsPanel, 1);
            Grid.SetColumn(ResultsPanel, 1);
            Grid.SetColumnSpan(ResultsPanel, 1);
        }
    }

    private void OpenSettings(object? sender, EventArgs e)
    {
        if (_isCompact)
        {
            SettingsBackdrop.IsVisible = true;
            SettingsPanel.IsVisible = true;
        }
    }

    private void CloseSettings(object? sender, EventArgs e)
    {
        if (_isCompact)
        {
            SettingsBackdrop.IsVisible = false;
            SettingsPanel.IsVisible = false;
        }
    }
}
