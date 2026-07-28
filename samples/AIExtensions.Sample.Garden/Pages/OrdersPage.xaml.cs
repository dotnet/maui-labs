namespace AIExtensions.Sample.Garden.Pages;

public partial class OrdersPage : ContentPage
{
    public OrdersPage()
    {
        InitializeComponent();
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//main/chat");
    }
}
