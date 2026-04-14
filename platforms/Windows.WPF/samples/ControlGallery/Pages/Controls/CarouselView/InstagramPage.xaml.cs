using CarouselGallery.Models;

namespace ControlGallery.Pages.Controls.CarouselView;
public partial class InstagramPage : ContentPage
{
    public InstagramPage()
    {
        InitializeComponent();
    }

    private void cv_CurrentItemChanged(object sender, CurrentItemChangedEventArgs e)
    {
        Description.Text = (e.CurrentItem as InstaPic).Description;
    }
}