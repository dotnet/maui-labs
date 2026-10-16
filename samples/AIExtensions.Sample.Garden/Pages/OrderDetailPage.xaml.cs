using AIExtensions.Sample.Garden.ViewModels;

namespace AIExtensions.Sample.Garden.Pages;

public partial class OrderDetailPage : ContentPage
{
    public OrderDetailPage(OrderDetailViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }

    private async void OnProductTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is OrderLineViewModel line)
        {
            var sku = line.Sku;
            if (!string.IsNullOrWhiteSpace(sku))
                await Shell.Current.GoToAsync($"product?sku={sku}");
        }
    }
}
