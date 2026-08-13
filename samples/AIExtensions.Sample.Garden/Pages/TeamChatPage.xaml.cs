using AIExtensions.Sample.Garden.ViewModels;

namespace AIExtensions.Sample.Garden.Pages;

public partial class TeamChatPage : ContentPage
{
    public TeamChatPage(TeamChatViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    private async void OnBackClicked(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//main/chat");
}
