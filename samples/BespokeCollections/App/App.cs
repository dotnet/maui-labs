namespace BespokeCollections.App;

public sealed class App(MainPage page) : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(page);
        window.Destroying += (_, _) => page.Dispose();
        return window;
    }
}
