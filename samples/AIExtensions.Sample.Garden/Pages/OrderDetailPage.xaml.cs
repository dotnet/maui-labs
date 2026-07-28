using AIExtensions.Sample.Garden.ViewModels;

namespace AIExtensions.Sample.Garden.Pages;

public partial class OrderDetailPage : ContentPage, IQueryAttributable
{
    public OrderDetailPage(OrderDetailViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("orderId", out var id) && id is string s)
        {
            if (BindingContext is OrderDetailViewModel vm)
                vm.OrderId = s;
        }
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
