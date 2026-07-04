using GenerativeUI.Sample.Garden.ViewModels;

namespace GenerativeUI.Sample.Garden;

public partial class MainPage : ContentPage
{
    public MainPage(ChatViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
