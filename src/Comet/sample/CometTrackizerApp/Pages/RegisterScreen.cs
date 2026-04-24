namespace CometTrackizerApp.Pages;

public class RegisterScreen : View
{
	readonly Action _onBack;
	readonly Action _onSignupWithEmail;

	public RegisterScreen(Action onBack, Action onSignupWithEmail)
	{
		_onBack = onBack;
		_onSignupWithEmail = onSignupWithEmail;
	}

	[Body]
	View body() => new ZStack
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

		// Social signup + email options
		VStack(16,
			TrackizerTheme.PrimaryImageButton(
				"Sign up with Apple", null, Colors.Black, TrackizerTheme.White, "apple.png"),

			TrackizerTheme.PrimaryImageButton(
				"Sign up with Google", null, Colors.White, Colors.Black, "google.png"),

			TrackizerTheme.PrimaryImageButton(
				"Sign up with Facebook", null, Color.FromArgb("#1771E6"), TrackizerTheme.White, "facebook.png"),

			TrackizerTheme.H2("or")
				.Color(TrackizerTheme.White)
				.HorizontalTextAlignment(TextAlignment.Center)
				.Margin(new Thickness(0, 40)),

			TrackizerTheme.ThemedButton("Sign up with E-mail", _onSignupWithEmail),

			TrackizerTheme.BodySmall(
				"By registering, you agree to our Terms of Use. Learn how we collect, use and share your data.")
				.Color(TrackizerTheme.Grey50)
				.HorizontalTextAlignment(TextAlignment.Center)
				.Margin(new Thickness(0, 24, 0, 0))
		)
		.Alignment(Alignment.Bottom)
		.Margin(new Thickness(25, 0, 25, 30))
	}
	.Background(TrackizerTheme.Grey80);
}
