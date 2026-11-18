namespace DevFlow.Sample.TvOS;

public class App : Application
{
	protected override Window CreateWindow(IActivationState? activationState)
		=> new(new NavigationPage(new MainPage()));
}
