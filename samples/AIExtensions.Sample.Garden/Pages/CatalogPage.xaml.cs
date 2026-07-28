namespace AIExtensions.Sample.Garden.Pages;

public partial class CatalogPage : ContentPage
{
    public CatalogPage()
    {
        InitializeComponent();
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//main/chat");
    }
}
