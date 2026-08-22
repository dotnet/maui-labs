using AIExtensions.Sample.Garden.ViewModels;

namespace AIExtensions.Sample.Garden.Pages;

public partial class ProductDetailPage : ContentPage, IQueryAttributable
{
    public ProductDetailPage(ProductDetailViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("sku", out var sku) && sku is string s)
        {
            if (BindingContext is ProductDetailViewModel vm)
                vm.Sku = s;
        }
    }

    protected override async void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        if (BindingContext is ProductDetailViewModel vm && !string.IsNullOrWhiteSpace(vm.Sku))
            await vm.LoadAsync(vm.Sku);
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }
}
