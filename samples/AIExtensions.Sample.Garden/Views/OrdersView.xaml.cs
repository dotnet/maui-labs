namespace AIExtensions.Sample.Garden.Views;

public partial class OrdersView : ContentView
{
    public OrdersView()
    {
        InitializeComponent();
    }

    private async void OnOrderTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is string orderId && !string.IsNullOrWhiteSpace(orderId))
            await Shell.Current.GoToAsync($"order?orderId={orderId}");
    }
}
