using AIExtensions.Sample.Garden.ViewModels;

namespace AIExtensions.Sample.Garden.Views;

public partial class OrdersView : ContentView
{
    public OrdersView()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (BindingContext is OrdersViewModel viewModel)
                await viewModel.InitializeAsync();
        };
    }

    private async void OnOrderTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is string orderId && !string.IsNullOrWhiteSpace(orderId))
            await Shell.Current.GoToAsync($"order?orderId={orderId}");
    }
}
