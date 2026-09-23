using ChatClientPlayground.ViewModels;

namespace ChatClientPlayground;

/// <summary>Hosts the single-page chat playground experience.</summary>
public partial class MainPage : ContentPage
{
    private const double CompactLayoutWidth = 760;
    private bool _isCompact;

    /// <summary>Initializes the page with its view model.</summary>
    public MainPage(MainViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        SizeChanged += PageSizeChanged;
    }

    private void PageSizeChanged(object? sender, EventArgs e)
    {
        var useCompactLayout = Width > 0 && Width < CompactLayoutWidth;
        if (useCompactLayout == _isCompact)
            return;

        _isCompact = useCompactLayout;
        OpenSettingsButton.IsVisible = useCompactLayout;
        CloseSettingsButton.IsVisible = useCompactLayout;

        if (useCompactLayout)
        {
            SettingsColumn.Width = 0;
            SettingsPanel.IsVisible = false;
            SettingsPanel.WidthRequest = Math.Min(330, Math.Max(0, Width - 24));
            SettingsPanel.HorizontalOptions = LayoutOptions.Start;
            Grid.SetColumnSpan(SettingsPanel, 2);
            Grid.SetColumn(ChatPanel, 0);
            Grid.SetColumnSpan(ChatPanel, 2);
            ApplyCompactHeader();
            ChatArea.IsCompact = true;
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
            ApplyDesktopHeader();
            ChatArea.IsCompact = false;
        }
    }

    private void OpenSettingsClicked(object? sender, EventArgs e)
    {
        if (_isCompact)
            SettingsPanel.IsVisible = true;
    }

    private void CloseSettingsClicked(object? sender, EventArgs e)
    {
        if (_isCompact)
            SettingsPanel.IsVisible = false;
    }

    private void ApplyCompactHeader()
    {
        HeaderGrid.ColumnDefinitions.Clear();
        HeaderGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        HeaderGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        HeaderGrid.RowDefinitions.Clear();
        HeaderGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        HeaderGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        Grid.SetRow(HeaderContent, 0);
        Grid.SetColumn(HeaderContent, 0);
        Grid.SetColumnSpan(HeaderContent, 2);
        Grid.SetRow(OpenSettingsButton, 1);
        Grid.SetColumn(OpenSettingsButton, 0);
        Grid.SetColumnSpan(OpenSettingsButton, 1);
        Grid.SetRow(ClearConversationButton, 1);
        Grid.SetColumn(ClearConversationButton, 1);
        Grid.SetColumnSpan(ClearConversationButton, 1);
    }

    private void ApplyDesktopHeader()
    {
        HeaderGrid.ColumnDefinitions.Clear();
        HeaderGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        HeaderGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        HeaderGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        HeaderGrid.RowDefinitions.Clear();
        HeaderGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        Grid.SetRow(OpenSettingsButton, 0);
        Grid.SetColumn(OpenSettingsButton, 0);
        Grid.SetColumnSpan(OpenSettingsButton, 1);
        Grid.SetRow(HeaderContent, 0);
        Grid.SetColumn(HeaderContent, 1);
        Grid.SetColumnSpan(HeaderContent, 1);
        Grid.SetRow(ClearConversationButton, 0);
        Grid.SetColumn(ClearConversationButton, 2);
        Grid.SetColumnSpan(ClearConversationButton, 1);
    }

}
