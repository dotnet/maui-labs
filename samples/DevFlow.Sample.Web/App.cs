namespace DevFlow.Sample.Web;

public class App : Application
{
	protected override Window CreateWindow(IActivationState? activationState)
		=> new(new NavigationPage(new MainPage()));
}
