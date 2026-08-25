using AIExtensions.Sample.Garden.Pages;
namespace AIExtensions.Sample.Garden;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        Routing.RegisterRoute("product", typeof(ProductDetailPage));
        Routing.RegisterRoute("review", typeof(ProductReviewPage));
        Routing.RegisterRoute("order", typeof(OrderDetailPage));
        Routing.RegisterRoute("cart", typeof(CartPage));
    }
}
