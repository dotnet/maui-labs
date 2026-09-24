using AIExtensions.Sample.ChatPlayground.ViewModels;

namespace AIExtensions.Sample.ChatPlayground;

/// <summary>Hosts the chat playground tab.</summary>
public partial class MainPage : ContentPage
{
    private bool _restored;

    /// <summary>Initializes the page with its view model.</summary>
    public MainPage(MainViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        Loaded += PageLoaded;
    }

    private async void PageLoaded(object? sender, EventArgs e)
    {
        if (_restored)
            return;

        _restored = true;
        await ((MainViewModel)BindingContext).RestoreCachedChatAsync();
    }
}
