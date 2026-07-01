using AIExtensions.Sample.Garden.Pages;

namespace AIExtensions.Sample.Garden;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // Detail pages under products — friendly URLs: /products/product?sku=X
        Routing.RegisterRoute("product", typeof(ProductDetailPage));
        // Review modal — /products/product/review?sku=X
        Routing.RegisterRoute("review", typeof(ProductReviewPage));
        // Order detail — /orders/order?orderId=X
        Routing.RegisterRoute("order", typeof(OrderDetailPage));
        // Cart stays modal — slides up from anywhere
        Routing.RegisterRoute("cart", typeof(CartPage));
    }
}
