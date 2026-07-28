using AIExtensions.Sample.Garden.Pages;

namespace AIExtensions.Sample.Garden;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // Detail pages under products — pushed on top of their parent tab
        Routing.RegisterRoute("product", typeof(ProductDetailPage));
        // Review modal — slides up from product detail
        Routing.RegisterRoute("review", typeof(ProductReviewPage));
        // Order detail — pushed on top of orders tab
        Routing.RegisterRoute("order", typeof(OrderDetailPage));
        // Cart stays modal — slides up from anywhere
        Routing.RegisterRoute("cart", typeof(CartPage));
    }
}
