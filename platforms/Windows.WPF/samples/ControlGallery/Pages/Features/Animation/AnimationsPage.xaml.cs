namespace ControlGallery.Pages.Features.Animation;

public partial class AnimationsPage : ContentPage
{
    public AnimationsPage()
    {
        InitializeComponent();

        BindingContext = new AnimationsPageViewModel();
    }
}
