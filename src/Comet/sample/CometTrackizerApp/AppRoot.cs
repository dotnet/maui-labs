using CometTrackizerApp.Models;
using CometTrackizerApp.Pages;

namespace CometTrackizerApp;

public enum AppScreen
{
	Welcome,
	Register,
	Signup,
	Signin,
	Home,
	NewSubscription
}

public class AppState
{
	public AppScreen Screen { get; set; } = AppScreen.Welcome;
	public User User { get; set; } = new();
	public HomeScreenView HomeView { get; set; } = HomeScreenView.Home;
}

public class AppRoot : Component<AppState>
{
	public override View Render()
	{
		if (!State.User.IsLoggedIn)
		{
			return RenderAuthFlow();
		}

		if (State.Screen == AppScreen.NewSubscription)
		{
			return new NewSubscriptionPage(
				onClose: () => SetState(s => s.Screen = AppScreen.Home));
		}

		return new HomeScreen(
			homeView: State.HomeView,
			onViewChanged: view => SetState(s => s.HomeView = view),
			onNewSubscription: () => SetState(s => s.Screen = AppScreen.NewSubscription),
			onShowBudgets: () => SetState(s => s.HomeView = HomeScreenView.Budgets));
	}

	View RenderAuthFlow() => State.Screen switch
	{
		AppScreen.Register => new RegisterScreen(
			onBack: () => SetState(s => s.Screen = AppScreen.Welcome),
			onSignupWithEmail: () => SetState(s => s.Screen = AppScreen.Signup)),

		AppScreen.Signup => new SignupScreen(
			onBack: () => SetState(s => s.Screen = AppScreen.Register),
			onSignup: () => SetState(s =>
			{
				s.User = new User { IsLoggedIn = true, Email = "j.doe@gmail.com", Name = "John Doe" };
				s.Screen = AppScreen.Home;
			}),
			onSignin: () => SetState(s => s.Screen = AppScreen.Signin)),

		AppScreen.Signin => new SigninScreen(
			onBack: () => SetState(s => s.Screen = AppScreen.Welcome)),

		_ => new WelcomeScreen(
			onGetStarted: () => SetState(s => s.Screen = AppScreen.Register),
			onHaveAccount: () => SetState(s => s.Screen = AppScreen.Signin)),
	};
}
