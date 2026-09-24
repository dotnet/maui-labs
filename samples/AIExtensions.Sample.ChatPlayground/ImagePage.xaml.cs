using AIExtensions.Sample.ChatPlayground.Controls;
using AIExtensions.Sample.ChatPlayground.ViewModels;

namespace AIExtensions.Sample.ChatPlayground;

public partial class ImagePage : ContentPage
{
    private readonly ResponsiveSettingsLayout _settingsLayout;

    public ImagePage(ImagePlaygroundViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        _settingsLayout = new ResponsiveSettingsLayout(
            RootGrid, SettingsPanel, ResultsPanel, SettingsBackdrop,
            OpenSettingsButton, CloseSettingsButton, 300);
        SizeChanged += PageSizeChanged;
    }

    private void PageSizeChanged(object? sender, EventArgs e) => _settingsLayout.Resize(Width);

    private void OpenSettings(object? sender, EventArgs e) => _settingsLayout.Open();

    private void CloseSettings(object? sender, EventArgs e) => _settingsLayout.Close();
}
