namespace CometTrackizerApp.Pages;

public class SigninScreen : View
{
	readonly Action _onBack;

	public SigninScreen(Action onBack)
	{
		_onBack = onBack;
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
	}
	.Background(TrackizerTheme.Grey80);
}
