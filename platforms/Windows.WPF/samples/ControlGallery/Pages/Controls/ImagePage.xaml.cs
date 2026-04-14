namespace ControlGallery.Pages;

public partial class ImagePage : ContentPage
{
    public ImagePage()
    {
        InitializeComponent();

        
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        ImgTarget.Source = ImageSource.FromResource("ControlGallery.Resources.food_01.png");
    }
}