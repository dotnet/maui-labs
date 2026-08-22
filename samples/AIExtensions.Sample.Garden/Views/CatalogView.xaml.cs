using AIExtensions.Sample.Garden.ViewModels;

namespace AIExtensions.Sample.Garden.Views;

public partial class CatalogView : ContentView
{
    public CatalogView()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (BindingContext is CatalogViewModel viewModel)
                await viewModel.InitializeAsync();
        };
    }

    private async void OnProductTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is string sku && !string.IsNullOrWhiteSpace(sku))
            await Shell.Current.GoToAsync($"product?sku={sku}");
    }

    private async void OnProductDetailClicked(object? sender, EventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is string sku && !string.IsNullOrWhiteSpace(sku))
            await Shell.Current.GoToAsync($"product?sku={sku}");
    }
}
