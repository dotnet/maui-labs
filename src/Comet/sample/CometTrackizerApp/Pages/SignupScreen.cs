using CometTrackizerApp.Components;

namespace CometTrackizerApp.Pages;

public class SignupScreenState
{
	public string Email { get; set; } = "";
	public string Password { get; set; } = "";
}

public class SignupScreen : Component<SignupScreenState>
{
	readonly Action _onBack;
	readonly Action _onSignup;
	readonly Action _onSignin;

	public SignupScreen(Action onBack, Action onSignup, Action onSignin)
	{
		_onBack = onBack;
		_onSignup = onSignup;
		_onSignin = onSignin;
	}

	public override View Render() => new ZStack
	{
		// Back button
		Image("back.png")
			.Frame(width: 24, height: 24)
			.Alignment(Alignment.TopLeading)
			.Margin(new Thickness(24, 32, 0, 0))
			.OnTap(_ => _onBack()),

		// Logo
		Image("full_logo.png")
			.Frame(width: 178)
			.Alignment(Alignment.Top)
			.Margin(new Thickness(0, 60, 0, 0)),

		// Form fields
		VStack(16,
			new RoundedEntryView("E-mail address", State.Email, false,
				text => SetState(s => s.Email = text)),

			new RoundedEntryView("Password", State.Password, true,
				text => SetState(s => s.Password = text)),

			TrackizerTheme.PrimaryButton("Get started, it's free!", _onSignup)
		)
		.Alignment(Alignment.Center)
		.Margin(new Thickness(24, 0)),

		// Sign in link at bottom
		VStack(20,
			TrackizerTheme.BodyMedium("Do you have already an account?")
				.Color(TrackizerTheme.White)
				.HorizontalTextAlignment(TextAlignment.Center),

			TrackizerTheme.ThemedButton("Sign In", _onSignin)
		)
		.Alignment(Alignment.Bottom)
		.Margin(new Thickness(24, 0, 24, 30))
	}
	.Background(TrackizerTheme.Grey80);
}
