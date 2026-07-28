using AIExtensions.Sample.Garden.ViewModels;

namespace AIExtensions.Sample.Garden.Pages;

public partial class CartPage : ContentPage
{
    public CartPage(CartViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    private async void OnCloseClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }
}
