using Microsoft.Maui.Controls;

namespace MauiLinuxApp;

public class App : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new MainPage());
    }
}
