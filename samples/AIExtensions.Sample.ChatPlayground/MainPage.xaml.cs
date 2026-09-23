using AIExtensions.Sample.ChatPlayground.ViewModels;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Hosts the single-page chat playground experience.</summary>
public partial class MainPage : ContentPage
{
    private const double CompactLayoutWidth = 760;
    private bool _isCompact;
    private bool _restored;

    /// <summary>Initializes the page with its view model.</summary>
    public MainPage(MainViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        SizeChanged += PageSizeChanged;
        Loaded += PageLoaded;
    }

    private async void PageLoaded(object? sender, EventArgs e)
    {
        if (_restored)
            return;

        _restored = true;
        await ((MainViewModel)BindingContext).RestoreCachedChatAsync();
    }

    private void PageSizeChanged(object? sender, EventArgs e)
    {
        var useCompactLayout = Width > 0 && Width < CompactLayoutWidth;
        if (useCompactLayout)
        {
            // Phones fill the available width; tablets retain a sidebar-sized settings overlay.
            SettingsPanel.WidthRequest = Width < 480 ? Math.Max(0, Width - 24) : 330;
        }

        if (useCompactLayout == _isCompact)
            return;

        _isCompact = useCompactLayout;
        OpenSettingsButton.IsVisible = useCompactLayout;
        CloseSettingsButton.IsVisible = useCompactLayout;
        SettingsBackdrop.IsVisible = false;

        if (useCompactLayout)
        {
            SettingsColumn.Width = 0;
            SettingsPanel.IsVisible = false;
            SettingsPanel.HorizontalOptions = LayoutOptions.Start;
            Grid.SetColumnSpan(SettingsPanel, 2);
            Grid.SetColumn(ChatPanel, 0);
            Grid.SetColumnSpan(ChatPanel, 2);
        }
        else
        {
            SettingsColumn.Width = 330;
            SettingsPanel.IsVisible = true;
            SettingsPanel.ClearValue(WidthRequestProperty);
            SettingsPanel.HorizontalOptions = LayoutOptions.Fill;
            Grid.SetColumnSpan(SettingsPanel, 1);
            Grid.SetColumn(ChatPanel, 1);
            Grid.SetColumnSpan(ChatPanel, 1);
        }
    }

    private void OpenSettingsClicked(object? sender, EventArgs e)
    {
        if (_isCompact)
        {
            SettingsBackdrop.IsVisible = true;
            SettingsPanel.IsVisible = true;
        }
    }

    private void CloseSettingsClicked(object? sender, EventArgs e)
    {
        if (_isCompact)
        {
            SettingsBackdrop.IsVisible = false;
            SettingsPanel.IsVisible = false;
        }
    }

    private void SettingsBackdropTapped(object? sender, TappedEventArgs e) =>
        CloseSettingsClicked(sender, e);
}
