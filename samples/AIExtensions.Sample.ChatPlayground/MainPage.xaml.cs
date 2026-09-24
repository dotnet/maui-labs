using AIExtensions.Sample.ChatPlayground.Controls;
using AIExtensions.Sample.ChatPlayground.ViewModels;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Hosts the chat playground tab.</summary>
public partial class MainPage : ContentPage
{
    private readonly ResponsiveSettingsLayout _settingsLayout;
    private bool _isNarrowHeader;
    private bool _restored;

    /// <summary>Initializes the page with its view model.</summary>
    public MainPage(MainViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        _settingsLayout = new ResponsiveSettingsLayout(
            RootGrid, SettingsPanel, ChatPanel, SettingsBackdrop,
            OpenSettingsButton, CloseSettingsButton, 330);
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
        var useNarrowHeader = Width > 0 && Width < 480;
        if (useNarrowHeader != _isNarrowHeader)
        {
            _isNarrowHeader = useNarrowHeader;
            Grid.SetRow(HeaderActions, useNarrowHeader ? 1 : 0);
            Grid.SetColumn(HeaderActions, useNarrowHeader ? 1 : 2);
        }

        _settingsLayout.Resize(Width);
    }

    private void OpenSettingsClicked(object? sender, EventArgs e) => _settingsLayout.Open();

    private void CloseSettingsClicked(object? sender, EventArgs e) => _settingsLayout.Close();

    private void SettingsBackdropTapped(object? sender, TappedEventArgs e) =>
        CloseSettingsClicked(sender, e);

    /// <inheritdoc />
    protected override bool OnBackButtonPressed()
    {
        if (BindingContext is MainViewModel { Library.IsOpen: true } viewModel)
        {
            viewModel.Library.Close();
            return true;
        }

        return base.OnBackButtonPressed();
    }
}
