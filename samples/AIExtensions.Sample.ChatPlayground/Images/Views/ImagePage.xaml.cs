
namespace AIExtensions.Sample.ChatPlayground;

public partial class ImagePage : ContentPage
{
    public ImagePage(ImagePlaygroundViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
