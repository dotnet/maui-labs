using AIExtensions.Sample.Garden.ViewModels;

namespace AIExtensions.Sample.Garden.Pages;

public partial class ProductReviewPage : ContentPage, IQueryAttributable
{
    public ProductReviewPage(ProductReviewViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    public async void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("sku", out var sku) && sku is string s)
        {
            if (BindingContext is ProductReviewViewModel vm)
                await vm.LoadAsync(s);
        }
    }
}
