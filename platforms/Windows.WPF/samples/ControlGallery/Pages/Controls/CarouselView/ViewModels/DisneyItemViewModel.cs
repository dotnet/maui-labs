namespace CarouselGallery.ViewModels;
public partial class DisneyItemViewModel : ObservableObject
{

    [ObservableProperty]
    private double _scale;

    public string ImageSource { get; set; }
}