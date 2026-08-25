using AIExtensions.Sample.Garden.Pages;
using AIExtensions.Sample.Garden.ViewModels;
using Microsoft.Maui.AI.Navigation;

namespace AIExtensions.Sample.Garden;

public partial class AppShell : Shell
{
    public AppShell(ShellNavigationService navigation)
    {
        InitializeComponent();

        navigation.RegisterRoute<ProductDetailPage>(
            "product",
            "//main/products",
            [new QueryParameterInfo("sku", nameof(ProductDetailViewModel.Sku), nameof(String))]);
        navigation.RegisterRoute<ProductReviewPage>(
            "review",
            "//main/products/product",
            [new QueryParameterInfo("sku", nameof(ProductReviewViewModel.Sku), nameof(String))]);
        navigation.RegisterRoute<OrderDetailPage>(
            "order",
            "//main/orders",
            [new QueryParameterInfo("orderId", nameof(OrderDetailViewModel.OrderId), nameof(String))]);
        navigation.RegisterRoute<CartPage>("cart", null, []);
    }
}
