namespace ShinyBle.App;

public class App(MainPage page) : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
        => new(new NavigationPage(page)) { Title = "DevFlow BLE" };
}
