namespace MauiMacOSApp;

public class App : Application
{
	protected override Window CreateWindow(IActivationState? activationState)
		=> new Window(new MainPage());
}
