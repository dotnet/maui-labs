using AIExtensions.Sample.Garden.ViewModels;

namespace AIExtensions.Sample.Garden.Views;

public partial class CartView : ContentView
{
    public CartView()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (BindingContext is CartViewModel viewModel)
                await viewModel.InitializeAsync();
        };
    }
}
