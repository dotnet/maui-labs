namespace AIExtensions.Sample.ChatPlayground.Controls;

internal sealed class ResponsiveSettingsLayout(
    Grid root, Border settingsPanel, View contentPanel, BoxView backdrop,
    ImageButton openButton, ImageButton closeButton, double sidebarWidth)
{
    private bool _isCompact;

    public void Resize(double width)
    {
        var compact = width > 0 && width < 760;
        if (compact)
            settingsPanel.WidthRequest = width < 480 ? Math.Max(0, width - 24) : sidebarWidth;
        if (compact == _isCompact)
            return;

        _isCompact = compact;
        openButton.IsVisible = compact;
        closeButton.IsVisible = compact;
        backdrop.IsVisible = false;
        if (compact)
        {
            root.ColumnDefinitions[0].Width = 0;
            settingsPanel.IsVisible = false;
            settingsPanel.HorizontalOptions = LayoutOptions.Start;
            Grid.SetColumnSpan(settingsPanel, 2);
            Grid.SetColumn(contentPanel, 0);
            Grid.SetColumnSpan(contentPanel, 2);
        }
        else
        {
            root.ColumnDefinitions[0].Width = sidebarWidth;
            settingsPanel.IsVisible = true;
            settingsPanel.ClearValue(VisualElement.WidthRequestProperty);
            settingsPanel.HorizontalOptions = LayoutOptions.Fill;
            Grid.SetColumnSpan(settingsPanel, 1);
            Grid.SetColumn(contentPanel, 1);
            Grid.SetColumnSpan(contentPanel, 1);
        }
    }

    public void Open()
    {
        if (_isCompact)
        {
            backdrop.IsVisible = true;
            settingsPanel.IsVisible = true;
        }
    }

    public void Close()
    {
        if (_isCompact)
        {
            backdrop.IsVisible = false;
            settingsPanel.IsVisible = false;
        }
    }
}
