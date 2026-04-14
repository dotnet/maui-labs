using System.Windows.Input;

namespace ControlGallery.Pages;

public partial class LabelPage : ContentPage
{
    public ICommand TapCommand => new Command<string>(async (url) => await Launcher.OpenAsync(url));

    public LabelPage()
    {
        InitializeComponent();
        BindingContext = this;
    }
}
