namespace CopilotChat.Sample;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
    }

    private async void OnOpenChatClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//chat");
    }
}
