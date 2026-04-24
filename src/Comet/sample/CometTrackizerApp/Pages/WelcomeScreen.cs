namespace CometTrackizerApp.Pages;

public class WelcomeScreen : View
{
	readonly Action _onGetStarted;
	readonly Action _onHaveAccount;

	public WelcomeScreen(Action onGetStarted, Action onHaveAccount)
	{
		_onGetStarted = onGetStarted;
		_onHaveAccount = onHaveAccount;
	}

	[Body]
	View body() => new ZStack
	{
		// Background accent glow — offset and subtle to approximate Reactor's rotated Border
		Border(Spacer())
			.Frame(width: 800, height: 800)
			.Background(TrackizerTheme.Accentp100.WithAlpha(0.05f))
			.ClipShape(new Ellipse())
			.Alignment(Alignment.Center)
			.Margin(new Thickness(200, 0, 0, 0)),

		// Logo at top
		Image("full_logo.png")
			.Frame(width: 178)
			.Alignment(Alignment.Top)
			.Margin(new Thickness(0, 60, 0, 0)),

		// Background welcome image
		Image("welcome_background.png")
			.Frame(width: 289)
			.Alignment(Alignment.Center),

		// Floating service images (static — Reactor uses RotatingImage animations)
		Image("welcome_you_tube.png")
			.Frame(width: 143)
			.Alignment(Alignment.Center)
			.Margin(new Thickness(80, 0, 0, 200)),

		Image("welcome_netflix.png")
			.Frame(width: 143)
			.Alignment(Alignment.Center)
			.Margin(new Thickness(0, 0, 80, 20)),

		Image("welcome_spotify.png")
			.Frame(width: 243)
			.Alignment(Alignment.Center)
			.Margin(new Thickness(50, 200, 0, 0)),

		// Bottom buttons
		VStack(16,
			TrackizerTheme.PrimaryButton("Get Started", _onGetStarted),
			TrackizerTheme.ThemedButton("I have an account", _onHaveAccount)
		)
		.Alignment(Alignment.Bottom)
		.Margin(new Thickness(25, 0, 25, 30))
	}
	.Background(TrackizerTheme.Grey80);
}
