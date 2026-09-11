using AIExtensions.Sample.Garden.Pages;

namespace AIExtensions.Sample.Garden;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // Clean AI URI: //main/products/product/<sku>
        Routing.RegisterRoute("product", typeof(ProductDetailPage));
        // Clean AI URI: //main/products/product/<sku>/review
        Routing.RegisterRoute("review", typeof(ProductReviewPage));
        // Clean AI URI: //main/orders/order/<orderId>
        Routing.RegisterRoute("order", typeof(OrderDetailPage));
        // Cart stays modal — slides up from anywhere
        Routing.RegisterRoute("cart", typeof(CartPage));
    }
}
