using AIExtensions.Sample.Garden.ViewModels;

namespace AIExtensions.Sample.Garden.Pages;

public partial class ProductReviewPage : ContentPage
{
    public ProductReviewPage(ProductReviewViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

}
