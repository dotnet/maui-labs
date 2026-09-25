using AIExtensions.Sample.ChatPlayground.Features.Images.ViewModels;

namespace AIExtensions.Sample.ChatPlayground.Features.Images.Views;

public partial class ImagePage : ContentPage
{
    public ImagePage(ImagePlaygroundViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
