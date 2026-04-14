namespace ControlGallery.Pages;

public partial class MapPage : ContentPage
{
    public MapPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);

        var hanaLoc = new Location(20.7557, -155.9880);

        MapSpan mapSpan = MapSpan.FromCenterAndRadius(hanaLoc, Distance.FromKilometers(3));
        map.MoveToRegion(mapSpan);

        map.Pins.Add(new Pin
        {
            Label = "Welcome to .NET MAUI!",
            Location = hanaLoc,
        });
    }

}